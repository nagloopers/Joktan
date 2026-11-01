using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Certificates.Tests.ViewModels;
using Joktan.Certificates.ViewModels;
using Joktan.Certificates.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Certificates.Tests.Views;

public class CertificatesViewTests : CertificateTestBase
{
    private CertificatesViewModel Page() => new(Store, _ => ["a client"]);

    private static void OpenFolderClick(CertificatesView view) =>
        typeof(CertificatesView).GetMethod("OnOpenFolderClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [null, null]);

    [Fact]
    public Task Shows_the_tree_the_selected_certificate_and_every_band() => Headless.Run(() =>
    {
        var root = Root();
        Server(root);
        Client(root);
        var vm = Page();
        var window = Headless.Show(new CertificatesView(), vm);
        Assert.Contains("root", Headless.All<TextBlock>(window).Select(t => t.Text));

        vm.Selected = vm.Roots[0];
        vm.Message = "A message";
        vm.Error = "An error";
        vm.IsConfirmingDelete = true;
        Headless.Settle();
        window.UpdateLayout();
        Headless.Settle();

        Assert.Contains("TLS", string.Join(" ", vm.Roots[0].Children.Select(c => c.Usage)));
        window.Close();
    });

    [Fact]
    public Task Being_shown_reads_the_tree_again() => Headless.Run(() =>
    {
        var vm = Page();
        Root();
        Assert.Empty(vm.Roots);

        var window = Headless.Show(new CertificatesView(), vm);

        Assert.Single(vm.Roots);
        window.Close();
    });

    [Fact]
    public Task The_editors_open_as_dialogs_and_say_how_they_ended() => Headless.Run(async () =>
    {
        var vm = Page();
        var view = new CertificatesView { DataContext = vm };
        Assert.False(await vm.EditNewCertificate!(new NewCertificateViewModel(Store, null)));
        Assert.False(await vm.EditExport!(new ExportCertificateViewModel(Store, Root())));
        vm.Refresh();

        var window = Headless.Show(view);
        var opened = new List<Window>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));

        var making = vm.NewAuthorityCommand.ExecuteAsync(null);
        await Headless.Until(() => opened.Count == 1);
        var editor = Assert.IsType<NewCertificateViewModel>(opened[0].DataContext);
        Assert.IsType<NewCertificateWindow>(opened[0]);
        Headless.Settle();
        Assert.Null(await editor.PickFolder!());
        editor.Name = "made";
        Headless.Settle();
        editor.SaveCommand.Execute(null);
        await making;
        Assert.Equal("made", vm.Selected!.Name);

        var exporting = vm.ExportCommand.ExecuteAsync(null);
        await Headless.Until(() => opened.Count == 2);
        var export = Assert.IsType<ExportCertificateViewModel>(opened[1].DataContext);
        Assert.IsType<ExportCertificateWindow>(opened[1]);
        Headless.Settle();
        Assert.Null(await export.PickFolder!());
        export.CancelCommand.Execute(null);
        await exporting;
        Assert.Null(vm.Error);
        window.Close();
    });

    [Fact]
    public Task Opening_the_folder_needs_a_selection_and_the_folder_there() => Headless.Run(() =>
    {
        var entry = Root();
        var vm = Page();
        var view = new CertificatesView { DataContext = vm };
        var window = Headless.Show(view);

        OpenFolderClick(view);

        vm.Selected = vm.Roots[0];
        OpenFolderClick(view);
        Headless.Settle();

        System.IO.Directory.Delete(entry.Folder, recursive: true);
        OpenFolderClick(view);
        Headless.Settle();
        window.Close();
    });
}
