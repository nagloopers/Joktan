using System;
using System.Collections.Generic;
using System.Linq;
using Joktan.Agent.Mcp;
using Joktan.Notes.ViewModels;
using static Joktan.Agent.Tools.AgentTools;

namespace Joktan.Agent.Tools;

public static class NotesTools
{
    public static IEnumerable<McpTool> Create(NotesViewModel notes) =>
    [
        Read("notes_list", "List the note pages as a tree, with their ids.", [], _ => notes.Roots.Select(Describe).ToList()),
        Read("notes_read", "Read a note page's Markdown.", [P.Text("id", "The page's id.")], args =>
        {
            var node = Find(notes, args.Text("id"));

            // A page's text is read from its file when it is first selected.
            if (node.Text is null)
            {
                var shown = notes.Selected;
                notes.Selected = node;
                notes.Selected = shown;
            }

            return new { node.Id, Title = node.DisplayTitle, node.Text };
        }),
        Change("notes_create", "Make a note page and show it.",
            [P.Text("title", "Its title."), P.OptText("text", "Its Markdown."), P.OptText("parent", "The id of the page to put it below; at the top otherwise.")], args =>
            {
                if (args.OptText("parent") is { } parent)
                {
                    notes.Selected = Find(notes, parent);
                    notes.NewChildPageCommand.Execute(null);
                }
                else
                {
                    notes.NewPageCommand.Execute(null);
                }

                notes.PageTitle = args.Text("title");
                notes.Text = args.OptText("text") ?? "";
                notes.IsEditing = false;
                return new { notes.Selected!.Id };
            }),
        Change("notes_update", "Change a note page's title or Markdown, and show it. The Markdown given replaces all of it.",
            [P.Text("id", "The page's id."), P.OptText("title", "The new title."), P.OptText("text", "The new Markdown.")], args =>
            {
                notes.Selected = Find(notes, args.Text("id"));
                if (args.OptText("title") is { } title)
                    notes.PageTitle = title;
                if (args.OptText("text") is { } text)
                    notes.Text = text;
                return "Saved.";
            }),
        Change("notes_move", "Move a note page: up or down among its neighbours, below the page above it (indent), or out of its parent (outdent).",
            [P.Text("id", "The page's id."), P.Choice("direction", "Where to.", "up", "down", "indent", "outdent")], args =>
            {
                notes.Selected = Find(notes, args.Text("id"));
                System.Windows.Input.ICommand command = args.Text("direction") switch
                {
                    "up" => notes.MoveUpCommand,
                    "down" => notes.MoveDownCommand,
                    "indent" => notes.IndentCommand,
                    "outdent" => notes.OutdentCommand,
                    _ => throw new ToolFailure("direction must be up, down, indent or outdent."),
                };
                if (!command.CanExecute(null))
                    throw new ToolFailure("It cannot move that way from where it is.");
                command.Execute(null);
                return "Moved.";
            }),
        Risky("notes_delete", "Delete a note page and any pages below it. This cannot be undone.", [P.Text("id", "The page's id.")], args =>
        {
            notes.Selected = Find(notes, args.Text("id"));
            notes.ConfirmDeleteCommand.Execute(null);
            Check(notes.Error);
            return "Deleted.";
        }),
        Read("notes_folder", "Say which folder holds the notes.", [], _ => new { notes.Folder, notes.DefaultFolder }),
    ];

    private static object Describe(NoteNodeViewModel node) => new
    {
        node.Id,
        Title = node.DisplayTitle,
        Children = node.Children.Select(Describe).ToList(),
    };

    private static NoteNodeViewModel Find(NotesViewModel notes, string id)
        => notes.Roots.SelectMany(r => r.SelfAndDescendants()).FirstOrDefault(n => n.Id == id)
           ?? throw new ToolFailure($"No note page has the id {id}. notes_list gives the ids.");
}
