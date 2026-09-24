# Architecture

How the pieces fit and why they are shaped the way they are. The house rules
that govern changes to them are in [`conventions.md`](conventions.md); the
user-facing manual is [`running.md`](running.md).

## Project map

```
src/Tapybara.Core/         engine, audio, settings, Win32 plumbing — no UI
  Audio/                   WASAPI capture, resampling, levels, normalisation
  Speech/                  whisper.cpp wrapper, Silero VAD, post-processing
  Dictation/               the hotkey → record → recognise → text state machine
  Calls/                   two-channel recording, alignment, transcript assembly
  Models/                  catalogue, downloader, files on disk
  Settings/                paths, settings record, model discovery
  Windows/                 hotkey, keyboard hook, clipboard, input, autostart
  Diagnostics/             the log

src/Tapybara.App/          WPF: tray, overlay, main and settings windows — no logic
tests/Tapybara.Core.Tests/ unit tests for the pure logic in Core
tools/Tapybara.Bench/      console harness for benchmarking and diagnostics
```

`Core` has no dependency on the user interface, and the bench harness is the
proof: the whole pipeline runs from a console. `Core` also owns no user-facing
strings — it reports typed values and `App` decides the wording.

## The dictation pipeline

```
hotkey ─► microphone ─► speech detection ─► Whisper ─► post-processing ─► insertion
         WASAPI          Silero VAD          whisper.cpp   filters,        clipboard
         16 kHz mono     measured timings    via Vulkan    replacements    + Ctrl+V
         float32
```

The model loads in parallel with recording, so by the time you stop speaking it
is already warm.

### Recognition settings that matter

Greedy sampling with **no cross-window context**. Whisper normally feeds each
30-second window's output into the next as context; on long dictation with
pauses that sends the model into repetition loops and triggers temperature
fallback, which is dramatically slower. Disabling it is the single most
important setting in the whole pipeline.

The engine is warmed with a second of synthetic tone at startup. Vulkan
compiles its compute shaders on first use — on an RTX 3060 that cost 8–12
seconds against 0.55 s for later runs — and without the warm-up the first real
dictation looks like a hang. A tone rather than silence, because on silence
Whisper short-circuits to "no speech" and the decoder shaders never compile.

The model unloads after a period of inactivity, so an idle Tapybara holds no
memory. The idle check lives inside the engine rather than in the timer that
wakes it: an orphaned timer scheduled before the previous dictation would
otherwise unload the model out from under a fresh one.

### Speech detection

Silero VAD runs before Whisper and finds where speech actually is. The point is
not saving time, though it does that. It is **measured timings**: Whisper
predicts timings along with the text, and on a recording that is mostly silence
those predictions drift — a line spoken at five seconds gets stamped at zero.
Each detected region is transcribed separately and its segments shifted by the
region's measured offset. Inside a short region full of speech there is very
little left to get wrong.

If the detector finds nothing but the recording is audible, the whole thing is
transcribed anyway. The detector is trained on human speech and can miss
synthesised or heavily degraded audio; losing a channel is worse than imprecise
timings.

### Post-processing

Output passes through a deliberately conservative hallucination filter, applied
**per segment**. On silence Whisper emits phrases it learned from YouTube
subtitles — "thanks for watching", "subscribe to the channel". Those segments
are dropped; the rest of the dictation is not. Ordinary phrases that merely
resemble them are left alone, which is why "спасибо" and "всем пока" are
deliberately absent from the marker list.

Paragraphs come from the gaps between segments. Whisper does not mark
paragraphs, but its timings say where the speaker stopped.

### Insertion

Text is inserted through the clipboard followed by a synthetic `Ctrl`+`V`. This
is the path that works everywhere, including Electron and Chromium apps where
direct accessibility-based insertion is accepted but silently ignored.

## Call recording

Two channels are captured separately: the microphone, and everything playing on
a chosen output device. That separation replaces speaker diarisation — for a
one-to-one conversation, who said what is known from which track it is on.
Diarisation is only needed to tell several *remote* speakers apart.

### Keeping the tracks aligned

WASAPI loopback delivers **nothing at all** while the far side is silent. The
microphone meanwhile records continuously. Written as they arrive, a minute of
the other side's silence becomes zero seconds of their track and the whole
chronology shifts.

So the far channel is padded with silence to fill the gaps — and it is measured
against **the near channel's own sample count**, not a stopwatch. A soundcard
clock and the system clock run at slightly different rates; measuring each
channel against its own source lets them drift apart by exactly that
difference. Measuring one against the other makes the drift common to both, and
therefore invisible.

### Surviving a hard kill

RIFF length fields are written when a WAV is closed. Any exit that is not a
clean shutdown leaves an hour-long recording that nothing will open. The writer
rewrites the header every five seconds, and `CallRepair` scans the calls folder
at startup and rebuilds any header that fell behind its data — walking the
chunk list rather than assuming a 44-byte header, because NAudio writes an
18-byte `fmt` chunk.

### Bleed filtering

Without headphones, the other side's voice comes out of the speakers and back
into the microphone, and Whisper attributes it to the microphone's owner. Two
independent signals catch it, because neither catches everything:

- **Text echo** — the same phrase recognised in both channels at the same time.
- **Energy** — leaked speech is markedly quieter than one's own while the far
  channel is active. Levels are compared against each channel's own 85th
  percentile, so a quietly configured microphone is not mistaken for leakage.

The transcript records how many lines were removed and by which signal. An
over-eager filter is then visible rather than silent.

