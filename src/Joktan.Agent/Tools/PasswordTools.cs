using System.Collections.Generic;
using System.Linq;
using Joktan.Agent.Mcp;
using Joktan.Passwords.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

/// <summary>
/// The password manager. Nothing here unlocks a vault or changes where passwords are kept, and only
/// <c>passwords_reveal</c> gives out a password, asking the user each time.
/// </summary>
public static class PasswordTools
{
    public static IEnumerable<McpTool> Create(PasswordsViewModel page) =>
    [
        Read("passwords_status", "Say whether the password manager is open, locked or not set up, and where it keeps passwords.", [], _ => new
        {
            State = page.State.ToString(),
            page.StorageText,
            page.StorageLocation,
        }),
        Read("passwords_list", "List folders and entries by path, with usernames and websites but no passwords.", [], _ =>
        {
            RequireOpen(page);
            return page.Roots.Select(Describe).ToList();
        }),
        Read("passwords_search", "Find entries by name, username or website. No passwords are given.", [P.Text("text", "What to look for.")], args =>
        {
            RequireOpen(page);
            return page.Roots.SelectMany(r => r.SelfAndDescendants()).Where(n => n.Matches(args.Text("text"))).Select(n => new { Path = PathOf(n), n.IsFolder, Username = n.Detail }).ToList();
        }),
        Read("passwords_get", "Read an entry: its username, website and notes, but not its password.", [P.Text("path", "The entry's path, from passwords_list.")], args =>
        {
            var entry = Find(page, args.Text("path")).Entry ?? throw new ToolFailure("That is a folder, not an entry.");
            return new { entry.Title, entry.Username, entry.Url, entry.Notes, entry.Modified };
        }),
        new McpTool("passwords_reveal", "Read an entry's password. The user is asked each time, and it goes to the AI provider, so only use it when they asked for this.", ToolRisk.Secret,
            [P.Text("path", "The entry's path, from passwords_list.")], args =>
            {
                RequireIdle(page);
                page.Selected = Find(page, args.Text("path"));
                if (!page.IsEntrySelected)
                    throw new ToolFailure("That is a folder, not an entry.");

                // Revealed and hidden again, so the password does not stay on the page.
                if (page.IsRevealed)
                    page.ToggleRevealCommand.Execute(null);
                page.ToggleRevealCommand.Execute(null);
                var password = page.RevealedPassword;
                page.ToggleRevealCommand.Execute(null);
                return System.Threading.Tasks.Task.FromResult<object?>(new { Password = password });
            }),
        Change("passwords_create_entry", "Make an entry. Give a password, or ask for one to be generated, which the agent does not get to see.",
            [
                P.Text("title", "Its name."), P.OptText("username", "Its username."), P.OptText("password", "Its password."),
                P.OptFlag("generate", "Generate the password instead."), P.OptNumber("length", "How long a generated password is."), P.OptFlag("symbols", "Whether a generated password has symbols."),
                P.OptText("url", "Its website."), P.OptText("notes", "Its notes."), P.OptText("folder", "The path of the folder to put it in; at the top otherwise."),
            ], args =>
            {
                RequireIdle(page);
                page.Selected = args.OptText("folder") is { } folder ? RequireFolder(page, folder) : null;
                page.NewEntryCommand.Execute(null);
                Fill(page, args);
                return Save(page);
            }),
        Change("passwords_update_entry", "Change an entry. Only what is given changes. The password can be replaced, or generated anew without the agent seeing it.",
            [
                P.Text("path", "The entry's path."), P.OptText("title", "Its new name."), P.OptText("username", "Its new username."), P.OptText("password", "Its new password."),
                P.OptFlag("generate", "Generate a new password."), P.OptNumber("length", "How long a generated password is."), P.OptFlag("symbols", "Whether a generated password has symbols."),
                P.OptText("url", "Its new website."), P.OptText("notes", "Its new notes."), P.OptText("folder", "The path of the folder to move it to; \"\" for the top."),
            ], args =>
            {
                RequireIdle(page);
                page.Selected = Find(page, args.Text("path"));
                if (!page.IsEntrySelected)
                    throw new ToolFailure("That is a folder, not an entry.");
                page.EditCommand.Execute(null);
                Fill(page, args);
                return Save(page);
            }),
        Change("passwords_create_folder", "Make a folder.", [P.Text("name", "Its name."), P.OptText("folder", "The path of the folder to put it in; at the top otherwise.")], args =>
        {
            RequireIdle(page);
            page.Selected = args.OptText("folder") is { } folder ? RequireFolder(page, folder) : null;
            page.NewFolderCommand.Execute(null);
            page.EditTitle = args.Text("name");
            return Save(page);
        }),
        Change("passwords_update_folder", "Rename a folder or move it.", [P.Text("path", "The folder's path."), P.OptText("name", "Its new name."), P.OptText("folder", "The path of the folder to move it to; \"\" for the top.")], args =>
        {
            RequireIdle(page);
            page.Selected = RequireFolder(page, args.Text("path"));
            page.EditCommand.Execute(null);
            Fill(page, args, "name");
            return Save(page);
        }),
        Risky("passwords_delete", "Delete an entry and its password, or a folder and everything in it. This cannot be undone.", [P.Text("path", "The path.")], args =>
        {
            RequireIdle(page);
            page.Selected = Find(page, args.Text("path"));
            var what = page.DeleteText;
            page.ConfirmDeleteCommand.Execute(null);
            Check(page.Error);
            return $"Done: {what}";
        }),
        Change("passwords_lock", "Lock an open vault. Only the user can open it again, on the Passwords page.", [], _ =>
        {
            page.LockCommand.Execute(null);
            return page.IsVault ? "Locked." : "Passwords are in the system keychain, which has no lock here.";
        }),
    ];

