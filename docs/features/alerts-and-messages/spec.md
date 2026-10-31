# Alerts and messages

People using Joktan write to each other, and anything that can publish to MQTT can raise an alert, through
one MQTT server kept apart from the clients on the MQTT page.

| Item      | Value |
|-----------|-------|
| Where     | Messages and Alerts buttons in the top bar; set up at Settings / Alerts and Messages |
| Status    | Implemented |

## Settings page

| Rule | Behaviour |
|------|-----------|
| Server card | Shows the server as the Broker card on the MQTT page does, with its state, **Connect** and **Disconnect** |
| **Edit server** | Opens the same editor as **Edit client**, under the same rules: available only while disconnected, and its name must differ from every client's |
| First use | The server is `messaging`, or `messaging 2` and so on if a client has that name, on `localhost` port 1883 |
| Connect when Joktan starts | A toggle switch, on by default. One try and up to 3 more, 5 seconds apart. If they all fail the server stays Failed until **Connect**. Once connected, a lost connection is retried as for any client |
| Topic prefix | `joktan` by default. An invalid one is reported and the one before stays in use |
| Show under Dev Tools / Brokers | A toggle switch, off by default. Off, the server is on neither the MQTT page nor the Topic Explorer. On, it has a tab on both, after the clients, and acts like a client there except that it cannot be removed. It does not count towards the last client that must stay |
| While its tab is shown | MQTT Dashboards can draw from it too, and Folders and Backup lists its folder of messages |
| Switching it off | It stays connected. Its folder messages stop being sent, and what its tabs subscribed to is unsubscribed. Graphs that draw from it wait, as for a removed client |
| Display name | Lowercase letters, digits, `_` and `-`, no spaces, at most 15 characters. An invalid one is reported and the one before stays in use. A name someone else on the server has is allowed, with a warning, and messages to it reach both |
| Picture | **Choose picture** keeps a 128 by 128 PNG copy, cut from the middle of the picture. The chosen file is left as it is. A file that cannot be read says so and changes nothing. With no picture, the first letter of the display name stands in |

## Messages panel

| Rule | Behaviour |
|------|-----------|
| To everyone | A message goes to everyone with the same prefix on the server |
| `@name text` | Goes to that one person. Case in the name is ignored. Typing `@` offers the names seen on the server |
| A draft starting with `@` | Goes to one person or nowhere. A name that is invalid or was never seen, or no text, says why and keeps the draft, so a mistyped name never reaches everyone |
| **Reply** | On others' messages. Fills in `@name`, and the reply goes to that person only, quoting the start of their message. **Cancel** drops it. Not offered on your own messages or those of someone with no name |
| Marks | A message to one person shows "to you" or "to @name" |
| No display name | Messages to everyone go as `anonymous`. Nobody can write to you, and you cannot write to one person until you choose a name |
| Unread | A dot on the button while there are unread messages. Opening the panel reads them. Your own are never unread |
| Clear | Clears the list on this computer only |

## Alerts panel

| Rule | Behaviour |
|------|-----------|
| Levels | **Info**, **Warning** and **Critical**, shown by word and by the colour of the row's dot. An unknown level counts as Info |
| Filter | Shows all levels or one |
| Bell dot | While there are unread alerts: red with a critical one among them, otherwise amber with a warning, otherwise the accent colour |
| Sending | Choose a level and write the text. It goes to everyone with the same prefix, with your name as its title |
| Retained alert | Counts once, though the server repeats it on each connection |

## On the wire

Prefix `joktan` shown. Payloads are JSON unless noted. All of it comes from whoever can publish on the server,
so it is checked: names are forced to fit the rule, text is cut to length, and only a PNG of at most 128 KB and
512 by 512 pixels is taken for a picture, judged from its header before it is decoded.

| Topic | QoS, retain | Payload |
|-------|-------------|---------|
| `joktan/messages` | 1, no | `id`, `sender` (client ID), `name`, `text`, `sent`, and `replyName`, `replyText` on a reply |
| `joktan/to/<name>` | 1, no | The same, for one person |
| `joktan/alerts/<source...>` | Any | `text`, and optionally `title` and `severity` (`info`, `warning`, `critical`), or plain text. The title falls back on the source |
| `joktan/people/<client ID>` | 1, retained | `name` and `avatar` (base64 PNG), published on connecting and after a change |

## Limits

- **Private is not secret.** A message to one person goes only to those subscribed to their topic, and Joktan
  only subscribes to the user's own. Anyone else who can read `joktan/to/#` or `#` on the server can still read
  it, including the Topic Explorer. Keeping it private takes access rules on the server, and TLS on the way.
- **Names are not proof of who someone is**, for the same reason: anyone who can publish can use any name.
- **Nothing is delivered later.** Only what arrives while connected is received.
- The latest 200 messages and 200 alerts are kept in `messaging-history.json`, which goes with the app settings
  in a backup. The name is in `mqtt.json` under `Messaging`, and the picture is `messaging-avatar.png`.
