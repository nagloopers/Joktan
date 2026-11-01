using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Joktan.Core;
using Joktan.Passwords.Resources;

namespace Joktan.Passwords;

/// <summary>Where the password manager keeps its entries.</summary>
public enum PasswordStorageKind
{
    /// <summary>One file, encrypted with a key made from a master password.</summary>
    Vault,

    /// <summary>The operating system's credential store, with the list of entries in a file beside the settings.</summary>
    Keychain,
}

/// <summary>
/// The entries of the password manager and where they are kept. It is opened before use (with
/// the master password, for a vault) and closed again when locked.
/// </summary>
public abstract class PasswordStorage
{
    /// <summary>The folders and entries, while open; empty when closed.</summary>
    public PasswordFolder Root { get; protected set; } = new();

    public bool IsOpen { get; protected set; }

    public abstract PasswordStorageKind Kind { get; }

    /// <summary>The file the storage is in, to show the user.</summary>
    public abstract string Location { get; }

    /// <summary>Whether it has been made already, so it is opened rather than created.</summary>
    public abstract bool Exists { get; }

    /// <summary>Makes it, empty, and opens it.</summary>
    public abstract void Create(string? masterPassword);

    /// <summary>Opens it; false when the master password is wrong.</summary>
    /// <exception cref="PasswordStorageException">It cannot be read.</exception>
    public abstract bool Open(string? masterPassword);

    public abstract string ReadPassword(PasswordEntry entry);

    /// <summary>Keeps an entry's password. Written to disk by <see cref="Save"/>, except in a keychain, where it is kept at once.</summary>
    /// <exception cref="PasswordStorageException">It cannot be kept.</exception>
    public abstract void WritePassword(PasswordEntry entry, string password);

    /// <summary>Forgets the passwords of entries that were removed. Done at once in a keychain.</summary>
    public abstract void DeletePasswords(IEnumerable<PasswordEntry> entries);

    /// <summary>Writes the folders and entries.</summary>
    /// <exception cref="PasswordStorageException">They cannot be written.</exception>
    public abstract void Save();

    /// <summary>Removes the storage and everything in it, once its entries are kept somewhere else.</summary>
    public abstract void Destroy();

    /// <summary>Forgets the entries and any key, until it is opened again.</summary>
    public virtual void Close()
    {
        Root = new PasswordFolder();
        IsOpen = false;
    }

    // Written next to the file and then moved over it, so a failed write never leaves half a file.
    protected static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, overwrite: true);
    }
}

public sealed class PasswordStorageException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Everything in one file: the folders and entries, passwords included, as JSON encrypted with
/// AES-256-GCM. The key is made from the master password with PBKDF2-SHA256. The file holds the
/// salt and iteration count it was made with, so either can be raised later.
/// </summary>
public sealed class VaultStorage(string path) : PasswordStorage
{
    public const string FileName = "passwords.vault";

    public const int MinimumMasterPasswordLength = 8;

    private const string FileFormat = "joktan-vault";
    private const int FileVersion = 1;
    private const string KeyDerivation = "PBKDF2-SHA256";
    private const int NewFileIterations = 600_000;

    private byte[]? _key;
    private byte[] _salt = [];
    private int _iterations = NewFileIterations;

    public static string DefaultPath => System.IO.Path.Combine(DataFolder.Path, FileName);

    public string Path { get; } = System.IO.Path.GetFullPath(path);

    public override PasswordStorageKind Kind => PasswordStorageKind.Vault;

    public override string Location => Path;

    public override bool Exists => File.Exists(Path);

    public override void Create(string? masterPassword)
    {
        Root = new PasswordFolder();
        NewKey(masterPassword ?? "");
        IsOpen = true;
        Save();
    }

    public override bool Open(string? masterPassword)
    {
        VaultFile file;
        try
        {
            file = JsonSerializer.Deserialize<VaultFile>(File.ReadAllText(Path))
                   ?? throw new PasswordStorageException(Strings.VaultStorage_TheVaultFileIsEmpty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new PasswordStorageException(string.Format(Strings.VaultStorage_TheVaultFileCannotBeRead, e.Message), e);
        }
        catch (JsonException e)
        {
            throw new PasswordStorageException(Strings.VaultStorage_ThisIsNotAVaultFile, e);
        }

        if (file.Format != FileFormat || file.Kdf != KeyDerivation || file.Iterations <= 0)
            throw new PasswordStorageException(Strings.VaultStorage_ThisIsNotAVaultFileJoktan);
        if (file.Version > FileVersion)
            throw new PasswordStorageException(Strings.VaultStorage_TheVaultWasWrittenByA);

        byte[] salt, nonce, tag, data;
        try
        {
            (salt, nonce, tag, data) = (Convert.FromBase64String(file.Salt), Convert.FromBase64String(file.Nonce),
                Convert.FromBase64String(file.Tag), Convert.FromBase64String(file.Data));
        }
        catch (FormatException e)
        {
            throw new PasswordStorageException(Strings.VaultStorage_TheVaultFileIsDamaged, e);
        }

        var key = DeriveKey(masterPassword ?? "", salt, file.Iterations);
        var plain = new byte[data.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, data, tag, plain, Header(file.Iterations, salt));
        }
        catch (AuthenticationTagMismatchException)
        {
            // A wrong password and a changed file look the same.
            CryptographicOperations.ZeroMemory(key);
            return false;
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new PasswordStorageException(Strings.VaultStorage_TheVaultFileIsDamaged, e);
        }

        try
        {
            Root = JsonSerializer.Deserialize<PasswordFolder>(plain) ?? new PasswordFolder();
        }
        catch (JsonException e)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new PasswordStorageException(Strings.VaultStorage_TheVaultSContentsAreDamaged, e);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        _key = key;
        _salt = salt;
        _iterations = file.Iterations;
        IsOpen = true;
        return true;
    }

