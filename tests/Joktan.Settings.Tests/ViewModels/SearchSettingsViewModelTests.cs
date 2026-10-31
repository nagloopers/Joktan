using System;
using Joktan.Settings.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class SearchSettingsViewModelTests : DataFolderTest
{
    public static TheoryData<string> Areas() => new("Notes", "Help", "Messages", "Alerts");

    [Theory]
    [MemberData(nameof(Areas))]
    public void A_switch_is_saved_at_once_and_says_it_changed(string area)
    {
        var settings = new SearchSettings();
        var page = new SearchSettingsViewModel(settings);
        var changed = 0;
        string? property = null;
        page.Changed += () => changed++;
        page.PropertyChanged += (_, e) => property = e.PropertyName;

        typeof(SearchSettingsViewModel).GetProperty(area)!.SetValue(page, false);

        Assert.Equal(1, changed);
        Assert.Equal(area, property);
        Assert.False((bool)typeof(SearchSettings).GetProperty(area)!.GetValue(settings)!);
        Assert.False((bool)typeof(SearchSettingsViewModel).GetProperty(area)!.GetValue(page)!);
        Assert.False((bool)typeof(SearchSettings).GetProperty(area)!.GetValue(SearchSettings.Load())!);
    }

    [Fact]
    public void The_page_is_titled_for_the_menu()
    {
        var page = new SearchSettingsViewModel(new SearchSettings());

        Assert.Equal("Search", page.Title);
        Assert.Equal("Settings / Search", page.Subtitle);
    }
}
