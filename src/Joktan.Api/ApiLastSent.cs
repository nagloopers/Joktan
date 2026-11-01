using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Joktan.Api.Resources;
using Joktan.Core;

namespace Joktan.Api;

/// <summary>A request as it was last sent, with its <c>{{variables}}</c> as written.</summary>
public sealed class ApiSentRequest
{
    public DateTime Time { get; set; }

    public ApiRequest Request { get; set; } = new();

    /// <summary>What was sent, to read; tokens and passwords are left out.</summary>
    public string Describe()
    {
        var lines = new List<string>();
        if (Request.Kind == ApiKind.Rest)
        {
            lines.Add($"{Request.Method} {Request.Url}");
            Add(Strings.ApiSentRequest_Query, Request.Query);
            Add(Strings.ApiSentRequest_Header, Request.Headers);
            if (Request.AuthKind != ApiAuthKind.None)
                lines.Add(string.Format(Strings.ApiSentRequest_Auth, Request.AuthKind));
            if (Request.BodyKind == ApiBodyKind.Form)
                Add(Strings.ApiSentRequest_Field, Request.Form);
            else if (Request.BodyKind != ApiBodyKind.None)
                lines.AddRange(["", Request.Body]);
        }
        else
        {
            lines.Add(string.Format(Strings.ApiSentRequest_At, Request.Service, Request.Rpc, Request.Address));
            Add(Strings.ApiSentRequest_Metadata, Request.Metadata);
            lines.AddRange(["", Request.Message]);
        }

        return string.Join("\n", lines);

        void Add(string label, List<ApiPair> pairs)
            => lines.AddRange(pairs.Where(p => p.Enabled && p.Name.Trim().Length > 0).Select(p => $"{label}: {p.Name} = {p.Value}"));
    }
}

/// <summary>
/// The request last sent from each request file, kept in the data folder rather than beside the
/// files, so sending does not change what is in version control.
/// </summary>
public static class ApiLastSent
{
    private static string StorePath => Path.Combine(DataFolder.Path, "api-last-sent.json");

    public static ApiSentRequest? Read(string requestPath)
        => Load().GetValueOrDefault(requestPath);

    public static void Write(string requestPath, ApiSentRequest sent)
    {
        var all = Load();
        all[requestPath] = sent;
        Save(all);
    }

    /// <summary>Keeps what was sent when a request, or a folder of them, moves.</summary>
    public static void Moved(string from, string to)
    {
        var all = Load();
        var found = all.Keys.Where(k => IsAt(k, from)).ToList();
        foreach (var key in found)
        {
            all[to + key[from.Length..]] = all[key];
            all.Remove(key);
        }

        if (found.Count > 0)
            Save(all);
    }

    /// <summary>Forgets what was sent from a request, or a folder of them, that was deleted.</summary>
    public static void Forget(string path)
    {
        var all = Load();
        var found = all.Keys.Where(k => IsAt(k, path)).ToList();
        foreach (var key in found)
            all.Remove(key);
        if (found.Count > 0)
            Save(all);
    }

    private static bool IsAt(string key, string path)
        => key == path || key.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static Dictionary<string, ApiSentRequest> Load()
    {
        try
        {
            if (File.Exists(StorePath))
                return JsonSerializer.Deserialize<Dictionary<string, ApiSentRequest>>(File.ReadAllText(StorePath)) ?? [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable, nothing is remembered.
        }

        return [];
    }

    private static void Save(Dictionary<string, ApiSentRequest> all)
    {
        try
        {
            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(all, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not remembering it only means Resend is not offered next time.
        }
    }
}
