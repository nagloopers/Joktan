using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt;

/// <summary>
/// One MQTT client the user has set up: the broker it connects to and how, and what its tab on the
/// MQTT page and in the topic explorer remembers. Passwords are not part of it; see
/// <see cref="Joktan.Core.CredentialStore"/>.
/// </summary>
public sealed class ClientProfile
{
    public const int MaxNameLength = 40;

    /// <summary>The name on the client's tab. Unique, ignoring case.</summary>
    public string Name { get; set; } = "";

    public string Host { get; set; } = "";

    public string Port { get; set; } = "1883";

    public string Username { get; set; } = "";

    public string ClientId { get; set; } = "";

    public bool UseTls { get; set; }

    /// <summary>The CA certificate the broker's certificate must chain to; empty to use the system's trusted roots.</summary>
    public string CaCertificate { get; set; } = "";

    /// <summary>The client's own certificate for mutual TLS, as PEM or PFX/P12; empty for none.</summary>
    public string ClientCertificate { get; set; } = "";

    /// <summary>The private key of a PEM client certificate, when it is not in the certificate file itself.</summary>
    public string ClientKey { get; set; } = "";

    /// <summary>The CA on the Certificates page that <see cref="CaCertificate"/> was copied from, or null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CaCertificateId { get; set; }

    /// <summary>The client certificate on the Certificates page that <see cref="ClientCertificate"/> was copied from, or null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientCertificateId { get; set; }

    /// <summary>Accepts any broker certificate, without checking who signed it or the host name. For testing only.</summary>
    public bool AcceptAnyServerCertificate { get; set; }

    /// <summary>The colour of the text on the client's tabs, as #RRGGBB; empty for the theme's.</summary>
    public string TabText { get; set; } = "";

    /// <summary>The colour the client's tabs are filled with, as #RRGGBB; empty for the theme's.</summary>
    public string TabFill { get; set; } = "";

    /// <summary>The colour behind the cards on the client's tabs, as #RRGGBB; empty for the theme's.</summary>
    public string PageBackground { get; set; } = "";

    /// <summary>The folder of messages this client sends.</summary>
    public string? Folder { get; set; }

    public string ManualTopic { get; set; } = "";

    public string ManualBody { get; set; } = "";

    public int ManualQos { get; set; }

    public bool ManualRetain { get; set; }

    /// <summary>Whether the manual message is gzipped before it is sent.</summary>
    public bool ManualGzip { get; set; }

    /// <summary>Whether the Broker card on the client's tab is collapsed.</summary>
    public bool BrokerCollapsed { get; set; }

    /// <summary>Whether the Manual message card on the client's tab is collapsed.</summary>
    public bool ManualCollapsed { get; set; }

    /// <summary>Whether the Folder messages card on the client's tab is collapsed.</summary>
    public bool FolderCollapsed { get; set; }

    /// <summary>Whether the Activity card on the client's tab is collapsed.</summary>
    public bool ActivityCollapsed { get; set; }

    /// <summary>The topic explorer's filters for this client, separated by commas.</summary>
    public string ExplorerFilters { get; set; } = "#";

    /// <summary>Whether the topic explorer is subscribed to <see cref="ExplorerFilters"/>; false after Unsubscribe.</summary>
    public bool ExplorerSubscribed { get; set; } = true;

    /// <summary>What the topic explorer draws for a topic, where the user chose; by topic, the paths in its payload.</summary>
    public Dictionary<string, List<string>> ExplorerCharts { get; set; } = new();

    public bool UsesClientCertificate => UseTls && ClientCertificate.Trim().Length > 0;

    public static string NewClientId() => $"joktan-{Guid.NewGuid():N}"[..15];

    /// <summary>Returns what is wrong with a client name, or null.</summary>
    public static string? CheckName(string name, IEnumerable<string> otherNames)
    {
        name = name.Trim();
        if (name.Length == 0)
            return Strings.ClientProfile_EnterANameForTheClient;
        if (name.Length > MaxNameLength)
            return string.Format(Strings.ClientProfile_TheNameMustNotBeLonger, MaxNameLength);
        if (!name.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-' or '.'))
            return Strings.ClientProfile_TheNameMayUseLettersDigits;
        if (otherNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            return string.Format(Strings.ClientProfile_ThereIsAlreadyAClientCalled, name);
        return null;
    }

    public ClientProfile Clone() => (ClientProfile)MemberwiseClone();
}
