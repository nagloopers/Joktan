extern alias ProtobufNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Joktan.Api.Resources;
using ParsedSet = ProtobufNet::Google.Protobuf.Reflection.FileDescriptorSet;

namespace Joktan.Api;

/// <summary>The services and their methods, from server reflection or .proto files.</summary>
public sealed class GrpcSchema(IReadOnlyList<ServiceDescriptor> services, TypeRegistry types)
{
    public IReadOnlyList<ServiceDescriptor> Services { get; } = services;

    /// <summary>Every message in the files read, for looking up the type in an Any, which need not be imported.</summary>
    public TypeRegistry Types { get; } = types;

    public ServiceDescriptor? FindService(string fullName) => Services.FirstOrDefault(s => s.FullName == fullName);

    public MethodDescriptor? FindMethod(string service, string method) => FindService(service)?.FindMethodByName(method);

    // The well-known types, which servers often leave out of reflection because every client has them.
    private static readonly FileDescriptor[] WellKnown =
    [
        AnyReflection.Descriptor, ApiReflection.Descriptor, DurationReflection.Descriptor, EmptyReflection.Descriptor,
        FieldMaskReflection.Descriptor, SourceContextReflection.Descriptor, StructReflection.Descriptor,
        TimestampReflection.Descriptor, TypeReflection.Descriptor, WrappersReflection.Descriptor, DescriptorReflection.Descriptor,
    ];

    /// <summary>Reads the services from .proto files, looking up imports beside each file and in the import folders.</summary>
    /// <exception cref="GrpcSchemaException">A file cannot be read or has errors.</exception>
    public static GrpcSchema FromProtoFiles(IReadOnlyList<string> files, IEnumerable<string> importPaths)
    {
        if (files.Count == 0)
            throw new GrpcSchemaException(Strings.GrpcSchema_AddTheProtoFilesThatDescribe);

        var set = new ParsedSet();
        foreach (var folder in files.Select(f => Path.GetDirectoryName(Path.GetFullPath(f))!).Concat(importPaths).Distinct())
            set.AddImportPath(folder);
        foreach (var file in files)
        {
            if (!File.Exists(file))
                throw new GrpcSchemaException(string.Format(Strings.GrpcSchema_DoesNotExist, file));
            if (!set.Add(Path.GetFileName(file), includeInOutput: true))
                throw new GrpcSchemaException(string.Format(Strings.GrpcSchema_CouldNotBeRead, file));
        }

        set.Process();
        var errors = set.GetErrors().Where(e => e.IsError).ToList();
        if (errors.Count > 0)
            throw new GrpcSchemaException(string.Join("\n", errors.Take(10).Select(e => $"{e.File}:{e.LineNumber}: {e.Message}")));

        // The parsed set is written out in protobuf form and read back as Google.Protobuf descriptors.
        using var stream = new MemoryStream();
        set.Serialize(ProtoBuf.Meta.RuntimeTypeModel.Default, stream, includeImports: true);
        var parsed = FileDescriptorSet.Parser.ParseFrom(stream.ToArray());
        return Build(parsed.File.Select(f => f.ToByteString()));
    }

    /// <summary>Asks the server which services it has (gRPC server reflection, v1 or v1alpha).</summary>
    /// <exception cref="GrpcSchemaException">The server cannot be reached or does not offer reflection.</exception>
    public static async Task<GrpcSchema> FromServerAsync(GrpcChannel channel, Metadata headers, CancellationToken cancel)
    {
        try
        {
            return await ReflectAsync(new V1(channel), headers, cancel);
        }
        catch (RpcException e) when (e.StatusCode == StatusCode.Unimplemented)
        {
            try
            {
                return await ReflectAsync(new V1Alpha(channel), headers, cancel);
            }
            catch (RpcException inner) when (inner.StatusCode == StatusCode.Unimplemented)
            {
                throw new GrpcSchemaException(Strings.GrpcSchema_TheServerDoesNotOfferGRPC);
            }
        }
        catch (RpcException e)
        {
            throw new GrpcSchemaException(string.Format(Strings.GrpcSchema_AskingTheServerForItsServices, e.Status.StatusCode, e.Status.Detail));
        }
    }

