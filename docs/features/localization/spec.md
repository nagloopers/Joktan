# Localization

Keeps all the text the app shows in resources, so that adding a language means adding translated
files and changes no code.

| Item      | Value                     |
|-----------|---------------------------|
| Menu path | None                      |
| Status    | Implemented, English only |

## Purpose

Joktan ships in English for now. The text is ready for other languages, and nothing a person sees
changes until one is added. There is no language setting: with one language it would offer no
choice.

## Where text lives

| Text | Where |
|------|-------|
| Words in views and view models | `src/<Assembly>/Resources/Strings.resx`, one for each assembly that shows text. English is the neutral language |
| Help topics | `src/Joktan.Help/Topics/<id>.md`. A translated topic is `Topics/<language>/<id>.md` |

The build makes a `Strings` class from each file, in the `<Assembly>.Resources` namespace, so a
missing key is a build error.

- In a view: `Text="{x:Static res:Strings.PomodoroView_Reset}"`, with `xmlns:res="using:Joktan.Pomodoro.Resources"`.
- In code: `Strings.Pomodoro_Start`, or `string.Format(Strings.Pomodoro_FocusOf, done, total)` when it has values to fill in.

A key is the type or view that uses the text, then its first words. The comment on a resource says
what each `{0}` holds.

## Adding a language

1. Copy each `Strings.resx` to `Strings.<language>.resx`, such as `Strings.fr.resx`, beside it, and translate the values. Keep every `{0}` and its format.
2. Add translated help topics under `src/Joktan.Help/Topics/<language>/`. A topic with no translation is shown in English.
3. Build. Each language becomes a satellite assembly, and `dotnet publish` carries it along.

The operating system's display language chooses the language, when the app starts. What has no
translation falls back to English.

## Rules

| Rule | Why |
|------|-----|
| What is shown comes from a resource, including messages in exceptions that reach the screen | A test fails on text written directly in a view. Code has no such test, so take care there |
| Logic never compares displayed text | A translation would change what it does. Compare the state itself |
| What is saved or sent is not translated | A saved file must mean the same in every language. Menu entries are saved by `NavItemViewModel.Id`, the English title, and built-in themes by name, shown through `ThemeEditor.DisplayName` |
| An enum shown on screen goes through a `Text()` extension | Its name in code is not a translation |
| A count is written with the singular and the plural as separate resources | Rather than adding an "s" |

## Not translated

| What | Why |
|------|-----|
| The agent's tool descriptions, tool errors and server instructions | Read by a model, not a person |
| Telemetry names and descriptions | Read by monitoring systems |
| JSON keys, protocol values, file names and command lines | Other programs read them |
| Product names, technical names and example values | The same in every language |
| Certificate defaults, such as `Joktan Root CA` | They become data in the certificates that are made |

## Limits

- Numbers and dates keep following the operating system's regional format, or a fixed format where one was already used. Translation does not touch them.
- The singular and plural pair suits languages with two forms. A language with more needs more resources at those places, and some sentences are built from translated parts.
- Spoken commands are recognised in English, and the speech model is told to expect English.
- Layouts are not arranged for right-to-left languages.
