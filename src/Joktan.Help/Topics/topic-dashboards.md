# MQTT Dashboards

**Dashboard / MQTT Dashboards** draws the numbers on MQTT topics as graphs, on dashboards you name and
lay out yourself. Each dashboard has its own tab. Graphs use the clients on the [MQTT](help:mqtt) page.

## Adding a graph

From the Topic Explorer, when you are already watching the topic:

1. Select the topic in the [Topic Explorer](help:topic-explorer) and press **Add to dashboard**.
2. The graph editor opens, with the numbers being drawn there already ticked and the messages received
   so far already on the preview.
3. To draw it on a graph you already have, choose that graph under **Add this topic to**.

From the dashboard, when you know the topic:

1. Press **Add graph**.
2. Choose the **Client**, type the **Topic**, and press **Subscribe** or Enter. If the client is not
   connected, press **Connect** beside the message.
3. The numbers in the topic's messages are listed as they arrive.
4. To draw another topic on the same graph, type it and press **Subscribe** again.

Then, in the editor, whichever way you came:

1. Give the graph a **Name**.
2. Choose its **Type**, and watch the preview change.
3. Tick up to 8 **Numbers to draw**, from any of the topics. Each can have its own name and colour.
4. Choose the **Dashboard**, or tick **Put it on a new dashboard**.
5. Press **Add to dashboard**.

If nothing is being sent to a topic yet, choose it beside the path box and add a number by its path, such as
`sensor.value` or `readings[sensor=temperature].value`. Leave the path empty for a payload that is a single number.

## Telling numbers apart with keys

A key says which thing a number was measured on, such as a line, a machine, an axis or a sensor. Keys keep each
number on its own line, even if the order in the message changes, and new devices appear by themselves.

**From the topic.** Press a level of a topic in the editor to make it a key. `plant/line1/vibration` becomes
`plant/{line}/vibration`, and the graph takes every line. You can also type it, or use `+` and `#`.

**From the message.** If a topic carries several things, such as axes `x`, `y` and `z`, the editor says so. Press
**Show one line for each**, or choose the field under **Add a key from the message**.

**From an array.** The editor finds the field that tells the items apart. For this message:

```
{ "readings": [ { "sensor": "temperature", "value": 20.8 },
                { "sensor": "humidity", "value": 40 } ] }
```

it offers `readings[sensor=temperature].value` for one, and `readings[sensor=*].value` for each. Avoid
`readings[0].value`, which draws the wrong thing if the order changes. A graph that uses one says so, and **Use it**
fixes it.

**Choosing a reading by the topic.** On `building/hvac/+/climate`, the path `readings[sensor={2}].value` reads the
item named by level 2 of the topic, counting from 0. With `building/hvac/{sensor}/climate`, write `{sensor}` instead.
The editor offers this when a level matches something in the message.

Under **Keys**, choose how to show each value:

- **One line**: a line for each value on this graph.
- **One graph**: a graph for each value on this dashboard.
- **One tab**: a tab for each value. Changing any of these tabs changes them all.
- **Only**: just one value, chosen from those seen.

These can be mixed, such as a tab per line, a graph per machine and a line per axis. The editor says what your
choices make. A graph or tab that is no longer needed can be removed with **Forget**, under **Edit layout**, or
**Forget this value**, by right-clicking the tab.

To remove a line, press **Edit layout**, then **Edit** on the graph. Under **Lines on this graph**, press the **×**
beside it, then **Save**. It stays off that graph until you press **Bring back**. A number with no keys, or an old
path such as `readings[1].value`, is removed with the **×** beside it under **Numbers to draw**.

A graph shows up to 8 lines, the most recently updated. A saved graph makes up to 24 graphs on a tab and 12 tabs, and
the app keeps up to 500 series. The page says when something is left out.

Gzipped messages work the same as plain ones, with nothing to set.

## Several topics on one graph

A graph can draw numbers from as many topics as you like, up to 8 numbers in all, to compare them over
the same time and on the same scale. All of a graph's topics use the same client.

- The first number to arrive on each topic is ticked for you. Clear it and tick another if you prefer.
- A number you have not named is called by as much of its topic as tells it from the others, such as
  `line1/temp value`. Use the name box beside it for something shorter.
- To add a topic later, press **Edit layout**, then **Edit**, or use **Add to dashboard** in the Topic
  Explorer and choose the graph. **Remove** beside a topic takes it and its numbers off.
- Every topic on a graph needs a number ticked before it can be saved, so that none goes unnoticed. A topic
  with nothing arriving yet can have a number added by its path.
- A topic that has sent nothing yet shows "–" beside its name, while the others draw as usual.

## Types of graph

- **Line**: how values moved over time.
- **Area**: a line filled down to zero, for amounts.
- **Bars**: one number shows its recent readings. Several show the latest of each, side by side.
- **Gauge**: the latest value on a dial. Set a fixed scale, so the dial means something.
- **Number**: the latest value in large type, with its trend below when there is room.

