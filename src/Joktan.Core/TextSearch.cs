using System;
using System.Collections.Generic;
using System.Linq;

namespace Joktan.Core;

/// <summary>Something the search box found.</summary>
/// <param name="Area">Where it was found, such as "Notes".</param>
/// <param name="Text">The part of it that shows why it was found.</param>
/// <param name="Open">Goes to it.</param>
public sealed record SearchHit(string Area, string Title, string Text, Action Open);

/// <summary>How text is searched: every word typed has to appear, ignoring case.</summary>
public static class TextSearch
{
    private const int SnippetLength = 90;

    public static string[] Words(string? typed) => (typed ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Whether every word appears in at least one of the texts.</summary>
    public static bool HasAll(IReadOnlyList<string> words, params IEnumerable<string> texts)
        => words.All(word => texts.Any(text => Has(text, word)));

    public static bool Has(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Where the words appear in a text, in order, with overlapping places joined.</summary>
    public static IReadOnlyList<(int Start, int Length)> Ranges(string text, IReadOnlyList<string> words)
    {
        var found = new List<(int Start, int End)>();
        foreach (var word in words.Where(w => w.Length > 0))
        {
            for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(word, at + word.Length, StringComparison.OrdinalIgnoreCase))
                found.Add((at, at + word.Length));
        }

        var joined = new List<(int Start, int End)>();
        foreach (var (start, end) in found.OrderBy(f => f.Start))
        {
            if (joined.Count > 0 && start <= joined[^1].End)
                joined[^1] = (joined[^1].Start, Math.Max(joined[^1].End, end));
            else
                joined.Add((start, end));
        }

        return joined.Select(j => (j.Start, j.End - j.Start)).ToList();
    }

    /// <summary>The lines of a text with their spacing and table bars tidied, leaving out empty ones.</summary>
    public static string[] Lines(string text) => text.Split('\n')
        .Select(line => string.Join(' ', line.Replace('|', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
        .Where(line => line.Length > 0)
        .ToArray();

    /// <summary>The line holding the most of the words, cut down to the part around the first found.</summary>
    public static string Snippet(IEnumerable<string> lines, IReadOnlyList<string> words)
    {
        var line = lines.MaxBy(l => words.Count(word => Has(l, word))) ?? "";
        var found = words.Select(word => line.IndexOf(word, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(0).Min();

        var start = found <= SnippetLength / 3 ? 0 : found - SnippetLength / 3;
        if (start > 0 && line.IndexOf(' ', start, found - start) is >= 0 and var space)
            start = space + 1;

        var length = Math.Min(SnippetLength, line.Length - start);
        return (start > 0 ? "…" : "") + line.Substring(start, length) + (start + length < line.Length ? "…" : "");
    }
}
