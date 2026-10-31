# Topic Explorer

Shows what is on an MQTT broker: which topics exist, what was last published on each, how often, and how
values have moved. It sends and watches through the clients on the [MQTT](help:mqtt) page, with one tab
for each.

## Watching a broker

1. Open the page, which starts it listening on every tab. Until it is first opened it receives nothing.
2. Press **Connect** on the tab. This connects the client on the MQTT page too, and **Disconnect** here
   disconnects it there.
3. Topics appear as a tree as messages arrive. Open a level to see below it.
4. Select a topic to see its value and history on the right.

Brokers, logins and certificates are set on the MQTT page, under [Clients and brokers](help:mqtt-clients).

## Choosing what to subscribe to

The filter box holds topic filters, separated by commas. `+` stands for one whole level and `#` for the
whole last level. The default `#` matches everything except the broker's own `$SYS` topics; add `$SYS/#`
to see those too.

- **Subscribe** replaces the tab's subscriptions with those in the box. Filters that were accepted are
  remembered for each client.
- **Unsubscribe** drops them. Topics already in the tree stay.
- Subscriptions are made again by themselves after a reconnect.
- Graphs on [MQTT Dashboards](help:topic-dashboards) have subscriptions of their own, so they keep
  receiving after **Unsubscribe**, and their topics do not appear here unless the filters match them.

## The tree

- Each level of a topic is a node. Beside it is what is below it, and its latest value after `=`.
- **Search topics** hides topics whose full name does not contain the text.
- **Pause** stops the tree changing. Messages that arrive while paused are discarded, not queued.
- **Clear** empties the tree and resets the totals, without unsubscribing.

## A selected topic

- **Topic**: the full topic. **Copy** copies it. **Add to dashboard**, for a topic that carries numbers,
  makes a graph of it, or adds it to a graph you already have.
- The latest message: **Received**, **QoS**, **Retained**, **Gzipped**, **Size** and **Value**. JSON is
  indented, gzipped payloads are unzipped, and anything that is not text is shown as hex.
- **History**: the last 100 messages on the topic. Select one to see it above.

## Graphs of values

**Recent values** draws a line for each number that is changing on the topic, over its last 60 values.
Items of an array share a line, named with `[*]`. Numbers that stay the same, and timestamps, get none, and
at most 12 lines are shown.

To keep a graph, with a name and a type of your choosing, press **Add to dashboard** beside the topic.
See [MQTT Dashboards](help:topic-dashboards).

**Choose values** picks what is drawn instead. It lists every number in the payload, as a single value, as
a whole array, or as one position of an array. Once you tick or clear one, only what is ticked is drawn,
including numbers that do not change. **Automatic** goes back to drawing whatever is changing. The choice
is kept for each topic of each client, between runs.

## Publishing from here

The **Publish** form sends a message through the tab's client. Selecting a topic fills it in. It takes a
**Body**, **QoS**, **Retain** and **Gzip**, as under [Manual messages](help:mqtt), and shows the result
below it.

**Clear retained value** removes the selected topic's stored value from the broker. It is enabled when the
topic's latest message was a retained one.

## In its own window

**Open in own window** moves the whole explorer into a separate window, which can go on another screen.
Nothing is lost by moving. **Back to main window**, or closing the window, returns it to the page.

## Configuring it

- Filters, and whether a tab was left unsubscribed, are remembered for each client.
- A tab's colours are set on its client: see [Appearance](help:mqtt-clients).
- Nothing that was received is kept between runs.
