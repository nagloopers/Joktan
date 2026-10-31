# Help

How to use and configure each module, built into the app, and opened for whatever is on screen.

| Item      | Value                                                  |
|-----------|--------------------------------------------------------|
| Opened by | The help icon beside the search box, a **Help** button, F1, or the macOS app menu |
| Status    | Implemented                                            |

## Purpose

Let someone find out how a page works, and how to set it up, without leaving the app or reading
the specifications. The specifications say what a feature must do; help says how to use it.

## Opening help

| From                              | Opens                                                       |
|-----------------------------------|-------------------------------------------------------------|
| The help icon beside the search box | The topic for the module being shown, which its tooltip names |
| F1 in the main window             | The same                                                    |
| **Help** in the header of a settings dialog or in the Topic Explorer's own window, or F1 there | The topic that window names |
| **Joktan Help** in the macOS app menu | The topic for the page being shown                      |

Placeholder pages have no topic of their own, and open **Getting started**. F1 in the help window
does nothing.

| Window                  | Topic                 |
|-------------------------|-----------------------|
| MQTT client settings    | Clients and brokers   |
| Message settings        | Message settings      |
| Graph editor            | MQTT Dashboards      |
| New certificate, Export | Certificates          |
| Topic Explorer's own window | Topic Explorer    |

## Help window

Help is a [tool window](../../design.md#tool-windows), not a page, so a page or dialog can be read
and set up side by side with its help. There is one: opening help again shows the topic in it and
brings it forward. It has no owner, so it can go on another screen, and it closes when the main
window closes.

1. A bar across the top, titled "Help", with **Back** and the window controls.
2. On the left, a search box and the topics, in the order of the main menu, with a module's further
   topics indented below it.
3. On the right, the topic, drawn by `MarkdownView`. Every topic is shown from its top.

**Back** returns to the topic read before, however it was reached, as far back as the window has
been since the app started.

### Search

Search finds topics in which every word typed appears, ignoring case, in the title or the text as
it is read, so link addresses and Markdown marks do not match. Topics with all the words in their
title come first. Each result shows the line with the most of the words, and the topic it is below.
The topic being read stays while searching; clearing the box brings back the full list, and opening
a topic by F1, a link or **Back** clears it.

### Links

A link written `[Certificates](help:certificates)` opens that topic. Any other link opens in the
browser, as it does in Notes. A link cannot go to a heading within a topic, which is why a long
subject, such as MQTT, is several topics.

## Topics

| Topic                                | File, in `src/Joktan.Help/Topics/`                      |
|--------------------------------------|---------------------------------------------------------|
| Getting started                      | `getting-started.md`                                    |
| Settings and data                    | `settings-and-data.md`                                  |
| Dashboard                            | `dashboard.md`                                          |
| MQTT Dashboards                     | `topic-dashboards.md`                                   |
| Notes                                | `notes.md`                                              |
| Passwords                            | `passwords.md`                                          |
| MQTT, and below it Clients and brokers, TLS and mutual TLS, Folder messages, Message settings, Varying fields | `mqtt.md`, `mqtt-clients.md`, `mqtt-tls.md`, `mqtt-folder-messages.md`, `mqtt-message-settings.md`, `mqtt-varying-fields.md` |
| Topic Explorer                       | `topic-explorer.md`                                     |
| Certificates                         | `certificates.md`                                       |
| API Client, and below it gRPC requests, Environments and variables | `api-client.md`, `api-client-grpc.md`, `api-client-variables.md` |
| Containers                           | `containers.md`                                         |

## Writing topics

Topics are Markdown, built into the assembly, so help always matches the version it came with and
works offline.

- **Change the topic when you change the module.** Help is not generated from the specifications,
  and nothing checks that it says what the app does.
- Call things what the screen calls them, and put buttons and fields in bold.
- Start with what the module is for, then how to use it, then how to configure it, then what to do
  when something goes wrong.
- Say what is not built yet only where someone would go looking for it.
- A topic starts with `# ` and its title, as listed in `HelpTopic.All`.
- Keep table cells short: a table scrolls sideways rather than wrapping. Prefer a list for anything
  longer than a few words.
- To add a topic, add its file and a line in `HelpTopic.All`. To point a window at a topic, set
  `core:HelpContext.Topic` on the window and add a `HelpButton` to it, or `HelpTopic` on its menu entry in `MainViewModel`.

## Implementation

| Part             | File                                                                                  |
| ---------------- | ------------------------------------------------------------------------------------- |
| Window           | `src/Joktan.Help/Views/HelpWindow.axaml`, `src/Joktan.Help/ViewModels/HelpViewModel.cs` |
| Topic list, search | `src/Joktan.Help/HelpTopic.cs`, `src/Joktan.Help/ViewModels/HelpTopicViewModel.cs`  |
| F1 and the app menu | `src/Joktan/App.axaml.cs`                                                          |
| A window's topic | `src/Joktan.Core/HelpContext.cs`                                                      |
| The Help button  | `src/Joktan.Core/Controls/HelpButton.cs`                                              |
| Markdown         | `src/Joktan.Core/Controls/MarkdownView.cs`                                            |

## Not included

- Screenshots, as `MarkdownView` does not load images
- Links to a heading within a topic, and highlighting what was searched for
- A Help entry on the main menu; the top bar is always on screen, and the menu holds pages
- A shortcut for macOS laptops, where F1 needs Fn, beyond the help icon and the app menu
