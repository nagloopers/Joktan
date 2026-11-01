using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Joktan.Certificates.Resources;

namespace Joktan.Certificates;

/// <summary>
/// One certificate Joktan made, with its key, in a folder of its own. The index keeps what the
/// files cannot say, such as its name and which CA signed it; everything else is read from the
/// certificate file.
/// </summary>
public sealed class StoredCertificate
{
    public const string CertificateFile = "cert.pem";
    public const string KeyFile = "key.pem";
    public const string ChainFile = "chain.pem";
    public const string AuthorityFile = "ca.pem";

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter<CertificateKind>))]
    public CertificateKind Kind { get; set; }

    /// <summary>The CA that signed it; null for a root CA.</summary>
    public string? IssuerId { get; set; }

    public DateTimeOffset Created { get; set; }

    /// <summary>
    /// The folder of its files. Missing for certificates made before the folder could be chosen,
    /// which are in <c>certificates/</c> under their ID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Location { get; set; }

    /// <summary>The certificate as read from its file; null when it could not be read.</summary>
    [JsonIgnore]
    public X509Certificate2? Certificate { get; private set; }

    /// <summary>Why the certificate or its key cannot be used, or null.</summary>
    [JsonIgnore]
    public string? Problem { get; private set; }

    [JsonIgnore]
    public string Folder { get; private set; } = "";

    /// <summary>The certificate alone, PEM.</summary>
    [JsonIgnore]
    public string CertificatePath => Path.Combine(Folder, CertificateFile);

    /// <summary>Its private key, unencrypted PKCS #8 PEM.</summary>
    [JsonIgnore]
    public string KeyPath => Path.Combine(Folder, KeyFile);

    /// <summary>The certificate followed by the intermediate CAs above it, PEM. What a broker shows.</summary>
    [JsonIgnore]
    public string ChainPath => Path.Combine(Folder, ChainFile);

    /// <summary>
    /// The CA certificates to trust it by, PEM: for a CA, itself and the CAs above it; for a server or
    /// client certificate, the CAs above it. Nearest first, the root last.
    /// </summary>
    [JsonIgnore]
    public string AuthorityPath => Path.Combine(Folder, AuthorityFile);

    [JsonIgnore]
    public bool HasKey => File.Exists(KeyPath);

    /// <summary>Whether it can sign certificates now: a CA with its key that has not expired.</summary>
    [JsonIgnore]
    public bool CanIssue => Kind.IsAuthority() && Certificate is not null && Problem is null && HasKey
                            && Certificate.NotAfter > DateTime.Now && Certificate.NotBefore <= DateTime.Now;

    [JsonIgnore]
    public string DisplayName => Name.Length > 0 ? Name : Id;

    /// <summary>Reads the certificate file from the entry's folder.</summary>
    internal void Load(string folder)
    {
        Folder = folder;
        Certificate?.Dispose();
        Certificate = null;
        Problem = null;
        try
        {
            if (!File.Exists(CertificatePath))
            {
                Problem = string.Format(Strings.StoredCertificate_IsMissingFrom, CertificateFile, Folder);
                return;
            }

            Certificate = X509Certificate2.CreateFromPem(File.ReadAllText(CertificatePath));
            if (!HasKey)
                Problem = string.Format(Strings.StoredCertificate_IsMissingFrom, KeyFile, Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            Problem = string.Format(Strings.StoredCertificate_CannotBeRead, CertificateFile, e.Message);
        }
    }

    /// <summary>The certificate with its private key, for signing. The caller disposes it.</summary>
    internal X509Certificate2 LoadWithKey()
    {
        try
        {
            return X509Certificate2.CreateFromPemFile(CertificatePath, KeyPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            throw new FormatException(string.Format(Strings.StoredCertificate_TheKeyOfCannotBeRead, DisplayName, e.Message), e);
        }
    }
}
