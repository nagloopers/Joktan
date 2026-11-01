using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Joktan.Core.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests;

public class MenuLayoutApplyTests
{
    private static NavItemViewModel Item(string title, params NavItemViewModel[] children) => new(title, null, children);

    private static MenuLayoutEntry Entry(string title, bool shown = true, params MenuLayoutEntry[] children)
        => new() { Title = title, Shown = shown, Children = children.Length > 0 ? children.ToList() : null };

    private static ObservableCollection<NavItemViewModel> Menu(params string[] titles)
        => new(titles.Select(t => Item(t)));

    private static void Apply(ObservableCollection<NavItemViewModel> menu, params MenuLayoutEntry[] entries)
        => new MenuLayout { Entries = entries.ToList() }.ApplyTo(menu);

    private static string[] Titles(ObservableCollection<NavItemViewModel> menu) => menu.Select(i => i.Title).ToArray();

    [Fact]
    public void A_saved_layout_knows_an_entry_by_its_id_whatever_its_title_says()
    {
        var menu = new ObservableCollection<NavItemViewModel> { Item("A"), new("Notes", "Notizen") };

        Apply(menu, Entry("Notes", shown: false), Entry("A"));

        Assert.Equal(["Notizen", "A"], Titles(menu));
        Assert.False(menu[0].IsShown);
        Assert.Equal("Notes", MenuLayout.From(menu).Entries![0].Title);
    }

    [Fact]
    public void A_renamed_entry_keeps_its_place_and_stays_hidden()
    {
        var menu = new ObservableCollection<NavItemViewModel> { Item("A"), new("New") { FormerTitle = "Old" } };

        Apply(menu, Entry("Old", shown: false), Entry("A"));

        Assert.Equal(["New", "A"], Titles(menu));
        Assert.False(menu[0].IsShown);
    }

    [Fact]
    public void An_entry_the_app_no_longer_has_is_passed_over()
    {
        var menu = Menu("A", "B");

        Apply(menu, Entry("B"), Entry("Gone", shown: false), Entry("A"));

        Assert.Equal(["B", "A"], Titles(menu));
    }

    [Fact]
    public void No_saved_layout_leaves_the_menu_as_it_comes()
    {
        var menu = Menu("A", "B");

        new MenuLayout().ApplyTo(menu);

        Assert.Equal(["A", "B"], Titles(menu));
    }

    [Fact]
    public void Puts_the_menu_in_the_saved_order()
    {
        var menu = Menu("A", "B", "C");

        Apply(menu, Entry("C"), Entry("A"), Entry("B"));

        Assert.Equal(["C", "A", "B"], Titles(menu));
    }

    [Fact]
    public void Hides_what_the_layout_hides()
    {
        var menu = Menu("A", "B");

        Apply(menu, Entry("A", shown: false), Entry("B"));

        Assert.False(menu[0].IsShown);
        Assert.True(menu[1].IsShown);
    }

    [Fact]
    public void An_entry_that_must_stay_stays()
    {
        var menu = new ObservableCollection<NavItemViewModel> { new("A") { CanHide = false } };

        Apply(menu, Entry("A", shown: false));

        Assert.True(menu[0].IsShown);
    }

    [Fact]
    public void A_new_entry_stays_after_the_one_it_follows_in_the_app()
    {
        var menu = Menu("A", "New", "B");

        Apply(menu, Entry("B"), Entry("A"));

        Assert.Equal(["B", "A", "New"], Titles(menu));
    }

    [Fact]
    public void A_new_first_entry_stays_first()
    {
        var menu = Menu("New", "A", "B");

        Apply(menu, Entry("B"), Entry("A"));

        Assert.Equal(["New", "B", "A"], Titles(menu));
    }

    [Fact]
    public void Entries_the_app_no_longer_has_and_repeats_are_ignored()
    {
        var menu = Menu("A", "B");

        Apply(menu, Entry("Gone"), Entry("B"), Entry("B", shown: false), Entry("A"));

        Assert.Equal(["B", "A"], Titles(menu));
        Assert.True(menu[0].IsShown);
    }

    [Fact]
    public void Applies_to_the_entries_within_a_group()
    {
        var group = Item("Group", Item("A"), Item("B"));
        var menu = new ObservableCollection<NavItemViewModel> { group };

        Apply(menu, Entry("Group", true, Entry("B", shown: false), Entry("A")));

        Assert.Equal(["B", "A"], Titles(group.Children));
        Assert.False(group.Children[0].IsShown);
    }

    [Fact]
    public void Reads_the_layout_from_a_menu()
    {
        var hidden = Item("B");
        hidden.IsShown = false;
        var menu = new[] { Item("Group", hidden), Item("A") };

        var layout = MenuLayout.From(menu);

        Assert.Equal(["Group", "A"], layout.Entries!.Select(e => e.Title));
        Assert.False(layout.Entries![0].Children![0].Shown);
    }
}

[Collection(ProcessStateCollection.Name)]
public class MenuLayoutFileTests : DataFolderTest
{
    [Fact]
    public void With_no_file_there_is_no_layout()
    {
        Assert.Null(MenuLayout.Load().Entries);
    }

    [Fact]
    public void What_is_saved_is_loaded()
    {
        new MenuLayout { Entries = [new MenuLayoutEntry { Title = "A", Shown = false }] }.Save();

        var entry = Assert.Single(MenuLayout.Load().Entries!);

        Assert.Equal("A", entry.Title);
        Assert.False(entry.Shown);
    }

    [Fact]
    public void A_file_that_cannot_be_read_gives_no_layout()
    {
        File.WriteAllText(Data.At("menu.json"), "not json");

        Assert.Null(MenuLayout.Load().Entries);
    }
}
