using System.IO;
using System.Threading.Tasks;
using Joktan.Certificates.ViewModels;
using Xunit;

namespace Joktan.Certificates.Tests.ViewModels;

public class ExportCertificateViewModelTests : CertificateTestBase
{
    private string Target()
    {
        var target = Folder.At("out");
        Directory.CreateDirectory(target);
        return target;
    }

    [Fact]
    public void A_server_certificate_offers_its_key_and_ca()
    {
        var root = Root();
        var vm = new ExportCertificateViewModel(Store, Server(root));

        Assert.Equal("Export server", vm.Title);
        Assert.Equal("server", vm.BaseName);
        Assert.True(vm.HasKey);
        Assert.True(vm.IncludeKey);
        Assert.True(vm.ShowAuthority);
        Assert.True(vm.IncludeAuthority);
        Assert.False(vm.ShowChain);
        Assert.False(vm.IncludeChain);
        Assert.False(vm.IsAuthority);
        Assert.Contains("server", vm.FilesText);
    }

    [Fact]
    public void A_client_certificate_is_named_client_and_a_ca_after_itself()
    {
        var root = Root();
        Assert.Equal("client", new ExportCertificateViewModel(Store, Client(root)).BaseName);

        var ca = new ExportCertificateViewModel(Store, root);
        Assert.Equal(CertificateStore.FileNameFrom("root"), ca.BaseName);
        Assert.True(ca.IsAuthority);
        Assert.False(ca.IncludeKey);
        Assert.False(ca.ShowAuthority);
        Assert.False(ca.IncludeAuthority);
    }

    [Fact]
    public void A_chain_is_offered_below_an_intermediate_ca()
    {
        var root = Root();
        var intermediate = Store.Create(Spec("intermediate", CertificateKind.IntermediateAuthority, root));

        var vm = new ExportCertificateViewModel(Store, Server(intermediate));

        Assert.True(vm.ShowChain);
        Assert.True(vm.IncludeChain);
    }

    [Fact]
    public void Problems_are_found_as_the_fields_change()
    {
        var vm = new ExportCertificateViewModel(Store, Root());
        var target = Target();

        vm.Folder = " ";
        Assert.Equal("Choose the folder to write the files to", vm.Error);
        Assert.False(vm.ExportCommand.CanExecute(null));

        vm.Folder = Folder.At("not-there");
        Assert.Equal("The folder does not exist", vm.Error);

        vm.Folder = target;
        Assert.Null(vm.Error);

        foreach (var name in new[] { " ", "a/b" })
        {
            vm.BaseName = name;
            Assert.Contains("file name", vm.Error);
        }

        vm.BaseName = "ca";
        vm.IncludePkcs12 = true;
        Assert.Equal("Enter a password for the PFX/P12 file", vm.Error);

        vm.Pkcs12Password = "secret";
        Assert.Null(vm.Error);
        Assert.True(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void Exporting_writes_the_files_remembers_the_folder_and_closes()
    {
        var target = Target();
        var vm = new ExportCertificateViewModel(Store, Server(Root())) { Folder = target, IncludePkcs12 = true, Pkcs12Password = "secret" };
        bool? closed = null;
        vm.CloseRequested += written => closed = written;

        vm.ExportCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal(target, Store.ExportFolder);
        Assert.NotEmpty(vm.Written);
        Assert.All(vm.Written, path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void Files_already_there_are_kept_unless_replacing_was_asked_for()
    {
        var target = Target();
        var entry = Root();
        new ExportCertificateViewModel(Store, entry) { Folder = target }.ExportCommand.Execute(null);
        var vm = new ExportCertificateViewModel(Store, entry) { Folder = target };
        bool? closed = null;
        vm.CloseRequested += written => closed = written;

        vm.ExportCommand.Execute(null);
        Assert.Null(closed);
        Assert.NotNull(vm.Error);

        vm.ReplaceExisting = true;
        Assert.Null(vm.Error);
        vm.ExportCommand.Execute(null);
        Assert.True(closed);
    }

    [Fact]
    public void Cancelling_closes_without_writing()
    {
        var vm = new ExportCertificateViewModel(Store, Root());
        bool? closed = null;
        vm.CloseRequested += written => closed = written;

        vm.CancelCommand.Execute(null);

        Assert.False(closed);
        Assert.Empty(vm.Written);
    }

    [Fact]
    public async Task Browsing_uses_the_folder_picked_if_any()
    {
        var cancel = TestContext.Current.CancellationToken;
        var vm = new ExportCertificateViewModel(Store, Root());
        var before = vm.Folder;

        await vm.BrowseCommand.ExecuteAsync(null).WaitAsync(cancel);
        vm.PickFolder = () => Task.FromResult<string?>(null);
        await vm.BrowseCommand.ExecuteAsync(null).WaitAsync(cancel);
        Assert.Equal(before, vm.Folder);

        vm.PickFolder = () => Task.FromResult<string?>(Folder.Path);
        await vm.BrowseCommand.ExecuteAsync(null).WaitAsync(cancel);
        Assert.Equal(Folder.Path, vm.Folder);
    }
}