    private static object Describe(PasswordNodeViewModel node) => node.IsFolder
        ? new { Path = PathOf(node), Kind = "folder", Children = node.Children.Select(Describe).ToList() }
        : new { Path = PathOf(node), Kind = "entry", Username = node.Detail, Website = node.Entry!.Url };

    // A node's own Path names only the folders above it, which is not enough to find it by. The root has no parent and no name of its own.
    private static string PathOf(PasswordNodeViewModel node)
    {
        var names = new List<string>();
        for (var at = node; at.Parent is not null; at = at.Parent)
            names.Insert(0, at.Name);
        return string.Join(" / ", names);
    }

    private static void RequireOpen(PasswordsViewModel page)
    {
        if (!page.IsOpen)
            throw new ToolFailure(page.IsLocked
                ? "The vault is locked, and only the user can open it. Ask them to unlock it on the Passwords page."
                : "The password manager is not set up. Ask the user to set it up on the Passwords page.");
    }

    // Working through the page's own editing would overwrite what the user is typing there.
    private static void RequireIdle(PasswordsViewModel page)
    {
        RequireOpen(page);
        if (page.IsEditing)
            throw new ToolFailure("The user is editing an entry on the Passwords page. Ask them to save or cancel it first.");
    }

    private static PasswordNodeViewModel Find(PasswordsViewModel page, string path)
    {
        RequireOpen(page);
        var found = page.Roots.SelectMany(r => r.SelfAndDescendants()).Where(n => PathOf(n) == path.Trim()).ToList();
        return found.Count switch
        {
            0 => throw new ToolFailure($"Nothing is at {path}. passwords_list gives the paths."),
            1 => found[0],
            _ => throw new ToolFailure($"{found.Count} things are at {path}, so it does not say which. Ask the user to rename one."),
        };
    }

    private static PasswordNodeViewModel RequireFolder(PasswordsViewModel page, string path)
    {
        var node = Find(page, path);
        return node.IsFolder ? node : throw new ToolFailure($"{path} is an entry, not a folder.");
    }

    private static void Fill(PasswordsViewModel page, ToolArgs args, string titleName = "title")
    {
        if (args.OptText(titleName) is { } title)
            page.EditTitle = title;
        if (args.OptText("username") is { } username)
            page.EditUsername = username;
        if (args.OptText("password") is { } password)
            page.EditPassword = password;
        if (args.OptText("url") is { } url)
            page.EditUrl = url;
        if (args.OptText("notes") is { } notes)
            page.EditNotes = notes;
        if (args.Flag("generate") == true)
        {
            if (args.Whole("length") is { } length)
                page.GenerateLength = length;
            if (args.Flag("symbols") is { } symbols)
                page.GenerateSymbols = symbols;
            page.GenerateCommand.Execute(null);
        }

        if (args.OptText("folder") is { } folder && page.Selected?.Parent is not null)
        {
            // Only asked about on an edit, when the choices name each folder but the top one is empty.
            var match = page.FolderChoices.FirstOrDefault(c => folder.Trim().Length == 0 ? c == page.FolderChoices[0] : PathOf(c.Node) == folder.Trim());
            if (match is null)
            {
                page.CancelCommand.Execute(null);
                throw new ToolFailure($"No folder is at {folder}.");
            }

            page.EditFolder = match;
        }
    }

    private static object Save(PasswordsViewModel page)
    {
        page.SaveCommand.Execute(null);
        Check(page.Error);
        return new { Saved = page.Selected is { } node ? PathOf(node) : null };
    }
}
