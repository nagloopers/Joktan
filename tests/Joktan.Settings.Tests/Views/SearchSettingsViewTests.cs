using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Settings.ViewModels;
using Joktan.Settings.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class SearchSettingsViewTests : DataFolderTest
{
    [Fact]
    public Task Each_area_has_a_switch_and_no_checkboxes() => Headless.Run(() =>
    {
        var page = new SearchSettingsViewModel(new SearchSettings { Help = false });
        var window = Headless.Show(new SearchSettingsView(), page);

        var switches = Headless.All<ToggleSwitch>(window).ToList();
        Assert.Equal(4, switches.Count);
        Assert.Empty(Headless.All<CheckBox>(window));
        Assert.Equal([true, false, true, true], switches.Select(s => s.IsChecked == true));

        switches[2].IsChecked = false;
        Headless.Settle();
        Assert.False(page.Messages);
        window.Close();
    });
}
