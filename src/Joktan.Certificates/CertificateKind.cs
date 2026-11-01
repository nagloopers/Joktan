using Joktan.Certificates.Resources;

namespace Joktan.Certificates;

/// <summary>What a certificate is for.</summary>
public enum CertificateKind
{
    /// <summary>A certificate authority that signs itself, at the top of a chain.</summary>
    RootAuthority,

    /// <summary>A certificate authority signed by another one, which can sign certificates in turn.</summary>
    IntermediateAuthority,

    /// <summary>A certificate a broker shows to the clients that connect to it.</summary>
    Server,

    /// <summary>A certificate a client shows to a broker that asks for one (mutual TLS).</summary>
    Client,
}

/// <summary>The algorithm and size of a certificate's key.</summary>
public enum KeyAlgorithm
{
    EcdsaP256,
    EcdsaP384,
    Rsa2048,
    Rsa4096,
}

public static class CertificateKinds
{
    public static bool IsAuthority(this CertificateKind kind)
        => kind is CertificateKind.RootAuthority or CertificateKind.IntermediateAuthority;

    public static string Text(this CertificateKind kind) => kind switch
    {
        CertificateKind.RootAuthority => Strings.CertificateKinds_RootCA,
        CertificateKind.IntermediateAuthority => Strings.CertificateKinds_IntermediateCA,
        CertificateKind.Server => Strings.CertificateKinds_Server,
        CertificateKind.Client => Strings.CertificateKinds_Client,
        _ => kind.ToString(),
    };

    public static string Text(this KeyAlgorithm algorithm) => algorithm switch
    {
        KeyAlgorithm.EcdsaP256 => "ECDSA P-256",
        KeyAlgorithm.EcdsaP384 => "ECDSA P-384",
        KeyAlgorithm.Rsa2048 => "RSA 2048",
        KeyAlgorithm.Rsa4096 => "RSA 4096",
        _ => algorithm.ToString(),
    };
}
