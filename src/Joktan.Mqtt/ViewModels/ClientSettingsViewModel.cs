using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Certificates;
using Joktan.Certificates.ViewModels;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Controls;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>Which certificate file the editor is asking the user for.</summary>
public enum CertificateFileKind
{
    Authority,
    Client,
    Key,
}

/// <summary>
/// Editor for one MQTT client: the broker, the login, and TLS with its certificates.
/// </summary>
public partial class ClientSettingsViewModel : ViewModelBase
{
    private const string PlainPort = "1883";
    private const string TlsPort = "8883";

    private readonly IReadOnlyList<string> _otherNames;
    private readonly CertificateStore? _certificates;

    // The copies a linked client already uses, which still count as the certificate they came from.
    private readonly string _copiedAuthority;
    private readonly string _copiedClient;
    private bool _loading = true;

    /// <param name="otherNames">The names of the other clients, which this one may not take.</param>
    /// <param name="certificates">The certificates made on the Certificates page, offered for TLS; null for none.</param>
    public ClientSettingsViewModel(ClientProfile profile, string password, string keyPassword, IReadOnlyList<string> otherNames, bool isNew,
        CertificateStore? certificates = null)
    {
        _otherNames = otherNames;
        _certificates = certificates;
        if (certificates is not null)
            (StoreAuthorities, StoreClients) = CertificatesViewModel.ChoicesFor(certificates);
        IsNew = isNew;
        Name = profile.Name;
        Host = profile.Host;
        Port = profile.Port;
        Username = profile.Username;
        Password = password;
        ClientId = profile.ClientId;
        UseTls = profile.UseTls;
        CaCertificate = profile.CaCertificate;
        ClientCertificate = profile.ClientCertificate;
        ClientKey = profile.ClientKey;
        KeyPassword = keyPassword;
        AcceptAnyServerCertificate = profile.AcceptAnyServerCertificate;
        TabText = profile.TabText;
        TabFill = profile.TabFill;
        PageBackground = profile.PageBackground;
        if (StoreAuthorities.FirstOrDefault(c => c.Id == profile.CaCertificateId) is { } authority)
        {
            SelectedStoreAuthority = authority;
            _copiedAuthority = profile.CaCertificate;
        }

        if (StoreClients.FirstOrDefault(c => c.Id == profile.ClientCertificateId) is { } client)
        {
            SelectedStoreClient = client;
            _copiedClient = profile.ClientCertificate;
        }

        _copiedAuthority ??= "";
        _copiedClient ??= "";
        _loading = false;
        Revalidate();
    }

    public bool IsNew { get; }

    public string Title => IsNew ? Strings.ClientSettings_NewClient : string.Format(Strings.ClientSettings_Client, Name);

    /// <summary>Asks the user for a certificate file. Set by the window, which owns the system dialog.</summary>
    public Func<CertificateFileKind, Task<string?>>? PickFile { get; set; }

    /// <summary>Raised when the editor should close; true if the settings were saved.</summary>
    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Host { get; set; } = "";

    [ObservableProperty]
    public partial string Port { get; set; } = PlainPort;

