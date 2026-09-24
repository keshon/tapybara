# Running Tapybara

Everything a user needs. Contributors want
[`conventions.md`](conventions.md) and [`architecture.md`](architecture.md)
instead.

## Requirements

| | |
|---|---|
| OS | Windows 10 (2004 or newer) or Windows 11, 64-bit |
| Runtime | none — release builds are self-contained |
| GPU | optional but strongly recommended: any Vulkan-capable NVIDIA, AMD or Intel |
| Disk | ~600 MB for the recommended model |

**No CUDA Toolkit is required.** The Vulkan backend ships inside your graphics
driver, so there is nothing extra to install. Which backend actually loaded is
shown in Settings › About — the library falls back silently from GPU to CPU
when a native library fails to load, and a fiftyfold difference in speed is
otherwise inexplicable.

## Install

Unzip the release anywhere and run `Tapybara.exe`. There is no installer, and
nothing is written outside the data folder.

A dot appears in the system tray. Click it to open the list of recorded calls;
right-click for the menu. **Open Settings › Models and download a model** —
nothing works until you do.

## Models

Two models are involved and they do different jobs.

**Recognition** turns audio into text. Any `ggml` Whisper model works; the
Models page offers the useful ones and fetches them from
[ggerganov/whisper.cpp on Hugging Face](https://huggingface.co/ggerganov/whisper.cpp).

| Model | Size | Notes |
|---|---|---|
| `large-v3-turbo-q5_0` | 550 MB | The recommended default |
| `large-v3-turbo-q8_0` | 870 MB | Marginally better, noticeably larger |
| `medium-q5_0`, `small-q5_1` | 540 / 190 MB | For machines without a usable GPU |
| Language-specific fine-tunes | varies | Often better punctuation and vocabulary for their language |

Quantized (`q5`, `q8`) and full-precision (`f16`) variants run at the same
speed on a GPU, so choose by quality and disk space rather than performance.

A practical finding: **fine-tuned models tend to punctuate without being
asked**, while the stock `large-v3-turbo` produces unpunctuated lowercase text
unless you give it a prompt. See [Prompts](#prompts).

**Speech detection** is a separate, much smaller model — Silero VAD, under a
megabyte — that finds which parts of a recording contain speech. It is optional
but recommended: without it Whisper predicts timings along with the text, and
on a recording that is mostly silence those predictions drift badly.

**Telling voices apart** needs two more, about 34 MB together, and only if you
record calls with more than one other person on them. One finds the moment the
speaker changes; the other turns a stretch of speech into something that can be
compared with another stretch. Both run on the CPU and neither is needed for
dictation, so the Models page keeps them in their own group and nothing prompts
you to fetch them until they would help.

Models you fetch yourself go in the models folder and appear straight away —
the folder is watched, so no restart is needed. Models you no longer want can
be deleted from the same page; the one currently loaded is unloaded first.

Downloads resume if the connection drops, and a file only takes its real name
once the length checks out, so an interrupted download can never leave a
half-written model that looks valid.

## Settings

The window is reached from the tray and organised by the question you arrived
with:

| Section | What is there |
|---|---|
| **General** | Interface language, theme, start with Windows, portable mode, privacy |
| **Dictation** | Hotkey, microphone, where the text goes, maximum length |
| **Calls** | Output device to record, folder, limits, telling voices apart, transcript labels |
| **Recognition** | Which model, decoding effort, language, prompt, speech detection, idle unload |
| **Models** | Where models live, what is installed, what to download |
| **Text** | Paragraph splitting, replacements |
| **About** | Version, compute backend, diagnostics, log |

Settings live in `%APPDATA%\Tapybara\settings.json` and are written
atomically, but you should not need to touch the file. A log sits next to it in
`logs\`, and Settings › About can open it.

### Storage and portable mode

By default, settings and models live in `%APPDATA%\Tapybara\`. Creating an
empty file named **`portable.txt`** next to the executable switches the
application to a `Data` folder beside itself instead — useful on a USB stick or
when you would rather leave nothing behind in the system. The older
`Tapybara.portable` name still works. There is also a toggle in
Settings › General, which creates the same file.

Detection is deliberately file-based rather than a setting: reading a setting
would require already knowing where settings live.

Either way the mode only sets the *default* location. The models folder can be
pointed anywhere, and **Use default** returns it to whatever the current mode
implies. Switching modes does not move existing files; changing the models
folder does offer to move them, because that is a different question with a
different answer.

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

### Speech detection

The detector is measured, not guessed. Settings › Recognition has a **Measure**
button: count from one to ten at your usual pace, and the detector runs at
several sensitivities so you can see where it starts losing speech. The
recommendation is deliberately one step softer than the strictest setting that
still keeps everything, because the sample is recorded in better conditions
than real speech.

## Recording calls

A call is recorded as two separate tracks — your microphone and your system
audio — and merged into one chronological `transcript.md` with timestamps and
names.

Start and stop a recording with `Ctrl`+`Alt`+`R` (changeable in Settings ›
Calls) or from the tray menu. While it runs, the floating indicator shows a red
ring and a ■ button that stops it; clicking the rest of the indicator does
nothing, so a stray click cannot start a dictation on top of the call.

Pick the output device the call actually plays on in Settings › Calls. If you
talk through a headset while your speakers are the system default, recording
"the default device" captures silence.

### Who was on the call

When a recording stops, a small window asks. Names are chips — the people you
talked to most recently come first — and there is a field for anyone new. It
does not take focus, because calls usually end on top of something you are
still typing into, and transcription waits for the answer.

Answering is worth the two seconds. One other person needs no voice analysis at
all: whoever spoke is settled by which track the speech landed on, and the name
goes straight into the transcript. Several people make the answer a hint, and
that hint does more for accuracy than any setting on the page — on a two-voice
check file, knowing the count put the boundary exactly where it belonged, and
the same audio without it came apart into four speakers.

Nothing is lost if you close the window: names and the note are saved however
it is closed. When the transcript is ready, clicking the notification opens
that call. **Recorded calls** — a click on the tray icon — lists everything
recorded, what state each is in, and lets you fix names or transcribe again
later.

### Telling voices apart

Only the far track is analysed. Yours belongs to the microphone owner by
construction, so asking a model to work it out would be guessing at something
already known — and leaving it out keeps the loudest, closest, most interrupting
voice out of the hardest part of the job.

It runs on the CPU at several times real time, which still means minutes on a
long call, so it happens in the background after the recording stops. Where
several people share one room and one microphone, no system of this kind is
reliable, including this one; the transcript records how the names were arrived
at rather than presenting a guess as a fact.

> **Recording captures the other party too.** In many places recording someone
> without telling them is unlawful, and everything audible on the chosen device
> is captured — notifications, music, another meeting — not only the call.
> Tapybara says this once before the first recording rather than starting
> quietly.

## Troubleshooting

**"No model found."** Settings › Models, download one. Nothing works without it.

**The hotkey does nothing.** Another application holds it. The tray menu shows
"Claim the hotkey again" when registration failed; free the combination and use
it, or pick a different one in Settings › Dictation.

**Text does not appear, but is on the clipboard.** The target window is running
as administrator and Tapybara is not. Windows blocks synthetic input across
that boundary. Run both at the same level, or paste manually.

**Recognition is very slow.** Settings › About shows which backend loaded. If
it says `Cpu`, the GPU path failed — usually an outdated graphics driver, since
the Vulkan runtime ships inside it.

**Recognition is nonsense.** Check the language in Settings › Recognition. A
language forced onto the wrong speech does not degrade the result gently; it
produces confident nonsense. `auto` is the safe setting.

**Something crashed.** The log is in `logs\` next to the settings file, and
Settings › About has both an "Open log" button and a "Copy diagnostics" button
that includes the last 30 lines.
