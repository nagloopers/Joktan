using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Joktan.Core;

namespace Joktan.Certificates;

/// <summary>What to write when a certificate is exported.</summary>
public sealed class ExportOptions
{
    public string Folder { get; init; } = "";

    /// <summary>The name the files share, without an extension, such as <c>server</c>.</summary>
    public string BaseName { get; init; } = "";

    public bool IncludeKey { get; init; }

    /// <summary>Writes <c>ca.crt</c>, the CA certificates to trust it by.</summary>
    public bool IncludeAuthority { get; init; }

    /// <summary>Puts the intermediate CAs after the certificate in its <c>.crt</c> file.</summary>
    public bool IncludeChain { get; init; }

    /// <summary>Also writes a PFX/P12 file with the certificate, its key and its CAs, protected by this password; null for none.</summary>
    public string? Pkcs12Password { get; init; }

    public bool ReplaceExisting { get; init; }
}

/// <summary>
/// The certificate authorities and certificates made on the Certificates page. Each lives in a
/// folder of its own, inside the folder chosen when it was made or <c>certificates/</c> in
/// Joktan's data folder, and <c>certificates.json</c> there holds the list, where each one is,
/// which CA signed which, and the defaults for new ones.
/// Specified in docs/features/dev-tools/connectivity/certificates/spec.md.
/// </summary>
public sealed class CertificateStore
{
    private const string IndexFileName = "certificates.json";
    private const string FolderName = "certificates";
    private const string ClientFolderName = "mqtt-certificates";

    private readonly string _root;
    private Index _index = new();

    private CertificateStore(string root)
    {
        _root = root;
    }

    /// <summary>Raised after a certificate is made or deleted.</summary>
    public event Action? Changed;

    public CertificateDefaults Defaults => _index.Defaults;

    /// <summary>Raised when the defaults have been saved, as more than one page changes them.</summary>
    public event Action? DefaultsChanged;

    /// <summary>The folder the last root CA was made in.</summary>
    public string AuthorityFolder => _index.AuthorityFolder is { Length: > 0 } folder ? folder : Folder;

    /// <summary>The folder the last export went to.</summary>
    public string ExportFolder
    {
        get => _index.ExportFolder;
        set
        {
            _index.ExportFolder = value;
            Save();
        }
    }

    /// <summary>Every certificate, in the order they were made.</summary>
    public IReadOnlyList<StoredCertificate> Entries => _index.Entries;

    public string Folder => Path.Combine(_root, FolderName);

    /// <summary>The folder MQTT clients' certificates are copied to when none is set in the defaults.</summary>
    public string DefaultClientFolder => Path.Combine(_root, ClientFolderName);

    /// <summary>Where the certificates MQTT clients use are copied to, one folder per client.</summary>
    public string ClientFolder => Defaults.ClientFolder.Trim() is { Length: > 0 } folder ? folder : DefaultClientFolder;

