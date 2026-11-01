using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Grpc.Net.Client;
using Joktan.Api.Resources;

namespace Joktan.Api;

/// <summary>Calls gRPC methods described at run time, with messages written as JSON.</summary>
public static class GrpcCaller
{
    private static readonly Marshaller<byte[]> Bytes = Marshallers.Create(b => b, b => b);

    /// <summary>A channel to the request's server, with variables filled in.</summary>
    public static GrpcChannel Channel(ApiRequest request, ApiVariables variables)
    {
        var address = variables.Apply(request.Address.Trim());
        if (address.Length == 0)
            throw new InvalidOperationException(Strings.GrpcCaller_EnterTheServerSAddressSuch);
        if (!address.Contains("://", StringComparison.Ordinal))
            address = "http://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(string.Format(Strings.GrpcCaller_IsNotAnHttpOrHttps, address));

        var handler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true };
        if (request.AcceptAnyCertificate)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        return GrpcChannel.ForAddress(uri, new GrpcChannelOptions { HttpHandler = handler, DisposeHttpClient = true });
    }

    public static Metadata Headers(ApiRequest request, ApiVariables variables)
    {
        var metadata = new Metadata();
        foreach (var pair in request.Metadata.Where(p => p.Enabled && p.Name.Trim().Length > 0))
        {
            var name = variables.Apply(pair.Name.Trim()).ToLowerInvariant();
            var value = variables.Apply(pair.Value);
            // Binary metadata is written as base64.
            if (name.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.Ordinal))
                metadata.Add(name, Convert.FromBase64String(value));
            else
                metadata.Add(name, value);
        }

        return metadata;
    }

    /// <summary>The messages to send: one JSON object, or for a client or two-way stream a JSON array of them.</summary>
    public static List<byte[]> Messages(string json, MethodDescriptor method, TypeRegistry? types = null)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json.Trim().Length == 0 ? "{}" : json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException e)
        {
            throw new ProtoJsonException(string.Format(Strings.GrpcCaller_TheMessageIsNotValidJSON, e.Message));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (!method.IsClientStreaming && root.GetArrayLength() != 1)
                    throw new ProtoJsonException(string.Format(Strings.GrpcCaller_TakesOneMessageWriteItAs, method.Name));
                return root.EnumerateArray().Select(m => ProtoJson.Encode(m, method.InputType, types)).ToList();
            }

            return [ProtoJson.Encode(root, method.InputType, types)];
        }
    }

    public static async Task<ApiResponse> CallAsync(ApiRequest request, MethodDescriptor method, ApiVariables variables, CancellationToken cancel, TypeRegistry? types = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var responses = new List<ByteString>();
        Metadata? headers = null;
        try
        {
            var messages = Messages(variables.Apply(request.Message), method, types);
            using var channel = Channel(request, variables);
            var invoker = channel.CreateCallInvoker();
            var type = (method.IsClientStreaming, method.IsServerStreaming) switch
            {
                (false, false) => MethodType.Unary,
                (false, true) => MethodType.ServerStreaming,
                (true, false) => MethodType.ClientStreaming,
                _ => MethodType.DuplexStreaming,
            };
            var call = new Method<byte[], byte[]>(type, method.Service.FullName, method.Name, Bytes, Bytes);
            var options = new CallOptions(Headers(request, variables),
                DateTime.UtcNow.AddSeconds(Math.Max(1, request.TimeoutSeconds)), cancel);

            Status status;
            Metadata trailers;
            switch (type)
            {
                case MethodType.Unary:
                {
                    using var unary = invoker.AsyncUnaryCall(call, null, options, messages[0]);
                    headers = await unary.ResponseHeadersAsync;
                    responses.Add(ByteString.CopyFrom(await unary.ResponseAsync));
                    (status, trailers) = (unary.GetStatus(), unary.GetTrailers());
                    break;
                }
                case MethodType.ServerStreaming:
                {
                    using var stream = invoker.AsyncServerStreamingCall(call, null, options, messages[0]);
                    headers = await stream.ResponseHeadersAsync;
                    while (await stream.ResponseStream.MoveNext(cancel))
                        responses.Add(ByteString.CopyFrom(stream.ResponseStream.Current));
                    (status, trailers) = (stream.GetStatus(), stream.GetTrailers());
                    break;
                }
                case MethodType.ClientStreaming:
                {
                    using var stream = invoker.AsyncClientStreamingCall(call, null, options);
                    foreach (var message in messages)
                        await stream.RequestStream.WriteAsync(message, cancel);
                    await stream.RequestStream.CompleteAsync();
                    headers = await stream.ResponseHeadersAsync;
                    responses.Add(ByteString.CopyFrom(await stream.ResponseAsync));
                    (status, trailers) = (stream.GetStatus(), stream.GetTrailers());
                    break;
                }
                default:
                {
                    using var stream = invoker.AsyncDuplexStreamingCall(call, null, options);
                    await DuplexAsync(stream, messages, responses, cancel);
                    headers = await stream.ResponseHeadersAsync;
                    (status, trailers) = (stream.GetStatus(), stream.GetTrailers());
                    break;
                }
            }

            stopwatch.Stop();
            return Result(status, trailers);
        }
        catch (RpcException e)
        {
            stopwatch.Stop();
            return Result(e.Status, e.Trailers);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return new ApiResponse { Status = Strings.GrpcCaller_Cancelled, Elapsed = stopwatch.Elapsed, Body = Body() };
        }
        catch (Exception e) when (e is ProtoJsonException or InvalidOperationException or FormatException or HttpRequestException)
        {
            return new ApiResponse { Status = RestSender.Message(e), Elapsed = stopwatch.Elapsed };
        }

        ApiResponse Result(Status status, Metadata trailers) => new()
        {
            Status = status.Detail.Length > 0 ? $"{status.StatusCode}: {status.Detail}" : status.StatusCode.ToString(),
            IsSuccess = status.StatusCode == StatusCode.OK,
            Elapsed = stopwatch.Elapsed,
            Size = Received().Sum(r => (long)r.Length),
            Headers = Pairs(headers),
            Trailers = Pairs(trailers),
            ContentType = "application/json",
            Body = Body(),
        };

        // A two-way stream adds answers from another task.
        List<ByteString> Received()
        {
            lock (responses)
                return [.. responses];
        }

        // One answer is shown as itself; a stream as an array of messages.
        string Body()
        {
            var decoded = Received().Select(r => ProtoJson.Decode(r, method.OutputType, types)).ToList();
            JsonNode? body = method.IsServerStreaming ? new JsonArray(decoded.ToArray<JsonNode?>()) : decoded.FirstOrDefault();
            return body is null ? "" : body.ToJsonString(RestSender.IndentedJson);
        }
    }

    private static async Task DuplexAsync(AsyncDuplexStreamingCall<byte[], byte[]> stream, List<byte[]> messages,
        List<ByteString> responses, CancellationToken cancel)
    {
        var reading = Task.Run(async () =>
        {
            while (await stream.ResponseStream.MoveNext(cancel))
            {
                lock (responses)
                    responses.Add(ByteString.CopyFrom(stream.ResponseStream.Current));
            }
        }, cancel);
        try
        {
            foreach (var message in messages)
                await stream.RequestStream.WriteAsync(message, cancel);
            await stream.RequestStream.CompleteAsync();
        }
        catch
        {
            stream.Dispose();
            try { await reading; }
            catch { /* Preserve the write failure. */ }
            throw;
        }
        await reading;
    }

    private static List<ApiPair> Pairs(Metadata? metadata) => metadata?
        .Select(e => new ApiPair { Name = e.Key, Value = e.IsBinary ? Convert.ToBase64String(e.ValueBytes) : e.Value })
        .ToList() ?? [];
}
