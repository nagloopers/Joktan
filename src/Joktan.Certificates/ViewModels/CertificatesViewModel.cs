using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Certificates.Resources;
using Joktan.Core.ViewModels;

namespace Joktan.Certificates.ViewModels;

/// <summary>
/// Dev Tools / Certificates: certificate authorities and the server and client
/// certificates they sign, for MQTT brokers and clients. Specified in
/// docs/features/dev-tools/connectivity/certificates/spec.md.
/// </summary>
public partial class CertificatesViewModel : PageViewModel
{
    private readonly CertificateStore _store;
    private readonly Func<StoredCertificate, List<string>> _usedBy;

    /// <param name="usedBy">Names the MQTT clients that use a certificate.</param>
    public CertificatesViewModel(CertificateStore store, Func<StoredCertificate, List<string>> usedBy)
        : base(Strings.Certificates_Certificates, Strings.Certificates_DevToolsCertificates)
    {
        _store = store;
        _usedBy = usedBy;
        Refresh();
    }

    /// <summary>The root CAs, each with what it signed below it.</summary>
    public ObservableCollection<CertificateNodeViewModel> Roots { get; } = [];

    public string IndexPath => _store.IndexPath;

    public bool IsEmpty => Roots.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(DeleteText))]
    [NotifyCanExecuteChangedFor(nameof(IssueCommand), nameof(ExportCommand), nameof(DeleteCommand))]
    public partial CertificateNodeViewModel? Selected { get; set; }

    public bool HasSelection => Selected is not null;

    /// <summary>What happened last, such as where files were exported to.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    [ObservableProperty]
    public partial string RenameText { get; set; } = "";

    public string DeleteText
    {
        get
        {
            if (Selected is null)
                return "";
            var count = _store.WithDescendants(Selected.Entry).Count - 1;
            var below = count switch
            {
                0 => "",
                1 => Strings.Certificates_AndThe1CertificateItSigned,
                _ => string.Format(Strings.Certificates_AndTheCertificatesBelowIt, count),
            };
            return string.Format(Strings.Certificates_DeleteWithTheirPrivateKeysThis, Selected.Name, below);
        }
    }

    /// <summary>Shows the new certificate editor and returns whether a certificate was made. Set by the view.</summary>
    public Func<NewCertificateViewModel, Task<bool>>? EditNewCertificate { get; set; }

    /// <summary>Shows the export editor and returns whether files were written. Set by the view.</summary>
    public Func<ExportCertificateViewModel, Task<bool>>? EditExport { get; set; }

    /// <summary>Rebuilds the tree from the store, keeping the selection and what was collapsed.</summary>
    public void Refresh()
    {
        var selectedId = Selected?.Entry.Id;
        var collapsed = Roots.SelectMany(r => r.SelfAndDescendants()).Where(n => !n.IsExpanded).Select(n => n.Entry.Id).ToHashSet();

        var nodes = _store.Entries.ToDictionary(
            e => e.Id,
            e => new CertificateNodeViewModel(e, _store.Find(e.IssuerId), _usedBy(e))
            {
                IsExpanded = !collapsed.Contains(e.Id),
            });

        Roots.Clear();
        foreach (var entry in _store.Entries)
        {
            // A certificate whose CA is gone is shown at the top, with its problem.
            if (entry.IssuerId is { } issuerId && nodes.TryGetValue(issuerId, out var parent) && issuerId != entry.Id)
                parent.Children.Add(nodes[entry.Id]);
            else
                Roots.Add(nodes[entry.Id]);
        }

        Selected = selectedId is not null && nodes.TryGetValue(selectedId, out var node) ? node : null;
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnSelectedChanged(CertificateNodeViewModel? value)
    {
        IsConfirmingDelete = false;
        RenameText = value?.Name ?? "";
    }

    [RelayCommand]
    private async Task NewAuthorityAsync()
    {
        if (EditNewCertificate is null)
            return;

        var editor = new NewCertificateViewModel(_store, null);
        if (await EditNewCertificate(editor) && editor.Created is { } created)
            Show(created, string.Format(Strings.Certificates_MadeTheRootCA, created.DisplayName));
    }

    private bool CanIssue => Selected?.IsAuthority == true;

    [RelayCommand(CanExecute = nameof(CanIssue))]
    private async Task IssueAsync(CertificateKind? kind)
    {
        if (EditNewCertificate is null || Selected is null)
            return;

        if (!Selected.Entry.CanIssue)
        {
            Error = CertificateStore.WhyCannotIssue(Selected.Entry);
            return;
        }

        var editor = new NewCertificateViewModel(_store, Selected.Entry, kind);
        if (await EditNewCertificate(editor) && editor.Created is { } created)
            Show(created, string.Format(Strings.Certificates_MadeSignedBy, created.DisplayName, Selected?.Name));
    }

    private bool CanExport => Selected?.Entry.Certificate is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (EditExport is null || Selected is null)
            return;

        var editor = new ExportCertificateViewModel(_store, Selected.Entry);
        if (await EditExport(editor))
        {
            Error = null;
            Message = string.Format(Strings.Certificates_WroteTo, string.Join(", ", editor.Written.Select(Path.GetFileName)), Path.GetDirectoryName(editor.Written[0]));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private void ConfirmDelete()
    {
        if (Selected is null)
            return;

        var removed = _store.Delete(Selected.Entry);
        Selected = null;
        Refresh();
        Error = null;
        Message = removed.Count == 1 ? string.Format(Strings.Certificates_Deleted, removed[0].DisplayName) : string.Format(Strings.Certificates_DeletedAndMore, removed[0].DisplayName, removed.Count - 1);
    }

    [RelayCommand]
    private void Rename()
    {
        if (Selected is null || RenameText.Trim() == Selected.Name)
            return;

        try
        {
            _store.Rename(Selected.Entry, RenameText);
            Error = null;
            Refresh();
        }
        catch (FormatException e)
        {
            Error = e.Message;
        }
    }

    private void Show(StoredCertificate created, string message)
    {
        Refresh();
        Selected = Roots.SelectMany(r => r.SelfAndDescendants()).FirstOrDefault(n => n.Entry == created);
        if (Selected is not null)
        {
            // Every CA above it is opened, so it can be seen.
            foreach (var node in Roots.SelectMany(r => r.SelfAndDescendants()).Where(n => n.Children.Contains(Selected) || IsAbove(n, Selected)))
                node.IsExpanded = true;
        }

        Error = null;
        Message = message;
    }

    private static bool IsAbove(CertificateNodeViewModel node, CertificateNodeViewModel target)
        => node.Children.Any(c => c == target || IsAbove(c, target));

    /// <summary>The CAs and client certificates that an MQTT client can use, for its settings.</summary>
    public static (List<CertificateChoice> Authorities, List<CertificateChoice> Clients) ChoicesFor(CertificateStore store)
    {
        var usable = store.Entries.Where(e => e.Certificate is not null && e.Problem is null).ToList();
        var authorities = usable.Where(e => e.Kind.IsAuthority())
            .Select(e => new CertificateChoice(e.Id, e.DisplayName, e.AuthorityPath, "", store.Find(e.IssuerId)?.DisplayName))
            .ToList();
        var clients = usable.Where(e => e.Kind == CertificateKind.Client)
            .Select(e => store.Find(e.IssuerId) is { } issuer
                ? new CertificateChoice(e.Id, e.DisplayName, e.CertificatePath, e.KeyPath, issuer.DisplayName, issuer.AuthorityPath)
                : new CertificateChoice(e.Id, e.DisplayName, e.CertificatePath, e.KeyPath, null, e.AuthorityPath))
            .ToList();
        return (authorities, clients);
    }
}

/// <summary>
/// A CA or client certificate from the Certificates page, offered in a client's TLS settings.
/// For a client certificate, <see cref="AuthorityPath"/> is the CA file of the CA that signed it.
/// </summary>
public sealed record CertificateChoice(string Id, string Name, string Path, string KeyPath, string? IssuerName, string AuthorityPath = "")
{
    public override string ToString() => IssuerName is null ? Name : string.Format(Strings.CertificateChoice_SignedBy, Name, IssuerName);
}
