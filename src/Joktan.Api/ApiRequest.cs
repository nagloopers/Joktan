using System.Collections.Generic;
using System.Text.Json.Serialization;
using Avalonia.Data.Converters;
using Joktan.Api.Resources;

namespace Joktan.Api;

[JsonConverter(typeof(JsonStringEnumConverter<ApiKind>))]
public enum ApiKind
{
    Rest,
    Grpc,
}

[JsonConverter(typeof(JsonStringEnumConverter<ApiBodyKind>))]
public enum ApiBodyKind
{
    None,
    Json,
    Text,
    Xml,
    Form,
}

[JsonConverter(typeof(JsonStringEnumConverter<ApiAuthKind>))]
public enum ApiAuthKind
{
    None,
    Bearer,
    Basic,
}

public static class ApiKindText
{
    /// <summary>What a ApiBodyKind is called on screen.</summary>
    public static string Text(this ApiBodyKind value) => value switch
    {
        ApiBodyKind.None => Strings.ApiBodyKind_None,
        ApiBodyKind.Json => Strings.ApiBodyKind_Json,
        ApiBodyKind.Text => Strings.ApiBodyKind_Text,
        ApiBodyKind.Xml => Strings.ApiBodyKind_Xml,
        ApiBodyKind.Form => Strings.ApiBodyKind_Form,
        _ => value.ToString(),
    };

    public static readonly FuncValueConverter<ApiBodyKind, string> ApiBodyKindConverter = new(value => value.Text());

    /// <summary>What a ApiAuthKind is called on screen.</summary>
    public static string Text(this ApiAuthKind value) => value switch
    {
        ApiAuthKind.None => Strings.ApiAuthKind_None,
        ApiAuthKind.Bearer => Strings.ApiAuthKind_Bearer,
        ApiAuthKind.Basic => Strings.ApiAuthKind_Basic,
        _ => value.ToString(),
    };

    public static readonly FuncValueConverter<ApiAuthKind, string> ApiAuthKindConverter = new(value => value.Text());
}

[JsonConverter(typeof(JsonStringEnumConverter<GrpcSchemaSource>))]
public enum GrpcSchemaSource
{
    /// <summary>Ask the server for its services (gRPC server reflection).</summary>
    Reflection,

    /// <summary>Read the services from .proto files.</summary>
    ProtoFiles,
}

/// <summary>A name and value, such as a header, query parameter or variable, that can be switched off.</summary>
public sealed class ApiPair
{
    public bool Enabled { get; set; } = true;

    public string Name { get; set; } = "";

    public string Value { get; set; } = "";
}

/// <summary>
/// One saved request, REST or gRPC, kept as a file of its own in the workspace. Values may hold
/// <c>{{variables}}</c> from the selected environment.
/// </summary>
public sealed class ApiRequest
{
    public ApiKind Kind { get; set; } = ApiKind.Rest;

    // REST

    public string Method { get; set; } = "GET";

    public string Url { get; set; } = "";

    public List<ApiPair> Query { get; set; } = [];

    public List<ApiPair> Headers { get; set; } = [];

    public ApiBodyKind BodyKind { get; set; } = ApiBodyKind.None;

    public string Body { get; set; } = "";

    public List<ApiPair> Form { get; set; } = [];

    public ApiAuthKind AuthKind { get; set; } = ApiAuthKind.None;

    public string Token { get; set; } = "";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    // gRPC

    /// <summary>The server, such as https://localhost:5001 or http://localhost:5000 for plain HTTP/2.</summary>
    public string Address { get; set; } = "";

    public GrpcSchemaSource Schema { get; set; } = GrpcSchemaSource.Reflection;

    public List<string> ProtoFiles { get; set; } = [];

    /// <summary>Folders that imports in the .proto files are looked up in, besides each file's own folder.</summary>
    public List<string> ImportPaths { get; set; } = [];

    /// <summary>The service's full name, such as greet.Greeter.</summary>
    public string Service { get; set; } = "";

    public string Rpc { get; set; } = "";

    /// <summary>The request message as JSON; for a client or two-way stream, a JSON array of messages.</summary>
    public string Message { get; set; } = "{}";

    public List<ApiPair> Metadata { get; set; } = [];

    // Both

    /// <summary>Accept any TLS certificate, for local servers with self-signed ones.</summary>
    public bool AcceptAnyCertificate { get; set; }

    public int TimeoutSeconds { get; set; } = 100;

    /// <summary>Environment variables to save from a successful response: the variable's name, and the path in the body.</summary>
    public List<ApiPair> Captures { get; set; } = [];
}

/// <summary>A named set of variables, such as one per server.</summary>
public sealed class ApiEnvironment
{
    public string Name { get; set; } = "";

    public List<ApiPair> Variables { get; set; } = [];
}

/// <summary>The environments of a workspace and which one is in use.</summary>
public sealed class ApiEnvironments
{
    public List<ApiEnvironment> Environments { get; set; } = [];

    /// <summary>The environment in use; empty for none.</summary>
    public string Selected { get; set; } = "";
}
