# Settings

Chooses which features the menu shows and in what order, the colours, fonts and text sizes of the app,
kept in themes, and where Joktan keeps things, with backing them up and restoring them.

| Item      | Value       |
|-----------|-------------|
| Menu path | Settings / Menu, Search, Themes, Colours, Fonts, Text Sizes, Alerts and Messages, Folders and Backup |
| Status    | Implemented |

## Purpose

Keep the menu down to what someone uses, in the order they reach for it, and let the app look the way
they want it to. Settings that belong to one feature stay on that feature's page.

## Pages

Settings is a group in the menu, with one page for each kind of setting.

| Page | Holds |
|------|-------|
| Menu | Which entries the menu shows, and their order |
| Search | What the search box looks through. See [its spec](../search/spec.md) |
| Themes | The theme in use, and removing saved themes |
| Colours | Every colour of the theme, and whether its controls are dark |
| Fonts | Every font of the theme |
| Text Sizes | Every text size of the theme |
| Alerts and Messages | The MQTT server for alerts and messages, and the name and picture on messages. See [its spec](../alerts-and-messages/spec.md) |
| Folders and Backup | Every folder Joktan keeps things in, backing them up, and restoring them. See [below](#folders-and-backup) |

## Menu page layout

1. Page header: title "Menu", subtitle "Settings / Menu".
2. A **Menu** card listing every menu entry in menu order, indented below its group. Each row has the
   entry's title, **Up** and **Down**, and a switch.

## Showing and hiding

| Rule | Behaviour |
|------|-----------|
| Switch | A toggle switch, not a checkbox. Takes effect in the menu at once |
| Group switched off | The group and everything in it leave the menu. The rows below it are disabled and keep their state |
| Every entry of a group switched off | The group leaves the menu as well, even with its own switch on |
| Settings and Settings / Menu | Their switches are on and disabled, so the page can always be reached. The other pages of Settings can be hidden |
| Hidden feature | Stays loaded. What it has running keeps running, its data is kept, and its help can still be opened |

## Ordering

**Up** and **Down** swap an entry with its neighbour among the entries that share its parent, hidden
ones included. An entry cannot move to another group. **Up** is disabled on the first entry and
**Down** on the last.

## Start-up

Joktan opens on the first leaf in the menu that is shown, with the groups above it expanded. With the
menu as it comes, this is Dashboard / Overview.

## Saved state

Each change is written straight away to `menu.json` in the [data folder](../../../src/Joktan.Help/Topics/settings-and-data.md):
the entries in order, as a tree, each with its title and whether it is shown.

| Case | Behaviour |
|------|-----------|
| No file, or one that cannot be read | The menu as it comes |
| An entry in the app but not in the file, such as a new feature | Shown, directly after the entry it follows in the app |
| An entry in the file but not in the app | Ignored, and dropped at the next save |
| A renamed entry | Entries are matched by title among their siblings, so it counts as a new one |
| Settings, or Settings / Menu, marked as hidden in the file | Shown |

## Alerts and Messages

Sets up the MQTT server behind the Messages and Alerts panels of the top bar. See
[alerts and messages](../alerts-and-messages/spec.md).

## Themes

A theme has a name and holds every colour, font and text size in the app, and whether controls take
their dark look. The tokens are listed in [`ThemeTokens`](../../../src/Joktan.Settings/Themes/ThemeTokens.cs),
and their use in the [design](../../design.md).

| Choice | Behaviour |
|--------|-----------|
| Follow system | Light or Dark according to the operating system, changing when it does. The default |
| Light, Dark | Built in: the values in `Tokens.axaml` |
| Green Dark | Built in: Dark with green surfaces, menu and accent |
| A saved theme | One the user made |

### Changing a theme

The Themes, Colours, Fonts and Text Sizes pages all have the same **Theme** card at the top, naming the
theme and whether it has changes not saved.

| Rule | Behaviour |
|------|-----------|
| A change | Shows in the whole app at once, and is held in a working copy until saved |
| **Save** | Writes the changes to the theme. Disabled for a built-in theme, which says to save as new |
| **Save as new** | Saves what is showing under a new name and changes to it. The name is required, and must differ from every other theme's in more than case |
| **Discard** | Goes back to the theme as last saved |
| Undoing a change by hand | The theme counts as unchanged again |
| Choosing another theme | Disabled until the changes are saved or discarded |
| Closing the app | Changes not saved are lost |
| **Delete** | Saved themes only, at once. If it is in use, the app goes back to Follow system |

### Values

| Kind | Control | Rule |
|------|---------|------|
| Colour | Picker and `#RRGGBB` text | Text that is not a colour outlines the box and changes nothing. There is no alpha. Meter tracks are the accent, warning and danger colours at low opacity, and Fluent's accent steps are worked out from the accent |
| Font | List of the fonts on this computer | Interface defaults to Inter, which comes with Joktan; Headings, Brand and Menu to the Interface font; Code to the system's monospaced font. A font this computer does not have is listed as not installed, kept, and drawn in Inter |
| Text size | Number from 8 to 72 | An empty box changes nothing |

Icons, spacing and the height of rows do not follow the text sizes, so very large text can be clipped.

### Saved state

The theme chosen and the saved themes are in `themes.json` in the data folder. A theme is read at
start-up before the splash screen opens.

| Case | Behaviour |
|------|-----------|
| No file, or one that cannot be read | Follow system, and no saved themes |
| The theme chosen no longer exists | Follow system |
| A value missing or unusable, such as after an update adds a token | Taken from Light or Dark, whichever matches the theme's controls. Sizes outside the range are brought within it |
| A saved theme named like a built-in one or an earlier saved one | Ignored, and dropped at the next save |

## Folders and Backup

One page for every folder Joktan keeps things in, which of them go in a backup, and backing up and restoring.
From top to bottom: **Back up**, **Folders** and **Restore** cards.

### Folders

| Row | Default | Changed here | In a backup by default | Holds |
|-----|---------|--------------|------------------------|-------|
| App settings | The data folder | Yes, and Joktan restarts. Not while `JOKTAN_DATA_DIR` sets it | Yes | MQTT clients, the menu, themes and other settings |
| Notes | `notes/` | Yes | Yes | The folder |
| MQTT Dashboards | The data folder | Yes | Yes | `dashboards.json` only |
| API requests | `api/` | Yes | Yes | The folder |
| Certificates | `certificates/` | No: each new certificate can choose its own folder | No, warns | The folder, `certificates.json`, and the folder of any certificate kept elsewhere |
| MQTT client certificates | `mqtt-certificates/` | Yes | No, warns | The folder |
| Passwords | `passwords.vault` | No: **Open Passwords**, as moving a vault needs it unlocked | Yes | The vault file, or with the keychain the list of entries only |
| MQTT messages | None | Yes, or empty for none | Yes | One row per client, listed together. See below |

- Changing a folder follows the same rule as the feature's own page, which is still there: a folder that already holds
  data is opened as it is. Otherwise what is open now is copied there, and the originals stay. Notes, MQTT Dashboards
  and API requests use the page's own code. MQTT client certificates and MQTT messages use this rule here.
- A path can be typed with `~` for the home folder, or picked with **Browse**. **Default** fills in the default, and
  **Use folder** applies it. Anything else is refused with the reason.
- Moved certificates already in use stay where they were copied to, so clients go on working.
- The rows are read again each time the page is opened, as folders change on other pages. A path being typed is left alone.
- **MQTT messages** has one row for each client on the MQTT page, however many there are. Rows appear, go and change name
  as clients are added, removed and renamed, at once, and a renamed client keeps its switch. A client with no folder has
  its switch off and disabled, and says there is nothing to back up. The heading is hidden if there are no rows.
- Each row has a toggle switch, not a checkbox, saved at once to `backup.json`. A client with no folder of messages adds nothing to a backup.

### Changing the App settings folder

**Use folder and restart** writes what features hold in memory, then acts according to the folder chosen.

| Folder chosen | Behaviour |
|---------------|-----------|
| Empty, or with only files starting with a dot | Everything in the data folder is copied there, including folders such as `notes/` that features keep inside it. The originals stay |
| Already holds Joktan's settings | Opened as it is. Nothing is copied or replaced |
| Holds other files | Refused with the reason, so settings are never mixed in with something else and do not look lost |
| Inside the folder in use | Refused with the reason |
| Cannot be written, or a copy that fails | Refused with the reason. What was copied is removed, and the folder in use stays |

- The choice is kept in `data-location.json` in the default folder, as it has to be found first, and is left out of the copy and of backups.
- In the copy, whole paths into the old folder in the `.json` files at its top level are changed to the new one, such as the certificates an MQTT client uses, so the old folder can be deleted. Folders chosen elsewhere are left alone.
- The folder is read once for each run, and the run that made the change goes on saving to the old one until it closes.
- **Default** and then the button goes back to the default folder, under the same rules, so settings left there are opened as they were.
- `JOKTAN_DATA_DIR` wins over the choice, which stays for when it is not set. The row then shows the folder and cannot change it.
- Passwords in the system keychain, and MQTT passwords, do not move, and are still found.

| At start-up | Behaviour |
|-------------|-----------|
| The chosen folder cannot be found, such as on a drive not connected | The default folder is used for that run, Joktan opens on this page and the row says so. The choice stays, so the next start goes back to it once it is there |

### Back up

| Setting | Default | Rules |
|---------|---------|-------|
| Backup folder | `Joktan Backups` in the user's documents | Created when first used. It is left out of its own backups, wherever it is |
| Backups kept | 10 | 1, 3, 5, 10, 20, 30, 50 or 100 |

**Back up now** writes what features hold in memory, then makes `joktan-backup-<date>-<time>.zip`.

- The zip is standard, so any zip tool can open it. It holds a `joktan-backup.json` listing what is in it.
- It is written under a temporary name and read back through to its end, so a backup that exists is whole.
- A file that cannot be read, or a folder that is missing, does not stop the backup. It is listed under the result.
- After a backup, the oldest zips past the number kept are deleted, counting `joktan-backup-*` and `joktan-safety-*`
  separately. Other files in the folder are never touched.
- Files and folders whose names start with a dot, such as `.git` and `.DS_Store`, are left out, and are left alone by a restore.
- **Zips are not encrypted**, and the page says so. Certificates hold private keys, so their switches start off, and they warn when turned on.
- Passwords in the system keychain, and MQTT passwords, cannot be read into a file, so are not in a backup.
- There is no schedule.

### Left out of app settings

These say where things are on this computer: `data-location.json`, `notes-location.json`, `api-location.json`, `dashboards-location.json`,
`passwords-settings.json` and `backup.json`. A restore puts things back where they are kept now, so they must stay. Also
left out are files backed up on their own rows. Folders named inside `mqtt.json` do go with it, so a restored client on
another computer may point at a folder that is not there, and its page says so.

### Restore

Choose a backup from the backup folder, or **Browse** for one anywhere. The page lists what it holds, how many files, and
where each will go, each with a switch, on to begin with. Something with no files stays off.

**Restore** asks first and names what will be replaced. **Restore and restart** then:

1. Writes what features hold in memory.
2. Reads the backup through to its end, so a damaged one changes nothing.
3. Zips what is there now of what is switched on, into `joktan-safety-<date>-<time>.zip` in the backup folder, and reads that
   back too. Restoring it undoes the restore. If it cannot be made, nothing changes and the page says why.
4. Leaves the restore waiting in `restore-pending.json`, and closes and restarts the app.
5. When the app starts, before anything is read, it waits for the old run to be gone and puts the files back.

| Case | Behaviour |
|------|-----------|
| Where it goes | Where that row is kept now, so it works from another folder or computer. Something no longer known goes back to its old path |
| Files made since the backup | Removed, except what is left out above and files starting with a dot |
| The page after a restart | Says what was restored and where the safety zip is |
| A restore that fails part way | Stops, says why and where the safety zip is, and does not try again at the next start |
| Joktan cannot restart itself | The restore happens the next time it is started |
| A file that is not a backup, or from a newer Joktan | Refused with the reason |
| A zip naming a path outside its folder | Refused, and nothing is written |

The app closes before the files go back because closing saves what it has open, which would otherwise overwrite them.

### Implementation

| Part | File |
|------|------|
| Page | `src/Joktan.Settings/Views/BackupView.axaml`, `src/Joktan.Settings/ViewModels/BackupViewModel.cs` |
| Zipping and restoring | `src/Joktan.Settings/Backup/BackupArchive.cs` |
| The restore at start-up | `src/Joktan.Settings/Backup/PendingRestore.cs`, `src/Joktan/Program.cs`, `src/Joktan/App.axaml.cs` |
| What each feature keeps | `src/Joktan/ViewModels/FolderSources.cs` |
