using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;

namespace Joktan.Certificates.ViewModels;

/// <summary>Settings / Certificate Defaults: what new certificates are filled in with. Saved as soon as they are valid.</summary>
public partial class CertificateDefaultsViewModel : PageViewModel
{
    private const int MaxValidityDays = 36500;

    private readonly CertificateStore _store;
    private bool _loading;
    private bool _saving;

    public CertificateDefaultsViewModel(CertificateStore store) : base("Certificate Defaults", "Settings / Certificate Defaults")
    {
        _store = store;
        KeyAlgorithms = System.Enum.GetValues<KeyAlgorithm>().Select(a => new Choice<KeyAlgorithm>(a, a.Text())).ToList();
        Load(store.Defaults);

        // Folders and Backup changes the folder too. Without this, the next edit here would put the old one back.
        store.DefaultsChanged += () =>
        {
            if (_saving)
                return;
            Load(store.Defaults);
            Error = null;
        };
    }

    public IReadOnlyList<Choice<KeyAlgorithm>> KeyAlgorithms { get; }

    /// <summary>Where MQTT clients' certificates go when no folder is entered.</summary>
    public string DefaultClientFolder => _store.DefaultClientFolder;

    /// <summary>Asks the user for a folder. Set by the view, which owns the system dialog.</summary>
    public System.Func<Task<string?>>? PickFolder { get; set; }

    [ObservableProperty]
    public partial string ClientFolder { get; set; } = "";

    [ObservableProperty]
    public partial string RootCommonName { get; set; } = "";

    [ObservableProperty]
    public partial string IntermediateCommonName { get; set; } = "";

    [ObservableProperty]
    public partial string ServerCommonName { get; set; } = "";

    [ObservableProperty]
    public partial string ServerAlternativeNames { get; set; } = "";

    [ObservableProperty]
    public partial string ClientCommonName { get; set; } = "";

    [ObservableProperty]
    public partial string Organization { get; set; } = "";

    [ObservableProperty]
    public partial string OrganizationalUnit { get; set; } = "";

    [ObservableProperty]
    public partial string Locality { get; set; } = "";

    [ObservableProperty]
    public partial string State { get; set; } = "";

    [ObservableProperty]
    public partial string Country { get; set; } = "";

    [ObservableProperty]
    public partial Choice<KeyAlgorithm>? SelectedKeyAlgorithm { get; set; }

    [ObservableProperty]
    public partial string AuthorityValidityDays { get; set; } = "";

    [ObservableProperty]
    public partial string CertificateValidityDays { get; set; } = "";

    /// <summary>Why the defaults are not saved, or null.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName == nameof(Error))
            return;

        Error = Check();
        if (Error is null)
            Save();
    }

    [RelayCommand]
    private async Task BrowseClientFolderAsync()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
            ClientFolder = folder;
    }

    [RelayCommand]
    private void Reset()
    {
        Load(new CertificateDefaults());
        Error = null;
        Save();
    }

    // What is being typed stays as it is, and is not replaced by the trimmed value saved.
    private void Save()
    {
        _saving = true;
        _store.SaveDefaults(ToDefaults());
        _saving = false;
    }

    private void Load(CertificateDefaults defaults)
    {
        _loading = true;
        RootCommonName = defaults.RootCommonName;
        IntermediateCommonName = defaults.IntermediateCommonName;
        ServerCommonName = defaults.ServerCommonName;
        ServerAlternativeNames = defaults.ServerAlternativeNames;
        ClientCommonName = defaults.ClientCommonName;
        Organization = defaults.Organization;
        OrganizationalUnit = defaults.OrganizationalUnit;
        Locality = defaults.Locality;
        State = defaults.State;
        Country = defaults.Country;
        SelectedKeyAlgorithm = KeyAlgorithms.FirstOrDefault(a => a.Value == defaults.KeyAlgorithm) ?? KeyAlgorithms[0];
        AuthorityValidityDays = defaults.AuthorityValidityDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
        CertificateValidityDays = defaults.CertificateValidityDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ClientFolder = defaults.ClientFolder;
        _loading = false;
    }

    private CertificateDefaults ToDefaults() => new()
    {
        RootCommonName = RootCommonName.Trim(),
        IntermediateCommonName = IntermediateCommonName.Trim(),
        ServerCommonName = ServerCommonName.Trim(),
        ServerAlternativeNames = ServerAlternativeNames.Trim(),
        ClientCommonName = ClientCommonName.Trim(),
        Organization = Organization.Trim(),
        OrganizationalUnit = OrganizationalUnit.Trim(),
        Locality = Locality.Trim(),
        State = State.Trim(),
        Country = Country.Trim().ToUpperInvariant(),
        KeyAlgorithm = SelectedKeyAlgorithm?.Value ?? KeyAlgorithm.EcdsaP256,
        AuthorityValidityDays = ParseDays(AuthorityValidityDays) ?? 3650,
        CertificateValidityDays = ParseDays(CertificateValidityDays) ?? 825,
        ClientFolder = ClientFolder.Trim(),
    };

    private string? Check()
    {
        foreach (var (name, label) in new[]
                 {
                     (RootCommonName, "root CA"), (IntermediateCommonName, "intermediate CA"),
                     (ServerCommonName, "server"), (ClientCommonName, "client"),
                 })
        {
            if (name.Trim().Length > 64)
                return $"The {label} common name must not be longer than 64 characters";
        }

        if (CertificateSpec.CheckFields(Organization, OrganizationalUnit, Locality, State, Country) is { } fieldError)
            return fieldError;
        if (CertificateSpec.ParseAlternativeNames(ServerAlternativeNames, out _) is { } namesError)
            return namesError;
        if (CertificateStore.CheckFolder(ClientFolder) is { } folderError)
            return $"MQTT clients' folder: {folderError}";
        if (ParseDays(AuthorityValidityDays) is null || ParseDays(CertificateValidityDays) is null)
            return $"The number of days must be a whole number from 1 to {MaxValidityDays}";
        return null;
    }

    private static int? ParseDays(string text)
        => int.TryParse(text.Trim(), out var days) && days is >= 1 and <= MaxValidityDays ? days : null;
}
