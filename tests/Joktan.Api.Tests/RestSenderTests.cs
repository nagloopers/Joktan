using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Joktan.Api.Tests;

public class ApiVariablesTests
{
    private static ApiVariables Variables(params (string Name, string Value)[] pairs)
        => new(pairs.Select(p => new ApiPair { Name = p.Name, Value = p.Value }));

    [Theory]
    [InlineData("{{a}}", "1")]
    [InlineData("x{{a}}y{{a}}", "x1y1")]
    [InlineData("{{ a }}", "1")]
    [InlineData("{{b}}", "{{b}}")]
    [InlineData("{a}", "{a}")]
    [InlineData("{{a b}}", "{{a b}}")]
    [InlineData("", "")]
    public void Fills_in_what_is_defined(string text, string expected)
        => Assert.Equal(expected, Variables(("a", "1")).Apply(text));

    [Fact]
    public void Names_are_trimmed_and_match_case()
    {
        var variables = Variables((" a ", "1"));

        Assert.Equal("1", variables.Apply("{{a}}"));
        Assert.Equal("{{A}}", variables.Apply("{{A}}"));
    }

    [Fact]
    public void The_last_of_a_name_wins_and_what_is_off_or_unnamed_does_not_count()
    {
        var variables = new ApiVariables(
        [
            new ApiPair { Name = "a", Value = "1" },
            new ApiPair { Name = "a", Value = "2" },
            new ApiPair { Name = "a", Value = "3", Enabled = false },
            new ApiPair { Name = "off", Value = "x", Enabled = false },
            new ApiPair { Name = " ", Value = "x" },
        ]);

        Assert.Equal("2", variables.Apply("{{a}}"));
        Assert.Equal("{{off}}", variables.Apply("{{off}}"));
    }

    [Fact]
    public void A_filled_in_value_is_not_filled_in_again()
    {
        Assert.Equal("{{b}}", Variables(("a", "{{b}}"), ("b", "2")).Apply("{{a}}"));
    }

    [Fact]
    public void Lists_each_name_that_is_not_defined_once()
    {
        Assert.Equal(["b", "c"], Variables(("a", "1")).Missing("{{a}} {{b}} {{ c }} {{b}}"));
        Assert.Empty(ApiVariables.None.Missing("plain"));
    }

    [Fact]
    public void None_leaves_everything_as_it_is()
    {
        Assert.Equal("{{a}}", ApiVariables.None.Apply("{{a}}"));
    }
}

public class RestSenderBuildTests
{
    private static ApiVariables Vars(params (string Name, string Value)[] pairs)
        => new(pairs.Select(p => new ApiPair { Name = p.Name, Value = p.Value }));

    private static HttpRequestMessage Build(ApiRequest request) => RestSender.Build(request, ApiVariables.None);

    [Fact]
    public void Adds_http_when_no_scheme_is_given()
    {
        Assert.Equal("http://example.com/", Build(new ApiRequest { Url = " example.com " }).RequestUri!.ToString());
        Assert.Equal("https://example.com/", Build(new ApiRequest { Url = "https://example.com" }).RequestUri!.ToString());
    }

