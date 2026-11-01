using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Joktan.Api.Resources;

namespace Joktan.Api;

/// <summary>
/// Reads values out of a response, to be saved in environment variables. Each capture is a pair of
/// the variable's name and where the value is: a path into a JSON body, such as
/// <c>data.items[0].id</c>, or nothing for the whole body.
/// </summary>
public static partial class ResponseCapture
{
    /// <summary>The values found, by variable name, and why any others could not be read.</summary>
    public static (List<ApiPair> Values, List<string> Problems) Read(ApiResponse response, IEnumerable<ApiPair> captures)
    {
        var values = new List<ApiPair>();
        var problems = new List<string>();
        var wanted = captures.Where(c => c.Enabled)
            .Select(c => (Name: c.Name.Trim().Trim('{', '}').Trim(), Path: c.Value.Trim()))
            .Where(c => c.Name.Length > 0)
            .ToList();
        if (wanted.Count == 0)
            return (values, problems);

        if (response.IsCutShort)
        {
            problems.Add(string.Format(Strings.ResponseCapture_TheBodyIsTooBigTo, string.Join(", ", wanted.Select(c => c.Name))));
            return (values, problems);
        }

        using var document = Parse(response.Body);
        foreach (var (name, path) in wanted)
        {
            if (Find(response.Body, document, path, out var problem) is { } value)
                values.Add(new ApiPair { Name = name, Value = value });
            else
                problems.Add(string.Format(Strings.ResponseCapture_DidNotChange, name, problem));
        }

        return (values, problems);
    }

    private static JsonDocument? Parse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Find(string body, JsonDocument? document, string path, out string problem)
    {
        problem = "";
        if (path.Length == 0 || path == "$")
        {
            if (body.Length == 0)
                problem = Strings.ResponseCapture_TheResponseHasNoBody;
            return body.Length == 0 ? null : document is null ? body : Text(document.RootElement);
        }

        if (document is null)
        {
            problem = string.Format(Strings.ResponseCapture_TheBodyIsNotJSONSo, path);
            return null;
        }

        var at = path.StartsWith('$') ? 1 : 0;
        var element = document.RootElement;
        while (at < path.Length)
        {
            var step = Step().Match(path, at);
            if (!step.Success || step.Length == 0)
            {
                problem = string.Format(Strings.ResponseCapture_IsNotAPathWriteIt, path);
                return null;
            }

            at += step.Length;
            var found = step.Groups[2].Success
                ? TryIndex(element, step.Groups[2].Value, out element)
                : TryProperty(element, step.Groups[step.Groups[1].Success ? 1 : 3].Value, out element);
            if (!found)
            {
                problem = string.Format(Strings.ResponseCapture_TheResponseHasNo, path);
                return null;
            }
        }

        if (element.ValueKind == JsonValueKind.Null)
        {
            problem = string.Format(Strings.ResponseCapture_IsNullInTheResponse, path);
            return null;
        }

        return Text(element);
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
    }

    private static bool TryIndex(JsonElement element, string index, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Array || !int.TryParse(index, out var i) || i >= element.GetArrayLength())
            return false;
        value = element[i];
        return true;
    }

    // Text is saved without its quotes; anything else as its JSON.
    private static string Text(JsonElement element)
        => element.ValueKind == JsonValueKind.String ? element.GetString()! : element.GetRawText();

    // One step of a path: .name or name, [0], or ["a name.with dots"].
    [GeneratedRegex(@"\G(?:\.?([^.\[\]\s""]+)|\[(\d+)\]|\[""([^""]+)""\])")]
    private static partial Regex Step();
}
