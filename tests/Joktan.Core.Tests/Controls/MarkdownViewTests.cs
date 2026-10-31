using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Media;
using Joktan.Core.Controls;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests.Controls;

public class MarkdownViewTests
{
    private static StackPanel Blocks(MarkdownView view) => (StackPanel)view.Child!;

    private static MarkdownView Drawn(string markdown) => new() { DrawsAtOnce = true, Markdown = markdown };

    private static string Plain(InlineCollection inlines) => string.Concat(inlines.Select(i => i switch
    {
        Run run => run.Text,
        Span span => Plain(span.Inlines),
        _ => "",
    }));

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(6)]
    public Task Headings_take_the_class_of_their_level(int level) => Headless.Run(() =>
    {
        var text = Assert.IsType<SelectableTextBlock>(Blocks(Drawn(new string('#', level) + " Title")).Children.Single());

        Assert.Contains($"md-h{level}", text.Classes);
        Assert.Equal("Title", Plain(text.Inlines!));
    });

    [Fact]
    public Task A_paragraph_keeps_its_line_breaks_as_runs() => Headless.Run(() =>
    {
        var text = Assert.IsType<SelectableTextBlock>(Blocks(Drawn("one\ntwo  \nthree")).Children.Single());

        Assert.Contains("md-paragraph", text.Classes);
        Assert.Equal("one two\nthree", Plain(text.Inlines!));
    });

    [Fact]
    public Task Emphasis_becomes_the_matching_span() => Headless.Run(() =>
    {
        var text = (SelectableTextBlock)Blocks(Drawn("*i* **b** ~~gone~~ ++under++ ==mark== ~sub~ `code` &amp; <br/>")).Children.Single();
        var inlines = text.Inlines!.ToList();

        Assert.Single(inlines.OfType<Italic>());
        Assert.Single(inlines.OfType<Bold>());
        Assert.Single(inlines.OfType<Underline>());
        Assert.Contains(inlines.OfType<Span>(), s => s.TextDecorations == TextDecorations.Strikethrough);
        Assert.Equal(2, inlines.OfType<Span>().Count(s => s.Classes.Contains("md-mark")));
        Assert.Contains(inlines.OfType<Run>(), r => r.Classes.Contains("md-code") && r.Text == "code");
        Assert.Contains(inlines.OfType<Run>(), r => r.Text == "&");
        Assert.Contains(inlines.OfType<Run>(), r => r.Classes.Contains("md-html") && r.Text == "<br/>");
    });

    [Fact]
    public Task An_image_shows_its_description() => Headless.Run(() =>
    {
        var text = (SelectableTextBlock)Blocks(Drawn("![a *fine* `pic`](pic.png)")).Children.Single();

        var run = Assert.IsType<Run>(text.Inlines!.Single());
        Assert.Equal("[a fine pic]", run.Text);
        Assert.Contains("md-html", run.Classes);
    });

    [Fact]
    public Task An_empty_link_shows_its_address() => Headless.Run(() =>
    {
        var text = (SelectableTextBlock)Blocks(Drawn("[](https://example.com)")).Children.Single();

        Assert.Equal("https://example.com", Plain(text.Inlines!));
    });

    [Fact]
    public Task Lists_are_marked_with_bullets_numbers_and_task_boxes() => Headless.Run(() =>
    {
        string Marker(Control row) => ((TextBlock)((Grid)row).Children[0]).Text!;

        var bullets = (StackPanel)Blocks(Drawn("- a\n- b")).Children.Single();
        Assert.Equal(["•", "•"], bullets.Children.Select(Marker));

        var numbers = (StackPanel)Blocks(Drawn("3. a\n4. b")).Children.Single();
        Assert.Equal(["3.", "4."], numbers.Children.Select(Marker));

        var tasks = (StackPanel)Blocks(Drawn("- [x] done\n- [ ] todo")).Children.Single();
        Assert.Equal(["☑", "☐"], tasks.Children.Select(Marker));
        Assert.Contains("task", ((Grid)tasks.Children[0]).Children[0].Classes);
    });

    [Fact]
    public Task Quotes_code_and_rules_get_their_own_blocks() => Headless.Run(() =>
    {
        var blocks = Blocks(Drawn("> said\n\n```\nfirst\nsecond\n```\n\n---\n\n[ref]: https://example.com\n")).Children;

        Assert.Equal(3, blocks.Count);
        Assert.Contains("md-quote", blocks[0].Classes);
        Assert.Contains("md-code", blocks[1].Classes);
        var code = (SelectableTextBlock)((ScrollViewer)((Border)blocks[1]).Child!).Content!;
        Assert.Equal("first\nsecond", code.Text);
        Assert.Contains("md-rule", blocks[2].Classes);
    });

    [Fact]
    public Task Tables_lay_out_their_cells_and_alignment() => Headless.Run(() =>
    {
        var scroller = (ScrollViewer)Blocks(Drawn("| L | C | R | N |\n|:--|:-:|--:|---|\n| 1 | 2 | 3 | 4 |\n")).Children.Single();
        var grid = (Grid)scroller.Content!;

        Assert.Equal(4, grid.ColumnDefinitions.Count);
        Assert.Equal(2, grid.RowDefinitions.Count);
        Assert.Equal(8, grid.Children.Count);
        Assert.Equal(4, grid.Children.Count(c => c.Classes.Contains("header")));

        TextAlignment AlignmentAt(int index) =>
            ((SelectableTextBlock)((StackPanel)((Border)grid.Children[index]).Child!).Children[0]).TextAlignment;
        Assert.Equal(TextAlignment.Left, AlignmentAt(4));
        Assert.Equal(TextAlignment.Center, AlignmentAt(5));
        Assert.Equal(TextAlignment.Right, AlignmentAt(6));
        Assert.Equal(TextAlignment.Left, AlignmentAt(7));
    });

    [Fact]
    public Task A_grid_table_with_a_spanning_cell_is_laid_out() => Headless.Run(() =>
    {
        const string table = """
            +---+---+
            | a | b |
            +===+===+
            | wide  |
            +---+---+
            """;
        var scroller = (ScrollViewer)Blocks(Drawn(table)).Children.Single();
        var grid = (Grid)scroller.Content!;

        Assert.Contains(grid.Children, c => Grid.GetColumnSpan(c) == 2);
    });

    [Fact]
    public Task Nothing_is_shown_for_no_text() => Headless.Run(() =>
    {
        Assert.Empty(Blocks(new MarkdownView { Markdown = null }).Children);
        Assert.Empty(Blocks(Drawn("")).Children);
    });

    [Fact]
    public Task A_new_page_draws_at_once_but_edits_wait_for_a_pause() => Headless.Run(async () =>
    {
        var view = new MarkdownView { Markdown = "first" };
        Assert.Single(Blocks(view).Children);

        view.Markdown = "first\n\nsecond";
        Assert.Single(Blocks(view).Children);

        await Headless.Until(() => Blocks(view).Children.Count == 2);
    });

    [Fact]
    public Task Other_properties_do_not_redraw() => Headless.Run(() =>
    {
        var view = Drawn("text");
        var before = Blocks(view).Children[0];

        view.Width = 100;

        Assert.Same(before, Blocks(view).Children[0]);
    });

    private static (Window Window, SelectableTextBlock Text, Point OnLink, Point OffLink) ShowLink(MarkdownView view)
    {
        var window = Headless.Show(view, width: 800, height: 300);
        var text = Headless.Find<SelectableTextBlock>(window);
        var lineCentre = text.TextLayout.Height / 2;
        var onLink = text.TranslatePoint(new Point(text.Padding.Left + 4, text.Padding.Top + lineCentre), window)!.Value;
        // Not the line's own length, which on Linux counts a position past the last character and so off the text.
        var last = text.TextLayout.HitTestTextPosition(text.Inlines!.Text!.Length - 1);
        var offLink = text.TranslatePoint(new Point(text.Padding.Left + last.Center.X, text.Padding.Top + lineCentre), window)!.Value;
        return (window, text, onLink, offLink);
    }

    [Fact]
    public Task Clicking_a_link_raises_the_event_and_a_handler_keeps_it_from_the_browser() => Headless.Run(() =>
    {
        var view = Drawn("[site](https://example.com/page) and more");
        Uri? clicked = null;
        EventHandler<LinkClickedEventArgs> handler = (_, e) =>
        {
            clicked = e.Uri;
            e.Handled = true;
        };
        view.LinkClicked += handler;
        var (window, _, onLink, _) = ShowLink(view);

        window.MouseDown(onLink, MouseButton.Left);
        window.MouseUp(onLink, MouseButton.Left);

        Assert.Equal(new Uri("https://example.com/page"), clicked);

        clicked = null;
        view.LinkClicked -= handler;
        window.MouseDown(onLink, MouseButton.Left);
        window.MouseUp(onLink, MouseButton.Left);
        Assert.Null(clicked);
        window.Close();
    });

    [Fact]
    public Task Clicks_elsewhere_or_with_another_button_do_not_follow_the_link() => Headless.Run(() =>
    {
        var view = Drawn("[site](https://example.com/page) and more");
        var clicks = 0;
        view.LinkClicked += (_, e) =>
        {
            clicks++;
            e.Handled = true;
        };
        var (window, _, onLink, offLink) = ShowLink(view);

        window.MouseDown(offLink, MouseButton.Left);
        window.MouseUp(offLink, MouseButton.Left);
        window.MouseDown(onLink, MouseButton.Right);
        window.MouseUp(onLink, MouseButton.Right);

        Assert.Equal(0, clicks);
        window.Close();
    });

    [Fact]
    public Task Dragging_across_a_link_selects_it_without_following_it() => Headless.Run(() =>
    {
        var view = Drawn("[site](https://example.com/page) and more");
        var clicks = 0;
        view.LinkClicked += (_, e) =>
        {
            clicks++;
            e.Handled = true;
        };
        var (window, _, onLink, offLink) = ShowLink(view);

        window.MouseDown(offLink, MouseButton.Left);
        window.MouseMove(onLink, RawInputModifiers.LeftMouseButton);
        window.MouseUp(onLink, MouseButton.Left);

        Assert.Equal(0, clicks);
        window.Close();
    });

    [Fact]
    public Task The_pointer_turns_into_a_hand_over_a_link() => Headless.Run(() =>
    {
        var (window, text, onLink, offLink) = ShowLink(Drawn("[site](https://example.com/page) and more"));

        window.MouseMove(onLink);
        Assert.NotNull(text.Cursor);

        window.MouseMove(offLink);
        Assert.Null(text.Cursor);
        window.Close();
    });

    [Theory]
    [InlineData("<https://example.com/auto>", "https://example.com/auto")]
    [InlineData("<me@example.com>", "mailto:me@example.com")]
    public Task Autolinks_can_be_clicked(string markdown, string expected) => Headless.Run(() =>
    {
        var view = Drawn(markdown);
        Uri? clicked = null;
        view.LinkClicked += (_, e) =>
        {
            clicked = e.Uri;
            e.Handled = true;
        };
        var (window, _, onLink, _) = ShowLink(view);

        window.MouseDown(onLink, MouseButton.Left);
        window.MouseUp(onLink, MouseButton.Left);

        Assert.Equal(new Uri(expected), clicked);
        window.Close();
    });

    [Fact]
    public Task A_relative_link_is_shown_but_has_nothing_to_open() => Headless.Run(() =>
    {
        var text = (SelectableTextBlock)Blocks(Drawn("[here](relative/page)")).Children.Single();

        Assert.Null(text.Tag);
    });

    [Fact]
    public Task A_link_nobody_handles_goes_to_the_system_browser_which_is_not_here() => Headless.Run(() =>
    {
        var view = Drawn("[site](https://example.com/page) and more");
        Uri? clicked = null;
        view.LinkClicked += (_, e) => clicked = e.Uri;
        var (window, _, onLink, _) = ShowLink(view);

        window.MouseDown(onLink, MouseButton.Left);
        window.MouseUp(onLink, MouseButton.Left);

        Assert.Equal(new Uri("https://example.com/page"), clicked);
        window.Close();
    });

    [Fact]
    public Task An_image_described_with_an_empty_part_and_a_line_of_html_still_draw() => Headless.Run(() =>
    {
        var blocks = Blocks(Drawn("<div>html block</div>\n\n![a <b>bold</b> one](x.png)\n\n[^note]\n")).Children;

        Assert.NotEmpty(blocks);
    });

    private static string[] Marked(InlineCollection inlines) => inlines.SelectMany(i => i switch
    {
        Run run when run.Classes.Contains(TextHighlight.MatchClass) => [run.Text!],
        Span span => Marked(span.Inlines),
        _ => Array.Empty<string>(),
    }).ToArray();

    [Fact]
    public Task Words_are_marked_inside_formatting_and_code_and_the_text_stays_the_same() => Headless.Run(() =>
    {
        var view = new MarkdownView { DrawsAtOnce = true, Markdown = "A **Door** and a door\n\n```\nthe door\n```" };

        view.HighlightWords = ["door"];

        var paragraph = (SelectableTextBlock)Blocks(view).Children[0];
        Assert.Equal(["Door", "door"], Marked(paragraph.Inlines!));
        Assert.Equal("A Door and a door", Plain(paragraph.Inlines!));

        var code = CodeOf(view);
        Assert.Equal(["door"], Marked(code.Inlines!));
        Assert.Equal("the door", Plain(code.Inlines!));

        view.HighlightWords = null;
        Assert.Empty(Marked(((SelectableTextBlock)Blocks(view).Children[0]).Inlines!));
    });

    private static SelectableTextBlock CodeOf(MarkdownView view)
        => (SelectableTextBlock)((ScrollViewer)((Border)Blocks(view).Children[1]).Child!).Content!;

    [Fact]
    public Task Marking_around_a_link_leaves_its_text_whole() => Headless.Run(() =>
    {
        var view = new MarkdownView { DrawsAtOnce = true, Markdown = "door [site](https://example.com) door", HighlightWords = ["door"] };

        var text = (SelectableTextBlock)Blocks(view).Children[0];

        Assert.Equal("door site door", Plain(text.Inlines!));
        Assert.Equal(["door", "door"], Marked(text.Inlines!));
    });

    [Fact]
    public Task The_view_scrolls_to_the_first_place_marked() => Headless.Run(() =>
    {
        var markdown = string.Join("\n\n", Enumerable.Range(0, 200).Select(i => i == 150 ? "the needle" : $"line {i}"));
        var view = new MarkdownView { DrawsAtOnce = true, Markdown = markdown };
        var scroller = new ScrollViewer { Content = view };
        var window = Headless.Show(scroller, null, 600, 300);

        view.HighlightWords = ["needle"];
        Headless.Settle();
        window.CaptureRenderedFrame();
        Headless.Settle();

        Assert.True(scroller.Offset.Y > 0);
        window.Close();
    });
}
