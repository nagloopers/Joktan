using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Certificates.Tests;

public class CertificateStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly CertificateStore _store;

    public CertificateStoreTests()
    {
        _store = CertificateStore.Load(_folder.Path);
    }

    public void Dispose() => _folder.Dispose();

    private static CertificateSpec Spec(string name, CertificateKind kind, StoredCertificate? issuer = null, string names = "", string folder = "") => new()
    {
        Kind = kind,
        Name = name,
        CommonName = name,
        IssuerId = issuer?.Id,
        AlternativeNames = names,
        NotAfter = DateTimeOffset.UtcNow.AddYears(1),
        Folder = folder,
    };

    private StoredCertificate Root(string name = "root") => _store.Create(Spec(name, CertificateKind.RootAuthority));

    private StoredCertificate Intermediate(StoredCertificate issuer, string name = "intermediate")
        => _store.Create(Spec(name, CertificateKind.IntermediateAuthority, issuer));

    private StoredCertificate Client(StoredCertificate issuer, string name = "client")
        => _store.Create(Spec(name, CertificateKind.Client, issuer));

    private static int CertificatesIn(string pemPath)
    {
        var collection = new X509Certificate2Collection();
        collection.ImportFromPemFile(pemPath);
        return collection.Count;
    }

    [Fact]
    public void An_empty_folder_gives_an_empty_store_with_defaults()
    {
        Assert.Empty(_store.Entries);
        Assert.Equal("Joktan Root CA", _store.Defaults.RootCommonName);
        Assert.Equal(_folder.At("certificates"), _store.Folder);
        Assert.Equal(_store.Folder, _store.AuthorityFolder);
        Assert.Equal(_folder.At("mqtt-certificates"), _store.ClientFolder);
    }

    [Fact]
    public void An_unreadable_index_gives_an_empty_store()
    {
        _folder.Write("certificates.json", "not json");

        Assert.Empty(CertificateStore.Load(_folder.Path).Entries);
    }

    [Fact]
    public void Making_a_root_writes_its_files()
    {
        var changes = 0;
        _store.Changed += () => changes++;

        var root = Root();

        Assert.Equal(1, changes);
        Assert.Equal("root", root.Name);
        Assert.Null(root.IssuerId);
        Assert.Null(root.Problem);
        Assert.True(root.HasKey);
        Assert.True(root.CanIssue);
        Assert.Equal(_folder.At("certificates", "root"), root.Folder);
        Assert.True(File.Exists(root.CertificatePath));
        Assert.Equal(1, CertificatesIn(root.ChainPath));
        Assert.Equal(1, CertificatesIn(root.AuthorityPath));
        Assert.Contains(root, _store.Entries);
    }

    [Fact]
    public void Chain_and_CA_files_hold_what_is_above_a_certificate()
    {
        var root = Root();
        var intermediate = Intermediate(root);
        var client = Client(intermediate);

        Assert.Equal(2, CertificatesIn(intermediate.AuthorityPath));
        Assert.Equal(1, CertificatesIn(intermediate.ChainPath));
        Assert.Equal(2, CertificatesIn(client.AuthorityPath));
        Assert.Equal(2, CertificatesIn(client.ChainPath));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Keys_are_only_for_the_user_to_read()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File modes are a Linux and macOS matter.");

        var root = Root();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(root.KeyPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root.Folder));
    }

    [Fact]
    public void A_certificate_can_be_made_in_a_folder_chosen()
    {
        var chosen = _folder.At("chosen");

        var root = _store.Create(Spec("root", CertificateKind.RootAuthority, folder: chosen));

        Assert.Equal(Path.Combine(chosen, "root"), root.Folder);
        Assert.Equal(chosen, _store.AuthorityFolder);
        Assert.Equal(chosen, CertificateStore.ParentOf(root));
    }

    [Fact]
    public void What_is_made_is_there_when_loaded_again()
    {
        var root = Root();
        var client = Client(root);
        _store.SaveDefaults(new CertificateDefaults { Organization = "Acme" });
        _store.ExportFolder = "/somewhere";

        var loaded = CertificateStore.Load(_folder.Path);

        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal(root.Id, loaded.Find(client.Id)!.IssuerId);
        Assert.NotNull(loaded.Find(root.Id)!.Certificate);
        Assert.True(loaded.Find(root.Id)!.CanIssue);
        Assert.Equal("Acme", loaded.Defaults.Organization);
        Assert.Equal("/somewhere", loaded.ExportFolder);
    }

    [Fact]
    public void A_certificate_from_before_folders_could_be_chosen_is_found_under_its_ID()
    {
        var root = Root();
        var old = _folder.At("certificates", root.Id);
        Directory.Move(root.Folder, old);
        var index = JsonNode.Parse(File.ReadAllText(_store.IndexPath))!;
        index["Entries"]![0]!.AsObject().Remove("Location");
        File.WriteAllText(_store.IndexPath, index.ToJsonString());

        var loaded = CertificateStore.Load(_folder.Path);

        Assert.Equal(old, loaded.Entries[0].Folder);
        Assert.Null(loaded.Entries[0].Problem);
    }

    [Fact]
    public void A_missing_file_is_reported_and_stops_it_signing()
    {
        var root = Root();
        File.Delete(root.CertificatePath);

        var entry = CertificateStore.Load(_folder.Path).Entries[0];

        Assert.Contains("cert.pem is missing", entry.Problem);
        Assert.Null(entry.Certificate);
        Assert.False(entry.CanIssue);
        Assert.Equal(entry.Problem, CertificateStore.WhyCannotIssue(entry));
    }

    [Fact]
    public void A_missing_key_is_reported_and_stops_it_signing()
    {
        var root = Root();
        File.Delete(root.KeyPath);

        var entry = CertificateStore.Load(_folder.Path).Entries[0];

        Assert.Contains("key.pem is missing", entry.Problem);
        Assert.NotNull(entry.Certificate);
        Assert.False(entry.HasKey);
        Assert.False(entry.CanIssue);
    }

    [Fact]
    public void A_damaged_certificate_is_reported()
    {
        var root = Root();
        File.WriteAllText(root.CertificatePath, "not a certificate");

        Assert.Contains("cert.pem cannot be read", CertificateStore.Load(_folder.Path).Entries[0].Problem);
    }

    [Fact]
    public void Entries_without_an_ID_are_dropped()
    {
        _folder.Write("certificates.json", """{"Entries":[null,{"Name":"no id"}]}""");

        Assert.Empty(CertificateStore.Load(_folder.Path).Entries);
    }

    [Fact]
    public void Follows_the_family_of_a_certificate()
    {
        var root = Root();
        var intermediate = Intermediate(root);
        var client = Client(intermediate);
        var other = Client(root, "other");

        Assert.Equal([intermediate, other], _store.IssuedBy(root));
        Assert.Equal([intermediate, root], _store.IssuersOf(client));
        Assert.Empty(_store.IssuersOf(root));
        Assert.Equal([root, intermediate, other, client], _store.WithDescendants(root));
    }

    [Fact]
    public void Finds_by_ID()
    {
        var root = Root();

        Assert.Same(root, _store.Find(root.Id));
        Assert.Null(_store.Find("nothing"));
        Assert.Null(_store.Find(null));
    }

    [Fact]
    public void Says_what_is_wrong_with_a_spec()
    {
        var root = Root();

        void Fails(CertificateSpec spec, string message)
            => Assert.Equal(message, Assert.Throws<FormatException>(() => _store.Create(spec)).Message);

        Fails(Spec("ROOT", CertificateKind.RootAuthority), "There is already a certificate called ROOT");
        Fails(new CertificateSpec { Name = "a", NotAfter = DateTimeOffset.UtcNow.AddDays(1) }, "Enter a common name (CN)");
        Fails(Spec("a", CertificateKind.Client, root, names: "!!"), "!! is not a host name, IP address or email address");
        Fails(Spec("a", CertificateKind.Server, root), "A server certificate needs at least one host name or IP address");
        Fails(new CertificateSpec { Kind = CertificateKind.RootAuthority, Name = "a", CommonName = "a", NotAfter = DateTimeOffset.UtcNow.AddDays(-1) }, "The expiry date must be in the future");
        Fails(Spec("a", CertificateKind.RootAuthority, folder: "relative"), "Enter the folder's full path, or choose it with Browse");
        Fails(Spec("a", CertificateKind.RootAuthority, root), "A root CA signs itself, so it has no issuer");
        Fails(Spec("a", CertificateKind.Client), "Choose the CA that signs the certificate");
    }

    [Fact]
    public void An_end_certificate_cannot_sign()
    {
        var client = Client(Root());

        var error = Assert.Throws<FormatException>(() => _store.Create(Spec("a", CertificateKind.Client, client)));

        Assert.Equal("client is not a CA, so it cannot sign certificates", error.Message);
    }

    [Fact]
    public void Deleting_takes_everything_it_signed_but_not_other_files_in_the_folder()
    {
        var root = Root();
        var client = Client(root);
        var keep = Root("keep");
        var yours = Path.Combine(client.Folder, "yours.txt");
        File.WriteAllText(yours, "x");
        var changes = 0;
        _store.Changed += () => changes++;

        var removed = _store.Delete(root);

        Assert.Equal([root, client], removed);
        Assert.Equal([keep], _store.Entries);
        Assert.False(Directory.Exists(root.Folder));
        Assert.True(File.Exists(yours));
        Assert.False(File.Exists(Path.Combine(client.Folder, StoredCertificate.KeyFile)));
        Assert.Equal(1, changes);
        Assert.Single(CertificateStore.Load(_folder.Path).Entries);
    }

    [Fact]
    public void Renaming_keeps_the_folder_and_checks_the_name()
    {
        var root = Root();
        var other = Root("other");
        var folder = root.Folder;

        _store.Rename(root, " renamed ");

        Assert.Equal("renamed", root.Name);
        Assert.Equal(folder, root.Folder);
        _store.Rename(root, "RENAMED");
        Assert.Throws<FormatException>(() => _store.Rename(root, other.Name));
    }

    [Theory]
    [InlineData("My Server!", "my-server")]
    [InlineData("  a--b  ", "a-b")]
    [InlineData("a_b.c-d", "a_b.c-d")]
    [InlineData("...", "certificate")]
    [InlineData("名前", "certificate")]
    public void File_names_are_plain(string name, string expected)
        => Assert.Equal(expected, CertificateStore.FileNameFrom(name));

    [Fact]
    public void A_folder_name_gets_a_number_when_it_is_taken()
    {
        Directory.CreateDirectory(_folder.At("certificates", "a"));
        _folder.Write("certificates/a-2", "a file");

        Assert.Equal(_folder.At("certificates", "a-3"), _store.FolderFor("", "A"));
        Assert.Equal(_folder.At("other", "a"), _store.FolderFor(_folder.At("other"), "A"));
    }

    [Fact]
    public void Offers_a_name_not_yet_taken()
    {
        Root();
        Root("root (2)");

        Assert.Equal("free", _store.UnusedName(" free "));
        Assert.Equal("ROOT (3)", _store.UnusedName("ROOT"));
        Assert.Equal(60, _store.UnusedName(new string('a', 100)).Length);
    }

    [Fact]
    public void Checks_a_folder_to_make_certificates_in()
    {
        var file = _folder.Write("file.txt", "x");

        Assert.Null(CertificateStore.CheckFolder(""));
        Assert.Null(CertificateStore.CheckFolder(_folder.Path));
        Assert.Equal("Enter the folder's full path, or choose it with Browse", CertificateStore.CheckFolder("relative/path"));
        Assert.Equal($"{file} is a file, not a folder", CertificateStore.CheckFolder(file));
    }

    [Fact]
    public void Lists_the_files_an_export_would_write()
    {
        var root = Root();
        var client = Client(root);
        var all = new ExportOptions { BaseName = "x", IncludeKey = true, IncludeAuthority = true, Pkcs12Password = "" };

        Assert.Equal(["x.crt", "x.key", "ca.crt", "x.p12"], _store.ExportFileNames(client, all));
        Assert.Equal(["x.crt", "x.key", "x.p12"], _store.ExportFileNames(root, all));
        Assert.Equal(["x.crt"], _store.ExportFileNames(client, new ExportOptions { BaseName = "x" }));
    }

    [Fact]
    public void Exports_what_was_asked_for()
    {
        var root = Root();
        var intermediate = Intermediate(root);
        var client = Client(intermediate);
        var target = _folder.At("export");
        Directory.CreateDirectory(target);

        var paths = _store.Export(client, new ExportOptions
        {
            Folder = target,
            BaseName = "x",
            IncludeKey = true,
            IncludeAuthority = true,
            IncludeChain = true,
        });

        Assert.Equal(["x.crt", "x.key", "ca.crt"], paths.Select(Path.GetFileName));
        Assert.Equal(2, CertificatesIn(paths[0]));
        Assert.Equal(File.ReadAllText(client.KeyPath), File.ReadAllText(paths[1]));
        Assert.Equal(2, CertificatesIn(paths[2]));
    }

    [Fact]
    public void Exports_a_certificate_alone_without_its_chain()
    {
        var client = Client(Intermediate(Root()));

        var path = _store.Export(client, new ExportOptions { Folder = _folder.Path, BaseName = "x" }).Single();

        Assert.Equal(1, CertificatesIn(path));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void An_exported_key_is_only_for_the_user_to_read()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File modes are a Linux and macOS matter.");
        var root = Root();

        var paths = _store.Export(root, new ExportOptions { Folder = _folder.Path, BaseName = "x", IncludeKey = true });

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(paths[1]));
    }

    [Fact]
    public void Exports_a_password_protected_file_with_the_CAs()
    {
        var root = Root();
        var client = Client(root);

        var path = _store.Export(client, new ExportOptions { Folder = _folder.Path, BaseName = "x", Pkcs12Password = "secret" })[1];

        var loaded = X509CertificateLoader.LoadPkcs12CollectionFromFile(path, "secret");
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, c => c.HasPrivateKey && c.Thumbprint == client.Certificate!.Thumbprint);
        Assert.Throws<CryptographicException>(() => X509CertificateLoader.LoadPkcs12CollectionFromFile(path, "wrong"));
    }

    [Fact]
    public void Says_what_is_wrong_with_an_export()
    {
        var root = Root();

        void Fails(ExportOptions options, string message)
            => Assert.Equal(message, Assert.Throws<FormatException>(() => _store.Export(root, options)).Message);

        Fails(new ExportOptions { Folder = _folder.Path, BaseName = " " }, "Enter a file name without a folder or extension, such as server");
        Fails(new ExportOptions { Folder = _folder.Path, BaseName = "a/b" }, "Enter a file name without a folder or extension, such as server");
        Fails(new ExportOptions { Folder = _folder.At("missing"), BaseName = "x" }, $"The folder {_folder.At("missing")} does not exist");

        File.Delete(root.KeyPath);
        Fails(new ExportOptions { Folder = _folder.Path, BaseName = "x", IncludeKey = true }, "root has no private key to export");
    }

    [Fact]
    public void An_export_does_not_replace_files_unless_told_to()
    {
        var root = Root();
        var existing = _folder.Write("x.crt", "mine");
        var options = new ExportOptions { Folder = _folder.Path, BaseName = "x" };

        var error = Assert.Throws<FormatException>(() => _store.Export(root, options));

        Assert.Equal("x.crt is already in the folder. Choose another folder or name, or replace them", error.Message);
        Assert.Equal("mine", File.ReadAllText(existing));

        _store.Export(root, new ExportOptions { Folder = _folder.Path, BaseName = "x", ReplaceExisting = true });
        Assert.NotEqual("mine", File.ReadAllText(existing));
    }

    [Fact]
    public void Copies_what_a_client_uses_into_a_folder_of_its_own()
    {
        var root = Root();
        var client = Client(root);

        var (authority, certificate, key) = _store.CopyForClient("My Client", root, client);

        Assert.Equal(_folder.At("mqtt-certificates", "my-client", "ca.crt"), authority);
        Assert.Equal(File.ReadAllText(client.CertificatePath), File.ReadAllText(certificate!));
        Assert.Equal(File.ReadAllText(client.KeyPath), File.ReadAllText(key!));
    }

    [Fact]
    public void Copies_only_what_was_chosen()
    {
        var root = Root();

        Assert.Equal((null, null, null), _store.CopyForClient("c", null, null));

        var (authority, certificate, key) = _store.CopyForClient("c", root, null);
        Assert.NotNull(authority);
        Assert.Null(certificate);
        Assert.Null(key);
    }

    [Fact]
    public void Copies_go_to_the_folder_in_the_defaults()
    {
        var chosen = _folder.At("chosen");
        _store.SaveDefaults(new CertificateDefaults { ClientFolder = chosen });

        var (authority, _, _) = _store.CopyForClient("c", Root(), null);

        Assert.Equal(chosen, _store.ClientFolder);
        Assert.Equal(Path.Combine(chosen, "c", "ca.crt"), authority);
    }

    [Fact]
    public void Says_why_a_ca_not_yet_valid_or_without_its_key_cannot_sign()
    {
        var root = Root();
        File.Delete(root.KeyPath);
        Assert.Equal("root cannot sign certificates", CertificateStore.WhyCannotIssue(root));

        var later = Root("later");
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var future = new CertificateRequest("CN=later", key, HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.Now.AddDays(5), DateTimeOffset.Now.AddDays(10));
        File.WriteAllText(later.CertificatePath, future.ExportCertificatePem());

        var loaded = CertificateStore.Load(_folder.Path).Find(later.Id)!;

        Assert.StartsWith("later is not valid until ", CertificateStore.WhyCannotIssue(loaded));
    }

    [Fact]
    public void A_certificate_that_cannot_be_read_cannot_be_exported()
    {
        var root = Root();
        File.WriteAllText(root.CertificatePath, "not a certificate");
        var loaded = CertificateStore.Load(_folder.Path);

        var problem = Assert.Throws<FormatException>(() => loaded.Export(loaded.Find(root.Id)!, new ExportOptions { Folder = _folder.Path, BaseName = "x" }));

        Assert.Contains("cannot be read", problem.Message);
    }

    [Fact]
    public void A_file_in_the_way_stops_an_export_and_says_so()
    {
        var root = Root();
        var target = _folder.At("out");
        var options = new ExportOptions { Folder = target, BaseName = "x", ReplaceExisting = true };
        Directory.CreateDirectory(Path.Combine(target, _store.ExportFileNames(root, options)[0]));

        var problem = Assert.Throws<FormatException>(() => _store.Export(root, options));

        Assert.StartsWith("The files cannot be written", problem.Message);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void A_folder_that_cannot_be_written_stops_a_certificate_and_says_so()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This uses the permissions of a Unix folder.");
        var locked = _folder.At("locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var problem = Assert.Throws<FormatException>(() => _store.Create(Spec("x", CertificateKind.RootAuthority, folder: locked)));

            Assert.StartsWith("The certificate cannot be saved", problem.Message);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
