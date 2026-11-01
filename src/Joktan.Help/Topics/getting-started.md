# Getting started

Joktan is a desktop workbench: notes and passwords, a view of this machine, and tools for
developers working with MQTT, REST, gRPC and containers.

## Finding your way around

- **The menu on the left** opens each page. Click a group to open or close it; several can be open
  at once. The button at the top left of the window hides the menu when you need the room.
- **The bar along the top** moves the window when dragged, and maximises it on a double-click.
- **Help** is the question mark beside the search box, a button in settings dialogs, or F1 in any
  window. It opens the topic for the module you are in, and stays open beside it while you work.

## What each page is for

- [Dashboard](help:dashboard): how busy this machine is.
- [MQTT Dashboards](help:topic-dashboards): your own graphs of the numbers on one or more MQTT topics.
- [Notes](help:notes): pages of Markdown that nest below one another.
- [Passwords](help:passwords): logins in folders, in an encrypted vault or the system keychain.
- [MQTT](help:mqtt): send test messages to brokers, by hand or on a schedule.
- [Topic Explorer](help:topic-explorer): watch what is on a broker.
- [Certificates](help:certificates): make the certificates that brokers and clients use for TLS.
- [API Client](help:api-client): send REST and gRPC requests and read the responses.
- [Containers](help:containers): run compose files with Docker or Podman.
- [Settings](help:settings-and-data): choose which features the menu shows and in what order, and
  the [theme](help:themes) with its colours, fonts and text sizes.
- [Agent](help:agent): the sparkle icon in the top bar opens a panel where an AI agent can do these things for you.
- [Search](help:search): the box in the top bar finds things in notes, help, messages and alerts.

Each page is set up on the page itself, so its help topic covers both using it and configuring it.
What applies to the whole app is under [Settings and data](help:settings-and-data).

## Try it: send a message and watch it arrive

You need Docker or Podman for the broker, and a copy of the Joktan repository for its compose file
and sample messages.

1. Open **Dev Tools / Containers** and press **Start** on the `joktan` project. Wait for the
   `mosquitto` row to turn green.
2. Open **Dev Tools / Brokers / MQTT**. The `main` client already points at `localhost`, port
   `1883`. Press **Connect**.
3. Under **Folder messages**, press **Choose folder** and pick `samples/mqtt-messages` in the
   repository.
4. Every message starts switched off. Turn on a message, or a whole group, with its switch in the
   **On** column.
5. Open **Dev Tools / Brokers / Topic Explorer** to watch the messages arrive.

Switch the messages off again when you are done: they stay on, and start again the next time the
client connects.
