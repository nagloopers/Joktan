using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Joktan.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using V1 = Grpc.Reflection.V1;
using V1Alpha = Grpc.Reflection.V1Alpha;

namespace Joktan.Api.Tests;

/// <summary>
/// A web server on this machine: gRPC over cleartext HTTP/2 on one port, with its messages framed by
/// hand so that any service can be described at run time, and REST over HTTP/1.1 on another.
/// </summary>
public sealed class FakeServer : IAsyncDisposable
{
    public const string Proto = """
        syntax = "proto3";
        package demo;

        import "common.proto";

        service Greeter {
          rpc One (Named) returns (Named);
          rpc Many (Named) returns (stream Named);
          rpc Gather (stream Named) returns (Named);
          rpc Both (stream Named) returns (stream Named);
          rpc Fails (Named) returns (Named);
          rpc Hangs (Named) returns (Named);
        }
        """;

    public const string Common = """
        syntax = "proto3";
        package demo;

        message Named { string name = 1; }
        """;

    private readonly WebApplication _app;
    private readonly TempFolder _folder = new();

    private FakeServer(WebApplication app, GrpcSchema schema)
    {
        _app = app;
        Schema = schema;
    }

    public GrpcSchema Schema { get; }

    /// <summary>What reflection does: the v1 service, only the older one, none, or a failure.</summary>
    public ReflectionMode Reflection { get; set; } = ReflectionMode.V1;

    public string GrpcAddress { get; private set; } = "";

    public string RestAddress { get; private set; } = "";

    public string? LastMetadata { get; private set; }

    public string LastRestBody { get; private set; } = "";

    public string? LastRestCookie { get; private set; }

    public MethodDescriptor Method(string name) => Schema.FindMethod("demo.Greeter", name)!;

    public static string WriteProtos(TempFolder folder)
    {
        folder.Write("protos/common.proto", Common);
        return folder.Write("protos/greeter.proto", Proto);
    }

