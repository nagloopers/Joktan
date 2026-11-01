using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Joktan.Certificates.Resources;

namespace Joktan.Certificates;

/// <summary>Everything needed to make one certificate.</summary>
public sealed class CertificateSpec
{
    public const int MaxNameLength = 64;

    // The upper bounds X.520 sets for each part of a name.
    private const int MaxCommonNameLength = 64;
    private const int MaxFieldLength = 128;

    public CertificateKind Kind { get; init; }

    /// <summary>The name shown in the list. Unique, ignoring case.</summary>
    public string Name { get; init; } = "";

    /// <summary>The CA that signs the certificate; null for a root CA, which signs itself.</summary>
    public string? IssuerId { get; init; }

    public string CommonName { get; init; } = "";

    public string Organization { get; init; } = "";

    public string OrganizationalUnit { get; init; } = "";

    public string Locality { get; init; } = "";

    public string State { get; init; } = "";

    public string Country { get; init; } = "";

    public string Email { get; init; } = "";

    /// <summary>Host names, IP addresses and email addresses, separated by commas, spaces or new lines.</summary>
    public string AlternativeNames { get; init; } = "";

    public KeyAlgorithm KeyAlgorithm { get; init; } = KeyAlgorithm.EcdsaP256;

    public DateTimeOffset NotAfter { get; init; }

    /// <summary>
    /// The folder to make the certificate's own folder in; empty for <c>certificates/</c> in
    /// Joktan's data folder.
    /// </summary>
    public string Folder { get; init; } = "";

    /// <summary>Returns what is wrong with a certificate's name in the list, or null.</summary>
    public static string? CheckName(string name, IEnumerable<string> otherNames)
    {
        name = name.Trim();
        if (name.Length == 0)
            return Strings.CertificateSpec_EnterANameForTheCertificate;
        if (name.Length > MaxNameLength)
            return string.Format(Strings.CertificateSpec_TheNameMustNotBeLonger, MaxNameLength);
        if (name.Any(char.IsControl))
            return Strings.CertificateSpec_TheNameMustNotContainControl;
        if (otherNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return string.Format(Strings.CertificateSpec_ThereIsAlreadyACertificateCalled, name);
        return null;
    }

    /// <summary>Returns what is wrong with the parts of the subject, or null.</summary>
    public string? CheckSubject()
    {
        if (CommonName.Trim().Length == 0)
            return Strings.CertificateSpec_EnterACommonNameCN;
        if (CommonName.Trim().Length > MaxCommonNameLength)
            return string.Format(Strings.CertificateSpec_TheCommonNameMustNotBe, MaxCommonNameLength);
        return CheckFields(Organization, OrganizationalUnit, Locality, State, Country)
               ?? (Email.Trim() is { Length: > 0 } email && !IsEmail(email) ? Strings.CertificateSpec_TheEmailAddressIsNotValid : null);
    }

    /// <summary>Returns what is wrong with organisation, unit, locality, state and country, or null.</summary>
    public static string? CheckFields(string organization, string unit, string locality, string state, string country)
    {
        foreach (var (value, label) in new[] { (organization, Strings.CertificateSpec_Organisation), (unit, Strings.CertificateSpec_OrganisationalUnit), (locality, Strings.CertificateSpec_Locality), (state, Strings.CertificateSpec_State) })
        {
            if (value.Trim().Length > MaxFieldLength)
                return string.Format(Strings.CertificateSpec_TheMustNotBeLongerThan, label, MaxFieldLength);
        }

        var code = country.Trim();
        if (code.Length > 0 && (code.Length != 2 || !code.All(char.IsAsciiLetter)))
            return Strings.CertificateSpec_TheCountryMustBeATwo;
        return null;
    }

    /// <summary>
    /// Splits the alternative names into host names, IP addresses and email addresses. Returns
    /// what is wrong, or null.
    /// </summary>
    public static string? ParseAlternativeNames(string text, out List<AlternativeName> names)
    {
        names = [];
        var entries = text.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (IPAddress.TryParse(entry, out var address) && (address.AddressFamily == AddressFamily.InterNetworkV6 || entry.Count(c => c == '.') == 3))
                names.Add(new AlternativeName(AlternativeNameType.IpAddress, address.ToString()));
            else if (entry.Contains('@'))
            {
                if (!IsEmail(entry))
                    return string.Format(Strings.CertificateSpec_IsNotAValidEmailAddress, entry);
                names.Add(new AlternativeName(AlternativeNameType.Email, entry));
            }
            else if (IsHostName(entry))
                names.Add(new AlternativeName(AlternativeNameType.Dns, entry.ToLowerInvariant()));
            else
                return string.Format(Strings.CertificateSpec_IsNotAHostNameIP, entry);
        }

        return null;
    }

    private static bool IsEmail(string text)
    {
        var at = text.IndexOf('@');
        return at > 0 && at == text.LastIndexOf('@') && IsHostName(text[(at + 1)..]) && !text[..at].Any(char.IsWhiteSpace);
    }

    // A wildcard is allowed as the whole of the first label only, as in *.example.com.
    private static bool IsHostName(string text)
    {
        if (text.Length is 0 or > 253)
            return false;

        var labels = text.TrimEnd('.').Split('.');
        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (i == 0 && label == "*" && labels.Length > 2)
                continue;
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-')
                return false;
            if (!label.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return false;
        }

        return true;
    }
}

public enum AlternativeNameType
{
    Dns,
    IpAddress,
    Email,
}

/// <summary>One subject alternative name: a host name, an IP address or an email address.</summary>
public sealed record AlternativeName(AlternativeNameType Type, string Value)
{
    public override string ToString() => Type switch
    {
        AlternativeNameType.IpAddress => string.Format(Strings.AlternativeName_IP, Value),
        AlternativeNameType.Email => string.Format(Strings.AlternativeName_Email, Value),
        _ => Value,
    };
}
