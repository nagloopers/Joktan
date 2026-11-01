using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Certificates.Tests.ViewModels;
using Joktan.Certificates.ViewModels;
using Joktan.Certificates.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Certificates.Tests.Views;

public class CertificateDefaultsViewTests : CertificateTestBase
{
    [Fact]
    public Task Shows_the_defaults_and_saves_what_is_typed() => Headless.Run(() =>
    {
        var vm = new CertificateDefaultsViewModel(Store);
        var window = Headless.Show(new CertificateDefaultsView(), vm);
        Assert.Contains("Settings / Certificate Defaults", Headless.All<TextBlock>(window).Select(t => t.Text));
        Assert.Contains(Store.Defaults.RootCommonName, Headless.All<TextBox>(window).Select(t => t.Text));

        vm.Organization = "Acme";
        Headless.Settle();

        Assert.Equal("Acme", CertificateStore.Load(Folder.Path).Defaults.Organization);
        window.Close();
    });

    [Fact]
    public Task Picking_a_folder_gives_nothing_without_a_system_dialog() => Headless.Run(async () =>
    {
        var vm = new CertificateDefaultsViewModel(Store);
        var view = new CertificateDefaultsView { DataContext = vm };
        Assert.Null(await vm.PickFolder!());

        var window = Headless.Show(view);
        Assert.Null(await vm.PickFolder!());
        window.Close();
    });
}
