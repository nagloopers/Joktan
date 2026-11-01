using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Settings.Themes;
using Joktan.Settings.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class SettingsTools
{
    public static IEnumerable<McpTool> Create(AgentContext app) =>
    [
        .. Search(app.SearchSettings),
        .. Menu(app),
        .. Themes(app.Themes),
        .. Backup(app.Backup),
    ];

    private static IEnumerable<McpTool> Search(SearchSettingsViewModel search) =>
    [
        Read("settings_search", "Read which areas the search box looks through.", [], _ => new { search.Notes, search.Help, search.Messages, search.Alerts }),
        Change("settings_search_update", "Switch areas of search on or off. Only what is given changes.", [P.OptFlag("notes", "Notes."), P.OptFlag("help", "Help."), P.OptFlag("messages", "Messages."), P.OptFlag("alerts", "Alerts.")], args =>
        {
            if (args.Flag("notes") is { } notes) search.Notes = notes;
            if (args.Flag("help") is { } help) search.Help = help;
            if (args.Flag("messages") is { } messages) search.Messages = messages;
            if (args.Flag("alerts") is { } alerts) search.Alerts = alerts;
            return "Saved.";
        }),
    ];

    private static IEnumerable<McpTool> Menu(AgentContext app) =>
    [
        Change("settings_menu_show", "Show or hide a menu entry.", [P.Text("page", "The entry's title, from app_pages."), P.Flag("shown", "Whether to show it.")], args =>
        {
            var item = AppTools.Find(app.Menu, args.Text("page")) ?? throw new ToolFailure($"No menu entry is called {args.Text("page")}.");
            if (!item.CanHide)
                throw new ToolFailure($"{item.Title} has to stay on the menu.");
            item.IsShown = args.Flag("shown")!.Value;
            return "Saved.";
        }),
        Change("settings_menu_move", "Move a menu entry up or down among its neighbours.", [P.Text("page", "The entry's title, from app_pages."), P.Choice("direction", "Which way.", "up", "down")], args =>
        {
            var item = AppTools.Find(app.Menu, args.Text("page")) ?? throw new ToolFailure($"No menu entry is called {args.Text("page")}.");
            var command = args.Text("direction") == "up" ? app.MenuSettings.MoveUpCommand : app.MenuSettings.MoveDownCommand;
            if (!command.CanExecute(item))
                throw new ToolFailure("It cannot move further that way.");
            command.Execute(item);
            return "Moved.";
        }),
    ];

    private static IEnumerable<McpTool> Themes(ThemeEditor themes) =>
    [
        Read("themes_list", "List the themes, and which is in use. A theme can be changed through themes_set and kept with themes_save.", [], _ => new
        {
            themes.Current, HasUnsavedChanges = themes.IsDirty, CanOverwriteCurrent = themes.CanOverwrite, FollowSystem = ThemeEditor.FollowSystem,
            Themes = themes.Themes.Select(t => t.Name).ToList(),
        }),
        Read("themes_tokens", "Read the current theme's colours, fonts and text sizes, by key.", [], _ => new
        {
            Colors = ThemeTokens.Colors.Select(t => new { t.Key, t.Label, Value = themes.Draft.Colors.GetValueOrDefault(t.Key) }).ToList(),
            Fonts = ThemeTokens.Fonts.Select(t => new { t.Key, t.Label, Value = themes.Draft.Fonts.GetValueOrDefault(t.Key) }).ToList(),
            TextSizes = ThemeTokens.TextSizes.Select(t => new { t.Key, t.Label, Value = themes.Draft.TextSizes.GetValueOrDefault(t.Key) }).ToList(),
        }),
        Change("themes_choose", "Use a theme, by name, or follow the system's light or dark.", [P.Text("name", "The theme's name.")], args =>
        {
            Check(themes.Choose(args.Text("name")));
            return $"Using {themes.Current}.";
        }),
        Change("themes_set", "Change colours, fonts and text sizes of the current theme. It shows at once; themes_save keeps it.",
            [P.Map("colors", "Keys and colours such as #102030."), P.Map("fonts", "Keys and font names."), P.Map("text_sizes", "Keys and sizes."), P.OptFlag("dark", "Whether the theme is a dark one.")], args =>
            {
                foreach (var (key, value) in args.Map("colors"))
                    themes.SetColor(key, value);
                foreach (var (key, value) in args.Map("fonts"))
                    themes.SetFont(key, value);
                foreach (var (key, value) in args.Map("text_sizes"))
                    themes.SetTextSize(key, double.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                if (args.Flag("dark") is { } dark)
                    themes.SetDark(dark);
                return "Changed. Not kept until saved.";
            }),
        Change("themes_save", "Keep the changes: in the current theme, or in a new one when a name is given. A built-in theme can only be saved under a new name.", [P.OptText("name", "A name for a new theme.")], args =>
        {
            Check(args.OptText("name") is { } name ? themes.SaveAs(name) : themes.Save());
            return $"Saved as {themes.Current}.";
        }),
        Change("themes_discard", "Throw away unsaved changes to the current theme.", [], _ =>
        {
            themes.Discard();
            return "Discarded.";
        }),
        Risky("themes_delete", "Delete a saved theme.", [P.Text("name", "The theme's name.")], args =>
        {
            var theme = themes.Themes.FirstOrDefault(t => t.Name == args.Text("name")) ?? throw new ToolFailure($"No theme is called {args.Text("name")}.");
            Check(themes.Delete(theme));
            return "Deleted.";
        }),
    ];

    private static IEnumerable<McpTool> Backup(BackupViewModel backup) =>
    [
        Read("folders_list", "List where the app keeps its data, what can be moved, and what backups include.", [], _ =>
        {
            backup.Refresh();
            return new
            {
                BackupFolder = backup.BackupFolderText, backup.KeepLast, backup.Status, backup.Error,
                Folders = backup.Rows.Concat(backup.MessageRows).Select(r => new { r.Name, Folder = r.CurrentText, r.CanChange, r.CanBackUp, Included = r.IsIncluded, r.Problem, r.Warning }).ToList(),
            };
        }),
        RiskyAsync("folders_change", "Move what the app keeps in a folder, as the Folders and Backup page does. Changing the app settings folder restarts the app when it is applied.",
            [P.Text("name", "The folder's name, from folders_list."), P.Text("path", "The new whole path.")], async args =>
            {
                var row = Row(backup, args.Text("name"));
                if (!row.CanChange)
                    throw new ToolFailure($"{row.Name} cannot be moved.");
                row.PathText = args.Text("path");
                await row.ApplyCommand.ExecuteAsync(null);
                Check(row.Problem);
                return row.Message ?? "Changed.";
            }),
        Change("backup_include", "Choose whether a backup includes a folder.", [P.Text("name", "The folder's name."), P.Flag("included", "Whether it is included.")], args =>
        {
            var row = Row(backup, args.Text("name"));
            if (!row.CanBackUp)
                throw new ToolFailure($"{row.Name} is not something a backup can hold.");
            row.IsIncluded = args.Flag("included")!.Value;
            return "Saved.";
        }),
        Change("backup_settings", "Change where backups go and how many are kept. Only what is given changes.", [P.OptText("folder", "The backup folder."), P.OptNumber("keep_last", "How many to keep.")], args =>
        {
            if (args.OptText("folder") is { } folder)
                backup.BackupFolderText = folder;
            if (args.Whole("keep_last") is { } keep)
                backup.KeepLast = keep;
            Check(backup.Error);
            return "Saved.";
        }),
        ChangeAsync("backup_now", "Make a backup now. The oldest ones past the number kept are deleted.", [], async _ =>
        {
            await backup.BackUpNowCommand.ExecuteAsync(null);
            Check(backup.Error);
            return new { backup.Status, Skipped = backup.Skipped.ToList() };
        }),
        Read("backup_list", "List the backups in the backup folder.", [], _ =>
        {
            backup.Refresh();
            return backup.Backups.Select(b => new { b.Name, b.Path }).ToList();
        }),
        Read("backup_open", "Read what a backup holds, ready to restore.", [P.Text("path", "The backup's path.")], args =>
        {
            backup.OpenBackup(args.Text("path"));
            Check(backup.RestoreError);
            return new { backup.RestoreSummary, Items = backup.RestoreRows.Select(r => new { r.Name, r.Detail, r.HasFiles }).ToList() };
        }),
        RiskyAsync("backup_restore", "Replace what the app holds with what a backup holds, keeping a safety copy first. The app restarts, which ends this conversation.",
            [P.Text("path", "The backup's path."), P.OptText("only", "The names to restore, separated by commas; all of them otherwise.")], async args =>
            {
                backup.OpenBackup(args.Text("path"));
                Check(backup.RestoreError);
                if (args.OptText("only") is { } only)
                {
                    var names = only.Split(',', StringSplitOptions.TrimEntries);
                    foreach (var row in backup.RestoreRows)
                        row.IsChosen = row.HasFiles && names.Contains(row.Name, StringComparer.OrdinalIgnoreCase);
                }

                backup.AskRestoreCommand.Execute(null);
                Check(backup.RestoreError);
                await backup.ConfirmRestoreCommand.ExecuteAsync(null);
                Check(backup.RestoreError);
                return "Restoring: the app is restarting.";
            }),
    ];

    private static FolderRowViewModel Row(BackupViewModel backup, string name)
    {
        backup.Refresh();
        return backup.Rows.Concat(backup.MessageRows).FirstOrDefault(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
               ?? throw new ToolFailure($"No folder is called {name}.");
    }
}
