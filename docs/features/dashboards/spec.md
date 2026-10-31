# Dashboards

Dashboards are the pages under **Dashboard** in the main menu. Each one gives an at-a-glance
view of one subject.

| Dashboard | Menu path | Status |
| --- | --- | --- |
| Overview | Dashboard / Overview | Implemented; specified below |
| MQTT Dashboards | Dashboard / MQTT Dashboards | Implemented; specified [below](#mqtt-dashboards) |

## Overview dashboard

![Overview dashboard](../../images/dashboard-dark.png)

### Purpose

Show the current state of the machine Joktan is running on: how busy the CPU is, how much memory
and disk are in use, and how long the machine has been up. It is the page shown when the app
starts.

### Behaviour

- Readings are taken every 2 seconds, starting when the app starts, whether or not the page is
  open. Returning to the page shows current values and the trend collected so far.
- CPU usage is measured between two consecutive readings, so it shows "–" for the first
  2 seconds.
- A reading that cannot be obtained on the current platform shows "–" (or "Not available" for a
  usage line). The rest of the page is unaffected.
- The page has no controls. Nothing on it can be clicked, filtered or configured.

### Layout

From top to bottom:

1. Page header: title "Overview", subtitle "Live metrics for this machine".
2. A row of four stat tiles of equal width.
3. Two cards side by side: "System" and "OpenTelemetry metrics".

The content scrolls vertically when it does not fit.

### Stat tiles

Each tile shows an icon, the current value, the tile's name, and one supporting line.

| Tile        | Value                                                            | Supporting line           | Meter | Trend |
|-------------|------------------------------------------------------------------|---------------------------|-------|-------|
| CPU usage   | Share of CPU time in use across all cores, as a whole percentage | "*N* logical processors"  | Yes   | Yes   |
| Memory used | Share of physical memory in use, as a whole percentage           | "*used* of *total*"       | Yes   | Yes   |
| Disk used   | Share of the system drive in use, as a whole percentage          | "*used* of *total*"       | Yes   | No    |
| Uptime      | Time since the machine started                                   | "Started *date and time*" | No    | No    |

**Meter.** A horizontal bar filled to the tile's percentage. Its colour reflects the level:

| Level   | Range            | Colour  |
|---------|------------------|---------|
| Normal  | Below 75%        | Accent  |
| Warning | 75% to below 90% | Warning |
| Danger  | 90% and above    | Danger  |

The percentage is always shown as text, so the level never depends on colour alone.

**Trend.** A sparkline along the bottom edge of the tile showing the last 30 readings (one
minute), on a fixed 0–100% scale with the newest reading at the right. It needs at least two
readings to draw, and fills from the right until a full minute has been collected.

### System card

Facts about the machine, read once when the app starts:

| Row                | Example          |
|--------------------|------------------|
| Machine name       | `workstation-01` |
| Operating system   | `macOS 27.0.1`   |
| Architecture       | `Arm64`          |
| Logical processors | `18`             |
| .NET runtime       | `10.0.12`        |

### OpenTelemetry metrics card

Lists every instrument of the `Joktan.Machine` meter with its latest value, and states the OTLP
endpoint the app exports to. The values come from the same readings as the tiles, so the card and
the exported data always agree.

| Instrument                      | Unit    | Shown as   |
|---------------------------------|---------|------------|
| `system.cpu.utilization`        | `1`     | Percentage |
| `system.cpu.logical.count`      | `{cpu}` | Count      |
| `system.memory.usage`           | `By`    | Size       |
| `system.memory.limit`           | `By`    | Size       |
| `system.memory.utilization`     | `1`     | Percentage |
| `system.filesystem.usage`       | `By`    | Size       |
| `system.filesystem.limit`       | `By`    | Size       |
| `system.filesystem.utilization` | `1`     | Percentage |
| `system.uptime`                 | `s`     | Seconds    |
| `process.memory.usage`          | `By`    | Size       |

Export settings are described in the [README](../../../README.md#telemetry).

### Formatting

| Kind                    | Rule                     | Example   |
|-------------------------|--------------------------|-----------|
| Percentage              | Whole number             | `68%`     |
| Size, 1 GB or more      | One decimal place, in GB | `32.7 GB` |
| Size, under 1 GB        | Whole number, in MB      | `165 MB`  |
| Uptime, 1 day or more   | Days and hours           | `9d 0h`   |
| Uptime, 1 hour to 1 day | Hours and minutes        | `5h 12m`  |
| Uptime, under 1 hour    | Minutes                  | `42m`     |
| Missing value           | En dash                  | `–`       |

Sizes use 1 GB = 1024 MB = 1024³ bytes. Numbers and dates follow the user's regional settings.

### Data sources

| Reading  | Windows                                                     | Linux           | macOS                             |
|----------|-------------------------------------------------------------|-----------------|-----------------------------------|
| CPU time | `GetSystemTimes`                                            | `/proc/stat`    | `host_statistics`                 |
| Memory   | `GlobalMemoryStatusEx`                                      | `/proc/meminfo` | `host_statistics64`, `hw.memsize` |
| Disk     | `DriveInfo` for the drive that holds the user's home folder | same            | same                              |
| Uptime   | `Environment.TickCount64`                                   | same            | same                              |

Definitions:

- **CPU usage** is 1 − (idle time ÷ total time) between two readings, across all cores. On Linux,
  time waiting for I/O counts as idle.
- **Memory used** is total minus available memory on Windows and Linux. On macOS it is app
  memory + wired + compressed, the figure Activity Monitor calls "Memory Used".
- **Disk used** is capacity minus the space available to the current user.

Only macOS has been run and checked against system tools. The Windows and Linux readers are
implemented but untested.

### Implementation

| Part          | File                                                                                                      |
| ------------- | --------------------------------------------------------------------------------------------------------- |
| Page          | `src/Joktan.Dashboard/Views/DashboardView.axaml`, `src/Joktan.Dashboard/ViewModels/DashboardViewModel.cs` |
| Stat tile     | `src/Joktan.Dashboard/ViewModels/MetricTileViewModel.cs`                                                  |
| Sparkline     | `src/Joktan.Core/Controls/Sparkline.cs`                                                                   |
| Sampling      | `src/Joktan.Dashboard/Telemetry/MachineMonitor.cs`, `src/Joktan.Dashboard/Telemetry/SystemReader.cs`      |
| Instruments   | `src/Joktan.Dashboard/Telemetry/MachineMetrics.cs`                                                        |

Styles and colour tokens are covered in [design.md](../../design.md#dashboard).

### Not included

- Per-core CPU, network, GPU, battery and temperature readings
- More than one drive
- Choosing the refresh interval or the length of the trend
- History beyond the last minute, or any history kept between runs
- Alerts or notifications when a level is reached

## MQTT Dashboards

### Purpose

Keep an eye on the numbers on MQTT topics: subscribe to a topic, choose how to draw it, and put the
graph on a named dashboard, laid out as wanted. The [topic explorer](../dev-tools/connectivity/topic-explorer/spec.md)
shows everything on a broker for a moment; a dashboard shows chosen things for as long as the app runs.

### Dashboards

- The page has one tab per dashboard, and **Add**, at the top of the page, adds another. There is always at least one,
  called "Main" to begin with.
- The button beside **Add** switches between view mode, the one the page starts in, and edit mode. It says
  **Edit** in view mode and **View** in edit mode.
- Edit mode puts a pencil, a palette and a **×** on every tab. Going back to view mode finishes a rename and
  withdraws a question. The mode is not remembered between runs.
- The pencil, double-clicking a tab, or **Rename** on a tab's right-click menu puts a box on the tab to type
  the name. Enter or leaving the box takes it, and Esc does not. An empty box keeps the old name.
- The palette offers the colours offered for a client's page, or **Default** for the theme's, and applies at
  once. The name and buttons on a coloured tab are white or navy, whichever has more contrast.
- The **×**, or **Remove dashboard** on a tab's right-click menu, asks at the top of the dashboard first, and
  says how many graphs go with it.
- Removing the only dashboard replaces it with an empty one called "Main", and the question says so.
- The pencil, the **×** and the menu act on their own tab, and select it if it was not selected.
- The bar at the top of a dashboard lists the clients its graphs use, each with its state, and
  **Connect** for one that is disconnected or failed. The connection is the client's own, so this
  connects it on the MQTT page too.

**Format** opens these, which apply at once:

| Setting               | Default | Rules                                                     |
|-----------------------|---------|-----------------------------------------------------------|
| Dashboard name        | -       | Shown on the tab; "Untitled" while empty; need not be unique. Also set from the tab |
| Columns               | 3       | 1 to 6. Fewer are used when a column would be under 180 wide |
| Row height            | 240     | 120 to 480, in steps of 20                                |
| Space between graphs  | 16      | 0 to 32, in steps of 4                                    |
| Background            | Theme's | One of the colours offered for a client's page, or Default |

### Graph editor

**Add graph** on a dashboard, **Edit** on a graph, and **Add to dashboard** in the topic explorer open the
same dialog. It subscribes to the graph's topics for as long as it is open, lists the numbers that arrive,
and shows the graph as it will look.

| Field           | Default                  | Rules                                                 |
|-----------------|--------------------------|-------------------------------------------------------|
| Client          | The MQTT page's selected | A client on the MQTT page                             |
| Topics          | None                     | At least one. Each can hold `+`, `#` and [`{name}`](#keys). **Subscribe** or Enter adds one |
| Name            | The first topic's last level | Required                                          |
| Type            | Line                     | Line, Area, Bars, Gauge or Number                     |
| Shows the last  | 5 minutes                | 1, 5, 15 or 30 minutes; 1, 6 or 24 hours              |
| Unit            | Empty                    | Shown after each value                                |
| Decimal places  | Automatic                | Automatic (up to three as needed), or 0 to 3          |
| Fixed scale     | Off                      | With a minimum below its maximum                      |
| Numbers to draw | The first to arrive on each topic that does not look like a time or counter | 1 to 8 in all, each with a name and one of 8 colours |
| Keys            | Each wildcard, and each key of a ticked number, as lines | See [Keys](#keys) |
| Dashboard       | The one on show          | Or a new one, named here. Not offered when editing    |
| Add this topic to | A new graph            | From the topic explorer: or a graph on that dashboard with the same client |
| Columns wide    | 1                        | 1 to 6; no wider than the dashboard's columns when drawn |
| Rows high       | 1                        | 1 to 6                                                |

- Numbers are found as in the topic explorer's [Choose values](../dev-tools/connectivity/topic-explorer/spec.md#selected-topic),
  and listed values and arrays first, with those that look like times or counters last. In an array whose items
  have a key, they are offered [by that key](#choosing-an-array-item) rather than by position. A path can also be
  typed, for a topic with nothing arriving yet.
- The levels of each topic are shown as buttons. Pressing a fixed level makes it a key, named from its value, so
  `line1` becomes `{line}`.
- With a wildcard in a topic, the status line says about how many messages a second match it.
- From the topic explorer, the numbers it is drawing start ticked, and the messages in the topic's
  history, up to 100, start on the graph.
- Editing starts from a copy of the graph's readings, so **Cancel** changes nothing. Choosing a shorter time in
  the dialog loses none of them.
- Up to 1,000 messages heard in the dialog are kept, so that changing a key shows at once on what has arrived.
- Changing the client clears what was received. Topics and ticked numbers stay.
- **Remove** beside a topic takes off the topic, its numbers and its readings.
- A topic typed in but not yet added when the graph is saved is added first.
- Each topic must have a number ticked, or the graph is not saved and the message names the topic, as a
  graph only keeps the topics it draws.
- Choosing a graph under **Add this topic to** loads that graph into the dialog, with the topic added and
  its messages from the topic explorer on it, unless the graph already draws the topic. Nothing changes until
  it is saved, and choosing **A new graph** goes back.
- What was received in the dialog stays on the graph when it is saved.

### Several topics

A graph's numbers can come from different topics of one client, and each number is kept with its topic, so
the same path on two topics is two numbers.

- A number with no name of its own, on a graph with several topics, is called by as many of its topic's
  last levels as tell it from the others, then its path: `line1/temp value`. The full topic and path show when
  pointing at it in the legend.
- A topic that has sent nothing yet shows "–" in the legend. The message over the graph is only for a graph
  with no readings at all.
- A topic on several graphs is subscribed to once and reaches each of them.
- Subscriptions follow the graphs: a topic is dropped when no graph draws it.

### Keys

A key says which thing a number was measured on, such as `line`, `machine`, `axis` or `sensor`. One number with one
set of key values is one series, which is stored, coloured and drawn on its own, and stays the same if the order in
a message changes.

| A key comes from         | Written as                         | Example values         |
|--------------------------|------------------------------------|------------------------|
| A level of the topic     | `plant/{line}/telemetry/vibration` | `line1`, `line2`       |
| A field of the message   | Added under **Keys** in the editor | `x`, `y`, `z`          |
| A field of an array item | `readings[sensor=*].value`         | `temperature`, `humidity` |

#### In a topic

- `{name}` is a `+` called name, and `{name#}` is a `#` called name, holding the rest of the topic. A name starts
  with a letter, and a key must be a whole level.
- `+` and `#` are keys as well, called `level2` for the level at index 2, counting from 0.
- `\{` at the start of a level is a real brace.
- Wildcards follow MQTT's rules, so a filter starting with one does not match a topic starting with `$`.

#### Choosing an array item

| Path                                        | Means                                                     |
|---------------------------------------------|-----------------------------------------------------------|
| `readings[sensor=temperature].value`        | The item whose `sensor` is `temperature`, wherever it is  |
| `readings[sensor=*].value`                  | One series per `sensor`                                   |
| `readings[sensor=temperature,room=a].value` | Both must match                                           |
| `groups[id=a].readings[sensor=t].value`     | Nested arrays                                             |
| `readings[meta.id=5].value`                 | A nested key. Matches both `5` and `"5"`                  |
| `readings[name="north side"].value`         | Quotes for spaces and punctuation. Case matters           |
| `readings[*].value`                         | One series: a batch of readings over time                 |
| `readings[0].value`                         | By position. Still read, but not offered if a key is found |

- An item that is not in a message adds no reading, and the line stays as it is.
- Several items that match are one batch, as for `[*]`.
- The key of an array is found by itself: a field holding text that differs on every item and does not look like a
  date, or a whole number in a field with a name such as `id` or `channel`. Fields such as `ts` and `unit` are not keys.
- A saved path by position goes on working. When a key is found, it says so beside the number, and **Use it** replaces it.

#### Linking a wildcard to a field

What a wildcard matched can choose what is read, by the key's name or the level's index counting from 0:

| Topic                            | Path                              | On `building/hvac/humidity/climate` reads |
|----------------------------------|-----------------------------------|-----------------------------------|
| `building/hvac/{sensor}/climate` | `readings[sensor={sensor}].value` | The item whose `sensor` is `humidity` |
| `building/hvac/+/climate`        | `readings[sensor={2}].value`      | The same                          |
| `building/hvac/{sensor}/climate` | `{sensor}.value`                  | The field `humidity`, then its `value` |

- A link in an array position, such as `readings[{2}]`, is refused, as it would depend on the order again.
- A link must point at a wildcard of that topic. This is checked as the path is added.
- When a level's value matches a key's value or a field in a message, the linked path is offered among the numbers.
- The linked level is still a key.

#### Show each value as

Each key has one choice, and they can be mixed:

| Choice    | Result                                                                  |
|-----------|-------------------------------------------------------------------------|
| One line  | A line per value on this graph. The default                             |
| One graph | A graph per value on this dashboard, made from one saved graph          |
| One tab   | A tab per value. The tabs are one dashboard, shown once per value       |
| Only      | One value, chosen from those seen. It no longer tells series apart      |

- A new value appears by itself, in the next free place, and never takes the focus.
- The editor says what its choices make, such as "This makes 2 tabs, 2 graphs on each, 3 lines on each graph".
- A graph's name and a number's name can hold `{key}`. Otherwise a graph is called `Vibration: press-07`, a tab
  `Plant: line1`, and a line by its key values.
- Graphs made from one saved graph are moved, resized, edited and removed together, on every tab, and removing asks
  how many go. **Forget** removes one graph, and **Forget this value** on a tab's menu removes one tab, until that value is
  seen again.
- Only one key of a graph can be shown as tabs, and all graphs on a dashboard must use the same one.
- A graph keeps a line's colour between runs, and the graphs and tabs seen, so they are back before a message arrives.
- A line with a key that has sent nothing for the time shown leaves the graph. Its graph and tab stay until forgotten.
- **Lines on this graph**, in the editor, lists the lines keys make on the graph being edited. **×** removes one from
  that graph, and **Bring back** undoes it. It stays away when its messages arrive and between runs, and goes on being
  read, so it comes back with its readings. Other graphs made from the same saved graph keep theirs. The last line
  stays, as **Remove** is for that. Nothing changes until the editor is saved. There is no **×** on a graph itself.
- **×** beside a number in the editor's **Numbers to draw** takes it off the graph. One that messages no longer offer,
  such as `readings[1].value` once the array has a key, also leaves the list.

#### Advice in the editor

- **Mixed series.** If a field outside any array has several values on a topic, the editor says "This number comes
  with 3 values of axis", and **Show one line for each** adds it as a key. Without one, they would zigzag on one line.
- **Different numbers on one scale.** If the `unit` fields beside the ticked numbers differ, or their sizes differ a
  thousandfold, the editor says so and **One graph for each** splits them. There is still no second axis.
- **A field that differs on nearly every message** is refused as a key, once 20 messages have been seen, as it would
  make a new line each time. Fields that look like dates or times are not offered.

#### Limits

| Limit               | Most | Past it                                                |
|---------------------|------|--------------------------------------------------------|
| Lines on a graph    | 8    | The most recently updated show, and it says "and 42 more" |
| Graphs per tab from one saved graph | 24 | New values are left out, and the page says so |
| Tabs from one key   | 12   | New values are left out, and the page says so          |
| Series in the app   | 500  | New series wait, and the page says so                  |

#### Details

**Details**, under **Edit layout**, says when the last message came and on which topic, how many in the last minute
were matched, used and skipped, and why. A graph with no readings shows the same reason over the graph.

### Graph types

| Type   | Draws                                                                                |
|--------|--------------------------------------------------------------------------------------|
| Line   | A line for each number over the time shown, with a dot on the latest reading          |
| Area   | The same, filled down to zero. The scale includes zero                                |
| Bars   | One number: its recent readings, as many as fit, newest at the right. Several: the latest of each, named below. Bars start at zero |
| Gauge  | The latest value on a half dial for each number, with the scale's ends labelled      |
| Number | The latest value in large type for each number, with a trend line if the tile is over 90 high |

- All the numbers on a graph share one scale. There is never a second axis.
- Without a fixed scale, the scale is fitted to what is in view and rounded out to whole steps.
- A value outside a fixed scale is held at its edge on bars and gauges, and clipped on lines and areas.
- A graph with several numbers on Line or Area has a legend with each name and latest value. One number
  on Line, Area or Bars has its latest value beside the graph's name instead.
- Pointing at a line or area shows a crosshair and the nearest reading of each number, within 48 of the
  pointer. Pointing at a bar shows its value.
- The scale shortens numbers from a thousand up, as 1.2K, 3M and 4G, and leaves out the unit.
- A number's colour stays with it when others are ticked or cleared. Colours are `Chart1Brush` to
  `Chart8Brush`, and each new number takes the first one not in use.

### Layout

**Edit layout** adds to each graph a bar with **‹**, **›**, **Edit**, **Details**, **Forget** for a graph made from a
key's value, and **Remove**, and a handle in its bottom corner.

- Dragging the bar onto another graph puts it in that graph's position. The graph being dragged fades, and
  the place it would go is outlined. Nothing moves until the pointer is released.
- Dragging the handle resizes the graph to whole columns and rows, changing once past halfway. It goes no
  wider than the dashboard's columns, and no higher than 6 rows.
- Removing a graph asks first.
- Graphs are placed in order, each in the first free place it fits, so a gap can be filled by a later graph.

### Receiving

- Graphs subscribe to the topics of their numbers through their clients when the app starts, whether or not the
  page or the topic explorer has been opened, and again after a reconnect.
- They subscribe separately from the topic explorer, so **Unsubscribe** there does not stop them. See
  [Subscriptions](../dev-tools/connectivity/topic-explorer/spec.md#subscriptions) there for how the two share
  a connection.
- Graphs are updated about seven times a second as messages arrive, and move along once a second.
- A number holding several values, for `[*]`, has them spread over the time since its last reading, at
  most 10 seconds, so a batch is drawn as a run of readings rather than one upright line.
- A retained message is a stored value and not a new reading, so it only counts when the number has no
  readings yet.
- Time is when the message arrived. A time inside the payload is not used.
- A gzipped payload works the same as a plain one, with nothing to set. It is recognised by its first two bytes,
  and unzipped when first needed, so a message nothing draws is not unzipped.
- A gzip stream that was cut off gives no reading. Nor does one that unzips past 16 MB, or a payload over 1 MB after
  unzipping. **Details** says which. Several gzip streams joined together are not read as gzip.

### Data

Dashboards, their format, tab colours and graphs, with their keys, the graphs and tabs seen and their lines'
colours, are saved to `dashboards.json`, as they change. Readings are not saved.

**Folder**, at the top of the page, chooses the folder the file is in. It is the data folder until another is
chosen, and the choice is kept in `dashboards-location.json` in the data folder. The controls are those of the
[notes folder](../notes/spec.md): a path or `~`, **Browse**, **Default**, **Cancel** and **Use folder**.

- A folder that already holds `dashboards.json` is opened as it is, and its graphs start empty.
- Otherwise the dashboards open now are written into it, and go on being saved there. Their graphs keep their
  readings, and the file in the old folder stays as it was.
- A chosen folder with no `dashboards.json` when it is read, whether the folder is missing or empty, or a file that
  cannot be read for any reason but its contents, leaves the page with an empty dashboard and nothing is saved, so that
  it cannot replace the real ones when the folder comes back. This covers an empty mount point and a synced folder that
  has not filled in yet. The page says so. The data folder itself is exempt, as it starts with no file.
- **Use folder** on that same folder opens the dashboards if they are back. If not, it writes nothing, says so, and
  offers **Start empty here**, which makes a new file only when pressed. It stops if a file has appeared by then.
- If dashboards appear in a folder in the moment before they would be copied there, nothing is replaced and the page says so.
- A save that fails is reported at the top of the page, until the next save that does not fail.

- A file from when a graph had one topic is read as that topic on each of its numbers, and saved in the
  new form the next time something changes.
- A graph knows its client by name and follows it when it is renamed. If the client is removed, the graph
  stays and says so, and comes back if a client of that name is added.
- A `dashboards.json` that cannot be understood is copied to `dashboards.json.unreadable`, as saving would
  otherwise replace it.
- Each number keeps what is in the time shown, and at most 4,000 readings. Past that every other reading is
  dropped, so a fast topic loses detail rather than time. Spikes can be lost this way.

### Implementation

| Part                 | File                                                                                                   |
| -------------------- | ------------------------------------------------------------------------------------------------------ |
| Page, routing        | `src/Joktan.TopicDashboards/Views/TopicDashboardsView.axaml`, `src/Joktan.TopicDashboards/ViewModels/TopicDashboardsViewModel.cs` |
| One dashboard        | `src/Joktan.TopicDashboards/Views/DashboardTabView.axaml`, `src/Joktan.TopicDashboards/ViewModels/DashboardTabViewModel.cs` |
| Grid                 | `src/Joktan.TopicDashboards/Controls/DashboardGrid.cs`                                                 |
| One graph            | `src/Joktan.TopicDashboards/Views/GraphCardView.axaml`, `src/Joktan.TopicDashboards/ViewModels/GraphCardViewModel.cs` |
| Drawing              | `src/Joktan.TopicDashboards/Controls/GraphView.cs`                                                     |
| Editor               | `src/Joktan.TopicDashboards/Views/GraphEditorWindow.axaml`, `src/Joktan.TopicDashboards/ViewModels/GraphEditorViewModel.cs` |
| Readings             | `src/Joktan.TopicDashboards/GraphData.cs`, `src/Joktan.TopicDashboards/GraphReader.cs`, `src/Joktan.TopicDashboards/ViewModels/ClientFeed.cs` |
| Topics, paths, offers | `src/Joktan.Mqtt/TopicPattern.cs`, `src/Joktan.Mqtt/PayloadPath.cs`, `src/Joktan.Mqtt/NumberOffers.cs` |
| Keys in the editor   | `src/Joktan.TopicDashboards/ViewModels/GraphKeyViewModel.cs`                                           |
| Saving               | `src/Joktan.TopicDashboards/DashboardStore.cs`                                                         |

Checked on macOS against Mosquitto 2 running under Podman.

### Not included

- Combining the values of a key, as an average, minimum, maximum or sum
- One graph drawing topics from different clients
- Text, true or false, and other values that are not numbers
- Keeping readings between runs, or exporting them
- Warning colours or alerts when a value passes a limit
- Placing graphs freely, with empty places left between them
- A time taken from inside the payload

### Decisions to confirm

These were not stated in the request and are the choices this spec makes:

1. **Dashboards are a new page under Dashboard**, and Overview stays as it is, since Overview shows this
   machine and has no MQTT client.
2. **A graph draws topics of one client**, with up to 8 numbers in all, or up to 8 lines on show when keys make more.
3. **Graphs start empty each run**, as the topic explorer does, rather than storing readings.
4. **Graphs subscribe when the app starts but do not connect the client**, so opening the app never
   connects to a broker by itself.
5. **A folder is chosen, not a file name**, as for notes and API requests, so the file is always
   `dashboards.json`, and all dashboards share one folder.