    /// <summary>Encrypts the vault with a key from a new master password, and writes it.</summary>
    public void ChangeMasterPassword(string masterPassword)
    {
        NewKey(masterPassword);
        Save();
    }

    public override string ReadPassword(PasswordEntry entry) => entry.Password ?? "";

    public override void WritePassword(PasswordEntry entry, string password) => entry.Password = password;

    public override void DeletePasswords(IEnumerable<PasswordEntry> entries)
    {
        // They go with the entries the next time the vault is written.
    }

    public override void Save()
    {
        if (_key is null)
            throw new InvalidOperationException(Strings.VaultStorage_TheVaultIsNotOpen);

        var plain = JsonSerializer.SerializeToUtf8Bytes(Root);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var data = new byte[plain.Length];
        try
        {
            using var aes = new AesGcm(_key, tag.Length);
            aes.Encrypt(nonce, plain, data, tag, Header(_iterations, _salt));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        var file = new VaultFile
        {
            Format = FileFormat,
            Version = FileVersion,
            Kdf = KeyDerivation,
            Iterations = _iterations,
            Salt = Convert.ToBase64String(_salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Data = Convert.ToBase64String(data),
        };
        try
        {
            WriteFile(Path, JsonSerializer.Serialize(file, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new PasswordStorageException(string.Format(Strings.VaultStorage_TheVaultCouldNotBeWritten, Path, e.Message), e);
        }
    }

    public override void Destroy()
    {
        Close();
        File.Delete(Path);
    }

    public override void Close()
    {
        if (_key is not null)
            CryptographicOperations.ZeroMemory(_key);
        _key = null;
        base.Close();
    }

    private void NewKey(string masterPassword)
    {
        if (_key is not null)
            CryptographicOperations.ZeroMemory(_key);
        _salt = RandomNumberGenerator.GetBytes(16);
        _iterations = NewFileIterations;
        _key = DeriveKey(masterPassword, _salt, _iterations);
    }

    private static byte[] DeriveKey(string masterPassword, byte[] salt, int iterations)
        => Rfc2898DeriveBytes.Pbkdf2(masterPassword, salt, iterations, HashAlgorithmName.SHA256, 32);

    // The settings the key was made with are authenticated along with the data, so they cannot be swapped.
    private static byte[] Header(int iterations, byte[] salt)
        => Encoding.UTF8.GetBytes($"{FileFormat}/{FileVersion}/{KeyDerivation}/{iterations}/{Convert.ToBase64String(salt)}");

    private sealed class VaultFile
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public string Kdf { get; set; } = "";
        public int Iterations { get; set; }
        public string Salt { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Tag { get; set; } = "";
        public string Data { get; set; } = "";
    }
}

/// <summary>
/// Each password in the operating system's credential store, filed under the entry's ID; the
/// folders and entries without their passwords in <c>passwords.json</c> in the data folder.
/// </summary>
public sealed class KeychainStorage(CredentialStore credentials, string? indexPath = null) : PasswordStorage
{
    public const string FileName = "passwords.json";

    public string IndexPath { get; } = indexPath ?? Path.Combine(DataFolder.Path, FileName);

    public bool IsAvailable => credentials.IsAvailable;

    public override PasswordStorageKind Kind => PasswordStorageKind.Keychain;

    public override string Location => IndexPath;

    public override bool Exists => File.Exists(IndexPath);

    public override void Create(string? masterPassword)
    {
        if (!IsAvailable)
            throw new PasswordStorageException(Strings.KeychainStorage_ThisComputerHasNoSystemKeychain);
        Root = new PasswordFolder();
        IsOpen = true;
        Save();
    }

    public override bool Open(string? masterPassword)
    {
        try
        {
            Root = JsonSerializer.Deserialize<PasswordFolder>(File.ReadAllText(IndexPath)) ?? new PasswordFolder();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new PasswordStorageException(string.Format(Strings.KeychainStorage_TheListOfEntriesCannotBe, e.Message), e);
        }
        catch (JsonException e)
        {
            throw new PasswordStorageException(Strings.KeychainStorage_TheListOfEntriesIsDamaged, e);
        }

        IsOpen = true;
        return true;
    }

    public override string ReadPassword(PasswordEntry entry) => credentials.Read(entry.Id) ?? "";

    public override void WritePassword(PasswordEntry entry, string password)
    {
        if (!credentials.Write(entry.Id, password))
            throw new PasswordStorageException(Strings.KeychainStorage_ThePasswordCouldNotBeSaved);
    }

    public override void DeletePasswords(IEnumerable<PasswordEntry> entries)
    {
        foreach (var entry in entries)
            credentials.Delete(entry.Id);
    }

    public override void Save()
    {
        // The passwords are in the keychain, never in the file.
        var copy = Root.Copy();
        foreach (var entry in copy.AllEntries())
            entry.Password = null;
        try
        {
            WriteFile(IndexPath, JsonSerializer.Serialize(copy, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new PasswordStorageException(string.Format(Strings.KeychainStorage_TheListOfEntriesCouldNot, e.Message), e);
        }
    }

    public override void Destroy()
    {
        DeletePasswords(Root.AllEntries().ToList());
        Close();
        File.Delete(IndexPath);
    }
}