    public static async Task<FakeServer> StartAsync()
    {
        var folder = new TempFolder();
        var schema = GrpcSchema.FromProtoFiles([WriteProtos(folder)], []);
        folder.Dispose();

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2);
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http1);
        });
        var app = builder.Build();
        var server = new FakeServer(app, schema);
        app.Run(server.HandleAsync);
        await app.StartAsync();

        var addresses = app.Services.GetService(typeof(IServer)) is IServer s ? s.Features.Get<IServerAddressesFeature>()!.Addresses.ToList() : [];
        server.GrpcAddress = addresses[0];
        server.RestAddress = addresses[1];
        return server;
    }

    private async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (context.Request.Protocol != "HTTP/2")
        {
            using var reader = new StreamReader(context.Request.Body);
            LastRestBody = await reader.ReadToEndAsync();
            LastRestCookie = context.Request.Headers.Cookie;
            if (path == "/cookie")
                context.Response.Headers.SetCookie = "session=abc";
            context.Response.StatusCode = path == "/missing" ? 404 : 200;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(path == "/hang"
                ? await Hang(context)
                : """{"token":"from the server","method":"GET"}""");
            return;
        }

        LastMetadata = context.Request.Headers["x-custom"];
        context.Response.ContentType = "application/grpc";
        context.Response.Headers["x-sent"] = "header";
        await context.Response.StartAsync();
        var messages = await ReadFramesAsync(context);

        if (path.StartsWith("/grpc.reflection.", StringComparison.Ordinal))
        {
            await ReflectAsync(context, path, messages);
            return;
        }

        var names = messages.Select(NameOf).ToList();
        switch (path)
        {
            case "/demo.Greeter/One":
                await WriteFrameAsync(context, Named("hello " + names[0]));
                break;
            case "/demo.Greeter/Many":
                await WriteFrameAsync(context, Named(names[0] + " 1"));
                await WriteFrameAsync(context, Named(names[0] + " 2"));
                break;
            case "/demo.Greeter/Gather":
                await WriteFrameAsync(context, Named(string.Join("+", names)));
                break;
            case "/demo.Greeter/Both":
                foreach (var name in names)
                    await WriteFrameAsync(context, Named("echo " + name));
                break;
            case "/demo.Greeter/Holds":
                await WriteFrameAsync(context, messages[0]);
                break;
            case "/demo.Greeter/Fails":
                Trailers(context, StatusCode.NotFound, "there is none");
                return;
            case "/demo.Greeter/Hangs":
                await Hang(context);
                return;
        }

        context.Response.AppendTrailer("x-after", "trailer");
        Trailers(context, StatusCode.OK);
    }

    private static async Task<string> Hang(HttpContext context)
    {
        await Task.Delay(Timeout.Infinite, context.RequestAborted).ContinueWith(_ => { }, TaskScheduler.Default);
        return "";
    }

    private async Task ReflectAsync(HttpContext context, string path, List<byte[]> messages)
    {
        var isV1 = path.StartsWith("/grpc.reflection.v1.", StringComparison.Ordinal);
        switch (Reflection)
        {
            case ReflectionMode.None:
            case ReflectionMode.V1Alpha when isV1:
                Trailers(context, StatusCode.Unimplemented);
                return;
            case ReflectionMode.Refused:
                Trailers(context, StatusCode.PermissionDenied, "not for you");
                return;
            case ReflectionMode.NoAnswer:
                Trailers(context, StatusCode.OK);
                return;
        }

        foreach (var message in messages)
        {
            if (isV1)
            {
                var request = V1.ServerReflectionRequest.Parser.ParseFrom(message);
                var response = new V1.ServerReflectionResponse();
                switch (request.MessageRequestCase)
                {
                    case V1.ServerReflectionRequest.MessageRequestOneofCase.ListServices:
                        response.ListServicesResponse = new V1.ListServiceResponse();
                        response.ListServicesResponse.Service.Add(new V1.ServiceResponse { Name = "demo.Greeter" });
                        response.ListServicesResponse.Service.Add(new V1.ServiceResponse { Name = "grpc.reflection.v1.ServerReflection" });
                        break;
                    case V1.ServerReflectionRequest.MessageRequestOneofCase.FileContainingSymbol when Reflection == ReflectionMode.Broken:
                        response.ErrorResponse = new V1.ErrorResponse { ErrorMessage = "no such symbol" };
                        break;
                    case V1.ServerReflectionRequest.MessageRequestOneofCase.FileContainingSymbol:
                        response.FileDescriptorResponse = new V1.FileDescriptorResponse();
                        response.FileDescriptorResponse.FileDescriptorProto.Add(GreeterFile);
                        break;
                    default:
                        response.FileDescriptorResponse = new V1.FileDescriptorResponse();
                        response.FileDescriptorResponse.FileDescriptorProto.Add(CommonFile);
                        break;
                }

                await WriteFrameAsync(context, response.ToByteArray());
            }
            else
            {
                var request = V1Alpha.ServerReflectionRequest.Parser.ParseFrom(message);
                var response = new V1Alpha.ServerReflectionResponse();
                switch (request.MessageRequestCase)
                {
                    case V1Alpha.ServerReflectionRequest.MessageRequestOneofCase.ListServices:
                        response.ListServicesResponse = new V1Alpha.ListServiceResponse();
                        response.ListServicesResponse.Service.Add(new V1Alpha.ServiceResponse { Name = "demo.Greeter" });
                        break;
                    case V1Alpha.ServerReflectionRequest.MessageRequestOneofCase.FileContainingSymbol:
                        response.FileDescriptorResponse = new V1Alpha.FileDescriptorResponse();
                        response.FileDescriptorResponse.FileDescriptorProto.Add(GreeterFile);
                        break;
                    default:
                        response.FileDescriptorResponse = new V1Alpha.FileDescriptorResponse();
                        response.FileDescriptorResponse.FileDescriptorProto.Add(CommonFile);
                        break;
                }

                await WriteFrameAsync(context, response.ToByteArray());
            }
        }

        Trailers(context, StatusCode.OK);
    }

    private ByteString GreeterFile => Schema.Services[0].File.SerializedData;

    private ByteString CommonFile => Schema.Services[0].File.Dependencies[0].SerializedData;

    private static void Trailers(HttpContext context, StatusCode status, string detail = "")
    {
        context.Response.AppendTrailer("grpc-status", ((int)status).ToString());
        if (detail.Length > 0)
            context.Response.AppendTrailer("grpc-message", detail);
    }

    private static async Task<List<byte[]>> ReadFramesAsync(HttpContext context)
    {
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer);
        var all = buffer.ToArray();
        var frames = new List<byte[]>();
        for (var at = 0; at + 5 <= all.Length;)
        {
            var length = (all[at + 1] << 24) | (all[at + 2] << 16) | (all[at + 3] << 8) | all[at + 4];
            frames.Add(all[(at + 5)..(at + 5 + length)]);
            at += 5 + length;
        }

        return frames;
    }

    private static async Task WriteFrameAsync(HttpContext context, byte[] message)
    {
        var frame = new byte[5 + message.Length];
        frame[1] = (byte)(message.Length >> 24);
        frame[2] = (byte)(message.Length >> 16);
        frame[3] = (byte)(message.Length >> 8);
        frame[4] = (byte)message.Length;
        message.CopyTo(frame, 5);
        await context.Response.Body.WriteAsync(frame);
        await context.Response.Body.FlushAsync();
    }

    // A message with one string field, number 1.
    private static byte[] Named(string name)
    {
        var text = Encoding.UTF8.GetBytes(name);
        return [0x0A, (byte)text.Length, .. text];
    }

    private static string NameOf(byte[] message) => message.Length < 2 ? "" : Encoding.UTF8.GetString(message, 2, message[1]);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _folder.Dispose();
    }
}

public enum ReflectionMode
{
    V1,
    V1Alpha,
    None,
    Refused,
    NoAnswer,
    Broken,
}
