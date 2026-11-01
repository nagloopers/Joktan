using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Google.Protobuf.Reflection;
using Grpc.Core;
using Joktan.Api.Resources;
using Joktan.Core.ViewModels;

namespace Joktan.Api.ViewModels;

/// <summary>One request being edited, sent and answered. Changes are written to its file as they are made.</summary>
public partial class ApiRequestViewModel : ViewModelBase
{
    // Changes are written this long after the last one.
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    // Schemas already loaded, by where they came from, so switching requests does not ask again.
    private static readonly ConcurrentDictionary<string, GrpcSchema> Schemas = new();

    // Properties that are not part of the saved request.
    private static readonly HashSet<string> Transient =
    [
        nameof(Path), nameof(IsSending), nameof(IsNotSending), nameof(Response), nameof(HasResponse), nameof(Error),
        nameof(Services), nameof(Methods), nameof(SchemaStatus), nameof(MethodText), nameof(IsBodyText), nameof(IsBodyForm),
        nameof(IsBearer), nameof(IsBasic), nameof(UsesReflection), nameof(UsesProtoFiles), nameof(IsRest), nameof(IsGrpc),
        nameof(ServiceChoice), nameof(RpcChoice), nameof(SavedVariables), nameof(LastSent), nameof(HasLastSent),
        nameof(CanResend), nameof(LastSentTime), nameof(LastSentText),
    ];

    private readonly Func<ApiVariables> _variables;
    private readonly Func<IReadOnlyList<ApiPair>, string?> _saveVariables;
    private readonly Action<ApiRequestViewModel> _described;
    private readonly DispatcherTimer _saveTimer;
    private readonly ApiKind _kind;
    private GrpcSchema? _schema;
    private CancellationTokenSource? _sending;
    private bool _loading = true;

    /// <param name="variables">The variables of the environment in use when the request is sent.</param>
    /// <param name="saveVariables">Saves values in the environment in use and gives its name, or null if none is in use.</param>
    /// <param name="described">Called when the method or kind changes, so the tree can show it.</param>
    public ApiRequestViewModel(string path, Func<ApiVariables> variables, Func<IReadOnlyList<ApiPair>, string?> saveVariables,
        Action<ApiRequestViewModel> described)
    {
        Path = path;
        _variables = variables;
        _saveVariables = saveVariables;
        _described = described;
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => Save();

        var request = ApiWorkspace.Read(path);
        _kind = request.Kind;
        Method = request.Method;
        Url = request.Url;
        Query = new ApiPairList(request.Query, Changed);
        Headers = new ApiPairList(request.Headers, Changed);
        BodyKind = request.BodyKind;
        Body = request.Body;
        Form = new ApiPairList(request.Form, Changed);
        AuthKind = request.AuthKind;
        Token = request.Token;
        Username = request.Username;
        Password = request.Password;
        Address = request.Address;
        Schema = request.Schema;
        foreach (var file in request.ProtoFiles)
            ProtoFiles.Add(new ApiPathViewModel(ProtoFiles, file));
        foreach (var folder in request.ImportPaths)
            ImportPaths.Add(new ApiPathViewModel(ImportPaths, folder));
        Service = request.Service;
        Rpc = request.Rpc;
        Message = request.Message;
        Metadata = new ApiPairList(request.Metadata, Changed);
        AcceptAnyCertificate = request.AcceptAnyCertificate;
        TimeoutSeconds = request.TimeoutSeconds;
        Captures = new ApiPairList(request.Captures, Changed);
        ProtoFiles.CollectionChanged += (_, _) => SchemaSourceChanged();
        ImportPaths.CollectionChanged += (_, _) => SchemaSourceChanged();
        LastSent = ApiLastSent.Read(path);
        _loading = false;

        if (IsGrpc)
            UseCachedSchema();
    }

    public string Path { get; set; }

    public bool IsRest => _kind == ApiKind.Rest;

    public bool IsGrpc => _kind == ApiKind.Grpc;

    /// <summary>Asks for .proto files, or a folder for imports. Set by the view.</summary>
    public Func<bool, Task<IReadOnlyList<string>>>? PickFiles { get; set; }

    /// <summary>Copies text to the clipboard. Set by the view.</summary>
    public Func<string, Task>? CopyText { get; set; }

    // ---- REST ----

    public IReadOnlyList<string> MethodChoices => RestSender.Methods;

    [ObservableProperty]
    public partial string Method { get; set; }

    [ObservableProperty]
    public partial string Url { get; set; }

