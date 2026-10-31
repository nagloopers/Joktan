using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdInline = Markdig.Syntax.Inlines.Inline;

namespace Joktan.Core.Controls;

/// <summary>
/// Shows Markdown as formatted text: headings, paragraphs, emphasis, lists and task lists, quotes,
/// code, tables, rules and links, which open in the browser. Images show their description. The look comes from the
/// <c>md-*</c> classes in Markdown.axaml.
/// </summary>
public sealed class MarkdownView : Decorator
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Markdown));

    /// <summary>The words to mark, for a page reached through search. The first place found is scrolled to.</summary>
    public static readonly StyledProperty<IReadOnlyList<string>?> HighlightWordsProperty =
        AvaloniaProperty.Register<MarkdownView, IReadOnlyList<string>?>(nameof(HighlightWords));

    /// <summary>Raised on a click on a link. Unless a handler marks it handled, the link opens in the browser.</summary>
    public static readonly RoutedEvent<LinkClickedEventArgs> LinkClickedEvent =
        RoutedEvent.Register<MarkdownView, LinkClickedEventArgs>(nameof(LinkClicked), RoutingStrategies.Bubble);

    // Typing quickly redraws once per pause rather than once per key.
    private static readonly TimeSpan RenderDelay = TimeSpan.FromMilliseconds(150);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();

    private readonly DispatcherTimer _timer;
    private readonly StackPanel _blocks = new();

    public MarkdownView()
    {
        Child = _blocks;
        _timer = new DispatcherTimer { Interval = RenderDelay };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Render();
        };
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public IReadOnlyList<string>? HighlightWords
    {
        get => GetValue(HighlightWordsProperty);
        set => SetValue(HighlightWordsProperty, value);
    }

    /// <summary>Draws every change at once, for text that is read and not typed.</summary>
    public bool DrawsAtOnce { get; set; }

    public event EventHandler<LinkClickedEventArgs>? LinkClicked
    {
        add => AddHandler(LinkClickedEvent, value);
        remove => RemoveHandler(LinkClickedEvent, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HighlightWordsProperty)
        {
            _timer.Stop();
            Render();
            return;
        }

        if (change.Property != MarkdownProperty)
            return;

        // A new page is drawn at once; edits to the same page wait for a pause.
        if (DrawsAtOnce || _blocks.Children.Count == 0 || string.IsNullOrEmpty(change.GetOldValue<string?>()))
        {
            _timer.Stop();
            Render();
        }
        else
        {
            _timer.Stop();
            _timer.Start();
        }
    }

    private void Render()
    {
        _blocks.Children.Clear();
        var document = Markdig.Markdown.Parse(Markdown ?? "", Pipeline);
        AddBlocks(_blocks, document);
        MarkWords();
    }

    private void MarkWords()
    {
        if (HighlightWords is not { Count: > 0 } words)
            return;

        // Every place is marked, so this does not stop at the first.
        var marked = _blocks.GetLogicalDescendants().OfType<TextBlock>().Where(text => TextHighlight.Mark(text, words)).ToList();
        if (marked.Count > 0)
            Dispatcher.UIThread.Post(() => marked[0].BringIntoView(), DispatcherPriority.Background);
    }

    private static void AddBlocks(Panel panel, ContainerBlock container)
    {
        foreach (var block in container)
        {
            if (Build(block) is { } control)
                panel.Children.Add(control);
        }
    }

    private static Control? Build(Block block) => block switch
    {
        HeadingBlock heading => Text(heading.Inline, $"md-h{Math.Clamp(heading.Level, 1, 6)}"),
        ParagraphBlock paragraph => Text(paragraph.Inline, "md-paragraph"),
        ListBlock list => List(list),
        QuoteBlock quote => Quote(quote),
        CodeBlock code => Code(code),
        ThematicBreakBlock => new Border { Classes = { "md-rule" } },
        Table table => Table(table),
        LinkReferenceDefinitionGroup => null,
        ContainerBlock other => Stack(other, "md-group"),
        LeafBlock leaf when leaf.Inline is not null => Text(leaf.Inline, "md-paragraph"),
        _ => null,
    };

    private static StackPanel Stack(ContainerBlock container, string style)
    {
        var panel = new StackPanel { Classes = { style } };
        AddBlocks(panel, container);
        return panel;
    }

    private static SelectableTextBlock Text(ContainerInline? inlines, string style)
    {
        var text = new SelectableTextBlock { Classes = { style }, TextWrapping = TextWrapping.Wrap, Inlines = [] };
        if (inlines is null)
            return text;

        var builder = new InlineBuilder();
        builder.Add(text.Inlines!, inlines);
        if (builder.Links.Count > 0)
        {
            text.Tag = builder.Links;
            text.PointerMoved += OnTextPointerMoved;
            text.AddHandler(PointerReleasedEvent, OnTextPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        return text;
    }

    private static Control List(ListBlock list)
    {
        var panel = new StackPanel { Classes = { "md-list" } };
        var number = 1;
        if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start))
            number = start;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            // A task's box takes the place of the bullet.
            var task = (item.FirstOrDefault() as ParagraphBlock)?.Inline?.FirstChild as TaskList;
            var marker = task is not null ? task.Checked ? "☑" : "☐"
                : list.IsOrdered ? $"{number}{list.OrderedDelimiter}"
                : "•";
            number++;

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var markerText = new TextBlock { Classes = { "md-marker" }, Text = marker };
            markerText.Classes.Set("task", task is not null);
            row.Children.Add(markerText);
            var content = Stack(item, "md-item");
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Control Quote(QuoteBlock quote) => new Border
    {
        Classes = { "md-quote" },
        Child = Stack(quote, "md-group"),
    };

    private static Control Code(CodeBlock code)
    {
        var text = string.Join("\n", code.Lines.Lines.Take(code.Lines.Count).Select(l => l.ToString()));
        return new Border
        {
            Classes = { "md-code" },
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new SelectableTextBlock { Classes = { "md-code" }, Text = text },
            },
        };
    }

    private static Control Table(Table table)
    {
        var grid = new Grid { Classes = { "md-table" } };
        var columns = Math.Max(table.ColumnDefinitions.Count, table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max());
        for (var c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var rowIndex = 0;
        foreach (var row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var columnIndex = 0;
            foreach (var cell in row.OfType<TableCell>())
            {
                var column = cell.ColumnIndex >= 0 ? cell.ColumnIndex : columnIndex;
                var content = Stack(cell, "md-group");
                var alignment = column < table.ColumnDefinitions.Count ? table.ColumnDefinitions[column].Alignment : null;
                foreach (var text in content.Children.OfType<SelectableTextBlock>())
                {
                    text.TextAlignment = alignment switch
                    {
                        TableColumnAlign.Center => TextAlignment.Center,
                        TableColumnAlign.Right => TextAlignment.Right,
                        _ => TextAlignment.Left,
                    };
                }

                var border = new Border { Classes = { "md-cell" }, Child = content };
                border.Classes.Set("header", row.IsHeader);
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, column);
                Grid.SetColumnSpan(border, Math.Max(1, cell.ColumnSpan));
                grid.Children.Add(border);
                columnIndex = column + Math.Max(1, cell.ColumnSpan);
            }

            rowIndex++;
        }

        // A wide table scrolls sideways instead of squeezing the page.
        return new ScrollViewer
        {
            Classes = { "md-table" },
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = grid,
        };
    }

    /// <summary>
    /// Turns a paragraph's inlines into runs, noting where each link's text starts and ends so a
    /// click on it can be found. Every piece of text is a <see cref="Run"/>, line breaks included,
    /// so those positions match the text block's own.
    /// </summary>
    private sealed class InlineBuilder
    {
        private int _offset;

        public List<LinkRange> Links { get; } = [];

        public void Add(InlineCollection target, ContainerInline container)
        {
            foreach (var inline in container)
            {
                if (Build(inline) is { } built)
                    target.Add(built);
            }
        }

        private Run Text(string text, string? style = null)
        {
            _offset += text.Length;
            var run = new Run(text);
            if (style is not null)
                run.Classes.Add(style);
            return run;
        }

        private Avalonia.Controls.Documents.Inline? Build(MdInline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    return Text(literal.Content.ToString());

                case CodeInline code:
                    return Text(code.Content, "md-code");

                case LineBreakInline lineBreak:
                    return Text(lineBreak.IsHard ? "\n" : " ");

                case TaskList:
                    // Drawn as the list item's marker.
                    return null;

                case HtmlEntityInline entity:
                    return Text(entity.Transcoded.ToString());

                case HtmlInline html:
                    return Text(html.Tag, "md-html");

                case AutolinkInline autolink:
                {
                    var start = _offset;
                    var span = new Span { Classes = { "md-link" }, Inlines = { Text(autolink.Url) } };
                    AddLink(start, autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url);
                    return span;
                }

                case LinkInline { IsImage: true } image:
                    return Text($"[{PlainText(image)}]", "md-html");

                case LinkInline link:
                {
                    var start = _offset;
                    var span = new Span { Classes = { "md-link" } };
                    Add(span.Inlines, link);
                    if (_offset == start)
                        span.Inlines.Add(Text(link.Url ?? ""));
                    AddLink(start, link.GetDynamicUrl?.Invoke() ?? link.Url);
                    return span;
                }

                case EmphasisInline emphasis:
                {
                    Span span = (emphasis.DelimiterChar, emphasis.DelimiterCount) switch
                    {
                        ('*' or '_', >= 2) => new Bold(),
                        ('*' or '_', _) => new Italic(),
                        ('~', >= 2) => new Span { TextDecorations = TextDecorations.Strikethrough },
                        ('+', >= 2) => new Underline(),
                        // ==marked==, and ~sub~ and ^super^ scripts, which are not drawn smaller
                        _ => new Span { Classes = { "md-mark" } },
                    };
                    Add(span.Inlines, emphasis);
                    return span;
                }

                case ContainerInline other:
                {
                    var span = new Span();
                    Add(span.Inlines, other);
                    return span;
                }

                default:
                    return null;
            }
        }

        // Only absolute addresses open; a relative one is shown as a link that does nothing.
        private void AddLink(int start, string? url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                Links.Add(new LinkRange(start, _offset, uri));
        }
    }

    private sealed record LinkRange(int Start, int End, Uri Uri);

    private static LinkRange? LinkAt(SelectableTextBlock text, PointerEventArgs e)
    {
        if (text.Tag is not List<LinkRange> { Count: > 0 } links)
            return null;

        var point = e.GetPosition(text) - new Point(text.Padding.Left, text.Padding.Top);
        var hit = text.TextLayout.HitTestPoint(point);
        if (!hit.IsInside)
            return null;
        var position = hit.TextPosition;
        return links.FirstOrDefault(l => position >= l.Start && position < l.End);
    }

    private static void OnTextPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is SelectableTextBlock text)
            text.Cursor = LinkAt(text, e) is not null ? HandCursor : null;
    }

    // A click on a link opens it; a drag that selects text does not.
    private static void OnTextPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not SelectableTextBlock text || e.InitialPressMouseButton != MouseButton.Left
            || text.SelectionStart != text.SelectionEnd || LinkAt(text, e) is not { } link)
            return;

        var clicked = new LinkClickedEventArgs(LinkClickedEvent, link.Uri);
        text.RaiseEvent(clicked);
        if (!clicked.Handled)
            TopLevel.GetTopLevel(text)?.Launcher.LaunchUriAsync(link.Uri);
    }

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private static string PlainText(ContainerInline container) => string.Concat(container.Select(i => i switch
    {
        LiteralInline literal => literal.Content.ToString(),
        CodeInline code => code.Content,
        ContainerInline child => PlainText(child),
        _ => "",
    }));
}

public sealed class LinkClickedEventArgs(RoutedEvent routedEvent, Uri uri) : RoutedEventArgs(routedEvent)
{
    public Uri Uri { get; } = uri;
}
