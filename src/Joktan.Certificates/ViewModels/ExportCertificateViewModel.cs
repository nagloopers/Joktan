using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Certificates.Resources;
using Joktan.Core.ViewModels;

namespace Joktan.Certificates.ViewModels;

/// <summary>
/// Writes a certificate and, if asked, its key, its CAs and a PFX/P12 file into a folder, for
/// a broker or another program to use.
/// </summary>
public partial class ExportCertificateViewModel : ViewModelBase
{
    private readonly CertificateStore _store;
    private readonly StoredCertificate _entry;
    private readonly bool _hasIntermediates;
    private bool _loading = true;

    public ExportCertificateViewModel(CertificateStore store, StoredCertificate entry)
    {
        _store = store;
        _entry = entry;
        _hasIntermediates = store.IssuersOf(entry).Any(i => i.Kind == CertificateKind.IntermediateAuthority);

        Folder = store.ExportFolder;
        BaseName = entry.Kind switch
        {
            CertificateKind.Server => "server",
            CertificateKind.Client => "client",
            _ => CertificateStore.FileNameFrom(entry.DisplayName),
        };

        // A CA's key is only needed to sign certificates elsewhere, so it is not offered by default.
        IncludeKey = entry.HasKey && !entry.Kind.IsAuthority();
        IncludeAuthority = entry.Kind != CertificateKind.RootAuthority;
        IncludeChain = _hasIntermediates && entry.Kind == CertificateKind.Server;
        _loading = false;
        Revalidate();
    }

    public string Title => string.Format(Strings.ExportCertificate_Export, _entry.DisplayName);

    /// <summary>Asks the user for a folder. Set by the window, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Raised when the editor should close; true if the files were written.</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>The paths written, once exported.</summary>
    public IReadOnlyList<string> Written { get; private set; } = [];

    [ObservableProperty]
    public partial string Folder { get; set; } = "";

    [ObservableProperty]
    public partial string BaseName { get; set; } = "";

    [ObservableProperty]
    public partial bool IncludeKey { get; set; }

    [ObservableProperty]
    public partial bool IncludeAuthority { get; set; }

    [ObservableProperty]
    public partial bool IncludeChain { get; set; }

    [ObservableProperty]
    public partial bool IncludePkcs12 { get; set; }

    [ObservableProperty]
    public partial string Pkcs12Password { get; set; } = "";

    [ObservableProperty]
    public partial bool ReplaceExisting { get; set; }

    [ObservableProperty]
    public partial string FilesText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial string? Error { get; set; }

    public bool HasKey => _entry.HasKey;

    public bool ShowAuthority => _entry.Kind != CertificateKind.RootAuthority;

    public bool ShowChain => _hasIntermediates;

    public bool IsAuthority => _entry.Kind.IsAuthority();

    private bool CanExport => Error is null;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is not (nameof(Error) or nameof(FilesText)))
            Revalidate();
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
            Folder = folder;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Export()
    {
        try
        {
            Written = _store.Export(_entry, Options());
        }
        catch (FormatException e)
        {
            Error = e.Message;
            return;
        }

        _store.ExportFolder = Folder.Trim();
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    private ExportOptions Options() => new()
    {
        Folder = Folder.Trim(),
        BaseName = BaseName.Trim(),
        IncludeKey = IncludeKey && HasKey,
        IncludeAuthority = IncludeAuthority && ShowAuthority,
        IncludeChain = IncludeChain && ShowChain,
        Pkcs12Password = IncludePkcs12 && HasKey ? Pkcs12Password : null,
        ReplaceExisting = ReplaceExisting,
    };

    private void Revalidate()
    {
        var options = Options();
        FilesText = string.Join(", ", _store.ExportFileNames(_entry, options));
        Error = Folder.Trim().Length == 0 ? Strings.ExportCertificate_ChooseTheFolderToWriteThe
            : !Directory.Exists(Folder.Trim()) ? Strings.ExportCertificate_TheFolderDoesNotExist
            : BaseName.Trim().Length == 0 || BaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || BaseName.Contains('/')
                ? Strings.ExportCertificate_EnterAFileNameWithoutA
            : IncludePkcs12 && Pkcs12Password.Length == 0 ? Strings.ExportCertificate_EnterAPasswordForThePFX
            : null;
    }
}
