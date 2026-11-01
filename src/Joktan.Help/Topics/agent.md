# Agent

The agent panel lets you ask an AI agent to do things in Joktan: make notes, publish MQTT messages, read a
topic, make certificates, send API requests, run containers, change a theme and more. It works on the same data as the
pages, and the pages update as it works.

The agent is a command-line program you already have, such as Claude Code. Joktan runs it under your own account
with that program, and what you and the agent say goes to its AI provider. Joktan connects it to the app through a
built-in [MCP](https://modelcontextprotocol.io) server.

## Opening the panel

Press the sparkle icon in the top bar. The panel opens on the right. Drag its left edge to make it wider or narrower.

To give it a window of its own, press the window button in the panel's header. Press the same place again to dock it.
The conversation carries on either way, and Joktan remembers where you left the panel. Closing the window closes the
panel, and the icon brings it back.

## Talking to the agent

Speech is off to begin with. Switch on **Voice input** on the Settings / Agent page, and a microphone appears beside the message box.
Hold it and talk, then let go. Your words are sent to the agent straight away, so there is nothing to press. If the agent is still working, they
wait in the box and go when it has finished. The first time, your system asks whether the app may use the microphone. There is nothing to install.

What you say is turned into text on this computer, by a speech model that ships with Joktan. No sound leaves your computer, and the
app downloads nothing. Your words only leave once you send them, when they go to the client's AI provider like anything you type.

Anything you say is a request like any other, such as "make a note of today's alerts". Two things do something else:

| Say | What happens |
|---|---|
| Only "stop" | Stops the agent and anything being read |
| Only "clear chat" | Clears the chat |

As messages go without being checked, a misheard word can reach the agent, which may act on it if it is an ordinary edit. Approval questions are always answered by clicking, so a misheard word cannot allow anything risky. A recording stops by itself after two
minutes. It understands English.

## What it asks you first

| What the agent does | What happens |
|---|---|
| Looks at something | It goes ahead |
| An ordinary edit, such as changing a note | It goes ahead |
| Deletes something, restores a backup or moves a data folder | You are asked |
| Sends something out: MQTT publish, API request, connecting to a broker | You are asked |
| Starts, stops or removes containers | You are asked |
| Reads a stored password | You are asked, every time |

**Allow for this conversation** stops asking about that tool until you clear the chat. A question nobody
answers within five minutes counts as declined.

## Passwords

The agent can list and edit entries while the vault is open, and it never sees a password unless you allow that one
reading. It can generate a new password without seeing it. It cannot unlock a vault or change where passwords are kept, so
a locked vault stays locked until you open it on the Passwords page.

A password you allow goes to the client's AI provider, and may stay in the client's own session files.

Passwords saved with MQTT clients and API requests are never given to the agent. Values in API environments are
readable, so keep secrets you don't want it to read out of them.

## Choosing the client

On the Settings / Agent page, choose **Claude Code**, **OpenCode**, **GitHub Copilot CLI** or a
**Custom command**. Install and sign in to the program first, in a terminal.

- **Program** is found in the usual folders. Give its path if not. **Check** says what was found.
- **Model** is passed to the client. Empty leaves the choice to the client.
- **More arguments** are added to its command line each turn.
- A **Custom command** takes your own arguments, with `{prompt}`, `{mcp_config}`, `{mcp_url}`, `{mcp_token}` and `{model}`
  filled in. What the program prints is the reply.

The panel's header names the client in use. The client cannot be changed while the agent is working. Changing it between turns starts a new session, and the new client is told what was said so far.

**Also let the agent use the client's own tools** is off to begin with, so the agent can only use Joktan's tools. Switched on,
it can also use the client's shell, file and web tools as that client allows. Those run outside Joktan, which cannot ask
you about them or list them in the panel. The agent works in an `agent-workspace` folder in the app settings folder.

## Clients outside the app

To use Joktan's tools from an IDE or a CLI you started yourself, switch on **Let outside clients connect** on the Settings /
Agent page and press **Copy configuration**. Paste it where that client keeps its MCP servers. It works while Joktan is
open, and what it asks for waits for your approval in the agent panel, which opens by itself.

The configuration holds a token, kept in the system's credential store, so treat it like a password. **Replace token**
shuts out everyone holding the old one. On a computer without a credential store, the token lasts until Joktan closes.

The server listens on this computer only and turns away anything without a token.

## Good to know

- Text the agent reads, such as a note, an MQTT message or an API response, could try to talk it into something. The
  questions above are what protects you, so read them before allowing.
- The agent cannot change this page's settings, so it cannot widen its own access.
- Some things stay with you: choosing files in a dialog, avatars, the look of a page such as collapsed sections, and Help
  window navigation. Unlocking the vault and setting it up are also yours alone.
- On Windows, only a real `.exe` is run. A client installed as a `.cmd` needs its `.exe` named under **Program**.

See also [Settings and data](help:settings-and-data), [Passwords](help:passwords) and [Search](help:search).
