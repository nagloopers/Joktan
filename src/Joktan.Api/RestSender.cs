using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Joktan.Api.Resources;

namespace Joktan.Api;

/// <summary>What came back from a request.</summary>
public sealed class ApiResponse
{
    /// <summary>Such as "200 OK" or "OK" for gRPC; the error for a request that never got an answer.</summary>
    public string Status { get; init; } = "";

    public bool IsSuccess { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>The size of the body, or of all the messages, in bytes.</summary>
    public long Size { get; init; }

    public IReadOnlyList<ApiPair> Headers { get; init; } = [];

    /// <summary>gRPC trailers.</summary>
    public IReadOnlyList<ApiPair> Trailers { get; init; } = [];

    /// <summary>The body, formatted when it is JSON or XML.</summary>
    public string Body { get; init; } = "";

    public string ContentType { get; init; } = "";

    /// <summary>Whether <see cref="Body"/> holds only the start of a body too big to show.</summary>
    public bool IsCutShort { get; init; }
}

/// <summary>Replaces <c>{{name}}</c> with the variable's value. Names that are not defined are left as they are.</summary>
public sealed partial class ApiVariables(IEnumerable<ApiPair> variables)
{
    private readonly Dictionary<string, string> _values = variables
        .Where(v => v.Enabled && v.Name.Trim().Length > 0)
        .GroupBy(v => v.Name.Trim(), StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);

    public static ApiVariables None { get; } = new([]);

    public string Apply(string text)
        => Reference().Replace(text, m => _values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    /// <summary>The names used in the text that no variable defines.</summary>
    public IEnumerable<string> Missing(string text)
        => Reference().Matches(text).Select(m => m.Groups[1].Value).Where(n => !_values.ContainsKey(n)).Distinct();

    [GeneratedRegex(@"\{\{\s*([^{}\s]+)\s*\}\}")]
    private static partial Regex Reference();
}

/// <summary>Sends REST requests.</summary>
public static class RestSender
{
    public static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    // Bodies bigger than this are cut short in the response view.
    private const int MaxShownBody = 2 * 1024 * 1024;

    // Kept for as long as the app runs, so that connections are reused. There are two so that a
    // connection made without checking the certificate never carries a request that wants it checked.
    private static readonly HttpClient CheckingClient = NewClient(false);
    private static readonly HttpClient TrustingClient = NewClient(true);

