# Notes

Pages of notes written in Markdown. A page can sit below another page, to any depth.

| Item      | Value       |
|-----------|-------------|
| Menu path | Notes       |
| Status    | Implemented |

## Purpose

Keep notes next to the tools: how a broker is set up, what a topic carries, what is left to do.
The notes are plain Markdown files, so they can also be read, synced or kept in version
control outside Joktan.

## Page layout

1. Page header: title "Notes", subtitle "Notes", and **Folder** and **New page** on the right.
2. While **Folder** is open, a band for choosing the notes folder (see [Folder](#folder)).
3. On the left, **Pages**: the pages as a tree, each below its parent, with **New child page**
   at the top and **Up**, **Down**, **Indent** and **Outdent** at the bottom.
4. On the right, the selected page in view mode or edit mode.

## Pages

| Button         | What it does                                                         |
|----------------|----------------------------------------------------------------------|
| New page       | Adds an untitled page at the end of the top level and opens it for editing |
| New child page | Adds an untitled page as the last child of the selected page and opens it for editing |
| Up, Down       | Moves the selected page before or after its neighbour, among the pages that share its parent |
| Indent         | Makes the selected page the last child of the page above it          |
| Outdent        | Moves the selected page out of its parent, to just after the parent  |
| Delete         | Deletes the selected page and every page below it, once confirmed    |

A page with no title is listed as "Untitled". Selecting a page expands the pages above it.

There is always a page selected while there are any: a Ctrl or ⌘ click on the selected page
does not clear it, and deleting a page selects the one after it, else the one before, else its
parent. Notes opens where it was left: the same page, in the same mode, when coming back from
another page of the app; and after a restart, the page open last, in view mode, with the same
pages expanded. When that page is gone, the first page is selected.

## View and edit modes

A page opens in **view mode**: its title, the pages above it (for example "Projects / Joktan"),
and the formatted page using the full width of the card. **Edit** switches to **edit mode**: the
title becomes a text box, and the Markdown is on the left with the formatted page beside it,
updated as you type. **Done** goes back to view mode. Ctrl+E (⌘E on macOS) switches between them.

Selecting another page shows it in view mode. A page made with **New page** or **New child page**
opens in edit mode.

## Markdown

CommonMark, parsed with [Markdig](https://github.com/xoofx/markdig), plus pipe and grid tables,
task lists, strikethrough (`~~text~~`), marked text (`==text==`) and bare web addresses as links.

| Element              | Shown as                                                       |
|----------------------|----------------------------------------------------------------|
| Headings 1 to 6      | 24, 20, 17, then 14 SemiBold; level 6 muted                    |
| Paragraphs           | 14 Regular, wrapped                                            |
| Bold, italic, struck out, inserted (`++text++`) | Bold, italic, strikethrough, underline |
| Inline code          | Code font on the hover colour                                  |
| Links                | Underlined in the info colour; a click opens absolute addresses in the browser |
| Lists                | Bullets or numbers in the muted colour; a task's ☐ or ☑ takes the place of the bullet |
| Quotes               | Muted text with an accent bar on the left                      |
| Code blocks          | Code font in a bordered box on the alternate surface; long lines scroll sideways |
| Tables               | Header row in SemiBold, a rule under each row, column alignment kept |
| Rules                | A 1-point line                                                 |
| Images, raw HTML     | Their description or the HTML text, muted; images are not loaded |

Text in the formatted page can be selected and copied one block at a time.

## Saving

Changes are saved as you type, 0.6 seconds after the last change, on a background thread, and
again when the app closes. Nothing needs to be saved by hand.

## Folder

Notes are kept in the `notes` folder of Joktan's data folder unless another folder is chosen.
**Folder** opens a band with the folder path, **Browse**, **Default** (which fills in the default
folder), **Cancel** and **Use folder**:

- A folder that already holds notes (it has an `index.json`) is opened as it is.
- Otherwise the notes open now are copied into it and opened there. The originals are left where
  they were; files already in the new folder are not overwritten.
- The path must be a full path; `~` stands for the home folder.

The chosen folder is remembered in `notes-location.json` in the data folder; choosing the
default folder removes that file.

## Files

| File             | Holds                                                            |
|------------------|------------------------------------------------------------------|
| `index.json`     | The pages' IDs and titles, how they nest and in what order, which are expanded, and the page open last |
| `<id>.md`        | One page's Markdown, named by the page's ID                      |

An index that cannot be read is treated as no notes; the page files are left where they are.

## Not built yet

- Dragging pages in the tree to move them
- Links from one page to another
- Showing images