    /// <summary>Reads the store from <paramref name="root"/>, or from Joktan's data folder.</summary>
    public static CertificateStore Load(string? root = null)
    {
        var store = new CertificateStore(root ?? DataFolder.Path);
        try
        {
            if (File.Exists(store.IndexPath))
                store._index = JsonSerializer.Deserialize<Index>(File.ReadAllText(store.IndexPath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable index is treated as an empty store; the folders are left alone.
        }

        store._index.Defaults ??= new CertificateDefaults();
        store._index.Entries = store._index.Entries.Where(e => e is not null && e.Id.Length > 0).ToList();
        foreach (var entry in store._index.Entries)
            entry.Load(entry.Location ?? store.FolderOf(entry.Id));
        return store;
    }

    public StoredCertificate? Find(string? id) => id is null ? null : _index.Entries.FirstOrDefault(e => e.Id == id);

    /// <summary>The certificates a CA signed directly.</summary>
    public IEnumerable<StoredCertificate> IssuedBy(StoredCertificate authority)
        => _index.Entries.Where(e => e.IssuerId == authority.Id);

    /// <summary>The CA that signed it, the one above that, and so on, ending with the root.</summary>
    public List<StoredCertificate> IssuersOf(StoredCertificate entry)
    {
        var chain = new List<StoredCertificate>();
        for (var issuer = Find(entry.IssuerId); issuer is not null && !chain.Contains(issuer); issuer = Find(issuer.IssuerId))
            chain.Add(issuer);
        return chain;
    }

    /// <summary>It and everything below it: the certificates it signed, those they signed, and so on.</summary>
    public List<StoredCertificate> WithDescendants(StoredCertificate entry)
    {
        var all = new List<StoredCertificate> { entry };
        for (var i = 0; i < all.Count; i++)
            all.AddRange(IssuedBy(all[i]).Where(e => !all.Contains(e)));
        return all;
    }

    /// <summary>
    /// Makes the certificate and its key and saves them. Every failure is reported as a
    /// <see cref="FormatException"/> whose message can be shown as it is.
    /// </summary>
    public StoredCertificate Create(CertificateSpec spec)
    {
        if (CertificateSpec.CheckName(spec.Name, _index.Entries.Select(e => e.Name)) is { } nameError)
            throw new FormatException(nameError);
        if (spec.CheckSubject() is { } subjectError)
            throw new FormatException(subjectError);
        if (CertificateSpec.ParseAlternativeNames(spec.AlternativeNames, out var names) is { } namesError)
            throw new FormatException(namesError);
        if (spec.Kind == CertificateKind.Server && names.Count == 0)
            throw new FormatException("A server certificate needs at least one host name or IP address");
        if (spec.NotAfter <= DateTimeOffset.UtcNow)
            throw new FormatException("The expiry date must be in the future");
        if (CheckFolder(spec.Folder) is { } folderError)
            throw new FormatException(folderError);

        StoredCertificate? issuer = null;
        if (spec.Kind == CertificateKind.RootAuthority)
        {
            if (spec.IssuerId is not null)
                throw new FormatException("A root CA signs itself, so it has no issuer");
        }
        else
        {
            issuer = Find(spec.IssuerId) ?? throw new FormatException("Choose the CA that signs the certificate");
            if (!issuer.CanIssue)
                throw new FormatException(WhyCannotIssue(issuer));
        }

        X509Certificate2 certificate;
        string keyPem;
        try
        {
            if (issuer is null)
                (certificate, keyPem) = CertificateMaker.Make(spec, null);
            else
            {
                using var signer = issuer.LoadWithKey();
                (certificate, keyPem) = CertificateMaker.Make(spec, signer);
            }
        }
        catch (CryptographicException e)
        {
            throw new FormatException($"The certificate cannot be made: {e.Message}", e);
        }

        var entry = new StoredCertificate
        {
            Id = NewId(),
            Name = spec.Name.Trim(),
            Kind = spec.Kind,
            IssuerId = issuer?.Id,
            Created = DateTimeOffset.Now,
        };

        var parent = ParentFolder(spec.Folder);
        var folder = FolderFor(parent, entry.Name);
        entry.Location = folder;
        try
        {
            if (parent == Folder)
                CreatePrivateFolder(parent);
            else
                Directory.CreateDirectory(parent);
            CreatePrivateFolder(folder);
            var issuers = issuer is null ? [] : IssuersOf(entry);
            var certificatePem = certificate.ExportCertificatePem() + "\n";

            File.WriteAllText(Path.Combine(folder, StoredCertificate.CertificateFile), certificatePem);
            WritePrivate(Path.Combine(folder, StoredCertificate.KeyFile), keyPem + "\n");
            File.WriteAllText(Path.Combine(folder, StoredCertificate.ChainFile),
                certificatePem + Pem(issuers.Where(i => i.Kind != CertificateKind.RootAuthority)));
            File.WriteAllText(Path.Combine(folder, StoredCertificate.AuthorityFile),
                spec.Kind.IsAuthority() ? certificatePem + Pem(issuers) : Pem(issuers));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDeleteFolder(folder);
            throw new FormatException($"The certificate cannot be saved in {folder}: {e.Message}", e);
        }
        finally
        {
            certificate.Dispose();
        }

        entry.Load(folder);
        _index.Entries.Add(entry);
        if (spec.Kind == CertificateKind.RootAuthority)
            _index.AuthorityFolder = parent;
        Save();
        Changed?.Invoke();
        return entry;
    }

    /// <summary>Deletes the certificate and everything it signed, with their keys. Returns what was deleted.</summary>
    public List<StoredCertificate> Delete(StoredCertificate entry)
    {
        var removed = WithDescendants(entry);
        foreach (var item in removed)
        {
            _index.Entries.Remove(item);
            TryDeleteFolder(item.Folder);
            item.Certificate?.Dispose();
        }

        Save();
        Changed?.Invoke();
        return removed;
    }

    public void Rename(StoredCertificate entry, string name)
    {
        if (CertificateSpec.CheckName(name, _index.Entries.Where(e => e != entry).Select(e => e.Name)) is { } error)
            throw new FormatException(error);

        entry.Name = name.Trim();
        Save();
        Changed?.Invoke();
    }

    public void SaveDefaults(CertificateDefaults defaults)
    {
        _index.Defaults = defaults.Clone();
        Save();
        DefaultsChanged?.Invoke();
    }

    /// <summary>
    /// Copies what an MQTT client uses into its own folder under <see cref="ClientFolder"/>:
    /// <c>ca.crt</c> from <paramref name="authority"/>, the CA and those above it, and
    /// <c>client.crt</c> and <c>client.key</c> from <paramref name="client"/>. Files already there
    /// are replaced. Returns the paths, null for what was not copied. Every failure is reported as
    /// a <see cref="FormatException"/> whose message can be shown as it is.
    /// </summary>
    public (string? Authority, string? Certificate, string? Key) CopyForClient(string clientName, StoredCertificate? authority, StoredCertificate? client)
    {
        if (authority is null && client is null)
            return (null, null, null);

        var folder = Path.Combine(ClientFolder, FileNameFrom(clientName));
        try
        {
            Directory.CreateDirectory(ClientFolder);
            CreatePrivateFolder(folder);

            string? authorityPath = null, certificatePath = null, keyPath = null;
            if (authority is not null)
            {
                authorityPath = Path.Combine(folder, "ca.crt");
                File.WriteAllText(authorityPath, File.ReadAllText(authority.AuthorityPath));
            }

            if (client is not null)
            {
                certificatePath = Path.Combine(folder, "client.crt");
                keyPath = Path.Combine(folder, "client.key");
                File.WriteAllText(certificatePath, File.ReadAllText(client.CertificatePath));
                WritePrivate(keyPath, File.ReadAllText(client.KeyPath));
            }

            return (authorityPath, certificatePath, keyPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FormatException($"The certificates cannot be copied to {folder}: {e.Message}", e);
        }
    }

    /// <summary>Returns what is wrong with a folder to make certificates in, or null. Empty is the default folder.</summary>
    public static string? CheckFolder(string folder)
    {
        folder = folder.Trim();
        if (folder.Length == 0)
            return null;
        if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(folder))
            return "Enter the folder's full path, or choose it with Browse";
        if (File.Exists(folder))
            return $"{folder} is a file, not a folder";
        return null;
    }

    /// <summary>
    /// The folder a certificate called <paramref name="name"/> would get inside
    /// <paramref name="parent"/>: its name in lower case, with a number added if it is taken.
    /// </summary>
    public string FolderFor(string parent, string name)
    {
        parent = ParentFolder(parent);
        var baseName = FileNameFrom(name);
        var folder = Path.Combine(parent, baseName);
        for (var i = 2; Directory.Exists(folder) || File.Exists(folder) || _index.Entries.Any(e => SameFolder(e.Folder, folder)); i++)
            folder = Path.Combine(parent, $"{baseName}-{i}");
        return folder;
    }

    /// <summary>The folder a certificate's own folder is in, such as the one chosen for its CA.</summary>
    public static string ParentOf(StoredCertificate entry) => Path.GetDirectoryName(entry.Folder) ?? entry.Folder;

    /// <summary>A file or folder name made from a certificate's name: lower case, with dashes for anything unusual.</summary>
    public static string FileNameFrom(string name)
    {
        var text = new string(name.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray()).Trim('-', '.');
        while (text.Contains("--", StringComparison.Ordinal))
            text = text.Replace("--", "-", StringComparison.Ordinal);
        return text.Length > 0 ? text : "certificate";
    }

    /// <summary>A name not yet taken, made from <paramref name="wanted"/> by adding a number if needed.</summary>
    public string UnusedName(string wanted)
    {
        wanted = wanted.Trim();
        if (wanted.Length > CertificateSpec.MaxNameLength - 4)
            wanted = wanted[..(CertificateSpec.MaxNameLength - 4)];
        bool Taken(string name) => _index.Entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (!Taken(wanted))
            return wanted;
        for (var i = 2; ; i++)
        {
            if (!Taken($"{wanted} ({i})"))
                return $"{wanted} ({i})";
        }
    }

    /// <summary>Why a CA cannot sign certificates now, for showing to the user.</summary>
    public static string WhyCannotIssue(StoredCertificate authority)
    {
        if (!authority.Kind.IsAuthority())
            return $"{authority.DisplayName} is not a CA, so it cannot sign certificates";
        if (authority.Problem is { } problem)
            return problem;
        if (authority.Certificate!.NotAfter <= DateTime.Now)
            return $"{authority.DisplayName} expired on {authority.Certificate.NotAfter:yyyy-MM-dd}";
        if (authority.Certificate.NotBefore > DateTime.Now)
            return $"{authority.DisplayName} is not valid until {authority.Certificate.NotBefore:yyyy-MM-dd}";
        return $"{authority.DisplayName} cannot sign certificates";
    }

    /// <summary>The files an export would write, by name.</summary>
    public List<string> ExportFileNames(StoredCertificate entry, ExportOptions options)
    {
        var names = new List<string> { options.BaseName + ".crt" };
        if (options.IncludeKey)
            names.Add(options.BaseName + ".key");
        if (options.IncludeAuthority && entry.Kind != CertificateKind.RootAuthority)
            names.Add("ca.crt");
        if (options.Pkcs12Password is not null)
            names.Add(options.BaseName + ".p12");
        return names;
    }

    /// <summary>
    /// Writes the certificate, and what else was asked for, to a folder. Returns the paths written.
    /// Every failure is reported as a <see cref="FormatException"/> whose message can be shown as it is.
    /// </summary>
    public List<string> Export(StoredCertificate entry, ExportOptions options)
    {
        if (entry.Certificate is null)
            throw new FormatException(entry.Problem ?? "The certificate cannot be read");
        if (options.BaseName.Trim().Length == 0 || options.BaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || options.BaseName.Contains('/'))
            throw new FormatException("Enter a file name without a folder or extension, such as server");
        if (!Directory.Exists(options.Folder))
            throw new FormatException($"The folder {options.Folder} does not exist");
        if ((options.IncludeKey || options.Pkcs12Password is not null) && !entry.HasKey)
            throw new FormatException($"{entry.DisplayName} has no private key to export");

        var names = ExportFileNames(entry, options);
        var paths = names.Select(n => Path.Combine(options.Folder, n)).ToList();
        if (!options.ReplaceExisting && paths.Where(File.Exists).Select(Path.GetFileName).ToList() is { Count: > 0 } existing)
            throw new FormatException($"{string.Join(", ", existing)} {(existing.Count == 1 ? "is" : "are")} already in the folder. Choose another folder or name, or replace them");

        try
        {
            var target = paths.GetEnumerator();
            target.MoveNext();
            File.WriteAllText(target.Current, File.ReadAllText(options.IncludeChain ? entry.ChainPath : entry.CertificatePath));
            if (options.IncludeKey)
            {
                target.MoveNext();
                WritePrivate(target.Current, File.ReadAllText(entry.KeyPath));
            }

            if (options.IncludeAuthority && entry.Kind != CertificateKind.RootAuthority)
            {
                target.MoveNext();
                File.WriteAllText(target.Current, File.ReadAllText(entry.AuthorityPath));
            }

            if (options.Pkcs12Password is { } password)
            {
                target.MoveNext();
                WritePrivate(target.Current, Pkcs12(entry, password));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FormatException($"The files cannot be written to {options.Folder}: {e.Message}", e);
        }
        catch (CryptographicException e)
        {
            throw new FormatException($"The PFX/P12 file cannot be made: {e.Message}", e);
        }

        return paths;
    }

    /// <summary>The file that holds the list.</summary>
    public string IndexPath => Path.Combine(_root, IndexFileName);

    private string FolderOf(string id) => Path.Combine(Folder, id);

    private string ParentFolder(string folder)
        => folder.Trim() is { Length: > 0 } chosen ? Path.GetFullPath(chosen) : Folder;

    private static bool SameFolder(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(IndexPath, JsonSerializer.Serialize(_index, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The list cannot be saved; the folders are still there.
        }
    }

    private string NewId()
    {
        while (true)
        {
            var id = Guid.NewGuid().ToString("N")[..12];
            if (Find(id) is null && !Directory.Exists(FolderOf(id)))
                return id;
        }
    }

    private static string Pem(IEnumerable<StoredCertificate> entries)
    {
        var text = new StringBuilder();
        foreach (var entry in entries)
        {
            if (entry.Certificate is not null)
                text.Append(entry.Certificate.ExportCertificatePem()).Append('\n');
        }

        return text.ToString();
    }

    private byte[] Pkcs12(StoredCertificate entry, string password)
    {
        using var certificate = entry.LoadWithKey();
        var collection = new X509Certificate2Collection { certificate };
        foreach (var issuer in IssuersOf(entry).Where(i => i.Certificate is not null))
            collection.Add(issuer.Certificate!);
        return collection.ExportPkcs12(Pkcs12ExportPbeParameters.Pbes2Aes256Sha256, password);
    }

    // Keys are kept unencrypted, so only the user may read them on Linux and macOS.
    private static void CreatePrivateFolder(string path)
    {
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void WritePrivate(string path, string text) => WritePrivate(path, Encoding.UTF8.GetBytes(text));

    private static void WritePrivate(string path, byte[] content)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(path, content);
            return;
        }

        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
        stream.Write(content);

        // A file that was already there keeps its mode, so it is set again.
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    // Only the files made here are deleted, and the folder only once empty, because it may be in
    // a folder the user chose and hold files of theirs.
    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (folder.Length == 0 || !Directory.Exists(folder))
                return;
            foreach (var file in new[] { StoredCertificate.CertificateFile, StoredCertificate.KeyFile, StoredCertificate.ChainFile, StoredCertificate.AuthorityFile })
                File.Delete(Path.Combine(folder, file));
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left behind; it is no longer in the list.
        }
    }

    private sealed class Index
    {
        public CertificateDefaults Defaults { get; set; } = new();

        public string ExportFolder { get; set; } = "";

        public string AuthorityFolder { get; set; } = "";

        public List<StoredCertificate> Entries { get; set; } = [];
    }
}
