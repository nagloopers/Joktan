# Pomodoro

A timer for periods of focus with breaks between them.

| Item      | Value       |
|-----------|-------------|
| Menu path | Pomodoro    |
| Status    | Implemented |

## Purpose

Work in timed periods from the same app as the tools, without another program to keep open.

## Page layout

1. Page header: title "Pomodoro", subtitle "Periods of focus with breaks between them".
2. Timer card: the phase ("Focus 2 of 4", "Short break", "Long break"), the time left, a progress
   bar, **Start** (**Pause** while running, **Resume** when paused), **Reset** and **Skip**, what
   happened when the last phase ended, and the count for today.
3. Settings card: the settings below, saved as they change.

## Phases

Each focus period is followed by a short break, except that the last of a cycle is followed by a
long break, after which the cycle begins again.

| Button | What it does |
|--------|--------------|
| Start, Pause, Resume | Starts or pauses the phase shown |
| Reset  | Stops, and goes back to the start of this phase. The cycle stays where it is |
| Skip   | Goes to the next phase without counting this one: a short break from a focus period, otherwise a focus period. Skipping a long break begins a new cycle |

The time left is worked out from when the phase ends rather than by counting ticks, so it stays
right when ticks come late or the computer sleeps. A phase slept through ends once, on waking.
The time shown is rounded up to a whole second.

## Settings

| Setting | Default | Range |
|---------|---------|-------|
| Focus, in minutes | 25 | 1 to 180 |
| Short break, in minutes | 5 | 1 to 180 |
| Long break, in minutes | 15 | 1 to 180 |
| Focus periods before a long break | 4 | 1 to 12 |
| Start the next phase by itself | Off | |
| Add an alert when a phase ends | On | |
| Say it aloud when a phase ends | On | |

A new length applies at once to a phase that has not begun; a phase under way keeps its length.
Values outside the range, such as in an edited file, are brought into it. Reset and Skip do not
announce anything; only a phase that runs out does.

## Top bar

While a phase is under way, running or paused, a button in the top bar shows the time left and
opens this page.

## When a phase ends

- An Info alert titled "Pomodoro" goes into the top bar's alerts, through
  `InboxViewModel.AddLocalAlert`. It is not published, and needs no connection.
- The message is read aloud through `SpeechOutput` with the system's default voice and speed,
  whatever the agent's speech settings are. The module takes both as callbacks from the app, so it
  depends on neither the MQTT nor the agent assembly.

## Agent

`pomodoro_status` reads the timer, `pomodoro_control` starts, pauses, resets or skips, and
`pomodoro_settings` changes the settings. The last two carry the Change risk.

## Files

`pomodoro.json` in the data folder holds the settings and the count for the day, and is backed up
with the app settings. An unreadable file leaves the defaults. The timer itself is not kept: after
a restart it is at the start of the first focus period.

## Not built yet

- A history beyond today
- Sound other than speech
- Operating system notifications
