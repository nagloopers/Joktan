using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Joktan.Agent.Mcp;
using Joktan.Api;
using Joktan.Api.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class ApiTools
{
    private static readonly ToolParameter RequestPath = P.Text("path", "The request's path, from api_list.");

    public static IEnumerable<McpTool> Create(ApiClientViewModel page) =>
    [
        Read("api_list", "List the API Client's folders and requests, by path within its workspace.", [], _ => new
        {
            Workspace = page.Folder,
            page.SelectedEnvironment,
            Items = page.Roots.Select(n => Describe(page, n)).ToList(),
        }),
        Read("api_read", "Read a saved request. Auth tokens and passwords show only as being set.", [RequestPath], args =>
        {
            var request = Open(page, args);
            return new
            {
                Kind = request.IsRest ? "REST" : "gRPC",
                request.Method, request.Url, Query = Pairs(request.Query), Headers = Pairs(request.Headers), BodyKind = request.BodyKind.ToString(), request.Body, Form = Pairs(request.Form),
                Auth = request.AuthKind.ToString(), TokenSet = request.Token.Length > 0, request.Username, PasswordSet = request.Password.Length > 0,
                request.Address, request.Service, request.Rpc, request.Message, Metadata = Pairs(request.Metadata), request.AcceptAnyCertificate, request.TimeoutSeconds,
                Captures = Pairs(request.Captures), Last = request.LastSentText,
            };
        }),
        Change("api_create_request", "Make a request.", [P.Choice("kind", "What kind.", "rest", "grpc"), P.Text("name", "Its name."), P.OptText("folder", "The path of the folder to put it in.")], args =>
        {
            Target(page, args);
            if (args.Text("kind") == "grpc")
                page.NewGrpcRequestCommand.Execute(null);
            else
                page.NewRestRequestCommand.Execute(null);
            return Rename(page, args.Text("name"));
        }),
        Change("api_create_folder", "Make a folder.", [P.Text("name", "Its name."), P.OptText("folder", "The path of the folder to put it in.")], args =>
        {
            Target(page, args);
            page.NewFolderCommand.Execute(null);
            return Rename(page, args.Text("name"));
        }),
        Change("api_update_request", "Change a saved request. Only what is given changes; a list given replaces the whole list. Lists are objects of names and values.",
            [
                RequestPath, P.OptText("method", "The HTTP method."), P.OptText("url", "The address. {{name}} uses a variable."), P.Map("query", "The query parameters."), P.Map("headers", "The headers."),
                P.OptChoice("body_kind", "The kind of body.", Enum.GetNames<ApiBodyKind>()), P.OptText("body", "The body."), P.Map("form", "The form fields."),
                P.OptChoice("auth", "The kind of auth.", Enum.GetNames<ApiAuthKind>()), P.OptText("token", "The bearer token."), P.OptText("username", "The basic auth user."), P.OptText("password", "The basic auth password."),
                P.OptText("address", "The gRPC address."), P.OptText("service", "The gRPC service."), P.OptText("rpc", "The gRPC method."), P.OptText("message", "The gRPC message, as JSON."), P.Map("metadata", "The gRPC metadata."),
                P.OptFlag("accept_any_certificate", "Accept any server certificate. Only for testing."), P.OptNumber("timeout_seconds", "How long to wait."), P.Map("captures", "What to save from the response as variables."),
            ], args =>
            {
                var request = Open(page, args);
                if (args.OptText("method") is { } method) request.Method = method;
                if (args.OptText("url") is { } url) request.Url = url;
                Replace(request.Query, args, "query");
                Replace(request.Headers, args, "headers");
                if (args.OptText("body_kind") is { } bodyKind) request.BodyKind = Enum.Parse<ApiBodyKind>(bodyKind, ignoreCase: true);
                if (args.OptText("body") is { } body) request.Body = body;
                Replace(request.Form, args, "form");
                if (args.OptText("auth") is { } auth) request.AuthKind = Enum.Parse<ApiAuthKind>(auth, ignoreCase: true);
                if (args.OptText("token") is { } token) request.Token = token;
                if (args.OptText("username") is { } username) request.Username = username;
                if (args.OptText("password") is { } password) request.Password = password;
                if (args.OptText("address") is { } address) request.Address = address;
                if (args.OptText("service") is { } service) request.Service = service;
                if (args.OptText("rpc") is { } rpc) request.Rpc = rpc;
                if (args.OptText("message") is { } message) request.Message = message;
                Replace(request.Metadata, args, "metadata");
                if (args.Flag("accept_any_certificate") is { } any) request.AcceptAnyCertificate = any;
                if (args.Whole("timeout_seconds") is { } timeout) request.TimeoutSeconds = timeout;
                Replace(request.Captures, args, "captures");
                request.Save();
                return "Saved.";
            }),
        RiskyAsync("api_send", "Send a saved request and read the response, in the environment chosen.", [RequestPath], async args =>
        {
            var request = Open(page, args);
            await request.SendCommand.ExecuteAsync(null);
            Check(request.Error);
            return request.Response is { } response
                ? new { response.Status, response.IsSuccess, response.Elapsed, response.Size, response.Body, response.Headers, response.Trailers, request.SavedVariables }
                : throw new ToolFailure("No response came back.");
        }),
        Change("api_duplicate", "Copy a saved request.", [RequestPath], args =>
        {
            Open(page, args);
            page.DuplicateCommand.Execute(null);
            Check(page.Error);
            return new { Path = Relative(page, page.Selected!) };
        }),
        Change("api_rename", "Rename a request or folder.", [P.Text("path", "Its path."), P.Text("name", "Its new name.")], args =>
        {
            page.Selected = Find(page, args.Text("path"));
            return Rename(page, args.Text("name"));
        }),
        Risky("api_delete", "Delete a request's file, or a folder and the files in it.", [P.Text("path", "Its path.")], args =>
        {
            page.Selected = Find(page, args.Text("path"));
            var what = page.DeleteText;
            page.ConfirmDeleteCommand.Execute(null);
            Check(page.Error);
            return $"Done: {what}";
        }),
        Read("api_environments", "List the environments and their variables. Values go to the AI provider, so do not read these for no reason.", [], _ => new
        {
            page.SelectedEnvironment,
            Environments = page.Environments.Select(e => new { e.Name, Variables = Pairs(e.Variables) }).ToList(),
        }),
        Change("api_environment_select", "Choose the environment that requests use.", [P.Text("name", "Its name, or \"No environment\".")], args =>
        {
            if (!page.EnvironmentChoices.Contains(args.Text("name")))
                throw new ToolFailure($"No environment is called {args.Text("name")}.");
            page.SelectedEnvironment = args.Text("name");
            return "Chosen.";
        }),
        Change("api_environment_set", "Set variables of an environment, making it if it is new.", [P.Text("environment", "Its name."), P.Map("variables", "The names and values.")], args =>
        {
            var name = args.Text("environment");
            var environment = page.Environments.FirstOrDefault(e => e.Name == name);
            if (environment is null)
            {
                page.NewEnvironmentCommand.Execute(null);
                environment = page.Environments.Last();
                environment.Name = name;
            }

            foreach (var (key, value) in args.Map("variables"))
            {
                if (environment.Variables.FirstOrDefault(v => v.Name == key) is { } existing)
                    existing.Value = value;
                else
                    environment.Variables.Add(new ApiPairViewModel(environment.Variables, new ApiPair { Name = key, Value = value }));
            }

            environment.Variables.OnChanged();
            Check(page.Error);
            return "Saved.";
        }),
    ];

    private static object Describe(ApiClientViewModel page, ApiNodeViewModel node) => node.IsFolder
        ? new { Path = Relative(page, node), Kind = "folder", Method = "", Children = node.Children.Select(c => Describe(page, c)).ToList() }
        : new { Path = Relative(page, node), Kind = "request", node.Method, Children = new List<object>() };

    private static string Relative(ApiClientViewModel page, ApiNodeViewModel node) => Path.GetRelativePath(page.Folder, node.Path);

    private static ApiNodeViewModel Find(ApiClientViewModel page, string path)
        => page.Roots.SelectMany(r => r.SelfAndDescendants()).FirstOrDefault(n => Relative(page, n) == path.Trim())
           ?? throw new ToolFailure($"Nothing is at {path}. api_list gives the paths.");

    private static ApiRequestViewModel Open(ApiClientViewModel page, ToolArgs args)
    {
        var node = Find(page, args.Text("path"));
        if (node.IsFolder)
            throw new ToolFailure("That is a folder, not a request.");
        page.Selected = node;
        return page.Request ?? throw new ToolFailure("The request could not be opened.");
    }

    private static void Target(ApiClientViewModel page, ToolArgs args)
    {
        if (args.OptText("folder") is not { } folder)
        {
            page.Selected = null;
            return;
        }

        var node = Find(page, folder);
        page.Selected = node.IsFolder ? node : throw new ToolFailure($"{folder} is a request, not a folder.");
    }

    private static object Rename(ApiClientViewModel page, string name)
    {
        page.EditName = name;
        page.RenameCommand.Execute(null);
        Check(page.Error);
        return new { Path = Relative(page, page.Selected!) };
    }

    private static List<object> Pairs(ApiPairList list) => list.Select(p => (object)new { p.Name, p.Value, p.Enabled }).ToList();

    private static void Replace(ApiPairList list, ToolArgs args, string name)
    {
        if (args.Raw.TryGetProperty(name, out _) is false)
            return;
        list.Clear();
        foreach (var (key, value) in args.Map(name))
            list.Add(new ApiPairViewModel(list, new ApiPair { Name = key, Value = value }));
        list.OnChanged();
    }
}