    private static async Task<GrpcSchema> ReflectAsync(IReflection reflection, Metadata headers, CancellationToken cancel)
    {
        var services = await reflection.ListServicesAsync(headers, cancel);
        var files = new Dictionary<string, ByteString>();
        var queue = new Queue<string>();
        foreach (var service in services.Where(s => !s.StartsWith("grpc.reflection.", StringComparison.Ordinal)))
        {
            foreach (var file in await reflection.FilesForSymbolAsync(service, headers, cancel))
                Add(file);
        }

        // Files the server did not send along with the ones that import them are asked for by name.
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (files.ContainsKey(name) || WellKnown.Any(w => w.Name == name))
                continue;
            foreach (var file in await reflection.FilesByNameAsync(name, headers, cancel))
                Add(file);
        }

        return Build(files.Values);

        void Add(ByteString bytes)
        {
            var proto = FileDescriptorProto.Parser.ParseFrom(bytes);
            if (files.TryAdd(proto.Name, bytes))
            {
                foreach (var dependency in proto.Dependency)
                    queue.Enqueue(dependency);
            }
        }
    }

    // Builds descriptors from serialized files, putting each after the files it imports.
    private static GrpcSchema Build(IEnumerable<ByteString> serialized)
    {
        var protos = serialized.Select(b => (Bytes: b, Proto: FileDescriptorProto.Parser.ParseFrom(b)))
            .GroupBy(p => p.Proto.Name).Select(g => g.First())
            .ToDictionary(p => p.Proto.Name);
        foreach (var known in WellKnown.Where(w => !protos.ContainsKey(w.Name)))
            protos[known.Name] = (known.SerializedData, FileDescriptorProto.Parser.ParseFrom(known.SerializedData));

        var ordered = new List<ByteString>();
        var done = new HashSet<string>();
        foreach (var name in protos.Keys)
            Visit(name, []);

        IReadOnlyList<FileDescriptor> built;
        try
        {
            built = FileDescriptor.BuildFromByteStrings(ordered);
        }
        catch (Exception e) when (e is DescriptorValidationException or ArgumentException or InvalidProtocolBufferException)
        {
            throw new GrpcSchemaException(string.Format(Strings.GrpcSchema_TheServiceDescriptionsDoNotFit, e.Message));
        }

        var services = built.SelectMany(f => f.Services)
            .Where(s => !s.FullName.StartsWith("grpc.reflection.", StringComparison.Ordinal))
            .OrderBy(s => s.FullName, StringComparer.Ordinal)
            .ToList();
        if (services.Count == 0)
            throw new GrpcSchemaException(Strings.GrpcSchema_NoServicesWereFound);
        return new GrpcSchema(services, TypeRegistry.FromFiles(built));

        void Visit(string name, HashSet<string> visiting)
        {
            if (done.Contains(name) || !protos.TryGetValue(name, out var file))
                return;
            if (!visiting.Add(name))
                throw new GrpcSchemaException(string.Format(Strings.GrpcSchema_ImportsItselfThroughOtherFiles, name));
            foreach (var dependency in file.Proto.Dependency)
            {
                if (!protos.ContainsKey(dependency))
                    throw new GrpcSchemaException(string.Format(Strings.GrpcSchema_ImportsWhichWasNotFound, name, dependency));
                Visit(dependency, visiting);
            }

            done.Add(name);
            ordered.Add(file.Bytes);
        }
    }

    // The two versions of the reflection service are the same apart from their generated types.
    private interface IReflection
    {
        Task<List<string>> ListServicesAsync(Metadata headers, CancellationToken cancel);

        Task<List<ByteString>> FilesForSymbolAsync(string symbol, Metadata headers, CancellationToken cancel);

        Task<List<ByteString>> FilesByNameAsync(string file, Metadata headers, CancellationToken cancel);
    }

    private sealed class V1(GrpcChannel channel) : IReflection
    {
        private readonly Grpc.Reflection.V1.ServerReflection.ServerReflectionClient _client = new(channel);

        public async Task<List<string>> ListServicesAsync(Metadata headers, CancellationToken cancel)
            => (await AskAsync(new Grpc.Reflection.V1.ServerReflectionRequest { ListServices = "" }, headers, cancel))
                .ListServicesResponse?.Service.Select(s => s.Name).ToList() ?? [];

        public async Task<List<ByteString>> FilesForSymbolAsync(string symbol, Metadata headers, CancellationToken cancel)
            => Files(await AskAsync(new Grpc.Reflection.V1.ServerReflectionRequest { FileContainingSymbol = symbol }, headers, cancel));

        public async Task<List<ByteString>> FilesByNameAsync(string file, Metadata headers, CancellationToken cancel)
            => Files(await AskAsync(new Grpc.Reflection.V1.ServerReflectionRequest { FileByFilename = file }, headers, cancel));

        private static List<ByteString> Files(Grpc.Reflection.V1.ServerReflectionResponse response)
            => response.ErrorResponse is { } error
                ? throw new GrpcSchemaException(string.Format(Strings.GrpcSchema_TheServerCouldNotDescribeA, error.ErrorMessage))
                : response.FileDescriptorResponse?.FileDescriptorProto.ToList() ?? [];

        private async Task<Grpc.Reflection.V1.ServerReflectionResponse> AskAsync(
            Grpc.Reflection.V1.ServerReflectionRequest request, Metadata headers, CancellationToken cancel)
        {
            using var call = _client.ServerReflectionInfo(headers, cancellationToken: cancel);
            await call.RequestStream.WriteAsync(request, cancel);
            await call.RequestStream.CompleteAsync();
            if (!await call.ResponseStream.MoveNext(cancel))
                throw new GrpcSchemaException(Strings.GrpcSchema_TheServerGaveNoAnswerTo);
            return call.ResponseStream.Current;
        }
    }

    private sealed class V1Alpha(GrpcChannel channel) : IReflection
    {
        private readonly Grpc.Reflection.V1Alpha.ServerReflection.ServerReflectionClient _client = new(channel);

        public async Task<List<string>> ListServicesAsync(Metadata headers, CancellationToken cancel)
            => (await AskAsync(new Grpc.Reflection.V1Alpha.ServerReflectionRequest { ListServices = "" }, headers, cancel))
                .ListServicesResponse?.Service.Select(s => s.Name).ToList() ?? [];

        public async Task<List<ByteString>> FilesForSymbolAsync(string symbol, Metadata headers, CancellationToken cancel)
            => Files(await AskAsync(new Grpc.Reflection.V1Alpha.ServerReflectionRequest { FileContainingSymbol = symbol }, headers, cancel));

        public async Task<List<ByteString>> FilesByNameAsync(string file, Metadata headers, CancellationToken cancel)
            => Files(await AskAsync(new Grpc.Reflection.V1Alpha.ServerReflectionRequest { FileByFilename = file }, headers, cancel));

        private static List<ByteString> Files(Grpc.Reflection.V1Alpha.ServerReflectionResponse response)
            => response.ErrorResponse is { } error
                ? throw new GrpcSchemaException(string.Format(Strings.GrpcSchema_TheServerCouldNotDescribeA, error.ErrorMessage))
                : response.FileDescriptorResponse?.FileDescriptorProto.ToList() ?? [];

        private async Task<Grpc.Reflection.V1Alpha.ServerReflectionResponse> AskAsync(
            Grpc.Reflection.V1Alpha.ServerReflectionRequest request, Metadata headers, CancellationToken cancel)
        {
            using var call = _client.ServerReflectionInfo(headers, cancellationToken: cancel);
            await call.RequestStream.WriteAsync(request, cancel);
            await call.RequestStream.CompleteAsync();
            if (!await call.ResponseStream.MoveNext(cancel))
                throw new GrpcSchemaException(Strings.GrpcSchema_TheServerGaveNoAnswerTo);
            return call.ResponseStream.Current;
        }
    }
}

public sealed class GrpcSchemaException(string message) : Exception(message);
