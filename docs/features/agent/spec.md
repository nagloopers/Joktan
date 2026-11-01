# Agent panel and MCP server

An AI agent works in the app through the app's own MCP server, and the user talks to it in a panel.

## Panel

- A sparkle button in the top bar shows and hides the panel.
- The header names the client in use. The client is chosen on Settings / Agent only, and not while a turn is running.
- Docked, it sits at the right of the page, below the top bar, in a column with a splitter (300 to 900 pixels wide).
- It can move to a window of its own and back. Both show the same `AgentViewModel`, so nothing is lost. The window has no owner, and it closes with the app.
- Closing the window closes the panel. Open or closed, docked or not, and the sizes are remembered in `agent.json`.
- The conversation lists messages in Markdown, tool calls with their state, approval questions and problems. It is kept in memory only, since it can hold a revealed password.
- While a turn runs, a moving bar and "Working…" show above the message box, and Send becomes Stop.
- Enter sends and Shift+Enter makes a new line. Stop ends the turn by killing the client's process tree. A bin button clears the chat: it stops any running turn, shows nothing more from it, and forgets the session and what was allowed.

## Prompts

- A lightning button beside the message box lists the prompts by name. It is yellow to begin with, and is **Prompts button** on Settings / Colours, under Agent. Picking one adds its text to the message box and puts the cursor there. It is not sent, so it can be finished off first, and it can be picked while a turn is running.
- Settings / Agent has a **Prompts** card: each prompt with **Remove**, which takes effect at once, and **Add prompt** with a name and the text. Both are needed, and a name must not already be in use, ignoring case.
- Six prompts come with the app, and can be removed like any other. The list is kept in `agent.json` whole, so one that was removed stays removed. A file from before there were prompts starts with the six.
- No tool changes prompts, as with the rest of Settings / Agent.

## Speech

- Replies are read aloud when switched on, in the panel's header or on Settings / Agent, with a voice and a speed from 50% to 200%. It is off to begin with.
- There are no new dependencies. Reading goes through `say`, PowerShell's System.Speech, or `espeak-ng`, `espeak` or `spd-say`, with the text on standard input.
- A reply is read when it is whole: at the next tool call, approval or problem, or the end of the turn. Markdown is removed and fenced code is left out. Approvals and problems are announced, not read out.
- Reading is in order, one at a time. Sending, Stop, clearing the chat and switching off all stop it.
- Voice input is built in, with nothing to install, on macOS and Windows. A microphone button, shown while the Voice input switch is on (off to begin with, like reading aloud), is held to record, for at most two minutes. OpenAL Soft (LGPL, its native library shipped unchanged) records, and Whisper.net (MIT) transcribes in English on this computer with `ggml-base.en.bin`, in `models/` beside the app.
- Whisper's native libraries cannot load from inside a single file, so the macOS and Linux packages are folders. The Windows package is one `Joktan.exe` with `models/` and `runtimes/<rid>/` beside it, the latter holding only the Whisper and OpenAL libraries and the Visual C++ runtime that they need, as a clean Windows may not have it.
- The model is fetched and its hash checked when the app is built, into `.models/` in the repository, which git ignores. A publish without it fails. The app has no download code, and no sound leaves the computer.
- Letting go sends what was said, added to anything in the message box, without a further press, or at the end of a running turn. A trailing "send" is dropped. All that was said being "stop" or "clear chat" does that instead. Approvals cannot be given by voice. Typed words are never read as commands. Reading aloud stops while recording.


## Clients

A client is a command-line agent, run once per turn. Every client runs in `agent-workspace` in the data folder, with its standard input closed, so that it never waits for a person, and with a PATH that includes the usual install folders, since an app started from the desktop gets a short one.

| Client | Command | Session | MCP configuration |
|---|---|---|---|
| Claude Code | `claude -p --output-format stream-json`, prompt on standard input | `--resume`, from `session_id` | `--mcp-config` file, `--strict-mcp-config` |
| OpenCode | `opencode run --standalone --format json -- <prompt>` | `--session`, from `sessionID` | file named by `OPENCODE_CONFIG` |
| GitHub Copilot CLI | `copilot --prompt= --output-format=json --no-ask-user` | `--resume=`, from `sessionId` | `--additional-mcp-config=@file` |
| Custom | The arguments given, with placeholders filled in after splitting | None | `{mcp_config}` |

- OpenCode's private server is asked for because the background one would not see the configuration.
- Configuration files hold the token, so they go in a new private temporary folder, are readable by the user only, and are removed after the turn.
- A client that gave no session is told the transcript instead. The same goes after changing client mid-conversation.
- The client's own tools are off unless switched on. Claude Code gets `--tools ""`, OpenCode denies all but `joktan_*`, and Copilot is only granted `joktan`.
- Copilot's flags come from GitHub's documentation. Its JSON event names are undocumented, and were checked against version 1.0.105 of the real client. Any line that is not JSON is shown as text.
- On Windows only an `.exe` is run, because a `.cmd` goes through cmd.exe, whose quoting a prompt could break out of.

## MCP server

- Streamable HTTP without streams: each POST to `/mcp` is one JSON-RPC exchange with one JSON reply, and GET gets 405. It answers `initialize`, `ping`, `tools/list` and `tools/call`.
- It starts with the first turn, on a free port, or with the app on the configured port when outside clients are let in.
- It listens on `localhost` only, needs a bearer token (401 otherwise) and turns away a foreign `Origin` (403). The listener turns away any other host name, which stops DNS rebinding.
- The panel's token is new each run and never written down. The outside token is off by default, and while on is kept in the credential store under "Joktan Agent", or in memory where there is none.
- Tools run on the UI thread and drive the same view-model commands as the pages, so their checks apply and the pages update. Dialogs are filled in and saved through the same hook a view would use.

## Tools

One `Tools/*Tools.cs` per feature. Every tool has a risk:

| Risk | Examples | Approval |
|---|---|---|
| Read | `notes_read`, `mqtt_clients` | None |
| Change | `notes_update`, `themes_set` | None |
| Risky | `notes_delete`, `mqtt_publish`, `mqtt_connect`, `api_send`, `containers_run`, `backup_restore`, `folders_change`, `certificates_export` | Ask; can be allowed per tool for the conversation |
| Secret | `passwords_reveal` | Ask every time |

- Approval is checked in the server, before a tool runs, so no client can skip it. Outside clients' questions appear in the panel, which opens if need be.
- An unanswered question is declined after five minutes. Stopping or ending a turn withdraws questions.
- No tool unlocks or sets up a vault, changes password storage, or returns an MQTT or API password. Settings / Agent has no tools, so an agent cannot widen its own access.
- A page in the middle of being edited is not overwritten: password tools refuse while the user is editing there. The manual MQTT form is put back after a publish.

## Not covered

File dialogs, avatars, Help window navigation and purely visual state such as collapsed sections have no tools.

## Testing

By request, this assembly has no test project, and it is left out of the coverage report with `-assemblyfilters:-Joktan.Agent`.