    public ApiPairList Query { get; }

    public ApiPairList Headers { get; }

    public IReadOnlyList<ApiBodyKind> BodyKinds { get; } = Enum.GetValues<ApiBodyKind>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBodyText), nameof(IsBodyForm))]
    public partial ApiBodyKind BodyKind { get; set; }

    public bool IsBodyText => BodyKind is ApiBodyKind.Json or ApiBodyKind.Text or ApiBodyKind.Xml;

    public bool IsBodyForm => BodyKind == ApiBodyKind.Form;

    [ObservableProperty]
    public partial string Body { get; set; }

    public ApiPairList Form { get; }

    public IReadOnlyList<ApiAuthKind> AuthKinds { get; } = Enum.GetValues<ApiAuthKind>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBearer), nameof(IsBasic))]
    public partial ApiAuthKind AuthKind { get; set; }

    public bool IsBearer => AuthKind == ApiAuthKind.Bearer;

    public bool IsBasic => AuthKind == ApiAuthKind.Basic;

    [ObservableProperty]
    public partial string Token { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    [RelayCommand]
    private void FormatBody()
    {
        if (RestSender.TryIndentJson(Body) is { } formatted)
            Body = formatted;
        else
            Error = Strings.ApiRequest_TheBodyIsNotValidJSON;
    }

    // ---- gRPC ----

    [ObservableProperty]
    public partial string Address { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesReflection), nameof(UsesProtoFiles))]
    public partial GrpcSchemaSource Schema { get; set; }

    public bool UsesReflection
    {
        get => Schema == GrpcSchemaSource.Reflection;
        set
        {
            if (value)
                Schema = GrpcSchemaSource.Reflection;
        }
    }

    public bool UsesProtoFiles
    {
        get => Schema == GrpcSchemaSource.ProtoFiles;
        set
        {
            if (value)
                Schema = GrpcSchemaSource.ProtoFiles;
        }
    }

    public ObservableCollection<ApiPathViewModel> ProtoFiles { get; } = [];

    public ObservableCollection<ApiPathViewModel> ImportPaths { get; } = [];

    /// <summary>The services found, by full name.</summary>
    public ObservableCollection<string> Services { get; } = [];

    /// <summary>The selected service's methods.</summary>
    public ObservableCollection<string> Methods { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServiceChoice))]
    public partial string Service { get; set; }

    /// <summary>
    /// The service as the list sees it. The list clears its choice when its items change; that is
    /// ignored, so the saved service is kept until another is chosen.
    /// </summary>
    public string? ServiceChoice
    {
        get => Service;
        set
        {
            if (!string.IsNullOrEmpty(value))
                Service = value;
        }
    }

    /// <summary>The method as the list sees it; see <see cref="ServiceChoice"/>.</summary>
    public string? RpcChoice
    {
        get => Rpc;
        set
        {
            if (!string.IsNullOrEmpty(value))
                Rpc = value;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MethodText), nameof(RpcChoice))]
    public partial string Rpc { get; set; }

    /// <summary>What has been found, or why not.</summary>
    [ObservableProperty]
    public partial string SchemaStatus { get; set; } = "";

    /// <summary>Such as "Server streaming: CountRequest → stream Number".</summary>
    public string MethodText
    {
        get
        {
            if (FindMethod() is not { } method)
                return "";
            var kind = (method.IsClientStreaming, method.IsServerStreaming) switch
            {
                (false, false) => Strings.ApiRequest_Unary,
                (false, true) => Strings.ApiRequest_ServerStreaming,
                (true, false) => Strings.ApiRequest_ClientStreamingWriteTheMessagesAs,
                _ => Strings.ApiRequest_TwoWayStreamingWriteTheMessages,
            };
            var input = (method.IsClientStreaming ? "stream " : "") + method.InputType.Name;
            var output = (method.IsServerStreaming ? "stream " : "") + method.OutputType.Name;
            return $"{kind} · {input} → {output}";
        }
    }

    [ObservableProperty]
    public partial string Message { get; set; }

    public ApiPairList Metadata { get; }

    partial void OnServiceChanged(string value)
    {
        if (_schema?.FindService(value) is { } service)
        {
            Sync(Methods, service.Methods.Select(m => m.Name).ToList());
            if (!Methods.Contains(Rpc))
                Rpc = Methods.FirstOrDefault() ?? "";
        }

        OnPropertyChanged(nameof(MethodText));
    }

    partial void OnSchemaChanged(GrpcSchemaSource value) => SchemaSourceChanged();

    partial void OnAddressChanged(string value)
    {
        if (Schema == GrpcSchemaSource.Reflection)
            SchemaSourceChanged();
    }

    [RelayCommand]
    private async Task AddProtoFilesAsync()
    {
        if (PickFiles is null)
            return;
        foreach (var file in await PickFiles(false))
        {
            if (ProtoFiles.All(p => p.Path != file))
                ProtoFiles.Add(new ApiPathViewModel(ProtoFiles, file));
        }

        Schema = GrpcSchemaSource.ProtoFiles;
    }

    [RelayCommand]
    private async Task AddImportPathAsync()
    {
        if (PickFiles is null)
            return;
        foreach (var folder in await PickFiles(true))
        {
            if (ImportPaths.All(p => p.Path != folder))
                ImportPaths.Add(new ApiPathViewModel(ImportPaths, folder));
        }
    }

    /// <summary>Finds the services, from the server or the .proto files.</summary>
    [RelayCommand]
    private async Task LoadServicesAsync()
    {
        Error = null;
        SchemaStatus = Schema == GrpcSchemaSource.Reflection ? Strings.ApiRequest_AskingTheServerForItsServices : Strings.ApiRequest_ReadingTheProtoFiles;
        try
        {
            ShowSchema(await FetchSchemaAsync(ToRequest()));
        }
        catch (Exception e) when (IsSchemaFailure(e))
        {
            SchemaStatus = SchemaFailure(e);
        }
    }

    // Finds a request's services, from the server or the .proto files, and keeps them.
    private async Task<GrpcSchema> FetchSchemaAsync(ApiRequest request)
    {
        var variables = _variables();
        GrpcSchema schema;
        if (request.Schema == GrpcSchemaSource.Reflection)
        {
            using var channel = GrpcCaller.Channel(request, variables);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)));
            schema = await GrpcSchema.FromServerAsync(channel, GrpcCaller.Headers(request, variables), timeout.Token);
        }
        else
        {
            schema = await Task.Run(() => GrpcSchema.FromProtoFiles(request.ProtoFiles, request.ImportPaths));
        }

        Schemas[SchemaKey(request)] = schema;
        return schema;
    }

    private static bool IsSchemaFailure(Exception e)
        => e is GrpcSchemaException or InvalidOperationException or OperationCanceledException or IOException
            or RpcException or System.Net.Http.HttpRequestException;

    private static string SchemaFailure(Exception e)
        => e is OperationCanceledException ? Strings.ApiRequest_TheServerDidNotAnswerIn : RestSender.Message(e);

    /// <summary>Fills the message with an example of every field of the method's request.</summary>
    [RelayCommand]
    private void Template()
    {
        if (FindMethod() is not { } method)
        {
            Error = Strings.ApiRequest_LoadTheServicesAndChooseA;
            return;
        }

        var example = ProtoJson.Template(method.InputType);
        Message = (method.IsClientStreaming ? new System.Text.Json.Nodes.JsonArray(example) : example)?
            .ToJsonString(RestSender.IndentedJson) ?? "{}";
    }

    [RelayCommand]
    private void FormatMessage()
    {
        if (RestSender.TryIndentJson(Message) is { } formatted)
            Message = formatted;
        else
            Error = Strings.ApiRequest_TheMessageIsNotValidJSON;
    }

    private void ShowSchema(GrpcSchema schema)
    {
        _schema = schema;
        Sync(Services, schema.Services.Select(s => s.FullName).ToList());
        if (!Services.Contains(Service))
            Service = Services.FirstOrDefault() ?? "";
        OnServiceChanged(Service);
        var methods = schema.Services.Sum(s => s.Methods.Count);
        SchemaStatus = $"{schema.Services.Count} {(schema.Services.Count == 1 ? Strings.ApiRequest_Service : Strings.ApiRequest_Services)}, {methods} {(methods == 1 ? Strings.ApiRequest_Method : Strings.ApiRequest_Methods)}";
    }

    private void UseCachedSchema()
    {
        if (Schemas.TryGetValue(SchemaKey(), out var schema))
        {
            ShowSchema(schema);
            return;
        }

        // Until the services are loaded, the lists hold the saved choice, so it shows and is kept.
        Sync(Services, Service.Length > 0 ? [Service] : []);
        Sync(Methods, Rpc.Length > 0 ? [Rpc] : []);
        SchemaStatus = Schema == GrpcSchemaSource.Reflection
            ? Strings.ApiRequest_LoadServicesToAskTheServer
            : Strings.ApiRequest_LoadServicesToReadThemFrom;
    }

    private void SchemaSourceChanged()
    {
        if (_loading)
            return;
        _schema = null;
        UseCachedSchema();
        OnPropertyChanged(nameof(MethodText));
        Changed();
    }

    private string SchemaKey() => SchemaKey(ToRequest());

    private string SchemaKey(ApiRequest request) => request.Schema == GrpcSchemaSource.Reflection
        ? "server:" + _variables().Apply(request.Address.Trim())
        : "files:" + string.Join("|", request.ProtoFiles.Select(p => p + "@" + (File.Exists(p) ? File.GetLastWriteTimeUtc(p).Ticks : 0)))
                   + "|" + string.Join("|", request.ImportPaths);

    // Changes a list to hold these items, in order, without removing the ones it keeps, so a
    // choice still in the list stays chosen.
    private static void Sync(ObservableCollection<string> list, IReadOnlyList<string> items)
    {
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (!items.Contains(list[i]))
                list.RemoveAt(i);
        }

        for (var i = 0; i < items.Count; i++)
        {
            var at = list.IndexOf(items[i]);
            if (at < 0)
                list.Insert(i, items[i]);
            else if (at != i)
                list.Move(at, i);
        }
    }

    private MethodDescriptor? FindMethod() => _schema?.FindMethod(Service, Rpc);

    // ---- Both ----

    [ObservableProperty]
    public partial bool AcceptAnyCertificate { get; set; }

    [ObservableProperty]
    public partial int TimeoutSeconds { get; set; }

    /// <summary>The variables to save from a successful response, each with its path in the body.</summary>
    public ApiPairList Captures { get; }

    /// <summary>Which variables the last response saved, and where.</summary>
    [ObservableProperty]
    public partial string? SavedVariables { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSending), nameof(CanResend))]
    public partial bool IsSending { get; set; }

    public bool IsNotSending => !IsSending;

    /// <summary>The request as last sent from this file, kept between runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastSent), nameof(CanResend), nameof(LastSentTime), nameof(LastSentText))]
    public partial ApiSentRequest? LastSent { get; set; }

    public bool HasLastSent => LastSent is not null;

    public bool CanResend => HasLastSent && !IsSending;

    public string LastSentTime => LastSent is { } sent ? string.Format(Strings.ApiRequest_Sent, sent.Time) : "";

    public string LastSentText => LastSent?.Describe() ?? "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResponse))]
    public partial ApiResponseViewModel? Response { get; set; }

    public bool HasResponse => Response is not null;

    [ObservableProperty]
    public partial string? Error { get; set; }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsSending)
            return;
        Save();
        await TransmitAsync(ToRequest(), async () =>
        {
            if (FindMethod() is null)
                await LoadServicesAsync();
            if (FindMethod() is { } method)
                return method;
            Error = _schema is null ? SchemaStatus : string.Format(Strings.ApiRequest_HasNoMethod, Service, Rpc);
            return null;
        });
    }

    /// <summary>Sends the request again as it was last sent, whatever has been edited since.</summary>
    [RelayCommand]
    private async Task ResendAsync()
    {
        if (IsSending || LastSent?.Request is not { } request)
            return;
        await TransmitAsync(request, async () =>
        {
            try
            {
                var schema = Schemas.TryGetValue(SchemaKey(request), out var known) ? known : await FetchSchemaAsync(request);
                if (schema.FindMethod(request.Service, request.Rpc) is { } method)
                    return method;
                Error = string.Format(Strings.ApiRequest_HasNoMethod, request.Service, request.Rpc);
            }
            catch (Exception e) when (IsSchemaFailure(e))
            {
                Error = SchemaFailure(e);
            }

            return null;
        });
    }

    private async Task TransmitAsync(ApiRequest request, Func<Task<MethodDescriptor?>> findMethod)
    {
        Error = null;
        SavedVariables = null;
        IsSending = true;
        _sending = new CancellationTokenSource();
        try
        {
            var variables = _variables();
            ApiResponse response;
            if (request.Kind == ApiKind.Rest)
            {
                Remember(request);
                response = await RestSender.SendAsync(request, variables, _sending.Token);
            }
            else
            {
                if (await findMethod() is not { } method)
                    return;

                // Loading the services may have moved the choice from a method that has gone.
                request.Service = method.Service.FullName;
                request.Rpc = method.Name;
                Remember(request);
                var types = Schemas.TryGetValue(SchemaKey(request), out var schema) ? schema.Types : null;
                response = await GrpcCaller.CallAsync(request, method, variables, _sending.Token, types);
            }

            Response = new ApiResponseViewModel(response, IsGrpc);
            var problems = new List<string>();
            var missing = variables.Missing(string.Join(" ", request.Url, request.Address, request.Body, request.Message, request.Token,
                string.Join(" ", request.Headers.Concat(request.Query).Concat(request.Metadata).Select(p => p.Name + p.Value)))).ToList();
            if (missing.Count > 0)
                problems.Add(string.Format(Strings.ApiRequest_NotDefinedInTheEnvironmentSo, string.Join(", ", missing.Select(m => "{{" + m + "}}"))));
            if (response.IsSuccess)
                SaveCaptures(response, request.Captures, problems);
            if (problems.Count > 0)
                Error = string.Join(" ", problems);
        }
        finally
        {
            IsSending = false;
            _sending.Dispose();
            _sending = null;
        }
    }

    private void Remember(ApiRequest request)
    {
        LastSent = new ApiSentRequest { Time = DateTime.Now, Request = request };
        ApiLastSent.Write(Path, LastSent);
    }

    private void SaveCaptures(ApiResponse response, List<ApiPair> captures, List<string> problems)
    {
        var (values, unread) = ResponseCapture.Read(response, captures);
        problems.AddRange(unread);
        if (values.Count == 0)
            return;
        var names = string.Join(", ", values.Select(v => v.Name));
        if (_saveVariables(values) is { } environment)
            SavedVariables = string.Format(Strings.ApiRequest_SavedTo, environment, names);
        else
            problems.Add(string.Format(Strings.ApiRequest_ChooseAnEnvironmentAtTheTop, names));
    }

    [RelayCommand]
    private void Cancel() => _sending?.Cancel();

    [RelayCommand]
    private async Task CopyBodyAsync()
    {
        if (Response is { Body.Length: > 0 } response && CopyText is not null)
            await CopyText(response.Body);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading)
            return;
        if (e.PropertyName is { } name && !Transient.Contains(name))
            Changed();
        if (e.PropertyName == nameof(Method))
            _described(this);
    }

    private void Changed()
    {
        if (_loading)
            return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes the request to its file now, if anything is waiting.</summary>
    public void Save()
    {
        if (!_saveTimer.IsEnabled)
            return;
        _saveTimer.Stop();
        try
        {
            ApiWorkspace.Write(Path, ToRequest());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error = string.Format(Strings.ApiRequest_CouldNotSave, Path, e.Message);
        }
    }

    public ApiRequest ToRequest() => new()
    {
        Kind = _kind,
        Method = Method,
        Url = Url,
        Query = Query.ToPairs(),
        Headers = Headers.ToPairs(),
        BodyKind = BodyKind,
        Body = Body,
        Form = Form.ToPairs(),
        AuthKind = AuthKind,
        Token = Token,
        Username = Username,
        Password = Password,
        Address = Address,
        Schema = Schema,
        ProtoFiles = ProtoFiles.Select(p => p.Path).ToList(),
        ImportPaths = ImportPaths.Select(p => p.Path).ToList(),
        Service = Service,
        Rpc = Rpc,
        Message = Message,
        Metadata = Metadata.ToPairs(),
        AcceptAnyCertificate = AcceptAnyCertificate,
        TimeoutSeconds = TimeoutSeconds,
        Captures = Captures.ToPairs(),
    };
}

/// <summary>A response as shown.</summary>
public sealed class ApiResponseViewModel(ApiResponse response, bool isGrpc)
{
    public string Status => response.Status;

    public bool IsSuccess => response.IsSuccess;

    public bool IsFailure => !response.IsSuccess;

    public string Elapsed => response.Elapsed.TotalMilliseconds < 1000
        ? string.Format(Strings.ApiResponse_Ms, response.Elapsed.TotalMilliseconds)
        : $"{response.Elapsed.TotalSeconds:0.00} s";

    public string Size => response.Size switch
    {
        < 1024 => $"{response.Size} B",
        < 1024 * 1024 => string.Format(Strings.ApiResponse_KB, response.Size / 1024.0),
        _ => string.Format(Strings.ApiResponse_MB, response.Size / 1024.0 / 1024.0),
    };

    public string Body => response.Body;

    public IReadOnlyList<ApiPair> Headers => response.Headers;

    public IReadOnlyList<ApiPair> Trailers => response.Trailers;

    public bool HasTrailers => isGrpc;
}
