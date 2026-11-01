using System.Text.Json.Serialization;

namespace Joktan.Certificates;

/// <summary>
/// What a new certificate is filled in with. Every value can be changed for each certificate, and
/// the defaults themselves can be changed on the Settings / Certificate Defaults page.
/// </summary>
public sealed class CertificateDefaults
{
    public string RootCommonName { get; set; } = "Joktan Root CA";

    public string IntermediateCommonName { get; set; } = "Joktan Intermediate CA";

    public string ServerCommonName { get; set; } = "localhost";

    /// <summary>The host names and IP addresses a server certificate is valid for, separated by commas.</summary>
    public string ServerAlternativeNames { get; set; } = "localhost, 127.0.0.1, ::1";

    public string ClientCommonName { get; set; } = "joktan-client";

    public string Organization { get; set; } = "Joktan";

    public string OrganizationalUnit { get; set; } = "Development";

    public string Locality { get; set; } = "";

    public string State { get; set; } = "";

    /// <summary>A two-letter country code, or empty.</summary>
    public string Country { get; set; } = "";

    /// <summary>
    /// Where the certificates MQTT clients use are copied to, one folder per client; empty for
    /// <c>mqtt-certificates/</c> in Joktan's data folder.
    /// </summary>
    public string ClientFolder { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter<KeyAlgorithm>))]
    public KeyAlgorithm KeyAlgorithm { get; set; } = KeyAlgorithm.EcdsaP256;

    public int AuthorityValidityDays { get; set; } = 3650;

    // Apple's systems refuse TLS server certificates valid for longer than 825 days.
    public int CertificateValidityDays { get; set; } = 825;

    public string CommonNameFor(CertificateKind kind) => kind switch
    {
        CertificateKind.RootAuthority => RootCommonName,
        CertificateKind.IntermediateAuthority => IntermediateCommonName,
        CertificateKind.Server => ServerCommonName,
        _ => ClientCommonName,
    };

    public int ValidityDaysFor(CertificateKind kind)
        => kind.IsAuthority() ? AuthorityValidityDays : CertificateValidityDays;

    public CertificateDefaults Clone() => (CertificateDefaults)MemberwiseClone();
}
