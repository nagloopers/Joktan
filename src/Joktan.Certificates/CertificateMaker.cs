using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Joktan.Certificates.Resources;

namespace Joktan.Certificates;

/// <summary>Makes a key pair and a certificate for it, signed by a CA or by itself.</summary>
public static class CertificateMaker
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";

    // Allows for a clock on another machine that is a little behind.
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Makes the certificate. <paramref name="issuer"/> must hold its private key; it is null for a
    /// root CA. Returns the certificate, without its key, and the key as unencrypted PKCS #8 PEM.
    /// </summary>
    public static (X509Certificate2 Certificate, string KeyPem) Make(CertificateSpec spec, X509Certificate2? issuer)
    {
        using var key = CreateKey(spec.KeyAlgorithm);
        using var issuerKey = issuer is null ? null : IssuerKey(issuer);
        var signingKey = issuerKey ?? key;
        var request = key switch
        {
            ECDsa ec => new CertificateRequest(Subject(spec), ec, HashFor(signingKey)),
            RSA rsa => new CertificateRequest(Subject(spec), rsa, HashFor(signingKey), RSASignaturePadding.Pkcs1),
            _ => throw new NotSupportedException(),
        };

        AddExtensions(request, spec, key is RSA, issuer);

        var notBefore = DateTimeOffset.UtcNow - ClockSkew;
        var notAfter = spec.NotAfter;
        if (issuer is not null)
        {
            // A certificate cannot be valid outside the time its CA is.
            if (notBefore < issuer.NotBefore.ToUniversalTime())
                notBefore = issuer.NotBefore.ToUniversalTime();
            if (notAfter > issuer.NotAfter.ToUniversalTime())
                notAfter = issuer.NotAfter.ToUniversalTime();
        }

        var signer = GeneratorFor(signingKey);
        var issuerName = issuer?.SubjectName ?? request.SubjectName;
        var certificate = request.Create(issuerName, signer, notBefore, notAfter, NewSerialNumber());
        return (certificate, key.ExportPkcs8PrivateKeyPem());
    }

    private static AsymmetricAlgorithm CreateKey(KeyAlgorithm algorithm) => algorithm switch
    {
        KeyAlgorithm.EcdsaP384 => ECDsa.Create(ECCurve.NamedCurves.nistP384),
        KeyAlgorithm.Rsa2048 => RSA.Create(2048),
        KeyAlgorithm.Rsa4096 => RSA.Create(4096),
        _ => ECDsa.Create(ECCurve.NamedCurves.nistP256),
    };

    private static AsymmetricAlgorithm IssuerKey(X509Certificate2 issuer)
        => (AsymmetricAlgorithm?)issuer.GetECDsaPrivateKey() ?? issuer.GetRSAPrivateKey()
           ?? throw new CryptographicException(Strings.CertificateMaker_TheCASPrivateKeyIs);

    // The hash follows the strength of the key that signs.
    private static HashAlgorithmName HashFor(AsymmetricAlgorithm signingKey) => signingKey switch
    {
        ECDsa { KeySize: >= 521 } => HashAlgorithmName.SHA512,
        ECDsa { KeySize: >= 384 } => HashAlgorithmName.SHA384,
        RSA { KeySize: >= 4096 } => HashAlgorithmName.SHA384,
        _ => HashAlgorithmName.SHA256,
    };

    private static X509SignatureGenerator GeneratorFor(AsymmetricAlgorithm key) => key switch
    {
        ECDsa ec => X509SignatureGenerator.CreateForECDsa(ec),
        RSA rsa => X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
        _ => throw new NotSupportedException(),
    };

    private static X500DistinguishedName Subject(CertificateSpec spec)
    {
        // Encoded most general first, the order OpenSSL and most tools show. The builder encodes the
        // last part added first, so they are added most specific first.
        var builder = new X500DistinguishedNameBuilder();
        if (spec.Email.Trim() is { Length: > 0 } email)
            builder.AddEmailAddress(email);
        builder.AddCommonName(spec.CommonName.Trim());
        if (spec.OrganizationalUnit.Trim() is { Length: > 0 } unit)
            builder.AddOrganizationalUnitName(unit);
        if (spec.Organization.Trim() is { Length: > 0 } organization)
            builder.AddOrganizationName(organization);
        if (spec.Locality.Trim() is { Length: > 0 } locality)
            builder.AddLocalityName(locality);
        if (spec.State.Trim() is { Length: > 0 } state)
            builder.AddStateOrProvinceName(state);
        if (spec.Country.Trim() is { Length: > 0 } country)
            builder.AddCountryOrRegion(country.ToUpperInvariant());
        return builder.Build();
    }

    private static void AddExtensions(CertificateRequest request, CertificateSpec spec, bool isRsa, X509Certificate2? issuer)
    {
        var subjectKey = new X509SubjectKeyIdentifierExtension(request.PublicKey, false);

        if (spec.Kind.IsAuthority())
        {
            request.CertificateExtensions.Add(X509BasicConstraintsExtension.CreateForCertificateAuthority());
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        }
        else
        {
            request.CertificateExtensions.Add(X509BasicConstraintsExtension.CreateForEndEntity());

            // An RSA key also encrypts the session key in older TLS key exchanges.
            var usage = isRsa ? X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.DigitalSignature;
            request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                [new Oid(spec.Kind == CertificateKind.Server ? ServerAuthentication : ClientAuthentication)], false));
        }

        CertificateSpec.ParseAlternativeNames(spec.AlternativeNames, out var names);
        if (names.Count > 0)
        {
            var builder = new SubjectAlternativeNameBuilder();
            foreach (var name in names)
            {
                switch (name.Type)
                {
                    case AlternativeNameType.IpAddress:
                        builder.AddIpAddress(System.Net.IPAddress.Parse(name.Value));
                        break;
                    case AlternativeNameType.Email:
                        builder.AddEmailAddress(name.Value);
                        break;
                    default:
                        builder.AddDnsName(name.Value);
                        break;
                }
            }

            request.CertificateExtensions.Add(builder.Build());
        }

        request.CertificateExtensions.Add(subjectKey);
        request.CertificateExtensions.Add(issuer is null
            ? X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(subjectKey)
            : X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, includeKeyIdentifier: true, includeIssuerAndSerial: false));
    }

    // 16 random bytes, positive, with the top bits set so the length never shrinks.
    private static byte[] NewSerialNumber()
    {
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] = (byte)((serial[0] & 0x7F) | 0x40);
        return serial;
    }
}
