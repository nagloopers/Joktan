# Search

The search box in the top bar finds things across notes, help, messages and alerts.

| Item      | Value |
|-----------|-------|
| Where     | The search box in the top bar; set up at Settings / Search |
| Status    | Implemented |

## Settings page

A **Search** card with one toggle switch for each area: **Notes**, **Help**, **Messages** and **Alerts**. All are on
to begin with. A change is saved at once to `search.json` in the data folder and applies to the box at once. A file
that cannot be read leaves them all on.

## Finding

| Rule | Behaviour |
|------|-----------|
| A match | Every word typed appears, ignoring case. The words can be in different parts of the same thing |
| Results | Update with each key, in the order Notes, Help, Messages, Alerts, and show the area, a title and the line with the most of the words, with the words marked |
| The marks' colours | **Highlight** and **Text on the highlight** on Settings / Colours, under Search. They belong to the theme. Yellow with dark text to begin with, in every built-in theme |
| Marks where it leads | A note or help topic opened from a result has every place the words appear marked, and scrolls to the first. They go when another page or topic is chosen, the note is edited, or the box is cleared. A message or alert keeps its marks until its panel closes |
| The box's hint | Names the areas switched on, or says search is switched off |
| Every area switched off | The results say so, with a button to the settings page |
| Nothing found | The results say so |

| Area | Looks through | Order | Opening a result |
|------|---------------|-------|------------------|
| Notes | Title and Markdown as written, so link addresses match. A page not yet opened is read from its file the first time | Pages with all the words in their title first, then tree order | Goes to the Notes page, on that page |
| Help | Title and text as read, as [the help window's search](../help/spec.md#search) does | Topics with all the words in their title first, then list order | Opens that topic in the help window |
| Messages | Sender's name and text. These are the latest 200 kept | Newest first | Opens the Messages panel, scrolled to the message and marked until the panel closes |
| Alerts | Title, level and text. These are the latest 200 kept, whatever the panel's filter shows | Newest first | Opens the Alerts panel the same way. A filter that would hide the alert goes back to all levels |

Passwords are never searched.

## Using the box

| Key or action | Behaviour |
|---------------|-----------|
| Typing | Shows the results. The box keeps the focus |
| Up and Down | Moves the highlight, starting on the first result |
| Enter | Opens the highlighted result. With the results hidden, shows them again |
| Escape | Hides the results, then clears the box |
| The **×** in the box | Shows while there is text. Clears the box, hides the results, removes the marks and leaves the focus in the box |
| Clicking a result | Opens it |
| Clicking elsewhere | Hides the results, and the click still counts |
| Clicking the box | Looks again and shows the results, while there is text in it |

Opening a result leaves the text in the box, so the next result is one click away. Results are worked out when the box
changes or is opened, so a message that arrives while they show is added the next time.

## Implementation

| Part | File |
|------|------|
| Box and results | `src/Joktan/Views/MainWindow.axaml`, `src/Joktan/ViewModels/SearchViewModel.cs` |
| Matching and snippets | `src/Joktan.Core/TextSearch.cs` |
| Each area | `Search` on `NotesViewModel` and `HelpViewModel`, `SearchMessages` and `SearchAlerts` on `InboxViewModel` |
| Settings page | `src/Joktan.Settings/Views/SearchSettingsView.axaml`, `src/Joktan.Settings/ViewModels/SearchSettingsViewModel.cs`, `src/Joktan.Settings/SearchSettings.cs` |
