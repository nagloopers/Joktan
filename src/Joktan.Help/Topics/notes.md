# Notes

Pages of notes written in Markdown. A page can sit below another page, to any depth. They are plain
files, so they can also be read, synced or kept in version control outside Joktan.

## Writing

1. Press **New page** for a page at the top level, or **New child page** for one below the selected
   page. It opens ready to edit.
2. Give it a title and write on the left. The formatted page is drawn on the right as you type.
3. Press **Done** to read it at full width. **Edit** goes back. Ctrl+E (⌘E on macOS) switches between
   the two.

There is nothing to save by hand: changes are saved as you type, and when the app closes.

## Arranging pages

Select a page in the list, then:

- **Up** and **Down** move it among the pages that share its parent.
- **Indent** makes it a child of the page above it.
- **Outdent** moves it out of its parent, to just after it.
- **Delete** deletes it and every page below it, once you confirm.

## What you can write

- Headings, `**bold**`, `*italic*`, `~~struck out~~`, `==marked==` and `++inserted++` text.
- Lists, and task lists written `- [ ] to do` and `- [x] done`.
- Quotes, rules, `inline code` and code blocks.
- Pipe and grid tables.
- Links. A click opens a full address in the browser; bare web addresses are links too.

Images are not loaded: they show their description. Links from one note to another are not built yet.

To find something in your notes, use the [search box](help:search) in the top bar.

## Choosing where notes are kept

Notes are kept in the `notes` folder of the [data folder](help:settings-and-data) until you choose
another. A shared or synced folder works too.

1. Press **Folder**.
2. Type a full path, or use **Browse**. `~` stands for your home folder. **Default** fills in Joktan's
   own folder.
3. Press **Use folder**.

What happens then depends on the folder:

- If it already holds notes, it is opened as it is.
- If not, the notes open now are copied into it. The originals stay where they were, and files
  already in the new folder are not overwritten.

Each page is one `.md` file named by the page's ID, and `index.json` holds the titles and how the
pages nest.
