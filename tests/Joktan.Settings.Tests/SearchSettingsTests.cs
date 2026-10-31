using System.IO;
using Joktan.Core;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests;

[Collection(ProcessStateCollection.Name)]
public class SearchSettingsTests : DataFolderTest
{
    private static string FilePath => Path.Combine(DataFolder.Path, "search.json");

    [Fact]
    public void Every_area_is_on_to_begin_with()
    {
        var settings = SearchSettings.Load();

        Assert.True(settings.Notes && settings.Help && settings.Messages && settings.Alerts);
    }

    [Fact]
    public void What_was_saved_comes_back()
    {
        new SearchSettings { Help = false, Alerts = false }.Save();

        var settings = SearchSettings.Load();

        Assert.True(settings.Notes && settings.Messages);
        Assert.False(settings.Help || settings.Alerts);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    public void A_file_that_cannot_be_used_leaves_every_area_on(string content)
    {
        Directory.CreateDirectory(DataFolder.Path);
        File.WriteAllText(FilePath, content);

        var settings = SearchSettings.Load();

        Assert.True(settings.Notes && settings.Help && settings.Messages && settings.Alerts);
    }

    [Fact]
    public void An_area_the_file_does_not_name_is_on()
    {
        Directory.CreateDirectory(DataFolder.Path);
        File.WriteAllText(FilePath, """{ "Notes": false }""");

        var settings = SearchSettings.Load();

        Assert.False(settings.Notes);
        Assert.True(settings.Help && settings.Messages && settings.Alerts);
    }
}
