using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Api.Tests;

public class GrpcCallerTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private FakeServer _server = null!;

    public async ValueTask InitializeAsync() => _server = await FakeServer.StartAsync();

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    private ApiRequest Request(string message = """{"name":"me"}""") => new()
    {
        Kind = ApiKind.Grpc,
        Address = _server.GrpcAddress,
        Message = message,
        TimeoutSeconds = 10,
    };

    private Task<ApiResponse> Call(string method, string message, CancellationToken? cancel = null) =>
        GrpcCaller.CallAsync(Request(message), _server.Method(method), ApiVariables.None, cancel ?? Cancel);

    [Fact]
    public async Task A_unary_call_shows_its_answer_headers_and_trailers()
    {
        var response = await Call("One", """{"name":"me"}""");

        Assert.True(response.IsSuccess);
        Assert.Equal("OK", response.Status);
        Assert.Equal("hello me", JsonNode.Parse(response.Body)!["name"]!.GetValue<string>());
        Assert.Contains(response.Headers, h => h.Name == "x-sent" && h.Value == "header");
        Assert.Contains(response.Trailers, t => t.Name == "x-after" && t.Value == "trailer");
        Assert.True(response.Size > 0);
        Assert.Equal("application/json", response.ContentType);
    }

    [Fact]
    public async Task A_server_stream_is_shown_as_an_array()
    {
        var response = await Call("Many", """{"name":"me"}""");

        Assert.Equal(["me 1", "me 2"], JsonNode.Parse(response.Body)!.AsArray().Select(n => n!["name"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_client_stream_sends_every_message()
    {
        var response = await Call("Gather", """[{"name":"a"},{"name":"b"}]""");

        Assert.Equal("a+b", JsonNode.Parse(response.Body)!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_two_way_stream_sends_and_shows_every_message()
    {
        var response = await Call("Both", """[{"name":"a"},{"name":"b"}]""");

        Assert.True(response.IsSuccess);
        Assert.Equal(["echo a", "echo b"], JsonNode.Parse(response.Body)!.AsArray().Select(n => n!["name"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_duplex_write_failure_stops_and_observes_the_reader_without_masking_the_error(bool failOnComplete)
    {
        var reader = new BlockingReader();
        var failure = new InvalidOperationException("write failed");
        var writer = new FailingWriter(reader.Started.Task, failure, failOnComplete);
        var disposed = false;
        using var stream = new AsyncDuplexStreamingCall<byte[], byte[]>(writer, reader,
            Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () =>
            {
                disposed = true;
                reader.Stop.TrySetResult();
            });
        var method = typeof(GrpcCaller).GetMethod("DuplexAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var task = (Task)method.Invoke(null, [stream, new List<byte[]> { Array.Empty<byte>() }, new List<ByteString>(), Cancel])!;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(5), Cancel));

        Assert.Same(failure, error);
        Assert.True(disposed);
        Assert.True(reader.Finished);
    }

    private sealed class BlockingReader : IAsyncStreamReader<byte[]>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Finished { get; private set; }
        public byte[] Current => [];

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Stop.Task;
            Finished = true;
            throw new Exception("reader failed during cleanup");
        }
    }

    private sealed class FailingWriter(Task readerStarted, Exception failure, bool failOnComplete) : IClientStreamWriter<byte[]>
    {
        public WriteOptions? WriteOptions { get; set; }

        public async Task WriteAsync(byte[] message)
        {
            await readerStarted;
            if (!failOnComplete)
                throw failure;
        }

        public Task WriteAsync(byte[] message, CancellationToken cancellationToken) => WriteAsync(message);

        public Task CompleteAsync() => Task.FromException(failure);
    }

    [Fact]
    public async Task A_failure_from_the_server_gives_its_code_and_detail()
    {
        var response = await Call("Fails", "{}");

        Assert.False(response.IsSuccess);
        Assert.Equal("NotFound: there is none", response.Status);
        Assert.Equal("", response.Body);
    }

    [Fact]
    public async Task A_call_can_be_cancelled()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        cancel.CancelAfter(100);

        var response = await Call("Hangs", "{}", cancel.Token);

        Assert.False(response.IsSuccess);
    }

    [Fact]
    public async Task A_call_that_takes_too_long_gives_up()
    {
        var request = Request();
        request.TimeoutSeconds = 0;

        var response = await GrpcCaller.CallAsync(request, _server.Method("Hangs"), ApiVariables.None, Cancel);

        Assert.StartsWith("DeadlineExceeded", response.Status);
    }

    [Fact]
    public async Task Metadata_and_variables_are_sent()
    {
        var request = Request("""{"name":"{{who}}"}""");
        request.Metadata = [new ApiPair { Name = "X-Custom", Value = "{{who}}" }, new ApiPair { Name = "off", Enabled = false }, new ApiPair { Name = " " }];
        var variables = new ApiVariables([new ApiPair { Name = "who", Value = "you" }]);

        var response = await GrpcCaller.CallAsync(request, _server.Method("One"), variables, Cancel);

        Assert.Contains("hello you", response.Body);
        Assert.Equal("you", _server.LastMetadata);
    }

    [Fact]
    public async Task A_message_that_is_not_valid_says_so_without_calling()
    {
        var response = await Call("One", "{ not json");

        Assert.False(response.IsSuccess);
        Assert.Contains("not valid JSON", response.Status);
    }

    [Fact]
    public void Binary_metadata_is_written_as_base64()
    {
        var binary = new ApiRequest { Metadata = [new ApiPair { Name = "x-data-bin", Value = "AQID" }] };
        Assert.Equal([1, 2, 3], GrpcCaller.Headers(binary, ApiVariables.None).Single().ValueBytes);
    }

    [Theory]
    [InlineData("", "Enter the server's address")]
    [InlineData("   ", "Enter the server's address")]
    [InlineData("ftp://example.com", "not an http or https address")]
    public void An_address_that_cannot_be_used_says_why(string address, string expected)
    {
        var problem = Assert.Throws<InvalidOperationException>(() => GrpcCaller.Channel(new ApiRequest { Address = address }, ApiVariables.None));

        Assert.Contains(expected, problem.Message);
    }

    [Fact]
    public void An_address_without_a_scheme_is_http_and_a_certificate_can_be_accepted_as_it_is()
    {
        using var plain = GrpcCaller.Channel(new ApiRequest { Address = "localhost:1234" }, ApiVariables.None);
        Assert.Equal("localhost:1234", plain.Target);

        using var trusting = GrpcCaller.Channel(new ApiRequest { Address = "https://localhost:1234", AcceptAnyCertificate = true }, ApiVariables.None);
        Assert.Equal("localhost:1234", trusting.Target);
    }

    [Fact]
    public void Messages_are_one_object_or_for_a_stream_an_array()
    {
        Assert.Single(GrpcCaller.Messages("", _server.Method("One")));
        Assert.Single(GrpcCaller.Messages("""[{"name":"a"}]""", _server.Method("One")));
        Assert.Equal(2, GrpcCaller.Messages("""[{"name":"a"},{"name":"b",}] // two""", _server.Method("Gather")).Count);

        var problem = Assert.Throws<ProtoJsonException>(() => GrpcCaller.Messages("""[{},{}]""", _server.Method("One")));
        Assert.Contains("takes one message", problem.Message);
    }

    [Fact]
    public async Task A_server_that_is_not_there_is_reported()
    {
        var request = Request();
        request.Address = "http://127.0.0.1:9";

        var response = await GrpcCaller.CallAsync(request, _server.Method("One"), ApiVariables.None, Cancel);

        Assert.False(response.IsSuccess);
    }
}

public class GrpcSchemaFromServerTests : IAsyncLifetime
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private FakeServer _server = null!;

    public async ValueTask InitializeAsync() => _server = await FakeServer.StartAsync();

    public async ValueTask DisposeAsync() => await _server.DisposeAsync();

    private async Task<GrpcSchema> Ask()
    {
        using var channel = GrpcChannel.ForAddress(_server.GrpcAddress);
        return await GrpcSchema.FromServerAsync(channel, new Metadata(), Cancel);
    }

    [Theory]
    [InlineData(ReflectionMode.V1)]
    [InlineData(ReflectionMode.V1Alpha)]
    public async Task The_server_describes_its_services_and_the_files_they_import(ReflectionMode mode)
    {
        _server.Reflection = mode;

        var schema = await Ask();

        Assert.Equal("demo.Greeter", Assert.Single(schema.Services).FullName);
        Assert.NotNull(schema.FindMethod("demo.Greeter", "Both"));
    }

    [Theory]
    [InlineData(ReflectionMode.None, "does not offer gRPC server reflection")]
    [InlineData(ReflectionMode.Refused, "failed: PermissionDenied: not for you")]
    [InlineData(ReflectionMode.NoAnswer, "gave no answer")]
    [InlineData(ReflectionMode.Broken, "could not describe a service: no such symbol")]
    public async Task A_server_that_will_not_describe_them_says_why(ReflectionMode mode, string expected)
    {
        _server.Reflection = mode;

        var problem = await Assert.ThrowsAsync<GrpcSchemaException>(Ask);

        Assert.Contains(expected, problem.Message);
    }
}

public class GrpcSchemaFromFilesTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void A_file_that_will_not_parse_is_reported()
    {
        var path = _folder.Write("bad.proto", "syntax = \"proto3\"; message {");

        Assert.Throws<GrpcSchemaException>(() => GrpcSchema.FromProtoFiles([path], []));
    }

    [Fact]
    public void A_missing_file_is_reported()
    {
        var problem = Assert.Throws<GrpcSchemaException>(() => GrpcSchema.FromProtoFiles([_folder.At("gone.proto")], []));

        Assert.Contains("does not exist", problem.Message);
    }

    [Fact]
    public void An_import_that_is_not_there_is_reported()
    {
        var path = _folder.Write("a.proto", "syntax = \"proto3\";\nimport \"gone.proto\";\n");

        Assert.Throws<GrpcSchemaException>(() => GrpcSchema.FromProtoFiles([path], []));
    }

    [Fact]
    public void Files_that_import_each_other_are_reported()
    {
        _folder.Write("b.proto", "syntax = \"proto3\";\nimport \"a.proto\";\n");
        var path = _folder.Write("a.proto", "syntax = \"proto3\";\nimport \"b.proto\";\n");

        Assert.Throws<GrpcSchemaException>(() => GrpcSchema.FromProtoFiles([path], []));
    }
}