    [Theory]
    [InlineData("", "Enter the address to send the request to.")]
    [InlineData("   ", "Enter the address to send the request to.")]
    [InlineData("ftp://example.com", "ftp://example.com is not an http or https address.")]
    [InlineData("http://", "http:// is not an http or https address.")]
    public void Says_what_is_wrong_with_the_address(string url, string message)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Build(new ApiRequest { Url = url }));

        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData("get", "GET")]
    [InlineData(" Post ", "POST")]
    public void The_method_is_in_capitals(string method, string expected)
        => Assert.Equal(expected, Build(new ApiRequest { Url = "x", Method = method }).Method.Method);

    [Fact]
    public async Task Fills_in_variables_everywhere()
    {
        var request = new ApiRequest
        {
            Url = "{{host}}/path",
            Query = [new ApiPair { Name = "{{key}}", Value = "{{value}}" }],
            Headers = [new ApiPair { Name = "{{header}}", Value = "{{value}}" }],
            BodyKind = ApiBodyKind.Text,
            Body = "{{value}}",
        };

        using var message = RestSender.Build(request, Vars(("host", "h.com"), ("key", "k"), ("value", "v"), ("header", "X-H")));

        Assert.Equal("http://h.com/path?k=v", message.RequestUri!.ToString());
        Assert.Equal("v", message.Headers.GetValues("X-H").Single());
        Assert.Equal("v", await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Escapes_the_query_and_adds_it_to_one_already_there()
    {
        var request = new ApiRequest
        {
            Url = "h.com?x=1",
            Query =
            [
                new ApiPair { Name = "a b", Value = "c&d" },
                new ApiPair { Name = "off", Value = "1", Enabled = false },
                new ApiPair { Name = " ", Value = "1" },
            ],
        };

        Assert.Equal("?x=1&a%20b=c%26d", Build(request).RequestUri!.Query);
    }

    [Theory]
    [InlineData(ApiBodyKind.Json, "application/json")]
    [InlineData(ApiBodyKind.Text, "text/plain")]
    [InlineData(ApiBodyKind.Xml, "application/xml")]
    public void Sends_a_body_as_its_kind(ApiBodyKind kind, string mediaType)
    {
        var message = Build(new ApiRequest { Url = "x", BodyKind = kind, Body = "b" });

        Assert.Equal(mediaType, message.Content!.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", message.Content.Headers.ContentType.CharSet);
    }

    [Fact]
    public void No_body_kind_sends_no_body()
    {
        Assert.Null(Build(new ApiRequest { Url = "x", Body = "ignored" }).Content);
    }

    [Fact]
    public async Task A_form_is_sent_encoded_without_what_is_off()
    {
        var message = Build(new ApiRequest
        {
            Url = "x",
            BodyKind = ApiBodyKind.Form,
            Form =
            [
                new ApiPair { Name = "a b", Value = "c&d" },
                new ApiPair { Name = "off", Value = "1", Enabled = false },
            ],
        });

        Assert.Equal("a+b=c%26d", await message.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Headers_that_are_off_or_unnamed_are_left_out()
    {
        var message = Build(new ApiRequest
        {
            Url = "x",
            Headers = [new ApiPair { Name = "X-Off", Value = "1", Enabled = false }, new ApiPair { Name = " ", Value = "1" }],
        });

        Assert.Empty(message.Headers);
    }

    [Fact]
    public void A_content_header_goes_on_the_body_and_replaces_the_one_there()
    {
        var message = Build(new ApiRequest
        {
            Url = "x",
            BodyKind = ApiBodyKind.Json,
            Body = "{}",
            Headers = [new ApiPair { Name = "Content-Type", Value = "application/vnd.api+json" }],
        });

        Assert.Equal("application/vnd.api+json", message.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void Bearer_sends_the_token()
    {
        var message = RestSender.Build(new ApiRequest { Url = "x", AuthKind = ApiAuthKind.Bearer, Token = " {{t}} " }, Vars(("t", "abc")));

        Assert.Equal("Bearer abc", message.Headers.Authorization!.ToString());
    }

    [Fact]
    public void Basic_sends_the_name_and_password_encoded()
    {
        var message = Build(new ApiRequest { Url = "x", AuthKind = ApiAuthKind.Basic, Username = "me", Password = "pä:ss" });

        var parameter = Encoding.UTF8.GetString(Convert.FromBase64String(message.Headers.Authorization!.Parameter!));
        Assert.Equal("Basic", message.Headers.Authorization.Scheme);
        Assert.Equal("me:pä:ss", parameter);
    }

    [Fact]
    public void Offers_the_usual_methods()
    {
        Assert.Equal(["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"], RestSender.Methods);
    }
}

public class RestSenderFormatTests
{
    [Fact]
    public void Indents_JSON_by_its_content_type_or_its_look()
    {
        Assert.Equal("{\n  \"a\": 1\n}", RestSender.Format("""{"a":1}""", "application/json").ReplaceLineEndings("\n"));
        Assert.Equal("{\n  \"a\": 1\n}", RestSender.Format("""  {"a":1}""", "text/plain").ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Leaves_what_only_looks_like_JSON()
    {
        Assert.Equal("{nope", RestSender.Format("{nope", "application/json"));
        Assert.Equal("[nope", RestSender.Format("[nope", "text/plain"));
    }

    [Fact]
    public void Indents_XML()
    {
        Assert.Contains("\n", RestSender.Format("<a><b>1</b></a>", "application/xml"));
        Assert.Equal("<a><b>", RestSender.Format("<a><b>", "text/xml"));
    }

    [Fact]
    public void Leaves_other_text()
    {
        Assert.Equal("<a><b>1</b></a>", RestSender.Format("<a><b>1</b></a>", "text/plain"));
        Assert.Equal("", RestSender.Format("", ""));
    }

    [Fact]
    public void Says_when_a_body_was_cut_short()
    {
        Assert.StartsWith("text\n\n… cut short: the body is ", RestSender.Format("text", "text/plain", 3 * 1024 * 1024));
        Assert.Equal("text", RestSender.Format("text", "text/plain", 2 * 1024 * 1024));
    }

    [Fact]
    public void Keeps_JSON_text_readable()
    {
        Assert.Equal("{\n  \"a\": \"Grüße\"\n}", RestSender.TryIndentJson("""{"a":"Grüße"}""")!.ReplaceLineEndings("\n"));
        Assert.Null(RestSender.TryIndentJson("nope"));
    }

    [Fact]
    public void Messages_include_the_inner_reason_once()
    {
        var error = new HttpRequestException("Outer failed", new IOException("Refused"));
        var repeated = new HttpRequestException("Refused here", new IOException("Refused"));

        Assert.Equal("Outer failed Refused", RestSender.Message(error));
        Assert.Equal("Refused here", RestSender.Message(repeated));
    }
}

public class RestSenderSendTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    // Answers one request on this machine with the response given, or not at all if it is null.
    private static async Task<(string Url, Task<string> Received)> ServeOnceAsync(byte[]? response)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/path";
        var received = Task.Run(async () =>
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(CancellationToken.None);
                var stream = client.GetStream();
                var text = new StringBuilder();
                var buffer = new byte[4096];
                while (!text.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer, CancellationToken.None);
                    if (read == 0)
                        break;
                    text.Append(Encoding.Latin1.GetString(buffer, 0, read));
                }

                if (response is not null)
                    await stream.WriteAsync(response, CancellationToken.None);
                else
                    await Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None);
                return text.ToString();
            }
            finally
            {
                listener.Stop();
            }
        }, CancellationToken.None);

        return await Task.FromResult((url, received));
    }

    private static byte[] Response(string status, string contentType, byte[] body)
    {
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nX-Test: yes\r\nConnection: close\r\n\r\n");
        return [.. head, .. body];
    }

    [Fact]
    public async Task Reads_what_comes_back()
    {
        var (url, received) = await ServeOnceAsync(Response("200 OK", "application/json", """{"a":1}"""u8.ToArray()));

        var response = await RestSender.SendAsync(
            new ApiRequest { Url = url, Headers = [new ApiPair { Name = "X-Sent", Value = "here" }] }, ApiVariables.None, Cancel);

        Assert.True(response.IsSuccess);
        Assert.Equal("200 OK", response.Status);
        Assert.Equal("application/json", response.ContentType);
        Assert.Equal(7, response.Size);
        Assert.False(response.IsCutShort);
        Assert.Contains("\"a\": 1", response.Body);
        Assert.Contains(response.Headers, h => h.Name == "X-Test" && h.Value == "yes");
        Assert.Contains("X-Sent: here", await received);
    }

    [Fact]
    public async Task An_error_status_is_an_answer_but_not_a_success()
    {
        var (url, _) = await ServeOnceAsync(Response("404 Not Found", "text/plain", "gone"u8.ToArray()));

        var response = await RestSender.SendAsync(new ApiRequest { Url = url }, ApiVariables.None, Cancel);

        Assert.False(response.IsSuccess);
        Assert.Equal("404 Not Found", response.Status);
        Assert.Equal("gone", response.Body);
    }

    [Fact]
    public async Task Reads_the_character_set_the_response_names()
    {
        var (url, _) = await ServeOnceAsync(Response("200 OK", "text/plain; charset=iso-8859-1", Encoding.Latin1.GetBytes("Grüße")));

        var response = await RestSender.SendAsync(new ApiRequest { Url = url }, ApiVariables.None, Cancel);

        Assert.Equal("Grüße", response.Body);
    }

    [Fact]
    public async Task An_unknown_character_set_is_read_as_UTF8()
    {
        var (url, _) = await ServeOnceAsync(Response("200 OK", "text/plain; charset=nonsense", Encoding.UTF8.GetBytes("Grüße")));

        var response = await RestSender.SendAsync(new ApiRequest { Url = url }, ApiVariables.None, Cancel);

        Assert.Equal("Grüße", response.Body);
    }

    [Fact]
    public async Task A_huge_body_is_cut_short()
    {
        var size = 2 * 1024 * 1024 + 1;
        var body = new byte[size];
        Array.Fill(body, (byte)'x');
        var (url, _) = await ServeOnceAsync(Response("200 OK", "text/plain", body));

        var response = await RestSender.SendAsync(new ApiRequest { Url = url }, ApiVariables.None, Cancel);

        Assert.True(response.IsCutShort);
        Assert.Equal(size, response.Size);
        Assert.Contains("… cut short", response.Body);
    }

    [Fact]
    public async Task A_refused_connection_is_reported_as_the_status()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var response = await RestSender.SendAsync(new ApiRequest { Url = $"http://127.0.0.1:{port}" }, ApiVariables.None, Cancel);

        Assert.False(response.IsSuccess);
        Assert.NotEmpty(response.Status);
    }

    [Fact]
    public async Task A_bad_address_is_reported_as_the_status()
    {
        var response = await RestSender.SendAsync(new ApiRequest { Url = "" }, ApiVariables.None, Cancel);

        Assert.False(response.IsSuccess);
        Assert.Equal("Enter the address to send the request to.", response.Status);
    }

    [Fact]
    public async Task Cancelling_says_so()
    {
        var (url, _) = await ServeOnceAsync(null);
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        var response = await RestSender.SendAsync(new ApiRequest { Url = url }, ApiVariables.None, cancel.Token);

        Assert.Equal("Cancelled", response.Status);
        Assert.False(response.IsSuccess);
    }

    [Fact]
    public async Task A_server_that_does_not_answer_times_out()
    {
        var (url, _) = await ServeOnceAsync(null);

        var response = await RestSender.SendAsync(new ApiRequest { Url = url, TimeoutSeconds = 1 }, ApiVariables.None, Cancel);

        Assert.Equal("No answer within 1 seconds", response.Status);
    }

    [Fact]
    public async Task A_cookie_from_one_request_does_not_go_out_with_the_next()
    {
        await using var server = await FakeServer.StartAsync();

        await RestSender.SendAsync(new ApiRequest { Url = server.RestAddress + "/cookie" }, ApiVariables.None, Cancel);
        var response = await RestSender.SendAsync(new ApiRequest { Url = server.RestAddress + "/next" }, ApiVariables.None, Cancel);

        Assert.True(response.IsSuccess);
        Assert.Null(server.LastRestCookie);
    }

    [Fact]
    public async Task A_cookie_header_the_user_wrote_is_sent()
    {
        await using var server = await FakeServer.StartAsync();
        var request = new ApiRequest { Url = server.RestAddress + "/next", Headers = [new ApiPair { Name = "Cookie", Value = "a=b" }] };

        await RestSender.SendAsync(request, ApiVariables.None, Cancel);

        Assert.Equal("a=b", server.LastRestCookie);
    }
}