All the numbers on one graph share one scale. Numbers of very different sizes are easier to read on
graphs of their own.

Point at a line, an area or a bar to read its values.

## Other settings for a graph

- **Shows the last**: how far back it goes, from 1 minute to 24 hours.
- **Unit**: shown after each value, such as `°C`.
- **Decimal places**: **Automatic** shows up to three, as needed.
- **Fixed scale**: keeps the scale between a **Minimum** and a **Maximum**, rather than fitting the values.
- **Columns wide** and **Rows high**: its size on the dashboard.

## Laying out a dashboard

Press **Edit layout**. Each graph then shows a bar across its top and a handle in its bottom corner.

- **Move** a graph by dragging its bar onto another graph, or with **‹** and **›**.
- **Resize** it by dragging the corner handle. It snaps to whole columns and rows. It can be as wide as the columns that fit, so if the
  window or a docked agent panel leaves room for only one column, it can get shorter but not narrower.
- **Edit** opens the graph editor again, and **Remove** takes the graph off, after asking. Graphs made from one
  saved graph are moved, resized, edited and removed together.
- **Details** says what has arrived for the graph, and why any of it was not used.

Press **Edit layout** again when you are done. Graphs fill the grid in order, each in the first place it
fits.

## Adding, renaming, colouring and removing dashboards

**Add**, at the top of the page, adds another dashboard, on a tab of its own.

Press **Edit**, beside it, to put three buttons on every tab. The button then says **View**: press it when you are done, to go back to just the names.

- **Rename**: press the pencil, type the name and press Enter. Esc keeps the old name, and so does leaving
  the box empty.
- **Tab colour**: press the palette and pick a colour. **Default** goes back to the theme's. The name on the
  tab turns white or dark, whichever reads better.
- **Remove**: press the **×**, then **Remove** at the top of the dashboard, or **Keep** if you change your
  mind. This takes its graphs too, and cannot be undone. There is always at least one dashboard, so removing
  the only one leaves an empty one called Main in its place.

You can also rename a dashboard at any time by double-clicking its tab. Right-click a tab for **Rename** and
**Remove dashboard**.

## Formatting a dashboard

Press **Format** to set these for the dashboard on the tab:

- **Dashboard name**, shown on the tab.
- **Columns**: from 1 to 6. A narrow window shows fewer, so graphs stay readable.
- **Row height** and **Space between graphs**.
- **Background**: a colour behind the graphs. **Default** goes back to the theme's.

Everything is saved as it changes.

## Choosing where dashboards are saved

Dashboards are saved as `dashboards.json` in the [data folder](help:settings-and-data) until you choose another
folder. A shared or synced folder works too.

1. Press **Folder**, at the top of the page.
2. Type a full path, or use **Browse**. `~` stands for your home folder. **Default** fills in Joktan's own folder.
3. Press **Use folder**.

What happens then depends on the folder:

- If it already holds a `dashboards.json`, those dashboards are opened as they are, and their graphs start empty.
- If not, the dashboards open now are copied into it, and their graphs keep what they have drawn. The originals
  stay where they were.

If the folder cannot be read when Joktan starts, or has no dashboards in it, such as on a drive that is not plugged in
or a synced folder that has not caught up, the page says so and saves nothing, so your dashboards are not replaced by
an empty one. Bring the folder back, then press **Folder** and **Use folder**, or choose another. If they are gone for
good, **Use folder** offers **Start empty here**. The page also says when a save fails.

Graphs name their MQTT clients, and clients stay on the [MQTT](help:mqtt) page. On another computer, a graph whose
client is missing says so until a client of that name is added.

## Receiving messages

- The bar at the top of a dashboard lists the clients its graphs use and how each stands. **Connect** is
  shown for one that is not connected, and connects it on the MQTT page too.
- Graphs subscribe for themselves as soon as the app starts, and keep drawing whether or not the page or
  the Topic Explorer is open. They subscribe again after a reconnect.
- Readings are not kept between runs, so a graph starts empty each time. A topic's retained value, if it
  has one, becomes its first reading.
- A retained value sent again, as happens on a reconnect, is not counted as a new reading.
- Each number keeps up to 4,000 readings. A topic faster than that keeps fewer of its older ones, so a
  long span on a very fast topic shows less detail.

## If a graph is empty

- **Connect … to receive …**: the client is not connected. Press **Connect** at the top of the dashboard.
- **Waiting for a message on …**: nothing has arrived since the app started, on any of the graph's topics.
  Check the topic in the [Topic Explorer](help:topic-explorer), where it must match exactly, including upper
  and lower case.
- **There is no MQTT client called …**: the client was removed. Add a client of that name, or press
  **Edit layout**, then **Edit**, and choose another. Renaming a client does not cause this; its graphs
  follow it.
- **Messages are arriving, but …**: the message does not have what the graph reads. It says what is missing, such
  as a key's value, or that a gzipped message could not be unzipped.
- A graph stays empty when the number is not in the messages. Only numbers can be drawn: text, true or
  false, and numbers written as text have no graph.
