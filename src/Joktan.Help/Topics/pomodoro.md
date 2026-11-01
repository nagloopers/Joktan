# Pomodoro

A timer for periods of focus with breaks between them: by default 25 minutes of focus and a 5 minute
break, with a 15 minute break after every fourth focus period.

## Using the timer

- **Start** begins the phase shown. **Pause** stops the clock, and **Resume** carries on.
- **Reset** stops the timer and goes back to the start of this phase.
- **Skip** moves on to the next phase. A focus period that is skipped is not counted, and is followed by a short break.

While a phase is under way, the time left also shows in the top bar, whichever page you are on. Click it to
come back here. The page also counts the focus periods finished today.

## When a phase ends

Joktan tells you in two ways, each with its own switch on this page:

- **Add an alert** puts it under the bell in the top bar. See [Alerts and messages](help:alerts-and-messages). It stays on
  this computer, and needs no connection to a server.
- **Say it aloud** reads it with your computer's own voice, so you hear it while Joktan is behind other windows.
  On Linux this needs `espeak-ng`, `espeak` or `spd-say`.

The next phase waits for you to press **Start**, unless **Start the next phase by itself** is switched on.

## Settings

The lengths of focus and of each break, and how many focus periods come before a long break, are set on this page. A new
length applies straight away to a phase that has not begun. A phase under way keeps the length it began with.

## Good to know

- The timer keeps going while you use other pages.
- It stops when Joktan closes, and starts from the first focus period next time. The count for today is kept.
- If the computer sleeps through the end of a phase, the phase ends when it wakes.
- The [agent](help:agent) can read and control the timer, and change these settings.
