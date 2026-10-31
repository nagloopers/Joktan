using System;
using System.IO;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests;

[Collection(ProcessStateCollection.Name)]
public class DataFolderTests
{
    [Fact]
    public void Tests_never_use_the_real_data_folder()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Joktan");

        Assert.NotEqual(real, DataFolder.Path);
        Assert.StartsWith(Path.GetTempPath(), DataFolder.Path);
    }

    [Fact]
    public void The_variable_chooses_the_folder()
    {
        var previous = Environment.GetEnvironmentVariable(DataFolder.Variable);
        try
        {
            Environment.SetEnvironmentVariable(DataFolder.Variable, "/some/folder");
            Assert.Equal("/some/folder", DataFolder.Path);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataFolder.Variable, previous);
        }
    }

    [Fact]
    public void The_default_is_a_Joktan_folder_in_the_application_data() => Assert.Equal(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Joktan"),
        DataFolder.DefaultPath);

    [Fact]
    public void With_the_variable_set_nothing_chosen_is_looked_for()
    {
        Assert.True(DataFolder.IsSetByVariable);
        Assert.Null(DataFolder.MissingFolder);
    }

    [Fact]
    public void With_no_choice_the_default_is_used()
    {
        using var folder = new TempFolder();

        Assert.Equal((folder.Path, (string?)null), DataFolder.Resolve(folder.Path));
    }

    [Fact]
    public void A_folder_chosen_is_used_and_choosing_the_default_again_forgets_it()
    {
        using var defaults = new TempFolder();
        using var chosen = new TempFolder();

        DataFolder.SaveChosen(chosen.Path, defaults.Path);
        Assert.Equal((chosen.Path, (string?)null), DataFolder.Resolve(defaults.Path));

        DataFolder.SaveChosen(defaults.Path, defaults.Path);
        Assert.Null(DataFolder.ReadChosen(defaults.Path));
        Assert.False(File.Exists(Path.Combine(defaults.Path, DataFolder.LocationFileName)));
    }

    [Fact]
    public void A_folder_that_cannot_be_found_falls_back_on_the_default_and_stays_chosen()
    {
        using var defaults = new TempFolder();
        var gone = Path.Combine(defaults.Path, "not-connected");

        DataFolder.SaveChosen(gone, defaults.Path);

        Assert.Equal((defaults.Path, gone), DataFolder.Resolve(defaults.Path));
        Assert.Equal(gone, DataFolder.ReadChosen(defaults.Path));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{ "Folder": "" }""")]
    [InlineData("""{ "Folder": "relative/path" }""")]
    public void A_choice_that_cannot_be_used_leaves_the_default(string content)
    {
        using var defaults = new TempFolder();
        File.WriteAllText(Path.Combine(defaults.Path, DataFolder.LocationFileName), content);

        Assert.Equal((defaults.Path, (string?)null), DataFolder.Resolve(defaults.Path));
    }
}
