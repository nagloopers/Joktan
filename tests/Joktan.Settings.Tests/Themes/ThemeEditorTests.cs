using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Joktan.Settings.Themes;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.Themes;

[Collection(ProcessStateCollection.Name)]
public class ThemeEditorTests : DataFolderTest
{
    private static object? Resource(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) ? value : null;

    private static string ColorKey => ThemeTokens.AccentBrush;

    [Fact]
    public Task Starts_following_the_system_with_the_themes_that_come_with_Joktan() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();

        Assert.Equal(ThemeEditor.FollowSystem, editor.Current);
        Assert.Equal([BuiltInThemes.LightName, BuiltInThemes.DarkName, BuiltInThemes.GreenDarkName], editor.Themes.Select(t => t.Name));
        Assert.False(editor.IsDirty);
        Assert.False(editor.CanOverwrite);
        Assert.True(editor.IsBuiltIn(ThemeEditor.FollowSystem));
        Assert.True(editor.IsBuiltIn(BuiltInThemes.DarkName));
        Assert.False(editor.IsBuiltIn("Mine"));
    });

    [Fact]
    public Task Built_in_themes_read_every_token_and_green_dark_starts_from_dark() => Headless.Run(() =>
    {
        var themes = BuiltInThemes.Load(BuiltInThemes.LoadTokens());

        Assert.False(themes[0].IsDark);
        Assert.True(themes[1].IsDark);
        Assert.True(themes[2].IsDark);
        foreach (var theme in themes)
        {
            Assert.All(ThemeTokens.Colors, t => Assert.NotNull(Theme.NormalizeColor(theme.Colors[t.Key])));
            Assert.All(ThemeTokens.Fonts, t => Assert.Equal("", theme.Fonts[t.Key]));
            Assert.All(ThemeTokens.TextSizes, t => Assert.True(theme.TextSizes[t.Key] > 0));
        }

        Assert.Equal("#1E8449", themes[2].Colors["AccentBrush"]);
        Assert.Equal(themes[1].Colors["WarningBrush"], themes[2].Colors["WarningBrush"]);
    });

    [Fact]
    public Task Applying_puts_the_theme_in_the_apps_resources() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        Assert.Equal(ThemeEditor.FollowSystem, editor.Current);

        editor.Choose(BuiltInThemes.GreenDarkName);

        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        var accent = Assert.IsAssignableFrom<ISolidColorBrush>(Resource(ColorKey));
        Assert.Equal(Color.Parse("#1E8449"), accent.Color);
        Assert.Equal(Color.Parse("#1E8449"), Assert.IsType<Color>(Resource("SystemAccentColor")));
        Assert.IsType<Color>(Resource("SystemAccentColorLight3"));
        Assert.IsType<Color>(Resource("SystemAccentColorDark3"));
        Assert.Equal(0.18, Assert.IsAssignableFrom<ISolidColorBrush>(Resource("AccentTrackBrush")).Opacity, 3);
        Assert.Equal(0.22, Assert.IsAssignableFrom<ISolidColorBrush>(Resource("WarningTrackBrush")).Opacity, 3);
        Assert.Equal(0.2, Assert.IsAssignableFrom<ISolidColorBrush>(Resource("DangerTrackBrush")).Opacity, 3);
        Assert.IsAssignableFrom<ISolidColorBrush>(Resource("ButtonForeground"));
        Assert.Equal(BuiltInThemes.GreenDarkName, editor.Current);
        Assert.Equal(BuiltInThemes.GreenDarkName, ThemeFile.Load().Current);
    });

    [Fact]
    public Task Applying_again_replaces_what_was_laid_over_before() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        var merged = Application.Current!.Resources.MergedDictionaries;
        var before = merged.Count;

        editor.Apply();
        editor.Apply();

        Assert.Equal(before + 1, merged.Count);
    });

    [Fact]
    public Task Choosing_what_is_chosen_or_a_theme_not_there_changes_nothing() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        var replaced = 0;
        editor.DraftReplaced += () => replaced++;

        Assert.Null(editor.Choose(ThemeEditor.FollowSystem));
        Assert.Null(editor.Choose("No such theme"));

        Assert.Equal(0, replaced);
        Assert.Equal(ThemeEditor.FollowSystem, editor.Current);
    });

    [Fact]
    public Task Changes_have_to_be_saved_or_discarded_before_choosing_another() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        editor.Apply();
        editor.SetColor(ColorKey, "#123456");
        Assert.True(editor.IsDirty);

        editor.Choose(BuiltInThemes.DarkName);
        Assert.Equal(ThemeEditor.FollowSystem, editor.Current);

        editor.Discard();
        Assert.False(editor.IsDirty);
        Assert.Null(editor.Choose(BuiltInThemes.DarkName));
        Assert.Equal(BuiltInThemes.DarkName, editor.Current);
    });

    [Fact]
    public Task A_changed_colour_font_and_size_show_at_once() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        editor.Apply();
        var fontChanges = 0;
        editor.FontChanged += () => fontChanges++;

        editor.SetColor(ColorKey, "#123456");
        Assert.Equal(Color.Parse("#123456"), Assert.IsAssignableFrom<ISolidColorBrush>(Resource(ColorKey)).Color);

        var sizeKey = ThemeTokens.TextSizes[0].Key;
        editor.SetTextSize(sizeKey, 1000);
        Assert.Equal(Theme.MaxTextSize, editor.Draft.TextSizes[sizeKey]);
        Assert.Equal(Theme.MaxTextSize, Resource(sizeKey));
        editor.SetTextSize(sizeKey, 0);
        Assert.Equal(Theme.MinTextSize, editor.Draft.TextSizes[sizeKey]);

        editor.SetFont(ThemeTokens.InterfaceFont, "Some Font");
        Assert.Equal(1, fontChanges);
        Assert.Contains("Some Font", Assert.IsType<FontFamily>(Resource(ThemeTokens.InterfaceFont)).Name);
        var followsInterface = ThemeTokens.Fonts.First(f => f.Key != ThemeTokens.InterfaceFont && f.Key != ThemeTokens.CodeFont);
        Assert.Contains("Some Font", Assert.IsType<FontFamily>(Resource(followsInterface.Key)).Name);
        Assert.DoesNotContain("Some Font", Assert.IsType<FontFamily>(Resource(ThemeTokens.CodeFont)).Name);

        editor.SetFont(ThemeTokens.CodeFont, "Mono Font");
        Assert.Contains("Mono Font", Assert.IsType<FontFamily>(Resource(ThemeTokens.CodeFont)).Name);
    });

    [Fact]
    public Task Before_being_applied_changes_only_go_in_the_working_copy() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();

        editor.SetColor(ColorKey, "#123456");
        editor.SetFont(ThemeTokens.InterfaceFont, "Some Font");
        editor.SetTextSize(ThemeTokens.TextSizes[0].Key, 20);

        Assert.Equal("#123456", editor.Draft.Colors[ColorKey]);
        Assert.Equal(20, editor.Draft.TextSizes[ThemeTokens.TextSizes[0].Key]);
    });

    [Fact]
    public Task Following_the_system_gives_way_to_a_change_and_returns_when_undone() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        editor.Apply();
        Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
        var original = editor.Draft.Colors[ColorKey];

        editor.SetColor(ColorKey, "#123456");
        Assert.NotEqual(ThemeVariant.Default, Application.Current.RequestedThemeVariant);

        editor.SetColor(ColorKey, original);
        Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
    });

    [Fact]
    public Task Saving_as_new_keeps_the_changes_in_a_theme_of_its_own() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        editor.SetColor(ColorKey, "#123456");

        Assert.Null(editor.SaveAs("  Mine "));

        Assert.Equal("Mine", editor.Current);
        Assert.False(editor.IsDirty);
        Assert.True(editor.CanOverwrite);
        Assert.Contains(editor.Themes, t => t.Name == "Mine");

        var loaded = ThemeEditor.Load();
        Assert.Equal("Mine", loaded.Current);
        Assert.Equal("#123456", loaded.Draft.Colors[ColorKey]);
    });

    [Theory]
    [InlineData("", "Give the theme a name")]
    [InlineData("   ", "Give the theme a name")]
    [InlineData(null, "Give the theme a name")]
    [InlineData("dark", "There is already a theme with that name")]
    [InlineData("FOLLOW SYSTEM", "There is already a theme with that name")]
    public Task A_name_that_cannot_be_used_says_why(string? name, string expected) => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();

        Assert.Equal(expected, editor.NameProblem(name));
        Assert.Equal(expected, editor.SaveAs(name ?? ""));
    });

    [Fact]
    public Task Saving_writes_to_a_saved_theme_but_not_to_a_built_in_one() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        editor.SetColor(ColorKey, "#123456");
        Assert.Null(editor.Save());
        Assert.True(editor.IsDirty);

        editor.SaveAs("Mine");
        editor.SetColor(ColorKey, "#654321");
        Assert.Null(editor.Save());

        Assert.False(editor.IsDirty);
        Assert.Equal("#654321", editor.Themes.Single(t => t.Name == "Mine").Colors[ColorKey]);
    });

    [Fact]
    public Task Deleting_the_theme_in_use_goes_back_to_following_the_system() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        editor.SaveAs("One");
        editor.Discard();
        editor.SaveAs("Two");
        var one = editor.Themes.Single(t => t.Name == "One");
        var two = editor.Themes.Single(t => t.Name == "Two");

        Assert.Null(editor.Delete(one));
        Assert.Equal("Two", editor.Current);

        Assert.Null(editor.Delete(two));
        Assert.Equal(ThemeEditor.FollowSystem, editor.Current);
        Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);

        var builtIn = editor.Themes[0];
        Assert.Null(editor.Delete(builtIn));
        Assert.Contains(builtIn, editor.Themes);
    });

    [Fact]
    public Task Saved_themes_with_a_taken_name_are_left_out_and_an_unknown_current_means_following() => Headless.Run(() =>
    {
        var mine = BuiltInThemes.Load(BuiltInThemes.LoadTokens())[0].Clone();
        mine.Name = "Mine";
        var taken = mine.Clone();
        taken.Name = BuiltInThemes.DarkName;
        var twice = mine.Clone();
        new ThemeFile { Current = "Gone", Themes = [mine, taken, twice] }.Save();

        var editor = ThemeEditor.Load();

        Assert.Equal(ThemeEditor.FollowSystem, editor.Current);
        Assert.Single(editor.Themes, t => t.Name == "Mine");
        Assert.Equal(4, editor.Themes.Count);
    });

    [Fact]
    public Task The_system_changing_its_theme_is_followed_only_when_there_is_nothing_else_to_keep() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        editor.Apply();
        var replaced = new List<string>();
        editor.DraftReplaced += () => replaced.Add(editor.Draft.Name);
        var app = Application.Current!;

        // There is no system here to change its mind, so the app's own variant stands in for it.
        app.RequestedThemeVariant = ThemeVariant.Dark;
        Assert.NotEmpty(replaced);
        app.RequestedThemeVariant = ThemeVariant.Default;

        editor.SetColor(ColorKey, "#123456");
        var before = replaced.Count;
        var other = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        app.RequestedThemeVariant = other;
        Assert.Equal(before, replaced.Count);
    });

    [Fact]
    public Task Changing_light_for_dark_shows_at_once() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        editor.Choose(BuiltInThemes.LightName);

        editor.SetDark(true);

        Assert.True(editor.IsDirty);
        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
    });

    [Fact]
    public Task A_choice_that_cannot_be_written_says_why() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        var path = Data.At(ThemeFileName());
        Directory.CreateDirectory(path);

        Assert.NotNull(editor.Choose(BuiltInThemes.DarkName));
    });

    private string ThemeFileName()
    {
        new ThemeFile().Save();
        var file = Assert.Single(Directory.GetFiles(Data.Path));
        File.Delete(file);
        return Path.GetFileName(file);
    }

    [Fact]
    public Task The_agents_prompts_button_is_yellow_and_can_be_set_like_any_colour() => Headless.Run(() =>
    {
        Assert.Contains(ThemeTokens.Colors, t => t.Key == "AgentPromptBrush" && t.Label == "Prompts button" && t.Group == "Agent");

        var editor = ThemeEditor.Load();
        Assert.Equal("#EAB308", editor.Themes.Single(t => t.Name == BuiltInThemes.LightName).Colors["AgentPromptBrush"], ignoreCase: true);
        Assert.Equal("#FFD54A", editor.Themes.Single(t => t.Name == BuiltInThemes.DarkName).Colors["AgentPromptBrush"], ignoreCase: true);

        editor.Apply();
        editor.SetColor("AgentPromptBrush", "#123456");

        Assert.True(Avalonia.Application.Current!.TryGetResource("AgentPromptBrush", null, out var brush));
        Assert.Equal(Avalonia.Media.Color.Parse("#123456"), ((Avalonia.Media.ISolidColorBrush)brush!).Color);
    });

    [Fact]
    public Task The_search_highlight_can_be_set_like_any_colour() => Headless.Run(() =>
    {
        Assert.Contains(ThemeTokens.Colors, t => t.Key == "SearchMatchBrush" && t.Label == "Highlight");
        Assert.Contains(ThemeTokens.Colors, t => t.Key == "OnSearchMatchBrush" && t.Label == "Text on the highlight");

        var editor = ThemeEditor.Load();
        Assert.All(editor.Themes, theme => Assert.Equal("#FFD54A", theme.Colors["SearchMatchBrush"], ignoreCase: true));

        editor.Apply();
        editor.SetColor("SearchMatchBrush", "#123456");

        Assert.Equal("#123456", editor.Draft.Colors["SearchMatchBrush"]);
        Assert.True(Avalonia.Application.Current!.TryGetResource("SearchMatchBrush", null, out var brush));
        Assert.Equal(Avalonia.Media.Color.Parse("#123456"), ((Avalonia.Media.ISolidColorBrush)brush!).Color);
    });
}
