# Tapybara

[![build](https://github.com/keshon/tapybara/actions/workflows/build.yml/badge.svg)](https://github.com/keshon/tapybara/actions/workflows/build.yml)

**[tapybara.keshon.ru](https://tapybara.keshon.ru/)** · [Download](https://github.com/keshon/tapybara/releases/latest)

Local voice dictation for Windows. Press a hotkey, speak, press it again — your
words are typed into whatever application has focus.

Everything runs on your machine. No audio, no text, and no telemetry ever
leaves it, and the app works with no internet connection at all — apart from
downloading a recognition model the first time.

> **Status: early development.** Dictation works end to end and is usable
> daily. Call recording works but is newer and less exercised.

## Why another dictation tool

Windows ships with dictation built in, but it is cloud-backed, and its quality
for languages other than English is mediocre. Tapybara runs
[whisper.cpp](https://github.com/ggerganov/whisper.cpp) locally on your GPU: it
is more accurate, it is private by construction, and on a mid-range graphics
card it transcribes a minute of speech in about a second.

## Features

- **Global hotkey** — start and stop dictation from any application.
- **Direct insertion** — text is pasted into the focused field, not just copied.
- **Floating indicator** — a pill above all windows showing a live microphone
  level, elapsed time and recognition progress. Drag it wherever you want it.
- **Runs in the tray** — no window, no taskbar entry, optional start with Windows.
- **Model downloads built in** — pick one in Settings › Models and it lands in
  the right folder.
- **Warm model** — loaded on demand, unloaded after idle, so an idle app costs
  no memory.
- **Clipboard hygiene** — inserted text does not pollute the Windows clipboard
  history (`Win`+`V`).
- **Speech detection** — a second, tiny model finds where speech actually is,
  which fixes timings, removes silence hallucinations and speeds things up.
- **Paragraphs from pauses** — Whisper does not mark paragraphs, but its
  segments carry timestamps, and a pause is a reliable signal that the speaker
  moved on. Optional, with a configurable threshold.
- **Escape cancels** — changed your mind mid-sentence? The audio is discarded.
  Escape is watched, not taken: other applications still receive it.
- **Recent dictations** — the last ten are one click away in the tray.
- **Call recording** — microphone and system audio as two separate channels,
  transcribed into one timeline with speaker labels.
- **Portable mode** — keep settings and models next to the executable.
- **English and Russian** interface, following the system language by default.
  Light and dark themes, following Windows by default.

## Requirements

| | |
|---|---|
| OS | Windows 10 (2004 or newer) or Windows 11, 64-bit |
| Runtime | .NET 10 |
| GPU | Optional but strongly recommended — any Vulkan-capable NVIDIA, AMD or Intel GPU |
| Disk | ~600 MB for the recommended model |

**No CUDA Toolkit is required.** The Vulkan backend ships inside your graphics
driver, so there is nothing extra to install.

## Getting started

On Windows, `build.cmd` does the whole thing:

```bash
build.cmd run
```

It stops a running instance first — politely, then forcibly if it does not
respond — which matters because the app lives in the tray and is easy to forget
about while it holds the output files locked.

Other modes: `build` (compile only), `debug`, `test`, `publish`
(self-contained build into `dist\`, no .NET needed on the target machine),
`stop`.

Or build it by hand:

```bash
dotnet build -c Release
```

A dot appears in the system tray. **Open Settings › Models and download a
model** — nothing works until you do. `Large v3 Turbo (q5_0)` is the
recommended starting point at about 550 MB; while you are there, the speech
detector is under a megabyte and worth taking.

Then put the caret in any text field, press `Ctrl`+`Alt`+`D`, speak, and press
it again. The text is inserted for you.

## Models

Two models are involved, and they do different jobs.

**Recognition** turns audio into text. Any `ggml` Whisper model works; the
Models page offers the useful ones and downloads them from
[ggerganov/whisper.cpp on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp).

| Model | Size | Notes |
|---|---|---|
| `large-v3-turbo-q5_0` | 550 MB | The recommended default |
| `large-v3-turbo-q8_0` | 870 MB | Marginally better, noticeably larger |
| `medium-q5_0`, `small-q5_1` | 540 / 190 MB | For machines without a usable GPU |
| Language-specific fine-tunes | varies | Often better punctuation and vocabulary for their language |

A practical finding: **fine-tuned models tend to punctuate without being asked**,
while the stock `large-v3-turbo` produces unpunctuated lowercase text unless you
give it a prompt. See [Prompts](#prompts).

Quantized (`q5`, `q8`) and full-precision (`f16`) variants run at the same speed
on a GPU, so choose by quality and disk space rather than performance.

**Speech detection** is a separate, much smaller model — Silero VAD, under a
megabyte — that finds which parts of a recording contain speech. It is optional
but recommended: without it Whisper predicts timings along with the text, and on
a recording that is mostly silence those predictions drift badly.

If you would rather fetch models yourself, drop them in
`%APPDATA%\Tapybara\models\` — the folder is watched, so they appear in the
list and in the tray menu straight away, without a restart. Models you no
longer want can be deleted from the same page; the one currently loaded is
unloaded first.

## Performance

Measured on an RTX 3060 (12 GB) and an i5-9600KF, transcribing a 28-second
sample with `large-v3-turbo`:

| Backend | Time | Speed |
|---|---|---|
| CPU | 26.2 s | 1.1× realtime |
| Vulkan, first run | 12.6 s | 2.2× realtime |
| Vulkan, warmed up | **0.52 s** | **53× realtime** |

The first run is slow because Vulkan compiles its compute shaders on first use.
Tapybara hides this by warming the engine up at startup, so your first real
dictation is already fast.

Which backend actually loaded is shown in Settings › About. The library falls
back silently from GPU to CPU when a backend fails to load, and a fiftyfold
speed difference is otherwise inexplicable.

## Configuration

Settings live in `%APPDATA%\Tapybara\settings.json` and are written
atomically, but you should not need to touch the file — everything is in the
settings window. A log sits next to it in `logs\`.

The settings window is organised by the question you arrived with:

| Section | What is there |
|---|---|
| **General** | Interface language, theme, start with Windows, portable mode, privacy |
| **Dictation** | Hotkey, microphone, where the text goes, maximum length |
| **Recognition** | Which model, language, prompt, speech detection, idle unload |
| **Calls** | Output device to record, folder, limits, transcript labels |
| **Models** | Where models live, what is installed, what to download, delete |
| **Text** | Paragraph splitting, replacements dictionary |
| **About** | Version, compute backend, diagnostics, log |

### Storage and portable mode

By default, settings and models live in `%APPDATA%\Tapybara\`. Creating an
empty file named **`portable.txt`** next to the executable switches the
application to a `Data` folder beside itself instead — useful on a USB stick or
when you would rather leave nothing behind in the system. The older
`Tapybara.portable` name still works. There is also a toggle in
Settings › General, which creates the same file.

Detection is deliberately file-based rather than a setting: reading a setting
would require already knowing where settings live. Either way the mode only
sets the *default* location — the models folder can be pointed anywhere, and
**Use default** returns it to whatever the current mode implies.

Switching modes does not move existing files. Changing the models folder does
offer to move them, because that is a different question with a different
answer: gigabytes you chose to relocate should come with you.

### Prompts

A prompt is not an instruction to the model — it is a piece of context that
biases decoding. Two things follow from that, both worth knowing:

- **It anchors style, not just vocabulary.** A prompt written with proper
  punctuation and capitalisation makes the model produce punctuated,
  capitalised output.
- **An irrelevant prompt actively hurts.** In our testing a prompt about
  project-management software, applied to a recording of someone counting to
  ten, made the model four times slower — it lost confidence and fell back to
  higher decoding temperatures.

Keep the prompt short and representative of what you actually dictate.

### Replacements

Speech recognition mangles product names and jargon in predictable ways. The
replacements list fixes them after the fact, matching whole words
case-insensitively, one per line:

```
хакинг фейс = Hugging Face
си шарп = C#
```

Names that end in punctuation — `C#`, `C++`, `.NET` — work as replacement
targets and as keys.

## How it works

```
hotkey ─► microphone ─► speech detection ─► Whisper ─► post-processing ─► insertion
         WASAPI          Silero VAD          whisper.cpp   filters,        clipboard
         16 kHz mono     measured timings    via Vulkan    replacements    + Ctrl+V
         float32
```

The model loads in parallel with recording, so by the time you stop speaking it
is already warm.

Recognition uses greedy sampling with **no cross-window context**. Whisper
normally feeds each 30-second window's output into the next as context; on long
dictation with pauses that sends the model into repetition loops and triggers
temperature fallback, which is dramatically slower. Disabling it is the single
most important setting in the whole pipeline.

Output passes through a deliberately conservative hallucination filter, applied
**per segment**. On silence, Whisper emits phrases it learned from YouTube
subtitles — "thanks for watching", "subscribe to the channel". Those segments
are dropped; the rest of the dictation is not. Ordinary phrases that merely
resemble them are left alone.

### Insertion

Text is inserted through the clipboard followed by a synthetic `Ctrl`+`V`. This
is the path that works everywhere, including Electron and Chromium applications
where direct accessibility-based insertion is accepted but silently ignored.

### Call recording

Two channels are captured separately: your microphone, and everything playing
on a chosen output device. That separation replaces speaker diarisation — for a
one-to-one conversation, who said what is known from which track it is on.

The far channel is padded against the near channel's own sample clock, because
WASAPI loopback delivers nothing at all while the other side is silent, and two
tracks measured against different clocks drift apart.

> **Recording captures the other party too.** In many jurisdictions recording
> someone without telling them is unlawful, and everything audible on the chosen
> device is captured — notifications, music, another meeting — not just the call.
> The app says this once, before the first recording.

## Windows-specific notes

Things that cost us time and may cost you some too:

- **Held modifiers.** Dictation is started with a chord like `Ctrl`+`Alt`+`D`,
  and the user is often still holding those keys when the paste is sent — which
  turns `Ctrl`+`V` into `Ctrl`+`Alt`+`V`. Synthesise key-up for every held
  modifier first, then wait briefly for the target application to process it.
- **A global hotkey takes the key away from everyone.** `RegisterHotKey` means
  the key stops reaching the foreground window entirely. That is fine for
  `Ctrl`+`Alt`+`D` and completely wrong for `Escape` — a low-level keyboard
  hook that passes the event on is the right tool for watching a key you do not
  own.
- **UIPI.** A normal-privilege process cannot send input to a window running
  elevated; `SendInput` returns 0. Do not swallow this — tell the user the text
  is still on the clipboard.
- **Clipboard history.** Setting the `CanIncludeInClipboardHistory` and
  `CanUploadToCloudClipboard` clipboard formats to 0 keeps dictations available
  for `Ctrl`+`V` while keeping them out of `Win`+`V`.
- **WAV headers are written last.** RIFF length fields are filled in when the
  file is closed, so any hard kill leaves an hour-long recording that nothing
  will open. Rewrite the header periodically and repair on startup.
- **Closing a console window is not `Ctrl`+`C`.** Windows sends
  `CTRL_CLOSE_EVENT`, surfaced in .NET as `SIGTERM`, and `CancelKeyPress` does
  not fire. A console tool that ignores this outlives its own window — and if it
  holds a global hotkey, nothing else can claim it.
- **DPI scaling.** `System.Windows.Forms.Screen` reports physical pixels while
  WPF positions windows in device-independent units. Convert — and ask the
  *target* monitor for its scale via `GetDpiForMonitor`, not the window's
  current one, or mixed-DPI setups misplace the window every time. Declare
  `PerMonitorV2` in the manifest, or Windows reports one scale for everything.
- **`MOD_NOREPEAT`.** Without it, holding the hotkey toggles dictation at the
  keyboard repeat rate.

## Releases

Every push to `main` builds and tests on Windows and leaves a ready-to-run
`Tapybara-<sha>-win-x64.zip` as a workflow artifact. Pushing a tag of the form
`v1.2.3` does the same and publishes it as a GitHub Release, with the version
baked into the executable.

```bash
git tag v0.2.0 && git push origin v0.2.0
```

The build is self-contained, so the target machine needs no .NET. Models are
not included — the app downloads them itself.

## Development

```
src/Tapybara.Core/        engine, audio, settings, Win32 plumbing — no UI
src/Tapybara.App/         WPF application: tray, overlay, settings
tests/Tapybara.Core.Tests/ unit tests for the pure logic in Core
tools/Tapybara.Bench/     console harness for benchmarking and diagnostics
```

All logic lives in `Core`, which has no dependency on the user interface. That
is what lets the whole pipeline be exercised from the console, and it is how the
benchmarks above were produced.

`Core` also owns no user-facing strings: it reports typed values, and `App`
decides the wording. The interface is translated through a record with
`required` properties, so a missing translation is a compile error rather than a
blank label at run time.

```bash
build.cmd test
```

During development, models are also picked up from a `models/` directory in the
repository root, so there is no need to copy gigabytes into `%APPDATA%` after
every build.

The benchmark harness:

```bash
dotnet run --project tools/Tapybara.Bench -- models
```

```bash
dotnet run --project tools/Tapybara.Bench -- rec 30
```

```bash
dotnet run --project tools/Tapybara.Bench -- run large-v3-turbo
```

```bash
dotnet run --project tools/Tapybara.Bench -- check
```

`rec` records a sample, `run` transcribes it and reports timings, `check`
verifies hotkey registration and clipboard access, `devices` lists audio
devices with their IDs. Add `--verbose` to see which native backend actually
loaded.

### The CUDA backend

The default build ships the Vulkan and CPU backends. The CUDA build is 150 MB
and needs `cublas64_13.dll` from the CUDA Toolkit, which a clean machine does
not have — so on most machines it would be 150 MB that loads, fails, and hands
over to Vulkan anyway. If you have the toolkit:

```bash
build.cmd publish cuda
```

House rules — naming, concurrency contracts, what a comment is for, what is
frozen and why — are in [`docs/conventions.md`](docs/conventions.md). Read that
before a first change.

Development notes in Russian, including a walkthrough of the C# used here, are
in [`docs/csharp-notes.ru.md`](docs/csharp-notes.ru.md). Code comments are in
Russian by design.

## Roadmap

- Import and export of the replacements dictionary, so vocabularies can be
  shared. Shipping preset dictionaries is deliberately **not** planned: a
  replacement fires without understanding context, and a wrong entry in a
  preset silently corrupts text for someone who never opened the list.
- Push-to-talk in addition to toggle
- Streaming recognition so the tail latency approaches zero
- Per-application system audio capture, so a call recording contains the call
  and not the notifications
- Automatic call detection, so recording starts when a call does
- Summaries of recorded calls

## License

MIT — see [LICENSE](LICENSE).
