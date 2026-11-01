using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace Joktan.Certificates.Tests;

public class CertificateMakerTests
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";

    private static readonly DateTimeOffset InAYear = DateTimeOffset.UtcNow.AddYears(1);

    private static CertificateSpec Spec(CertificateKind kind, KeyAlgorithm algorithm = KeyAlgorithm.EcdsaP256, string names = "") => new()
    {
        Kind = kind,
        CommonName = "cn",
        AlternativeNames = names,
        KeyAlgorithm = algorithm,
        NotAfter = InAYear,
    };

    private static X509Certificate2 WithKey((X509Certificate2 Certificate, string KeyPem) made)
        => X509Certificate2.CreateFromPem(made.Certificate.ExportCertificatePem(), made.KeyPem);

    private static T Extension<T>(X509Certificate2 certificate) where T : X509Extension
        => certificate.Extensions.OfType<T>().Single();

    private static bool Chains(X509Certificate2 certificate, params X509Certificate2[] authorities)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
        chain.ChainPolicy.ExtraStore.AddRange(authorities);
        return chain.Build(certificate);
    }

    [Fact]
    public void A_root_CA_signs_itself_and_can_sign_others()
    {
        var (certificate, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null);

        Assert.Equal(certificate.SubjectName.Name, certificate.IssuerName.Name);
        Assert.True(Extension<X509BasicConstraintsExtension>(certificate).CertificateAuthority);
        Assert.True(Extension<X509KeyUsageExtension>(certificate).KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign));
        Assert.Empty(certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>());
    }

    [Theory]
    [InlineData(CertificateKind.Server, ServerAuthentication)]
    [InlineData(CertificateKind.Client, ClientAuthentication)]
    public void An_end_certificate_cannot_sign_and_is_for_its_purpose(CertificateKind kind, string purpose)
    {
        var root = WithKey(CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null));

        var (certificate, _) = CertificateMaker.Make(Spec(kind, names: "localhost"), root);

        Assert.False(Extension<X509BasicConstraintsExtension>(certificate).CertificateAuthority);
        Assert.Equal(X509KeyUsageFlags.DigitalSignature, Extension<X509KeyUsageExtension>(certificate).KeyUsages);
        Assert.Equal(purpose, Assert.Single(Extension<X509EnhancedKeyUsageExtension>(certificate).EnhancedKeyUsages.Cast<Oid>()).Value);
        Assert.Equal(root.SubjectName.Name, certificate.IssuerName.Name);
    }

    [Fact]
    public void An_RSA_key_can_also_encipher_keys()
    {
        var root = WithKey(CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null));

        var (certificate, _) = CertificateMaker.Make(Spec(CertificateKind.Server, KeyAlgorithm.Rsa2048), root);

        Assert.Equal(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            Extension<X509KeyUsageExtension>(certificate).KeyUsages);
    }

    [Theory]
    [InlineData(KeyAlgorithm.EcdsaP256, 256)]
    [InlineData(KeyAlgorithm.EcdsaP384, 384)]
    public void Makes_an_elliptic_curve_key_of_the_size_chosen(KeyAlgorithm algorithm, int size)
    {
        var (certificate, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority, algorithm), null);

        using var key = certificate.GetECDsaPublicKey();
        Assert.Equal(size, key!.KeySize);
    }

    [Fact]
    public void Makes_an_RSA_key_of_the_size_chosen()
    {
        var (certificate, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority, KeyAlgorithm.Rsa2048), null);

        using var key = certificate.GetRSAPublicKey();
        Assert.Equal(2048, key!.KeySize);
    }

    [Fact]
    public void An_RSA_CA_can_sign_an_elliptic_curve_certificate_and_the_other_way_round()
    {
        var rsa = WithKey(CertificateMaker.Make(Spec(CertificateKind.RootAuthority, KeyAlgorithm.Rsa2048), null));
        var ec = WithKey(CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null));

        var (byRsa, _) = CertificateMaker.Make(Spec(CertificateKind.Client), rsa);
        var (byEc, _) = CertificateMaker.Make(Spec(CertificateKind.Client, KeyAlgorithm.Rsa2048), ec);

        Assert.True(Chains(byRsa, rsa));
        Assert.True(Chains(byEc, ec));
    }

    [Theory]
    [InlineData(KeyAlgorithm.EcdsaP256, "1.2.840.10045.4.3.2")]
    [InlineData(KeyAlgorithm.EcdsaP384, "1.2.840.10045.4.3.3")]
    public void The_hash_follows_the_strength_of_the_signing_key(KeyAlgorithm algorithm, string signatureOid)
    {
        var (certificate, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority, algorithm), null);

        Assert.Equal(signatureOid, certificate.SignatureAlgorithm.Value);
    }

    [Fact]
    public void The_subject_holds_the_parts_given_most_general_first()
    {
        var spec = new CertificateSpec
        {
            Kind = CertificateKind.RootAuthority,
            CommonName = " cn ",
            Organization = "org",
            OrganizationalUnit = "unit",
            Locality = "town",
            State = "state",
            Country = "za",
            Email = "me@example.com",
            NotAfter = InAYear,
        };

        var (certificate, _) = CertificateMaker.Make(spec, null);

        // In the order they are encoded, which is the order OpenSSL shows.
        var raw = certificate.SubjectName.RawData;
        var positions = new[] { "ZA", "state", "town", "org", "unit", "cn", "me@example.com" }
            .Select(part => raw.AsSpan().IndexOf(System.Text.Encoding.ASCII.GetBytes(part)))
            .ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public void Empty_parts_of_the_subject_are_left_out()
    {
        var (certificate, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null);

        Assert.Equal("CN=cn", certificate.Subject);
    }

    [Fact]
    public void Alternative_names_go_in_the_certificate()
    {
        var (certificate, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority, names: "a.example.com 10.0.0.1"), null);

        var names = certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Equal(["a.example.com"], names.EnumerateDnsNames());
        Assert.Equal(["10.0.0.1"], names.EnumerateIPAddresses().Select(a => a.ToString()));
    }

    [Fact]
    public void Without_alternative_names_there_is_no_such_extension()
    {
        var (certificate, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null);

        Assert.Empty(certificate.Extensions.OfType<X509SubjectAlternativeNameExtension>());
    }

    [Fact]
    public void Starts_a_little_in_the_past_and_ends_when_asked()
    {
        var (certificate, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null);

        Assert.True(certificate.NotBefore.ToUniversalTime() < DateTime.UtcNow.AddMinutes(-4));
        Assert.Equal(InAYear.UtcDateTime, certificate.NotAfter.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void A_certificate_ends_no_later_than_its_CA()
    {
        var shortLived = new CertificateSpec { Kind = CertificateKind.RootAuthority, CommonName = "ca", NotAfter = DateTimeOffset.UtcNow.AddDays(1) };
        var root = WithKey(CertificateMaker.Make(shortLived, null));

        var (certificate, _) = CertificateMaker.Make(Spec(CertificateKind.Client), root);

        Assert.Equal(root.NotAfter, certificate.NotAfter);
    }

    [Fact]
    public void Serial_numbers_are_long_positive_and_different()
    {
        var (first, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null);
        var (second, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null);

        Assert.Equal(16, first.SerialNumberBytes.Length);
        Assert.NotEqual(first.SerialNumber, second.SerialNumber);
        Assert.Equal(0x40, first.SerialNumberBytes.Span[0] & 0xC0);
    }

    [Fact]
    public void The_key_comes_back_as_unencrypted_PEM_and_fits_the_certificate()
    {
        var made = CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null);

        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", made.KeyPem);
        Assert.False(made.Certificate.HasPrivateKey);
        Assert.True(WithKey(made).HasPrivateKey);
    }

    [Fact]
    public void A_CA_without_its_key_cannot_sign()
    {
        var (root, _) = CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null);

        Assert.Throws<CryptographicException>(() => CertificateMaker.Make(Spec(CertificateKind.Client), root));
    }

    [Fact]
    public void A_whole_chain_checks_out()
    {
        var root = WithKey(CertificateMaker.Make(Spec(CertificateKind.RootAuthority), null));
        var intermediate = WithKey(CertificateMaker.Make(Spec(CertificateKind.IntermediateAuthority), root));
        var (leaf, _) = CertificateMaker.Make(Spec(CertificateKind.Server, names: "localhost"), intermediate);

        Assert.True(Chains(leaf, root, intermediate));
    }

    [Fact]
    public void An_rsa_4096_key_signs_with_a_stronger_hash_and_an_email_can_be_an_alternative_name()
    {
        var spec = new CertificateSpec
        {
            Kind = CertificateKind.RootAuthority,
            Name = "big",
            CommonName = "big",
            KeyAlgorithm = KeyAlgorithm.Rsa4096,
            NotAfter = DateTimeOffset.UtcNow.AddYears(1),
        };

        var (issuer, key) = CertificateMaker.Make(spec, null);

        Assert.Equal("1.2.840.113549.1.1.12", issuer.SignatureAlgorithm.Value);

        using var withKey = X509Certificate2.CreateFromPem(issuer.ExportCertificatePem(), key);
        var (client, _) = CertificateMaker.Make(new CertificateSpec
        {
            Kind = CertificateKind.Client,
            Name = "client",
            CommonName = "client",
            AlternativeNames = "me@example.com",
            NotAfter = DateTimeOffset.UtcNow.AddMonths(1),
        }, withKey);

        Assert.Contains(client.Extensions, e => e.Oid?.Value == "2.5.29.17");
    }

    [Fact]
    public void A_certificate_starts_no_earlier_than_its_ca()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=later", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(5), DateTimeOffset.UtcNow.AddDays(5));

        var (made, _) = CertificateMaker.Make(new CertificateSpec
        {
            Kind = CertificateKind.Client,
            Name = "client",
            CommonName = "client",
            NotAfter = DateTimeOffset.UtcNow.AddDays(1),
        }, ca);

        Assert.True(made.NotBefore >= ca.NotBefore.AddSeconds(-1));
    }
}
