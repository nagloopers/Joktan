# Settings and data

Settings is a group in the menu. Its Menu page arranges the menu, its [Search](help:search) page chooses
what the search box looks through, its Themes, Colours, Fonts and
Text Sizes pages set how the app looks, its [Alerts and Messages](help:alerts-and-messages) page sets
up their MQTT server and how you appear in messages, and its [Folders and Backup](help:folders-and-backup) page shows
where everything is kept, and backs it up and restores it. Everything else is set up on the page it
belongs to, and is saved as it changes. Nothing here needs setting up before you start.

## The menu

On the Settings / Menu page, each entry of the menu has a switch and **Up** and **Down** buttons. Changes
apply at once and are remembered.

- **The switch** shows or hides the entry. Hiding a group hides everything in it; its entries keep
  their own switches for when the group comes back. A group with every entry hidden leaves the menu
  as well.
- **Up** and **Down** move the entry among those beside it, so a group's entries stay in that group.
- **Settings** and its **Menu** page cannot be hidden, so you can always get back here. They can be
  moved.

Hiding a feature only takes it off the menu. What it has running keeps running, such as an MQTT
client that is connected or a vault waiting to lock, and its data is kept. Its help stays available.

Joktan opens on the first page of the menu.

## Appearance

By default Joktan follows the light or dark setting of your operating system. To choose another look,
or make your own, see [Themes](help:themes).

## The data folder

Everything Joktan keeps goes in a `Joktan` folder under your application data folder:

- Windows: `%APPDATA%\Joktan`
- macOS: `~/Library/Application Support/Joktan`
- Linux: `~/.config/Joktan`

To keep it somewhere else, start Joktan with the `JOKTAN_DATA_DIR` environment variable set to
another folder. This is useful for testing, or for running a second, separate copy.

## What is in it

| File or folder | Holds |
| --- | --- |
| `mqtt.json` | MQTT clients, Topic Explorer filters, and the alerts and messages server with your display name |
| `messaging-avatar.png` | Your picture for messages, if you chose one |
| `messaging-history.json` | The latest messages and alerts |
| `dashboards.json` | MQTT dashboards, their graphs, format and tab colours, unless another folder was chosen |
| `dashboards-location.json` | The dashboards folder that was chosen |
| `backup.json` | Where backups go, how many are kept, and what they hold |
| `menu.json` | The order of the menu, and what it shows |
| `search.json` | What the search box looks through |
| `themes.json` | The theme in use, and the themes you saved |
| `mqtt-certificates/` | Certificates copied for MQTT clients |
| `certificates.json` | The list of certificates, and their defaults |
| `certificates/` | Certificates, unless another folder was chosen |
| `notes/` | Notes, unless another folder was chosen |
| `notes-location.json` | The notes folder that was chosen |
| `passwords.vault` | The default vault file |
| `passwords.json` | Entries without their passwords, with the keychain |
| `passwords-settings.json` | Where the passwords are, and the lock time |
| `api/` | API requests, unless another folder was chosen |
| `api-location.json` | The requests folder that was chosen |
| `api-state.json` | The request open last |
| `api-last-sent.json` | What each request last sent |
| `containers.json` | Compose files, the engine, and whether the logs are collapsed |

Some things can be kept elsewhere, such as in a synced folder or in version control. See
[Notes](help:notes), [Passwords](help:passwords), [API Client](help:api-client),
[Certificates](help:certificates) and [MQTT Dashboards](help:topic-dashboards).

## Passwords and secrets

- MQTT broker passwords and key passwords are kept in the operating system's credential store: the
  macOS Keychain, Windows Credential Manager, or the Secret Service on Linux through `secret-tool`.
  They are never written to `mqtt.json`. Where there is no credential store they are not saved, and
  have to be typed in again each run.
- Private keys made on the [Certificates](help:certificates) page are not encrypted.
- API requests and environments are saved as written, tokens and passwords included.

## Telemetry

Joktan exports metrics and traces using OpenTelemetry, by default to a collector at
`http://localhost:4317`. If nothing is listening there, the data is dropped and the app runs as
normal. Topics, message bodies and the like are never included.

It is set with the standard environment variables, when starting Joktan:

- `OTEL_EXPORTER_OTLP_ENDPOINT`: where to send, such as `http://localhost:4318`.
- `OTEL_EXPORTER_OTLP_PROTOCOL`: `grpc`, or `http/protobuf`.
- `OTEL_SDK_DISABLED=true`: turns telemetry off.
