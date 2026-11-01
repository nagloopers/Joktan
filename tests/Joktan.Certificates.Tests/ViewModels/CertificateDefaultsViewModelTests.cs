using System.Threading.Tasks;
using Joktan.Certificates.ViewModels;
using Xunit;

namespace Joktan.Certificates.Tests.ViewModels;

public class CertificateDefaultsViewModelTests : CertificateTestBase
{
    [Fact]
    public void Starts_from_the_stores_defaults()
    {
        var vm = new CertificateDefaultsViewModel(Store);

        Assert.Equal(Store.Defaults.RootCommonName, vm.RootCommonName);
        Assert.Equal(Store.Defaults.KeyAlgorithm, vm.SelectedKeyAlgorithm!.Value);
        Assert.Equal(Store.DefaultClientFolder, vm.DefaultClientFolder);
        Assert.Equal(4, vm.KeyAlgorithms.Count);
        Assert.Null(vm.Error);
    }

    [Fact]
    public void Good_values_are_saved_at_once_and_read_again()
    {
        var vm = new CertificateDefaultsViewModel(Store)
        {
            RootCommonName = " My root ",
            IntermediateCommonName = "Mine",
            ServerCommonName = "Server",
            ServerAlternativeNames = "a.example",
            ClientCommonName = "Client",
            Organization = "Org",
            OrganizationalUnit = "Unit",
            Locality = "Town",
            State = "State",
            Country = "za",
            AuthorityValidityDays = "100",
            CertificateValidityDays = " 50 ",
            ClientFolder = Folder.Path,
        };

        Assert.Null(vm.Error);
        var loaded = CertificateStore.Load(Folder.Path).Defaults;
        Assert.Equal("My root", loaded.RootCommonName);
        Assert.Equal("ZA", loaded.Country);
        Assert.Equal(100, loaded.AuthorityValidityDays);
        Assert.Equal(50, loaded.CertificateValidityDays);
        Assert.Equal(Folder.Path, loaded.ClientFolder);
    }

    [Theory]
    [InlineData("RootCommonName", "The root CA common name")]
    [InlineData("IntermediateCommonName", "The intermediate CA common name")]
    [InlineData("ServerCommonName", "The server common name")]
    [InlineData("ClientCommonName", "The client common name")]
    public void A_common_name_may_not_be_too_long(string property, string expected)
    {
        var vm = new CertificateDefaultsViewModel(Store);
        var before = Store.Defaults.RootCommonName;

        typeof(CertificateDefaultsViewModel).GetProperty(property)!.SetValue(vm, new string('x', 65));

        Assert.StartsWith(expected, vm.Error);
        Assert.Equal(before, CertificateStore.Load(Folder.Path).Defaults.RootCommonName);
    }

    [Fact]
    public void Other_problems_are_found_and_not_saved()
    {
        var vm = new CertificateDefaultsViewModel(Store);

        vm.Country = "too long";
        Assert.NotNull(vm.Error);
        vm.Country = "";

        vm.ServerAlternativeNames = "not valid name!";
        Assert.NotNull(vm.Error);
        vm.ServerAlternativeNames = "";

        vm.ClientFolder = "relative/path";
        Assert.StartsWith("MQTT clients' folder: ", vm.Error);
        vm.ClientFolder = "";

        foreach (var days in new[] { "0", "36501", "x" })
        {
            vm.AuthorityValidityDays = days;
            Assert.StartsWith("The number of days", vm.Error);
        }

        vm.AuthorityValidityDays = "1";
        vm.CertificateValidityDays = "";
        Assert.StartsWith("The number of days", vm.Error);

        vm.CertificateValidityDays = "1";
        Assert.Null(vm.Error);
    }

    [Fact]
    public void Resetting_goes_back_to_what_is_built_in()
    {
        var vm = new CertificateDefaultsViewModel(Store) { RootCommonName = "Mine" };
        vm.Country = "too long";

        vm.ResetCommand.Execute(null);

        Assert.Null(vm.Error);
        Assert.Equal(new CertificateDefaults().RootCommonName, vm.RootCommonName);
        Assert.Equal(new CertificateDefaults().RootCommonName, CertificateStore.Load(Folder.Path).Defaults.RootCommonName);
    }

    [Fact]
    public void No_algorithm_chosen_means_the_usual_one()
    {
        var vm = new CertificateDefaultsViewModel(Store) { SelectedKeyAlgorithm = null };

        Assert.Equal(KeyAlgorithm.EcdsaP256, CertificateStore.Load(Folder.Path).Defaults.KeyAlgorithm);
        Assert.Null(vm.SelectedKeyAlgorithm);
    }

    [Fact]
    public void A_folder_changed_elsewhere_is_shown_and_not_put_back_by_the_next_edit()
    {
        var vm = new CertificateDefaultsViewModel(Store);
        var defaults = Store.Defaults.Clone();
        defaults.ClientFolder = Folder.Path;

        Store.SaveDefaults(defaults);
        Assert.Equal(Folder.Path, vm.ClientFolder);

        vm.Organization = "Acme";

        var loaded = CertificateStore.Load(Folder.Path).Defaults;
        Assert.Equal(Folder.Path, loaded.ClientFolder);
        Assert.Equal("Acme", loaded.Organization);
    }

    [Fact]
    public void What_is_being_typed_is_not_replaced_by_what_was_saved()
    {
        var vm = new CertificateDefaultsViewModel(Store) { Organization = "Acme " };

        Assert.Equal("Acme ", vm.Organization);
        Assert.Equal("Acme", Store.Defaults.Organization);
    }

    [Fact]
    public async Task Browsing_uses_the_folder_picked_if_any()
    {
        var cancel = TestContext.Current.CancellationToken;
        var vm = new CertificateDefaultsViewModel(Store);

        await vm.BrowseClientFolderCommand.ExecuteAsync(null).WaitAsync(cancel);
        vm.PickFolder = () => Task.FromResult<string?>(null);
        await vm.BrowseClientFolderCommand.ExecuteAsync(null).WaitAsync(cancel);
        Assert.Equal("", vm.ClientFolder);

        vm.PickFolder = () => Task.FromResult<string?>(Folder.Path);
        await vm.BrowseClientFolderCommand.ExecuteAsync(null).WaitAsync(cancel);
        Assert.Equal(Folder.Path, vm.ClientFolder);
    }
}
