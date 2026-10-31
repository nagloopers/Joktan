using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace Joktan.Core.Controls;

/// <summary>Marks the words someone searched for in a text block, in the look of the <c>search-match</c> class.</summary>
public sealed class TextHighlight : AvaloniaObject
{
    public const string MatchClass = "search-match";

    /// <summary>The text to show, in place of the block's own Text.</summary>
    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<TextHighlight, TextBlock, string?>("Source");

    public static readonly AttachedProperty<IReadOnlyList<string>?> WordsProperty =
        AvaloniaProperty.RegisterAttached<TextHighlight, TextBlock, IReadOnlyList<string>?>("Words");

    static TextHighlight()
    {
        SourceProperty.Changed.AddClassHandler<TextBlock>((text, _) => Show(text));
        WordsProperty.Changed.AddClassHandler<TextBlock>((text, _) => Show(text));
    }

    public static string? GetSource(TextBlock text) => text.GetValue(SourceProperty);

    public static void SetSource(TextBlock text, string? value) => text.SetValue(SourceProperty, value);

    public static IReadOnlyList<string>? GetWords(TextBlock text) => text.GetValue(WordsProperty);

    public static void SetWords(TextBlock text, IReadOnlyList<string>? value) => text.SetValue(WordsProperty, value);

    /// <summary>Splits the runs of a text block, its plain Text included, around the words. The text itself stays the same. Returns whether any were found.</summary>
    public static bool Mark(TextBlock text, IReadOnlyList<string> words)
    {
        if (text.Inlines is not { Count: > 0 })
        {
            if (string.IsNullOrEmpty(text.Text) || TextSearch.Ranges(text.Text, words).Count == 0)
                return false;

            var whole = text.Text;
            text.Inlines = [new Run(whole)];
        }

        return Mark(text.Inlines!, words);
    }

    private static void Show(TextBlock text)
    {
        var inlines = new InlineCollection { new Run(GetSource(text) ?? "") };
        text.Inlines = inlines;
        if (GetWords(text) is { } words)
            Mark(inlines, words);
    }

    private static bool Mark(InlineCollection inlines, IReadOnlyList<string> words)
    {
        var found = false;
        for (var i = inlines.Count - 1; i >= 0; i--)
        {
            if (inlines[i] is Span span)
            {
                found |= Mark(span.Inlines, words);
            }
            else if (inlines[i] is Run { Text: { } text } run && TextSearch.Ranges(text, words) is { Count: > 0 } ranges)
            {
                found = true;
                inlines.RemoveAt(i);
                inlines.InsertRange(i, Split(run, text, ranges));
            }
        }

        return found;
    }

    private static IEnumerable<Run> Split(Run run, string text, IReadOnlyList<(int Start, int Length)> ranges)
    {
        var at = 0;
        foreach (var (start, length) in ranges)
        {
            if (start > at)
                yield return Part(run, text[at..start]);

            var match = Part(run, text.Substring(start, length));
            match.Classes.Add(MatchClass);
            yield return match;
            at = start + length;
        }

        if (at < text.Length)
            yield return Part(run, text[at..]);
    }

    private static Run Part(Run run, string text)
    {
        var part = new Run(text);
        part.Classes.AddRange(run.Classes.Where(c => !c.StartsWith(':')));
        return part;
    }
}