    public static async Task<ApiResponse> SendAsync(ApiRequest request, ApiVariables variables, CancellationToken cancel)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var message = Build(request, variables);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)));
            var client = request.AcceptAnyCertificate ? TrustingClient : CheckingClient;

            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var bytes = await response.Content.ReadAsByteArrayAsync(cancel);
            stopwatch.Stop();

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var headers = response.Headers.Concat(response.Content.Headers)
                .SelectMany(h => h.Value.Select(v => new ApiPair { Name = h.Key, Value = v }))
                .ToList();
            return new ApiResponse
            {
                Status = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim(),
                IsSuccess = response.IsSuccessStatusCode,
                Elapsed = stopwatch.Elapsed,
                Size = bytes.Length,
                Headers = headers,
                ContentType = contentType,
                IsCutShort = bytes.Length > MaxShownBody,
                Body = Format(Decode(bytes, response.Content.Headers.ContentType?.CharSet), contentType, bytes.Length),
            };
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return Failed(Strings.RestSender_Cancelled, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return Failed(string.Format(Strings.RestSender_NoAnswerWithinSeconds, request.TimeoutSeconds), stopwatch.Elapsed);
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or UriFormatException or FormatException)
        {
            return Failed(Message(e), stopwatch.Elapsed);
        }
    }

    private static HttpClient NewClient(bool acceptAnyCertificate)
    {
        // Without cookies, since what one request is given must not go out with the next. Connections
        // are renewed now and then to notice an address that has moved. Each request has its own timeout.
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };
        if (acceptAnyCertificate)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>The request as it is sent, with variables filled in.</summary>
    public static HttpRequestMessage Build(ApiRequest request, ApiVariables variables)
    {
        var url = variables.Apply(request.Url.Trim());
        if (url.Length == 0)
            throw new InvalidOperationException(Strings.RestSender_EnterTheAddressToSendThe);
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "http://" + url;

        var query = request.Query.Where(p => p.Enabled && p.Name.Trim().Length > 0)
            .Select(p => Uri.EscapeDataString(variables.Apply(p.Name.Trim())) + "=" + Uri.EscapeDataString(variables.Apply(p.Value)))
            .ToList();
        if (query.Count > 0)
            url += (url.Contains('?') ? "&" : "?") + string.Join("&", query);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(string.Format(Strings.RestSender_IsNotAnHttpOrHttps, url));

        var message = new HttpRequestMessage(new HttpMethod(request.Method.Trim().ToUpperInvariant()), uri);
        message.Content = Body(request, variables);

        foreach (var header in request.Headers.Where(h => h.Enabled && h.Name.Trim().Length > 0))
        {
            var name = variables.Apply(header.Name.Trim());
            var value = variables.Apply(header.Value);
            if (!message.Headers.TryAddWithoutValidation(name, value) && message.Content is not null)
            {
                // Content headers, such as Content-Type, belong to the body.
                message.Content.Headers.Remove(name);
                message.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        switch (request.AuthKind)
        {
            case ApiAuthKind.Bearer:
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", variables.Apply(request.Token.Trim()));
                break;
            case ApiAuthKind.Basic:
                var pair = $"{variables.Apply(request.Username)}:{variables.Apply(request.Password)}";
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)));
                break;
        }

        return message;
    }

    private static HttpContent? Body(ApiRequest request, ApiVariables variables) => request.BodyKind switch
    {
        ApiBodyKind.Json => new StringContent(variables.Apply(request.Body), Encoding.UTF8, "application/json"),
        ApiBodyKind.Text => new StringContent(variables.Apply(request.Body), Encoding.UTF8, "text/plain"),
        ApiBodyKind.Xml => new StringContent(variables.Apply(request.Body), Encoding.UTF8, "application/xml"),
        ApiBodyKind.Form => new FormUrlEncodedContent(request.Form
            .Where(p => p.Enabled && p.Name.Trim().Length > 0)
            .Select(p => new KeyValuePair<string, string>(variables.Apply(p.Name.Trim()), variables.Apply(p.Value)))),
        _ => null,
    };

    private static string Decode(byte[] bytes, string? charset)
    {
        var encoding = Encoding.UTF8;
        try
        {
            if (!string.IsNullOrEmpty(charset))
                encoding = Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            // An unknown character set is read as UTF-8.
        }

        return encoding.GetString(bytes, 0, Math.Min(bytes.Length, MaxShownBody));
    }

    /// <summary>Indents JSON and XML; other text is shown as it is.</summary>
    public static string Format(string body, string contentType, long size = 0)
    {
        var formatted = body;
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || LooksLikeJson(body))
            formatted = TryIndentJson(body) ?? body;
        else if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                formatted = XDocument.Parse(body).ToString();
            }
            catch (System.Xml.XmlException)
            {
                // Not well-formed; shown as it came.
            }
        }

        return size > MaxShownBody ? formatted + string.Format(Strings.RestSender_CutShortTheBodyIsBytes, size) : formatted;
    }

    public static string? TryIndentJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, IndentedJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static bool LooksLikeJson(string body) => body.TrimStart() is { Length: > 0 } t && (t[0] == '{' || t[0] == '[');

    private static ApiResponse Failed(string status, TimeSpan elapsed) => new() { Status = status, Elapsed = elapsed };

    // The innermost message usually says what actually went wrong, such as a refused connection.
    public static string Message(Exception e)
    {
        var messages = new List<string>();
        for (var inner = e; inner is not null; inner = inner.InnerException)
        {
            if (!messages.Any(m => m.Contains(inner.Message, StringComparison.Ordinal)))
                messages.Add(inner.Message);
        }

        return string.Join(" ", messages);
    }
}
