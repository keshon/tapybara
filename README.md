# TapRecorder

Local voice dictation for Windows. Press a hotkey, speak, press it again — your
words are typed into whatever application has focus.

Everything runs on your machine. No audio, no text, and no telemetry ever
leaves it, and the app works with no internet connection at all.

> **Status: early development.** Dictation works end to end and is usable
> daily. Call recording is designed but not implemented yet.

## Why another dictation tool

Windows ships with dictation built in, but it is cloud-backed, and its quality
for languages other than English is mediocre. TapRecorder runs
[whisper.cpp](https://github.com/ggerganov/whisper.cpp) locally on your GPU: it
is more accurate, it is private by construction, and on a mid-range graphics
card it transcribes a minute of speech in about a second.

## Features

- **Global hotkey** — start and stop dictation from any application.
- **Direct insertion** — text is pasted into the focused field, not just copied.
- **Floating indicator** — a pill above all windows showing a live microphone
  level, elapsed time and recognition progress.
- **Runs in the tray** — no window, no taskbar entry, optional start with Windows.
- **Warm model** — loaded on demand, unloaded after idle, so an idle app costs
  no memory.
- **Clipboard hygiene** — inserted text does not pollute the Windows clipboard
  history (`Win`+`V`).
- **Switchable models** — pick any ggml Whisper model from the tray menu.
- **Paragraphs from pauses** — Whisper does not mark paragraphs, but its
  segments carry timestamps, and a pause is a reliable signal that the speaker
  moved on. Optional, with a configurable threshold.
- **Escape cancels** — changed your mind mid-sentence? The audio is discarded.
- **Settings window** — model, hotkey capture, prompt, replacements, storage.
- **Portable mode** — keep settings and models next to the executable.
- **English and Russian** interface, following the system language by default.

## Requirements

| | |
|---|---|
| OS | Windows 10 (2004 or newer) or Windows 11, 64-bit |
| Runtime | .NET 10 |
| GPU | Optional but strongly recommended — any Vulkan-capable NVIDIA, AMD or Intel GPU |
| Disk | ~1 GB for a recognition model |

**No CUDA Toolkit is required.** The Vulkan backend ships inside your graphics
driver, so there is nothing extra to install.

## Getting started

On Windows, `build.cmd` does the whole thing:

```bash
build.cmd run
```

It stops a running instance first, which matters because the app lives in the
tray and is easy to forget about while it holds the output files locked.
Other modes: `build` (compile only), `debug`, `publish` (self-contained build
into `dist\`, no .NET needed on the target machine), `stop`.

Or build it by hand:

```bash
dotnet build -c Release
```

Download a Whisper model in `ggml` format and place it in
`%APPDATA%\TapRecorder\models\`. Models are available from
[ggerganov/whisper.cpp on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp).
`ggml-large-v3-turbo` is a good default.

Run it:

```bash
dotnet run --project src/TapRecorder.App -c Release
```

A dot appears in the system tray. Put the caret in any text field, press
`Ctrl`+`Alt`+`D`, speak, and press it again. The text is inserted for you.

## Models

Any `ggml` Whisper model works. Two useful reference points:

| Model | Size | Notes |
|---|---|---|
| `ggml-large-v3-turbo` | 1.5 GB | The general-purpose default |
| Language-specific fine-tunes | varies | Often better punctuation and vocabulary for their language |

A practical finding: **fine-tuned models tend to punctuate without being asked**,
while the stock `large-v3-turbo` produces unpunctuated lowercase text unless you
give it a prompt. See [Prompts](#prompts).

Quantized (`q5`, `q8`) and full-precision (`f16`) variants run at the same speed
on a GPU, so choose by quality and disk space rather than performance.

## Performance

Measured on an RTX 3060 (12 GB) and an i5-9600KF, transcribing a 28-second
sample with `large-v3-turbo`:

| Backend | Time | Speed |
|---|---|---|
| CPU | 26.2 s | 1.1× realtime |
| Vulkan, first run | 12.6 s | 2.2× realtime |
| Vulkan, warmed up | **0.52 s** | **53× realtime** |

The first run is slow because Vulkan compiles its compute shaders on first use.
TapRecorder hides this by warming the engine up at startup, so your first real
dictation is already fast.

## Configuration

Settings live in `%APPDATA%\TapRecorder\settings.json` and are written
atomically. The tray menu covers the common ones; the rest are edited in the
file for now.

| Setting | Meaning |
|---|---|
| `modelFileName` | Which model to load |
| `language` | Recognition language, or `auto` to detect |
| `prompt` | Vocabulary and style hint — see below |
| `hotkey` | Modifiers and virtual key code |
| `autoPaste` | Insert automatically, or only copy to the clipboard |
| `excludeFromClipboardHistory` | Keep dictations out of `Win`+`V` |
| `idleUnloadMinutes` | Unload the model after this much inactivity |
| `replacements` | Literal text substitutions applied to the result |
| `splitParagraphsByPauses` | Start a new paragraph after a pause in speech |
| `paragraphPauseSeconds` | How long a pause has to be |
| `modelsDirectory` | Where to look for models, if not the default |
| `uiLanguage` | `en`, `ru`, or absent to follow the system |

### Storage and portable mode

By default, settings and models live in `%APPDATA%\TapRecorder\`. Creating an
empty file named `TapRecorder.portable` next to the executable switches the
application to a `Data` folder beside itself instead — useful on a USB stick or
when you would rather leave nothing behind in the system.

Detection is deliberately file-based rather than a setting: reading a setting
would require already knowing where settings live. Switching modes does not
move existing files, because models are measured in gigabytes and that decision
belongs to you.

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
`replacements` map fixes them after the fact, matching whole words
case-insensitively:

```json
{
  "replacements": {
    "Hacking Face": "Hugging Face"
  }
}
```

## How it works

```
hotkey ─► microphone capture ─► Whisper ─► post-processing ─► insertion
         WASAPI, 16 kHz mono    whisper.cpp   filters,          clipboard
         float32                via Vulkan    replacements      + Ctrl+V
```

The model loads in parallel with recording, so by the time you stop speaking it
is already warm.

Recognition uses greedy sampling with **no cross-window context**. Whisper
normally feeds each 30-second window's output into the next as context; on long
dictation with pauses that sends the model into repetition loops and triggers
temperature fallback, which is dramatically slower. Disabling it is the single
most important setting in the whole pipeline.

Output passes through a deliberately conservative hallucination filter. On
silence, Whisper emits phrases it learned from YouTube subtitles — "thanks for
watching", "subscribe to the channel". Those are dropped. Ordinary phrases that
merely resemble them are not.

### Insertion

Text is inserted through the clipboard followed by a synthetic `Ctrl`+`V`. This
is the path that works everywhere, including Electron and Chromium applications
where direct accessibility-based insertion is accepted but silently ignored.

## Windows-specific notes

Things that cost us time and may cost you some too:

- **Held modifiers.** Dictation is started with a chord like `Ctrl`+`Alt`+`D`,
  and the user is often still holding those keys when the paste is sent — which
  turns `Ctrl`+`V` into `Ctrl`+`Alt`+`V`. Synthesise key-up for every held
  modifier first, then wait briefly for the target application to process it.
- **UIPI.** A normal-privilege process cannot send input to a window running
  elevated; `SendInput` returns 0. Do not swallow this — tell the user the text
  is still on the clipboard.
- **Clipboard history.** Setting the `CanIncludeInClipboardHistory` and
  `CanUploadToCloudClipboard` clipboard formats to 0 keeps dictations available
  for `Ctrl`+`V` while keeping them out of `Win`+`V`.
- **Closing a console window is not `Ctrl`+`C`.** Windows sends
  `CTRL_CLOSE_EVENT`, surfaced in .NET as `SIGTERM`, and `CancelKeyPress` does
  not fire. A console tool that ignores this outlives its own window — and if it
  holds a global hotkey, nothing else can claim it.
- **DPI scaling.** `System.Windows.Forms.Screen` reports physical pixels while
  WPF positions windows in device-independent units. Convert, and make sure the
  window has a handle before you ask for its DPI, or you will silently get a
  scale factor of 1 and misplace the window on every non-100% display.
- **`MOD_NOREPEAT`.** Without it, holding the hotkey toggles dictation at the
  keyboard repeat rate.

## Development

```
src/TapRecorder.Core/     engine, audio, settings, Win32 plumbing — no UI
src/TapRecorder.App/      WPF application: tray, overlay
tools/TapRecorder.Bench/  console harness for benchmarking and diagnostics
```

All logic lives in `Core`, which has no dependency on the user interface. That
is what lets the whole pipeline be exercised from the console, and it is how the
benchmarks above were produced.

During development, models are also picked up from a `models/` directory in the
repository root, so there is no need to copy gigabytes into `%APPDATA%` after
every build.

The benchmark harness:

```bash
dotnet run --project tools/TapRecorder.Bench -- models
```

```bash
dotnet run --project tools/TapRecorder.Bench -- rec 30
```

```bash
dotnet run --project tools/TapRecorder.Bench -- run large-v3-turbo
```

```bash
dotnet run --project tools/TapRecorder.Bench -- check
```

`rec` records a sample, `run` transcribes it and reports timings, `check`
verifies hotkey registration and clipboard access. Add `--verbose` to see which
native backend actually loaded — this matters, because the library falls back
silently from GPU to CPU when a backend fails to load.

Development notes in Russian, including a walkthrough of the C# used here, are
in [`docs/csharp-notes.ru.md`](docs/csharp-notes.ru.md). Code comments are in
Russian by design.

## Roadmap

- Model downloads from a Hugging Face URL
- Import and export of the replacements dictionary, so vocabularies can be
  shared. Shipping preset dictionaries is deliberately **not** planned: a
  replacement fires without understanding context, and a wrong entry in a
  preset silently corrupts text for someone who never opened the list.
- Push-to-talk in addition to toggle
- Voice activity detection to trim silence
- Streaming recognition so the tail latency approaches zero
- Dictation history
- **Call recording**: microphone and system audio captured as separate
  channels, automatic transcription with speaker labels, and summaries

## License

MIT — see [LICENSE](LICENSE).
