using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Certificates.Resources;
using Joktan.Core.ViewModels;

namespace Joktan.Certificates.ViewModels;

/// <summary>An item in a drop-down list: a value and what it is called.</summary>
public sealed record Choice<T>(T Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>
/// Editor for a new certificate: a root CA, or a certificate signed by a CA in the store. It is
/// filled in from the defaults and from the CA that signs it.
/// </summary>
public partial class NewCertificateViewModel : ViewModelBase
{
    private readonly CertificateStore _store;
    private readonly StoredCertificate? _issuer;
    private readonly CertificateDefaults _defaults;
    private bool _loading = true;

    public NewCertificateViewModel(CertificateStore store, StoredCertificate? issuer, CertificateKind? kind = null)
    {
        _store = store;
        _issuer = issuer;
        _defaults = store.Defaults;

        Kinds = issuer is null
            ? [Option(CertificateKind.RootAuthority)]
            : [Option(CertificateKind.Server), Option(CertificateKind.Client), Option(CertificateKind.IntermediateAuthority)];
        KeyAlgorithms = Enum.GetValues<KeyAlgorithm>().Select(a => new Choice<KeyAlgorithm>(a, a.Text())).ToList();

        var issuerSubject = issuer?.Certificate?.SubjectName;
        Organization = Part(issuerSubject, "2.5.4.10") ?? _defaults.Organization;
        OrganizationalUnit = Part(issuerSubject, "2.5.4.11") ?? _defaults.OrganizationalUnit;
        Locality = Part(issuerSubject, "2.5.4.7") ?? _defaults.Locality;
        State = Part(issuerSubject, "2.5.4.8") ?? _defaults.State;
        Country = Part(issuerSubject, "2.5.4.6") ?? _defaults.Country;
        SelectedKeyAlgorithm = KeyAlgorithms.First(a => a.Value == _defaults.KeyAlgorithm);

        // A root CA goes where the last one did; a certificate goes beside the CA that signs it.
        Folder = issuer is null ? store.AuthorityFolder : CertificateStore.ParentOf(issuer);

        SelectedKind = Kinds.FirstOrDefault(k => k.Value == kind) ?? Kinds[0];
        ApplyKindDefaults(SelectedKind.Value);
        _loading = false;
        Revalidate();
    }

    public string Title => _issuer is null ? Strings.NewCertificate_NewRootCA : string.Format(Strings.NewCertificate_NewCertificateSignedBy, _issuer.DisplayName);

    public string IssuerText => _issuer is null
        ? Strings.NewCertificate_ItselfARootCAIsThe
        : string.Format(Strings.NewCertificate_ValidUntil, _issuer.DisplayName, _issuer.Kind.Text(), _issuer.Certificate?.NotAfter);

    public IReadOnlyList<Choice<CertificateKind>> Kinds { get; }

    public bool CanChooseKind => Kinds.Count > 1;

    public IReadOnlyList<Choice<KeyAlgorithm>> KeyAlgorithms { get; }

    /// <summary>Asks the user for a folder. Set by the window, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>The certificate that was made, once saved.</summary>
    public StoredCertificate? Created { get; private set; }

    /// <summary>Raised when the editor should close; true if a certificate was made.</summary>
    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAlternativeNames), nameof(AlternativeNamesHint), nameof(KindHint))]
    public partial Choice<CertificateKind> SelectedKind { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string CommonName { get; set; } = "";

    /// <summary>The folder the certificate's own folder is made in.</summary>
    [ObservableProperty]
    public partial string Folder { get; set; } = "";

    /// <summary>Where the files will be written.</summary>
    [ObservableProperty]
    public partial string FolderPreview { get; set; } = "";

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
    public partial string Email { get; set; } = "";

    [ObservableProperty]
    public partial string AlternativeNames { get; set; } = "";

    [ObservableProperty]
    public partial Choice<KeyAlgorithm> SelectedKeyAlgorithm { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidityText))]
    public partial DateTime? ValidUntil { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? Error { get; set; }

    /// <summary>The first day it can expire on: tomorrow.</summary>
    public DateTime FirstDate => DateTime.Today.AddDays(1);

    /// <summary>The last day it can expire on: the day its CA does.</summary>
    public DateTime? LastDate => _issuer?.Certificate?.NotAfter.Date;

    public string ValidityText
    {
        get
        {
            if (ValidUntil is not { } until)
                return "";
            var days = (until.Date - DateTime.Today).Days;
            var text = days == 1 ? Strings.NewCertificate_1DayFromToday : string.Format(Strings.NewCertificate_DaysFromToday, days);
            return LastDate is { } last && until.Date == last ? string.Format(Strings.NewCertificate_TheDayExpires, text, _issuer!.DisplayName) : text;
        }
    }

    public bool ShowAlternativeNames => !SelectedKind.Value.IsAuthority();

    public string AlternativeNamesHint => SelectedKind.Value == CertificateKind.Server
        ? Strings.NewCertificate_TheHostNamesAndIPAddresses
        : Strings.NewCertificate_OptionalHostNamesIPAddressesOr;

    public string KindHint => SelectedKind.Value switch
    {
        CertificateKind.RootAuthority => Strings.NewCertificate_SignsItselfAndThenTheCertificates,
        CertificateKind.IntermediateAuthority => Strings.NewCertificate_ACASignedByThisOne,
        CertificateKind.Server => Strings.NewCertificate_TheCertificateABrokerShowsTo,
        _ => Strings.NewCertificate_TheCertificateAClientShowsTo,
    };

    private bool CanSave => Error is null;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is not (nameof(Error) or nameof(ValidityText) or nameof(FolderPreview)))
            Revalidate();
    }

    // The common name, alternative names and expiry follow the kind, unless they were changed.
    partial void OnSelectedKindChanged(Choice<CertificateKind> oldValue, Choice<CertificateKind> newValue)
    {
        if (_loading || oldValue is null)
            return;

        var old = oldValue.Value;
        if (CommonName.Trim().Length == 0 || CommonName == _defaults.CommonNameFor(old))
            CommonName = _defaults.CommonNameFor(newValue.Value);
        if (AlternativeNames.Trim().Length == 0 || AlternativeNames == AlternativeNamesFor(old))
            AlternativeNames = AlternativeNamesFor(newValue.Value);
        if (ValidUntil is null || ValidUntil == DefaultExpiry(old))
            ValidUntil = DefaultExpiry(newValue.Value);
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
            Folder = folder;
    }

    [RelayCommand]
    private void ValidFor(string years)
    {
        var until = DateTime.Today.AddYears(int.Parse(years, System.Globalization.CultureInfo.InvariantCulture));
        ValidUntil = LastDate is { } last && until > last ? last : until;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        Revalidate();
        if (Error is not null)
            return;

        try
        {
            Created = _store.Create(ToSpec());
        }
        catch (FormatException e)
        {
            Error = e.Message;
            return;
        }

        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    private void ApplyKindDefaults(CertificateKind kind)
    {
        CommonName = _defaults.CommonNameFor(kind);
        AlternativeNames = AlternativeNamesFor(kind);
        ValidUntil = DefaultExpiry(kind);
    }

    private string AlternativeNamesFor(CertificateKind kind)
        => kind == CertificateKind.Server ? _defaults.ServerAlternativeNames : "";

    private DateTime DefaultExpiry(CertificateKind kind)
    {
        var until = DateTime.Today.AddDays(Math.Max(1, _defaults.ValidityDaysFor(kind)));
        return LastDate is { } last && until > last ? last : until;
    }

    private CertificateSpec ToSpec() => new()
    {
        Kind = SelectedKind.Value,
        Name = Name.Trim().Length > 0 ? Name.Trim() : _store.UnusedName(CommonName),
        IssuerId = _issuer?.Id,
        CommonName = CommonName.Trim(),
        Organization = Organization.Trim(),
        OrganizationalUnit = OrganizationalUnit.Trim(),
        Locality = Locality.Trim(),
        State = State.Trim(),
        Country = Country.Trim().ToUpperInvariant(),
        Email = Email.Trim(),
        AlternativeNames = ShowAlternativeNames ? AlternativeNames : "",
        KeyAlgorithm = SelectedKeyAlgorithm.Value,
        Folder = Folder.Trim(),

        // The end of the chosen day, as the user's clock reads it.
        NotAfter = new DateTimeOffset((ValidUntil ?? FirstDate).Date.AddDays(1).AddSeconds(-1)),
    };

    private void Revalidate()
    {
        Error = Check();
        FolderPreview = CertificateStore.CheckFolder(Folder) is null
            ? string.Format(Strings.NewCertificate_ItsFilesGoIn, _store.FolderFor(Folder, ToSpec().Name))
            : "";
    }

    private string? Check()
    {
        if (_issuer is not null && !_issuer.CanIssue)
            return CertificateStore.WhyCannotIssue(_issuer);
        if (Name.Trim().Length > 0 && CertificateSpec.CheckName(Name, _store.Entries.Select(e => e.Name)) is { } nameError)
            return nameError;
        if (CertificateStore.CheckFolder(Folder) is { } folderError)
            return folderError;

        var spec = ToSpec();
        if (spec.CheckSubject() is { } subjectError)
            return subjectError;
        if (CertificateSpec.ParseAlternativeNames(spec.AlternativeNames, out var names) is { } namesError)
            return namesError;
        if (spec.Kind == CertificateKind.Server && names.Count == 0)
            return Strings.NewCertificate_EnterAtLeastOneHostName;
        if (ValidUntil is not { } until)
            return Strings.NewCertificate_ChooseTheExpiryDate;
        if (until.Date < FirstDate)
            return Strings.NewCertificate_TheExpiryDateMustBeAfter;
        if (LastDate is { } last && until.Date > last)
            return string.Format(Strings.NewCertificate_TheExpiryDateCannotBeAfter, last, _issuer!.DisplayName);
        return null;
    }

    private static Choice<CertificateKind> Option(CertificateKind kind) => new(kind, kind.Text());

    // One part of a CA's name, such as its organisation, to fill in a certificate it signs.
    private static string? Part(X500DistinguishedName? name, string oid)
    {
        if (name is null)
            return null;

        foreach (var part in name.EnumerateRelativeDistinguishedNames())
        {
            if (!part.HasMultipleElements && part.GetSingleElementType().Value == oid)
                return part.GetSingleElementValue();
        }

        return null;
    }
}