Levels come from an `EnergyEnvelope` — one value per 20 ms — rather than the
raw samples. An hour of two-channel audio is about 460 MB of float arrays held
only to compute a few dozen averages.

### Voices, names and the three files

A call folder holds three things that make up a transcript, each owned by
someone different:

- `transcript.json` — what the machine heard: every line with its channel,
  timings, text and, on the far channel, a voice letter (`A`, `B`…) in order
  of first appearance. Written by `CallTranscriber`.
- `meta.json` — what the person knows: who was on the call, the call's title,
  and which name each voice letter carries. Written by the windows, always
  through `CallMeta.Update`, which re-reads the file under a lock so that one
  writer's stale snapshot does not erase another's fields.
- `transcript.md` — a rendering of the two, rebuilt by
  `CallTranscriptRenderer` in milliseconds whenever either changes.

Names are never handed to voices by order. Diarisation can tell voices apart;
it cannot know which one is Kirill, and assigning the first listed name to the
first voice heard was a coin toss on every two-person call that the transcript
then presented as fact. A voice is named by the person, from quotes. The one
exception is a single listed participant: then everything on the far channel
is theirs by construction.

That split is what makes the operations cheap. Transcription is minutes of
Whisper. Re-splitting voices — because the person said three people were on the
call after the split looked for two — reruns only diarisation over the stored
lines, a minute of CPU. Renaming a voice is a re-render. Transcription starts
the moment a recording stops and reads the participant list from `meta.json`
only when it reaches the split, by which time the answer is usually there.

## Windows-specific notes

Things that cost us time and may cost you some too.

**Held modifiers.** Dictation is started with a chord like `Ctrl`+`Alt`+`D`,
and the user is often still holding those keys when the paste is sent — which
turns `Ctrl`+`V` into `Ctrl`+`Alt`+`V`. Synthesise key-up for every held
modifier first, then wait briefly for the target application to process it.

**A global hotkey takes the key away from everyone.** `RegisterHotKey` means
the key stops reaching the foreground window entirely. That is fine for
`Ctrl`+`Alt`+`D` and completely wrong for `Escape` — a low-level keyboard hook
that passes the event on is the right tool for watching a key you do not own.
Such a hook must also return promptly: Windows silently unhooks one that
exceeds `LowLevelHooksTimeout`.

**UIPI.** A normal-privilege process cannot send input to a window running
elevated; `SendInput` returns 0. Do not swallow this — tell the user the text
is still on the clipboard.

**Clipboard history.** Setting the `CanIncludeInClipboardHistory` and
`CanUploadToCloudClipboard` clipboard formats to 0 keeps dictations available
for `Ctrl`+`V` while keeping them out of `Win`+`V`.

**WASAPI callbacks are not yours.** Handlers run on the capture thread, must
return promptly, and must not throw — an exception escaping there kills the
thread and the only symptom is a recording that stops growing.

**An `MMDevice` must outlive the capture that uses it.** NAudio reaches back
into it on every start; disposing it right after constructing the capture turns
into a refusal to open the device.

**A corrupt model is accepted, then fails later.** `WhisperFactory.FromPath`
succeeds on a truncated file; the failure surfaces at `CreateBuilder()`, which
is after the user has already spoken. Tear the engine down on load failure
rather than accepting dictation that cannot be transcribed.

**DPI scaling.** `System.Windows.Forms.Screen` reports physical pixels while
WPF positions windows in device-independent units. Convert — and ask the
*target* monitor for its scale via `GetDpiForMonitor`, not the window's current
one, or mixed-DPI setups misplace the window every time. Declare `PerMonitorV2`
in the manifest, or Windows reports one scale for everything.

**`MOD_NOREPEAT`.** Without it, holding the hotkey toggles dictation at the
keyboard repeat rate.

**Closing a console window is not `Ctrl`+`C`.** Windows sends
`CTRL_CLOSE_EVENT`, surfaced in .NET as `SIGTERM`, and `CancelKeyPress` does
not fire. A console tool that ignores this outlives its own window — and if it
holds a global hotkey, nothing else can claim it.

## Native backends

Whisper.net tries CUDA, then Vulkan, then CPU, and falls through **silently**
when a native library fails to load. Settings › About reports which one is in
use for exactly that reason.

The default build ships Vulkan and CPU. The CUDA build is 150 MB and needs
`cublas64_13.dll` from the CUDA Toolkit, which a clean machine does not have —
so for almost everyone it would be 150 MB that loads, fails to resolve a
dependency and hands over to Vulkan anyway. If you have the toolkit:

```bash
build.cmd publish cuda
```

## The bench harness

```bash
dotnet run --project tools/Tapybara.Bench -- models
dotnet run --project tools/Tapybara.Bench -- devices
dotnet run --project tools/Tapybara.Bench -- rec 30
dotnet run --project tools/Tapybara.Bench -- run large-v3-turbo
dotnet run --project tools/Tapybara.Bench -- vad sample.wav
dotnet run --project tools/Tapybara.Bench -- call 15
dotnet run --project tools/Tapybara.Bench -- transcribe <folder>
dotnet run --project tools/Tapybara.Bench -- check
```

`rec` records a sample, `run` transcribes it and reports timings, `vad` shows
what the detector considers speech at several thresholds, `check` verifies
hotkey registration and clipboard access, `devices` lists audio devices with
their IDs. Add `--verbose` to see which native backend actually loaded.

During development, models are also picked up from a `models/` directory in the
repository root, so there is no need to copy gigabytes into `%APPDATA%` after
every build.