    [ObservableProperty]
    public partial string Username { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string ClientId { get; set; } = "";

    [ObservableProperty]
    public partial bool UseTls { get; set; }

    [ObservableProperty]
    public partial string CaCertificate { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeyFile))]
    public partial string ClientCertificate { get; set; } = "";

    [ObservableProperty]
    public partial string ClientKey { get; set; } = "";

    [ObservableProperty]
    public partial string KeyPassword { get; set; } = "";

    [ObservableProperty]
    public partial bool AcceptAnyServerCertificate { get; set; }

    /// <summary>The CAs made on the Certificates page.</summary>
    public IReadOnlyList<CertificateChoice> StoreAuthorities { get; } = [];

    /// <summary>The client certificates made on the Certificates page.</summary>
    public IReadOnlyList<CertificateChoice> StoreClients { get; } = [];

    public bool HasStoreAuthorities => StoreAuthorities.Count > 0;

    public bool HasStoreClients => StoreClients.Count > 0;

    /// <summary>The CA from the Certificates page that the CA certificate file belongs to, if any.</summary>
    [ObservableProperty]
    public partial CertificateChoice? SelectedStoreAuthority { get; set; }

    /// <summary>The client certificate from the Certificates page in use, if any.</summary>
    [ObservableProperty]
    public partial CertificateChoice? SelectedStoreClient { get; set; }

    /// <summary>The colours offered for the tab and page.</summary>
    public IReadOnlyList<Swatch> Palette => TabColors.Palette;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewTextBrush))]
    public partial string TabText { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewTextBrush), nameof(PreviewFillBrush))]
    public partial string TabFill { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewPageBrush))]
    public partial string PageBackground { get; set; } = "";

    public IBrush? PreviewTextBrush => TabColors.TextBrush(TabText, TabFill);

    public object PreviewFillBrush => TabColors.BrushOrUnset(TabFill);

    public object PreviewPageBrush => TabColors.BrushOrUnset(PageBackground);

    /// <summary>A PFX/P12 file holds its own key, so the key file is only asked for with PEM.</summary>
    public bool ShowKeyFile => !CertificateLoader.IsPkcs12(ClientCertificate.Trim());

    /// <summary>What the chosen certificate files hold, for example who the client certificate names.</summary>
    [ObservableProperty]
    public partial string CertificateInfo { get; set; } = "";

    /// <summary>Why a certificate file cannot be used. It does not stop the settings being saved.</summary>
    [ObservableProperty]
    public partial string? CertificateProblem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? Error { get; set; }

    private bool CanSave => Error is null;

    public static bool IsValidHost(string host) => host.Trim() is { Length: > 0 } h && !h.Any(char.IsWhiteSpace);

    public static int? ParsePort(string port)
        => int.TryParse(port.Trim(), out var value) && value is >= 1 and <= 65535 ? value : null;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is not (nameof(Error) or nameof(CertificateInfo) or nameof(CertificateProblem) or nameof(SelectedStoreAuthority) or nameof(SelectedStoreClient)))
            Revalidate();
    }

    // The usual port follows TLS being switched on or off, unless another one was typed in.
    partial void OnUseTlsChanged(bool value)
    {
        if (_loading)
            return;
        if (value && Port.Trim() == PlainPort)
            Port = TlsPort;
        else if (!value && Port.Trim() == TlsPort)
            Port = PlainPort;
    }

    partial void OnSelectedStoreAuthorityChanged(CertificateChoice? value)
    {
        if (!_loading && value is not null)
            CaCertificate = value.Path;
    }

    // A client certificate brings its key, and the CAs that signed it when no CA was chosen yet.
    partial void OnSelectedStoreClientChanged(CertificateChoice? value)
    {
        if (_loading || value is null)
            return;

        ClientCertificate = value.Path;
        ClientKey = value.KeyPath;
        KeyPassword = "";
        if (CaCertificate.Trim().Length == 0 && value.AuthorityPath.Length > 0)
            CaCertificate = value.AuthorityPath;
    }

    // The lists show the certificate from the Certificates page in use, if any; a file chosen
    // another way is not one of theirs.
    partial void OnCaCertificateChanged(string value)
    {
        if (_loading || SelectedStoreAuthority is { } chosen && (SamePath(chosen.Path, value) || SamePath(_copiedAuthority, value)))
            return;
        SelectedStoreAuthority = StoreAuthorities.FirstOrDefault(c => SamePath(c.Path, value));
    }

    partial void OnClientCertificateChanged(string value)
    {
        if (_loading || SelectedStoreClient is { } chosen && (SamePath(chosen.Path, value) || SamePath(_copiedClient, value)))
            return;
        SelectedStoreClient = StoreClients.FirstOrDefault(c => SamePath(c.Path, value));
    }

    private static bool SamePath(string a, string b) => a.Trim().Length > 0 && string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);

    // An empty value, from Default, goes back to the theme's colour.
    [RelayCommand]
    private void PickTabText(string hex) => TabText = hex;

    [RelayCommand]
    private void PickTabFill(string hex) => TabFill = hex;

    [RelayCommand]
    private void PickPageBackground(string hex) => PageBackground = hex;

    [RelayCommand]
    private async Task BrowseAsync(CertificateFileKind kind)
    {
        if (PickFile is null || await PickFile(kind) is not { } path)
            return;

        switch (kind)
        {
            case CertificateFileKind.Authority:
                CaCertificate = path;
                break;
            case CertificateFileKind.Client:
                ClientCertificate = path;
                break;
            case CertificateFileKind.Key:
                ClientKey = path;
                break;
        }
    }

    [RelayCommand]
    private void ClearFile(CertificateFileKind kind)
    {
        switch (kind)
        {
            case CertificateFileKind.Authority:
                CaCertificate = "";
                break;
            case CertificateFileKind.Client:
                ClientCertificate = "";
                ClientKey = "";
                KeyPassword = "";
                break;
            case CertificateFileKind.Key:
                ClientKey = "";
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!CopyCertificates())
            return;
        CloseRequested?.Invoke(true);
    }

    // Certificates picked from the Certificates page are used from a copy in the client's own
    // folder, so the client never reads the folders that hold the CAs' keys.
    private bool CopyCertificates()
    {
        if (_certificates is null || !UseTls || (SelectedStoreAuthority is null && SelectedStoreClient is null))
            return true;

        try
        {
            var (authority, certificate, key) = _certificates.CopyForClient(Name.Trim(),
                _certificates.Find(SelectedStoreAuthority?.Id), _certificates.Find(SelectedStoreClient?.Id));
            _loading = true;
            if (authority is not null)
                CaCertificate = authority;
            if (certificate is not null)
            {
                ClientCertificate = certificate;
                ClientKey = key!;
            }
        }
        catch (FormatException e)
        {
            Error = e.Message;
            return false;
        }
        finally
        {
            _loading = false;
        }

        return true;
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    /// <summary>The settings as entered. Call only after the editor was saved.</summary>
    public ClientProfile ToProfile() => new()
    {
        Name = Name.Trim(),
        Host = Host.Trim(),
        Port = Port.Trim(),
        Username = Username.Trim(),
        ClientId = ClientId.Trim() is { Length: > 0 } id ? id : ClientProfile.NewClientId(),
        UseTls = UseTls,
        CaCertificate = CaCertificate.Trim(),
        ClientCertificate = ClientCertificate.Trim(),
        ClientKey = ShowKeyFile ? ClientKey.Trim() : "",
        AcceptAnyServerCertificate = AcceptAnyServerCertificate,
        TabText = TabColors.Normalize(TabText),
        TabFill = TabColors.Normalize(TabFill),
        PageBackground = TabColors.Normalize(PageBackground),
        CaCertificateId = UseTls ? SelectedStoreAuthority?.Id : null,
        ClientCertificateId = UseTls ? SelectedStoreClient?.Id : null,
    };

    private void Revalidate()
    {
        Error = ProfileError();
        CheckCertificates();
    }

    private string? ProfileError()
    {
        if (ClientProfile.CheckName(Name, _otherNames) is { } nameError)
            return nameError;
        if (!IsValidHost(Host))
            return Strings.ClientSettings_EnterTheBrokerSHostName;
        if (ParsePort(Port) is null)
            return Strings.ClientSettings_ThePortMustBeAWhole;
        if (Password.Length > 0 && Username.Trim().Length == 0)
            return Strings.ClientSettings_APasswordIsOnlySentWith;
        if (ClientId.Trim().Any(char.IsWhiteSpace))
            return Strings.ClientSettings_TheClientIDMustNotContain;
        if (!TabColors.IsValid(TabText) || !TabColors.IsValid(TabFill) || !TabColors.IsValid(PageBackground))
            return Strings.ClientSettings_AColourMustBeLike3BB001;
        if (UseTls && ShowKeyFile && ClientKey.Trim().Length > 0 && ClientCertificate.Trim().Length == 0)
            return Strings.ClientSettings_ChooseTheClientCertificateThatGoes;
        return null;
    }

    // Reads the files now, so a wrong file or password shows up before connecting.
    private void CheckCertificates()
    {
        var info = new List<string>();
        CertificateProblem = null;
        if (!UseTls)
        {
            CertificateInfo = "";
            return;
        }

        try
        {
            if (CaCertificate.Trim().Length > 0)
            {
                var authorities = CertificateLoader.LoadAuthorities(CaCertificate);
                info.Add(authorities.Count == 1
                    ? string.Format(Strings.ClientSettings_CA, authorities[0].GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false))
                    : string.Format(Strings.ClientSettings_CACertificates, authorities.Count));
            }

            if (ClientCertificate.Trim().Length > 0)
            {
                using var client = CertificateLoader.LoadClient(ClientCertificate, ShowKeyFile ? ClientKey : null, KeyPassword);
                var subject = client.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);
                info.Add(string.Format(Strings.ClientSettings_ClientCertificateValidUntil, subject, client.NotAfter));
            }
        }
        catch (FormatException e)
        {
            CertificateProblem = e.Message;
        }

        CertificateInfo = string.Join(" · ", info);
    }
}
