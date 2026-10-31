# MQTT Dashboards: current state and design

MQTT Dashboards is implemented. It draws numbers from MQTT topics
as live graphs, on named tabs laid out by the user. Readings are held in memory only, so every graph starts
empty on each run.

A number is identified by keys rather than its position in a message, and topics can hold wildcards. Phase 1 of the
[design](#design-keys-wildcards-and-grouping) is built. [Phases 2 to 4](#other-telemetry-gaps) are not.

This page records what the code does as of app version 2.0.0, and what to look at before changing it. The rules for each behaviour are in the [spec](spec.md#mqtt-dashboards), and the user help is in
`src/Joktan.Help/Topics/topic-dashboards.md`. Change those two with any change to the feature.

The items under [Known gaps](#known-gaps) come from reading the code, and have not been reproduced in the app.

## What is implemented

### Dashboards

| Feature             | What it does                                                                     |
|---------------------|----------------------------------------------------------------------------------|
| Tabs                | One per dashboard, with always at least one. The first is called "Main"          |
| Adding              | **Add** at the top of the page, or a new dashboard named in the graph editor     |
| Edit mode           | **Edit** puts a pencil, a palette and a **×** on every tab. It is not remembered |
| Renaming            | Pencil, double-click or right-click. Enter or leaving the box takes the name     |
| Tab colour          | A palette, or **Default**. The text is white or navy, whichever reads better     |
| Removing            | Asks first and says how many graphs go. The only dashboard is replaced by "Main" |
| Format              | Name, 1 to 6 columns, row height 120 to 480, gap 0 to 32, and background colour  |
| Client bar          | The clients its graphs use, each with its state, and **Connect**                 |

### Graphs

| Feature             | What it does                                                                       |
|---------------------|------------------------------------------------------------------------------------|
| Types               | Line, Area, Bars, Gauge and Number                                                 |
| Numbers             | 1 to 8 on a graph, each with its own name and one of 8 colours, or up to 8 lines on show from keys |
| Topics              | Several topics of one client, with `+`, `#` and `{name}`                           |
| Keys                | From the topic, a message field or an array item, shown as lines, graphs, tabs or one value |
| Time shown          | 1, 5, 15 or 30 minutes; 1, 6 or 24 hours                                           |
| Display             | Unit, decimal places (automatic or 0 to 3), and a fixed or fitted scale            |
| Scale               | One per graph, shared by all its numbers. There is no second axis                  |
| Reading values      | Headline value or legend, a crosshair on lines and areas, and a tooltip on bars    |
| Empty graphs        | Says why: no such client, not connected, or waiting for a message                  |

### Graph editor

The editor is one dialog, opened from **Add graph**, from **Edit** on a graph, and from **Add to dashboard** in
the Topic Explorer.

- It subscribes to the graph's topics while open, lists the numbers that arrive, and shows a live preview.
- The first number to arrive on a topic is ticked for you, and a path can be typed for a topic with nothing
  arriving yet.
- Editing works on a copy of the graph's readings, so **Cancel** changes nothing.
- From the Topic Explorer, the numbers drawn there start ticked, and up to 100 messages of the topic's history
  start on the graph. The topic can go on a new graph, or on a graph on that dashboard with the same client.

### Layout

**Edit layout** adds these to each graph:

- A bar to drag onto another graph, which takes that graph's position. **‹** and **›** do the same in steps.
- A corner handle that resizes to whole columns and rows, up to 6 rows high.
- **Edit**, and **Remove**, which asks first.

Graphs fill the grid in order, each in the first free place it fits. A narrow window uses fewer columns, so
that none is under 180 wide.

### Receiving

- Graphs subscribe when the app starts, whether or not the page is open, and again after a reconnect. They do
  not connect a client themselves.
- A topic on several graphs is subscribed to once, and is dropped when no graph draws it.
- Gzipped payloads work the same as plain ones, and are unzipped when first needed.
- A renamed client is followed. A removed client leaves its graphs waiting, in case one of that name returns.
- A retained message only counts when a number has no readings yet.
- The items of an array, for `[*]`, are spread over the time since the last reading, at most 10 seconds.
- Time is when the message arrived.
- Each number keeps what is in the time shown, up to 4,000 readings. Past that, every other reading is dropped.

### Saving

- Dashboards, their format, tab colours and graphs are saved to `dashboards.json` as they change, on a
  background thread, and once more when the app exits. Readings are not saved.
- **Folder** chooses where the file is kept, and the choice is kept in `dashboards-location.json` in the data
  folder. A folder that holds a `dashboards.json` is opened as it is. Any other gets a copy of the open ones.
- A chosen folder with no file, such as a missing drive or an empty mount point, or an unreadable file, leaves an
  empty dashboard, shows a message, and saves nothing. Retrying writes nothing unless **Start empty here** is pressed.
- A file that is not valid JSON is copied to `dashboards.json.unreadable`.
- Values outside their limits in a hand-edited file are brought within them when it is read.
- A file from when a graph had one topic is read and moved to the current form.

## How it fits together

`MainViewModel` creates one `TopicDashboardsViewModel` at startup, which is why graphs receive while the page is
closed. The MQTT page owns the clients and connections, and the dashboards only use them.

A message reaches a graph like this:

1. `ClientFeed` hears every message on its client's connection, on the MQTT thread, and queues those matching the
   filters its graphs need.
2. Every 150 ms, `TopicDashboardsViewModel` takes up to 5,000 messages per client, on the UI thread, and routes
   each to the saved graphs that match, remembering the answer for each topic.
3. `GraphSource` has `GraphReader` read the message into series, by topic, path and keys, and keeps them in
   `GraphData`. A new value of a tab or graph key adds a place, and the page arranges its tabs and graphs.
4. Each card showing that saved graph picks its own series, its `Revision` changes, and `GraphView` draws again.
5. Once a second, every graph forgets readings past its left edge, series with keys that have gone quiet are
   forgotten, and graphs on the selected tab are redrawn so they move along.

Subscriptions are shared through `MqttConnection.SetSubscriptionsAsync`, which takes a set of filters per owner.
The owners here are `dashboards` for graphs and `dashboard-editor` for an open editor, so the Topic Explorer's
**Unsubscribe** does not affect either.

| Part                 | Where (under `src/Joktan.TopicDashboards/`)                       |
|----------------------|-------------------------------------------------------------------|
| Page, routing        | `ViewModels/TopicDashboardsViewModel.cs`, `Views/TopicDashboardsView.axaml` |
| One dashboard        | `ViewModels/DashboardTabViewModel.cs`, `Views/DashboardTabView.axaml` |
| Grid                 | `Controls/DashboardGrid.cs`                                       |
| One graph            | `ViewModels/GraphCardViewModel.cs`, `ViewModels/GraphSeriesViewModel.cs`, `Views/GraphCardView.axaml` |
| Drawing              | `Controls/GraphView.cs`, `GraphColors.cs`                         |
| Editor               | `ViewModels/GraphEditorViewModel.cs`, `ViewModels/GraphValueChoiceViewModel.cs`, `Views/GraphEditorWindow.axaml` |
| Readings, keys       | `GraphData.cs`, `GraphReader.cs`, `ViewModels/ClientFeed.cs`      |
| Keys in the editor   | `ViewModels/GraphKeyViewModel.cs`                                 |
| Saved form, file     | `DashboardStore.cs`                                               |

Topic patterns, keyed paths and what the editor offers are in `src/Joktan.Mqtt/`: `TopicPattern.cs`, `PayloadPath.cs`
and `NumberOffers.cs`. The Topic Explorer opens the editor through `TopicDashboardsViewModel.NewEditor`, from
`src/Joktan.TopicExplorer/ViewModels/ClientExplorerViewModel.cs`.

## Tests

`tests/Joktan.TopicDashboards.Tests` and new files in `tests/Joktan.Mqtt.Tests` cover this feature. Run the whole
solution with `dotnet test Joktan.slnx`.

| File                     | Methods | Covers                                                            |
|--------------------------|---------|-------------------------------------------------------------------|
| `GraphDataTests.cs`      | 27      | Readings, array spreading, pruning, retained messages, series keys, topic names |
| `GraphReaderTests.cs`    | 24      | Reading by topic, path and key, links, Only, limits, gzip, health |
| `DashboardStoreTests.cs` | 28      | Saving, limits, keys, older files, the chosen folder, failed writes |
| `GraphColorsTests.cs`    | 1       | A name for every colour                                           |
| `PayloadPathTests.cs`    | 19      | Keyed paths, order, nesting, quotes, links                        |
| `NumberOffersTests.cs`   | 16      | Finding keys, offering numbers, times last, units, links          |
| `TopicPatternTests.cs`   | 10      | `{name}`, wildcards, escapes, suggested names, filter sets        |

Nothing in the repository tests `TopicDashboardsViewModel`, `DashboardTabViewModel`, `GraphCardViewModel`,
`GraphEditorViewModel`, `DashboardGrid` or `GraphView`, because making an MQTT client view model reads the Keychain,
which tests must not touch. The page and editor were instead run once, with a throwaway program outside the repository,
against Mosquitto on macOS. Separating the credential store from the client view model would let those checks be kept.

## Known gaps

Found by reading the code, most serious first.

1. **Gauges and numbers do not show that a value is old.** Pruning always keeps the last reading
   (`GraphData.cs:46`), and it stays the latest value however old it is (`GraphSeriesViewModel.cs:36`). Once a graph
   has readings it has no status either (`TopicDashboardsViewModel`, `StatusOf`), so a disconnected client's gauge looks
   live. Only the client bar shows the disconnection.
2. **The tab to reopen is found by name, and names need not be unique.** Of two tabs with the same name, the first
   is always the one reopened (`TopicDashboardsViewModel`, `Open`).
3. **A lost pointer capture is not handled while resizing.** Moving handles it and resizing does not
   (`DashboardTabView.axaml.cs:56`, `:102`), so a resize could carry on when the pointer next crosses the handle.
4. **The message after changing folder never goes.** It stays on the page until the folder is next changed
   (`TopicDashboardsViewModel`, `ApplyFolderAsync`).
5. **Several gzip streams joined together are not read as gzip.** Cut-off streams are found by the length in the
   stream's trailer, which for joined streams gives only the last one's length (`Gzip.cs`). None is expected in practice.

## Not implemented

The spec leaves these out on purpose:

- Combining the values of a key, as an average, minimum, maximum or sum
- One graph drawing topics from different clients
- Text, true or false, and other values that are not numbers
- Keeping readings between runs, or exporting them
- Warning colours or alerts when a value passes a limit
- Placing graphs freely, with empty places left between them
- A time taken from inside the payload

The spec also lists five [decisions to confirm](spec.md#decisions-to-confirm), which are still open. Phases 2 to 4
below reverse several of these exclusions, so the spec changes as each is built, along with the help topic.

## Design: keys, wildcards and grouping

**Status: built in app version 2.0.0, and specified in the [spec](spec.md#keys).** This section keeps the reasoning. Limits
are starting values, to be checked against real use.

Where the build differs from what was first proposed:

- A dashboard takes one tab key, so tabs from two keys at once are not offered.
- Past the limit on graphs or tabs, new values are left out and the page says so, rather than asking first.
- The rate for a wildcard shows while listening, not before subscribing, as it cannot be known sooner.
- A link is offered as a number to tick, not as a question.
- Numbers are ticked by what they look like, not by whether they change, as the first message cannot show that.
- Lines from keys are removed in the editor, with **Lines on this graph**. The graph itself has no **×**, as asked.
- The Topic Explorer still lists numbers by position. It can adopt `PayloadPath` and `NumberOffers` later.

### The problem

Before this, a number was one path on one exact topic, which drew the wrong thing without saying so:

| Message                                 | Before                               | What goes wrong                                  |
|-----------------------------------------|--------------------------------------|--------------------------------------------------|
| `building/hvac/zone1/climate`           | `readings[0].value`                  | If the order changes, humidity is drawn as temperature |
| The same                                | `readings[*].value`                  | Temperature and humidity become one line         |
| `plant/line2/telemetry/vibration`       | `rms`                                | Axes `x`, `y` and `z` share a topic, so they zigzag on one line |
| `devices/sensor-02/heartbeat`, and more | One topic at a time                  | Each device must be added by hand, and new ones never appear |
| Most samples                            | The first number is ticked           | It is often `ts`, so the first thing drawn is a timestamp |

### The model

This is the model used by Prometheus, InfluxDB and Grafana, in the app's own words:

- A **number** is what is measured: `rms`, or the `value` of a reading.
- A **key** says which thing it was measured on: `line`, `machine`, `axis`, `sensor`.
- One number with one set of key values is one **series**, the unit that is stored, coloured and drawn.

A series keeps its identity if array order changes, if a message adds a field, and across runs. Keys come from
three places, and all three then behave the same:

| Source              | Written as                         | Example result                     |
|---------------------|------------------------------------|------------------------------------|
| A level of the topic | `plant/{line}/telemetry/vibration` | `line` = `line1`, `line2`          |
| A field of the message | Key field `axis`                | `axis` = `x`, `y`, `z`             |
| A field of an array item | `readings[sensor=*].value`    | `sensor` = `temperature`, `humidity` |

### Grouping in the topic

`{name}` in a topic is a `+` with a name: it subscribes to `plant/+/telemetry/vibration` and calls that level
`line`. This is the same `{name}` that message settings already use to send to a varying topic (see
`samples/mqtt-messages/devices/fleet/heartbeat.send.json`), so one topic can be written the same way for sending and
drawing.

- Wildcards are required for this feature: `+` and `#` are allowed on any graph, with or without a name. An unnamed
  `+` is still offered as a key, named after its position until renamed.
- `{name#}` names a `#`, and holds the rest of the topic, such as `{area#}`.
- A real `{` in a topic is written `\{`.
- Nobody has to type this. After a topic is added, its levels are shown as buttons, and pressing one turns it into
  a key. The name is suggested from the values, so `line1` and `line2` suggest `line`.
- Exact topics go on working as they do now.

### Choosing an array item by key

| Path                                  | Means                                                  |
|---------------------------------------|--------------------------------------------------------|
| `readings[sensor=temperature].value`  | The item whose `sensor` is `temperature`, wherever it is |
| `readings[sensor=*].value`            | One series per `sensor`                                |
| `readings[sensor=temperature,room=a].value` | Both must match                                  |
| `groups[id=a].readings[sensor=t].value` | Nested arrays                                        |
| `readings[meta.id=5].value`           | The key is nested. Matches both `5` and `"5"`          |
| `readings[name="north side"].value`   | Quotes for spaces and punctuation. Case matters        |
| `readings[*].value`                   | Unchanged: one series, a batch of readings over time   |
| `readings[0].value`                   | Unchanged, and no longer offered when a key is found   |

- **Item not in a message:** no reading is added and the line keeps its place. The graph can say why (see
  [Why is it empty?](#why-is-it-empty)).
- **Several items match:** they count as a batch, as for `[*]`, and the editor warns once.

### Linking a wildcard to a field

The value a wildcard matches can choose what is read from the message, so one definition serves every topic it
matches. A path refers to a topic level by name, or by its index in the topic, counting from 0:

```text
Topic   building/hvac/+/climate            the + is level 2
Path    readings[sensor={2}].value

The same, with a name:
Topic   building/hvac/{sensor}/climate
Path    readings[sensor={sensor}].value

Message on building/hvac/temperature/climate
  reads the item whose sensor is temperature, wherever it is in the array
```

| Path                            | Topic matched                  | Reads                                        |
|---------------------------------|--------------------------------|----------------------------------------------|
| `readings[sensor={2}].value`    | `building/hvac/humidity/climate` | The item whose `sensor` is `humidity`        |
| `{1}.value`                     | `line/temp`                    | The field `temp`, then its `value`           |
| `channels.{1}`                  | `line/ch1`                     | The field `ch1` inside `channels`            |

- A link can be used in a field name or a key's value, but not as an array position, as that would bring back the
  dependence on order. `readings[{2}]` is refused, with the reason.
- `{2}` must point at a wildcard level. If it points at a fixed level, or past the end of the topic, the editor says
  so as it is typed. A name is preferred over an index and is what the editor writes, as an index breaks if a level
  is added to the topic.
- The linked level is still a key, so it can be shown as lines, graphs or tabs like any other.
- If a message has nothing for that value, no reading is added, and Details says which value was missing.
- The editor offers the link itself: when a level's value matches a field name or a key's value in the message, the
  linked path is listed among the numbers to tick.

### Show each value as

This one control carries the whole feature. Each key gets a row in the editor, showing the values seen so far:

```text
Topic   plant/{line}/telemetry/vibration          [ line ] telemetry  vibration
Number  rms  ✓

Key       Seen                  Show each value as
line      line1, line2          [ One tab        ▾ ]
machine   press-07, press-09    [ One graph      ▾ ]
axis      x, y, z               [ One line       ▾ ]    suggested
                                             Add a key from the message: [ ▾ ]
Preview: 2 tabs, 2 graphs on each, 3 lines on each graph
```

| Choice            | Result                                                       |
|-------------------|--------------------------------------------------------------|
| One line          | A line per value, on this graph. The default                 |
| One graph         | A copy of the graph per value, on this dashboard             |
| One tab           | A tab per value, holding the graphs that use this key        |
| Only              | One value, picked from those seen; the others are left out   |
| Combine           | Average, minimum, maximum or sum across values (phase 3)     |

Choices can be mixed, as in the example, where `line` gives tabs, `machine` gives graphs and `axis` gives lines.

- A new value appears by itself, including a new device with no change to the dashboard. It takes the next free
  place and never takes the focus from what is on screen.
- Names come from the keys, such as `press-07 x`, and can be changed with a pattern such as `{machine} / {axis}`.
- A value keeps its colour and place between runs, as seen values are saved in `dashboards.json`.

**Tabs from a key.** Choosing One tab makes one dashboard that is shown as many tabs, so editing a graph on any of
them changes all of them, and removing asks and removes all. A separate copy per tab would soon differ, and a new
line could never join. The tabs share a colour and a small marker to show they belong together.

### Ease of use

Most people should press a few things and type nothing:

1. **Start from a message.** The editor shows the latest message as a tree, with keys and numbers marked. Pressing
   a number draws it. Paths stay available for a topic with nothing arriving, but are never needed otherwise.
2. **Find keys without being asked.** In an array of objects, a field whose text or whole numbers differ on each
   item, stay the same between messages, and are not `ts` or `unit`, is taken as the key. So the climate message
   offers `temperature` and `humidity`, not `[0]` and `[1]`.
3. **Warn about mixed series.** If `rms` arrives with several values of `axis` on a topic, the editor says "This
   number comes with 3 values of axis" and offers One line per axis as one press.
4. **Offer sensible numbers first.** Fields named like `ts`, `time`, `seq` or
   `since`, and values that look like an epoch time, are listed last and not ticked.
5. **Warn about different units.** If `unit` differs across values, or sizes differ a thousandfold, suggest One graph.
   The one-scale rule stays, so the fix is to split the graph, not add a second axis.
6. **Show the outcome before saving.** The preview says how many tabs, graphs and lines this makes.
7. **Fix old graphs with one press.** A saved `readings[0].value` keeps working, and shows "This depends on the
   order of the array. Use sensor = temperature?". The file changes only if accepted.

### Limits

Wildcards make the number of series unbounded, and this is the classic way telemetry tools fail. A device ID in a
key, or a `#` on a busy broker, can create thousands. Each series can hold 4,000 readings of 16 bytes, so 64 KB.

| Limit                 | Start at   | Past it                                                     |
|-----------------------|------------|-------------------------------------------------------------|
| Lines on a graph      | 8          | The most recently updated show, and the legend says "and 42 more" |
| Graphs from one saved graph, per tab | 24 | New values are left out, and the page says so      |
| Tabs from one key     | 12         | New values are left out, and the page says so               |
| Series in the app     | 500, or 32 MB | New series wait, and the page says so                    |

- A key that looks unique per message, such as `ts` or a request ID, is refused with the reason.
- With a wildcard, the editor shows how many messages arrive per second while listening.
- A value not heard from for the graph's time span leaves the graph. It stays on saved tabs until forgotten.

### Why is it empty?

Each graph has **Details**, under **Edit layout**, since a wrong key otherwise looks the same as a quiet broker:

```text
Last message 4s ago, on plant/line2/telemetry/vibration
Last minute: 30 matched, 28 used, 2 skipped: no value of axis
Series: 3
```

The same reason shows over a graph with no readings. It also covers what was dropped without a word before: a payload over 1 MB, more than 500 fields, more than 60
values at one path (all in `JsonNumbers.cs`), and text that is not a number.

### Gzipped payloads

Everything above works the same on a gzipped payload, with nothing to set. `ReceivedMessage` already recognises gzip
by its first two bytes and unzips it, and keys, links and key finding all read the unzipped payload. The editor's
message tree shows the content, with "gzip" beside it as in the Topic Explorer.

Gzip had these gaps, which matter more with wildcards. All four are now fixed as proposed, except that the 1 MB limit stays:

| Gap                        | Today                                                             | Proposed                                  |
|----------------------------|-------------------------------------------------------------------|-------------------------------------------|
| Size limits disagree       | Unzips to 16 MB (`Gzip.cs:14`), but numbers are only read up to 1 MB (`JsonNumbers.cs:19`), so a batch that unzips past 1 MB draws nothing | Details says "unzipped to 3 MB, over the 1 MB limit". Consider a higher limit for batches |
| Cut-off streams            | Returns what it got rather than failing, which is the two skipped tests in `GzipTests.cs` | Fix it, so a cut-off payload is a skipped message. A cut-off bare number could otherwise be read as a wrong value |
| Payloads that fail to unzip | Treated as plain binary, so they give no numbers and no reason   | Details says "looks like gzip, but could not be unzipped" |
| Cost on every message      | Unzipped on the MQTT client's thread as each message arrives, before anyone checks if it is wanted (`MqttConnection.cs:386`) | Unzip on first use, so a broad `#` does not unzip what nothing draws |

Tests for the path reader run each path form on both plain and gzipped payloads.

### Saved form

Additive, so current files stay valid and are not rewritten until the graph is changed:

```json
{
  "Name": "Vibration",
  "Client": "plant-broker",
  "Values": [{ "Topic": "plant/{line}/telemetry/vibration", "Path": "rms" }],
  "Keys": [
    { "Name": "line", "Show": "Tab" },
    { "Name": "machine", "Field": "machine", "Show": "Graph" },
    { "Name": "axis", "Field": "axis", "Show": "Line" }
  ],
  "SeriesColors": { "plant/{line}/telemetry/vibration|rms|axis=x": 0 },
  "RemovedLines": [],
  "Seen": [{ "line": "line1", "machine": "press-07" }]
}
```

### Worked examples, from `samples/mqtt-messages`

| Message                 | Topic                              | Number                       | Keys                  | Result                             |
|-------------------------|------------------------------------|------------------------------|-----------------------|------------------------------------|
| Climate                 | `building/hvac/{zone}/climate`     | `readings[sensor=*].value`   | `sensor` graph, `zone` line | A temperature and a humidity graph, each with a line per zone |
| Vibration               | `plant/{line}/telemetry/vibration` | `rms`                        | `axis` line, `machine` graph | Three lines per machine            |
| Climate, one topic per sensor | `building/hvac/{sensor}/climate` | `readings[sensor={sensor}].value` | `sensor` line   | The topic chooses the reading      |
| Fleet                   | `devices/{device}/heartbeat`       | `battery`                    | `device` line         | New devices appear by themselves   |
| Gateway batch           | `devices/{gateway}/batch`          | `readings[*].value`          | `gateway` graph       | Each batch placed by its own `ts` (phase 2) |

## Other telemetry gaps

These are the gaps that make a telemetry display untrustworthy or hard to use, in the order they should be built.
Phase 1 is the design above, and is done. Nothing below is built.

### Phase 2: data that can be trusted

| Gap                       | Today                                      | Proposed                                                 |
|---------------------------|--------------------------------------------|----------------------------------------------------------|
| Time from the message     | Arrival time only, so a delayed batch looks live | "Time from field": ISO text, or epoch seconds or milliseconds, detected. Arrival remains the default |
| Readings out of order     | Assumed in order                           | Placed by time. A time far in the future falls back to arrival, and says so |
| Clock differences         | Not visible                                | Details shows the difference between sender and arrival  |
| Duplicates                | QoS 1 redelivery counts twice              | Skip the same series, time and value when time is from a field |
| Lost messages             | Not visible                                | With a `seq` field, count the gaps in Details            |
| Outages drawn as data     | A straight line joins both sides of an outage | Break the line when a gap is well over the usual interval |
| Connection lost           | Only in the client bar                     | A band on the graph for the time disconnected            |
| Old values look live      | [Known gap 1](#known-gaps)                 | Latest value dims and shows "12 min ago". Stale time set per graph, with a default from the usual interval |
| Spikes are lost           | Every other reading is dropped. At 24 hours, 4,000 readings is one per 21.6 s | Keep minimum, maximum, average and count per time bucket, so a spike always survives. Recent time stays raw |
| Memory per graph          | The same topic on two graphs is stored twice | One store per series, shared, so a new graph starts with what is known |
| Unbounded queue           | `ClientFeed` has no limit if messages outrun the 5,000 per tick | A limit per client, with the drop count in Details |
| Unit                      | Typed by hand                              | "Unit from field", such as `unit` in the climate message |

### Phase 3: more than plain numbers

| Gap                       | Proposed                                                                                    |
|---------------------------|---------------------------------------------------------------------------------------------|
| States                    | A state timeline for text and true or false, such as `state: running` and `status: online`  |
| Value names               | Map values to names and colours, such as `1` = Running                                      |
| Numbers as text           | Read `"21.5"` as a number, when the graph opts in                                           |
| Quality flags             | Only keep readings where a field matches, such as `quality = good`, using the same filter as Only |
| Counters                  | "Rate" and "Change per period" for counters such as `kwh`, handling a reset to zero. Suggested when a number only rises |
| Scaling                   | Multiply and offset, for raw counts or °C to °F                                             |
| Combine                   | Average, minimum, maximum or sum across key values, on shared time buckets, such as the lowest battery in a fleet |
| Statistics                | Minimum, maximum, average and latest in the legend                                          |
| Limits on a graph         | Warning and danger levels, drawn as lines, and as colour on gauges and numbers. Never by colour alone, as elsewhere in the app |

### Phase 4: history and daily use

| Gap                       | Proposed                                                                                    |
|---------------------------|---------------------------------------------------------------------------------------------|
| Keeping readings          | Optional per dashboard, in a local file of bucketed data with a size limit. Off by default  |
| Looking back              | Pause, drag to zoom, and a time range, without stopping collection                          |
| Comparing                 | A shared crosshair across a dashboard                                                       |
| Selector                  | A list at the top of a dashboard choosing a key's value, such as `line`. Better than tabs past about six values |
| Export                    | CSV of what is on a graph, and an image                                                     |
| Sharing                   | Duplicate a graph, and export or import one dashboard. Client names stay, and never credentials |
| Alerts                    | Notify after a limit is passed for a set time, with a margin to stop flapping, and on no data |
| Display                   | Full-screen mode for a wall screen, with tabs that rotate                                   |
| Other formats             | Sparkplug B first, as the industrial standard, then CBOR, MessagePack and InfluxDB line protocol. All are read as numbers plus keys, so the model does not change |

### Decisions

Confirmed on 2026-10-31:

1. **Topics take wildcards**, and a wildcard's value can choose what is read from the message, by name or level index.
2. **Grouping in a topic means a topic level used as a key.** A named set of unrelated topics is not part of this.
3. **Tabs from a key are one dashboard shown many times**, not copies.
4. **One scale per graph stays.** Different units are split into graphs rather than given a second axis.
5. **Gzipped payloads work the same as plain ones**, with nothing to set.

Still to confirm, and the choices this design makes until then:

1. **Keys belong to the graph**, and apply to each number that has them, so a graph stays one setting per key.
2. **Arrival time stays the default**, as a wrong time inside a payload is worse than a slightly late one.
3. **Readings stay in memory by default**, so nothing is stored on disk unless asked for.
4. **One graph still uses one client**, since keys now cover what several clients were needed for, except for
   comparing brokers.
