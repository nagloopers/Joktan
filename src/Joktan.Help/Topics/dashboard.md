# Dashboard

**Dashboard / Overview** shows the state of the machine Joktan is running on. It is the page shown
when the app starts. To draw values from MQTT topics, use [MQTT Dashboards](help:topic-dashboards).

## Reading the page

Readings are taken every 2 seconds, whether or not the page is open, so coming back to it shows
current values and the trend so far.

- **CPU usage**: the share of CPU time in use across all cores.
- **Memory used**: the share of physical memory in use, and how much that is.
- **Disk used**: the share of the system drive in use.
- **Uptime**: how long the machine has been running, and when it started.

The bar on a tile turns amber from 75% and red from 90%. CPU and memory also draw their last minute
along the bottom of the tile.

Below the tiles, **System** lists facts about the machine, and **OpenTelemetry metrics** lists what
Joktan exports, with the latest values and where they are sent.

A value that shows "–" could not be read on this platform. CPU usage also shows it for the first 2
seconds, because it is measured between two readings.

## Configuring it

Nothing on the page can be set: it has no controls, and the interval and the length of the trend are
fixed.

Where the metrics are exported to is set with environment variables when Joktan starts. See
[Telemetry](help:settings-and-data).
