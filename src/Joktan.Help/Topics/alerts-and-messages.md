# Alerts and messages

The **Messages** and **Alerts** buttons in the top bar go through one MQTT server, set up under Settings /
Alerts and Messages. A dot on a button means something is unread.

## Setting it up

1. Press **Edit server**. It has the same settings as a client on the MQTT page: name, host, port, login, TLS
   and colours. See [Clients and brokers](help:mqtt-clients) and [TLS and mutual TLS](help:mqtt-tls).
2. Choose a **Display name**: lowercase letters, digits, `_` and `-`, no spaces, at most 15 characters. Others
   write to you with `@` and this name.
3. **Choose picture** adds a small picture to your messages, if you want one.

Joktan connects by itself when it starts, and tries 3 more times if it cannot. Switch off **Connect when
Joktan starts** to connect only with **Connect**.

Everyone on the same server with the same **Topic prefix** shares messages and alerts.

## Messages

- A message goes to everyone.
- `@bob the door code changed` goes to bob only. Typing `@` offers the names seen on the server.
- **Reply** answers that person only, and quotes their message. **Cancel** stops replying.

Anything starting with `@` goes to one person or nowhere: with a name that nobody has, it says so and is not
sent.

## Alerts

Alerts have three levels, **Info**, **Warning** and **Critical**, and the panel can show just one. The dot on
the bell is red if a critical alert is unread, amber for a warning, and otherwise the accent colour.

Send one from the panel, or publish one from anything that speaks MQTT, to `joktan/alerts/` and a name of
your choice:

```json
{ "title": "Disk full", "text": "Line 1 has 2% left", "severity": "critical" }
```

Plain text works too, as an Info alert.

## Good to know

- **Private is not secret.** Joktan only listens for messages to you, but anyone who can read everything on the
  server can read them too. Use a server with access rules and TLS for anything sensitive.
- **A name is not proof of who someone is.** Anyone who can publish on the server can use any name.
- **Nothing is delivered later.** Only what arrives while you are connected is received.
- [Pomodoro](help:pomodoro) can add alerts of its own when a phase ends. They stay on this computer.
- The latest 200 messages and 200 alerts are kept. **Clear** empties a list on this computer only.
- The [search box](help:search) in the top bar finds what is kept.

## Showing the server under Brokers

The server is kept apart from your clients, so it is not on the [MQTT](help:mqtt) or
[Topic Explorer](help:topic-explorer) pages. Switch on **Show under Dev Tools / Brokers** to give it a tab on
both. There it works like any client, except that it cannot be removed. While its tab is there,
[MQTT Dashboards](help:topic-dashboards) can draw from it too.

Switching it off again leaves it connected. Folder messages it was sending stop, and it unsubscribes from what
those tabs were watching. Any graphs drawn from it wait until it comes back.
