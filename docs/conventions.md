# Conventions

These are the house rules. Most are enforced by tooling — `TreatWarningsAsErrors`
with `EnforceCodeStyleInBuild` means the analyzers and `.editorconfig` are part
of the build, not a suggestion — and the rest by review. If a rule gets in the
way of something that genuinely needs doing, pragmatism wins; just leave a
comment saying why this is the exception.

The language walkthrough lives in [`csharp-notes.ru.md`](csharp-notes.ru.md).
This file is about the rules, not the language.

## Design principles

`Tapybara.Core` never references a UI type. No `System.Windows`, no
`System.Drawing`, no WPF, no WinForms — the project file doesn't even set
`UseWPF`. The bench harness is proof this holds: the whole pipeline runs from
a console with no window in sight, and that is how the numbers in the README
were produced. Anything that knows about windows belongs in `Tapybara.App`.

**Core owns no user-facing strings either.** It reports typed values —
`DictationNotice`, `CallTranscriptionStage`, `CallStopReason`, `ModelTier` —
and the app decides the wording. This isn't tidiness: while Core handed out
Russian text directly, an English user got "Диктовка не удалась" in the tray of
a fully English interface, and the localisation mechanism could not have caught
it. If Core needs to say something new, it grows an enum value, not a sentence.

The same applies to file content it writes for a human. `CallTranscriber` takes
a `CallTranscriptLabels` record from the caller rather than embedding headings.

No frameworks, no DI container, no MVVM library. `App.xaml.cs` is the
composition root and wires everything by hand; there is not enough of it to
need machinery. Settings cards are built in code rather than marked up in XAML
because there are forty of them and one builder is honester than forty
copy-pasted blocks with drifting margins.

An interface or abstract base exists when there are two real implementations or
a real test seam. `AudioCapture` earns it — `MicrophoneCapture` and
`SystemAudioCapture` differ by exactly one method — and almost nothing else
does. Concrete classes are the default.

Options objects are `sealed record` with `init` properties, assembled once and
never mutated: nobody changes the language on a running engine.

## Naming

`AppSettings` is *the* settings type. Please don't add a second settings-shaped
record; the reason there is exactly one is that there used to be two — the app's
field and the settings window's snapshot — and they silently overwrote each
other's changes.

`TranscriptSegment` is the only segment type. `InstalledModel` is what is on
disk, `CatalogModel` is what can be downloaded; those are genuinely different
things and the names say so.

Private fields are `_camelCase`, enforced as a build warning by
`.editorconfig`. Namespaces are file-scoped. `var` only when the type is
apparent from the right-hand side — `var capture = new MicrophoneCapture()`
yes, `var x = Resolve()` no. Braces always, even on one-line `if`s.

Async methods end in `Async` and take a `CancellationToken` last when they can
be cancelled at all. A method that returns `Task` and cannot be cancelled
should say so in its doc rather than accept a token it ignores.

Strings that select behaviour get an enum: `AppTheme`, `ModelKind`,
`DictationState`, `ConfirmChoice`. A raw string literal steering a `switch` is
worth flagging in review. The exceptions are values that belong to something
outside this codebase — Whisper language codes, WASAPI device ids — which stay
strings because that is what they are.

## Frozen identifiers

Some names are written to disk on a user's machine and cannot be renamed later.

**`AppSettings` property names.** They are the keys in `settings.json`.
Renaming one does not migrate it — `System.Text.Json` simply doesn't find the
old key and the property silently takes its default. Someone's carefully tuned
detector threshold resets to 0.35 and nothing tells them. Add properties, don't
rename them; if a rename is truly needed, read the old key too for a few
releases.

**Data files next to the settings.** `settings.json`, `dictations.jsonl`
(one dictation per line) and `voices.json` (the voice book). They travel with
portable mode because they live in the same folder; renaming one strands the
user's history or forgets every voice they named.

**Call folder layout.** `mic.wav`, `system.wav`, `meta.json`, `transcript.json`, `transcript.md`,
declared as constants on `CallSession`. They are English and independent of the
interface language on purpose: they are read by code as well as by people, and
renaming them would break recordings already sitting on disk.

**Model file names.** `ggml-*.bin` is what the app globs for, and the substring
`silero` is the entire mechanism separating speech detectors from recognition
models — they live in one folder and are named alike otherwise. `ModelStorage.IsSpeechDetector`
is the single place that decision is made; a test asserts every catalog entry
matches its own kind.

**Portable markers.** `portable.txt` is what the app creates;
`Tapybara.portable` is still recognised because people already have it. New
names may be added to `AppPaths.PortableMarkerNames`; existing ones stay.

**Kernel object names.** `Local\Tapybara.SingleInstance` and
`Local\Tapybara.Wake`. Rename either and a new build stops seeing an old
running instance — two copies then fight over the global hotkey, and the loser
reports that the hotkey is taken by "another application" which is itself.

## Concurrency contracts

**The WASAPI callback thread is not yours.** `AudioCapture` raises
`SamplesAvailable` and `LevelChanged` on it. Handlers must return promptly and
must not throw: an exception escaping into that thread kills it silently, and
the only symptom is a recording that stops growing. `AudioCapture.Publish`
wraps every handler call for exactly this reason. Disk writes from that thread
go a whole chunk at a time through `TimelineWavWriter`, never a call per sample.

`DrainPipeline` is serialised behind its own lock with a re-entrancy flag,
because it is reached both from the audio callback and from `StopAsync`'s
timeout path, and it shares one scratch array and one resampler — neither of
which is thread-safe.

**A low-level keyboard hook is on a stricter clock still.** `KeyWatcher`'s
callback compares a virtual-key code and raises an event, and that is all it is
allowed to do. Windows silently unhooks a hook that exceeds
`LowLevelHooksTimeout`.

**Events from Core arrive on background threads.** `App` is the only place
they cross into the UI, through `OnUi`. Core never touches a `Dispatcher`.

`SemaphoreSlim` where the critical section contains an `await`;
`System.Threading.Lock` where it doesn't. `_rebuildGate` makes engine rebuilds
single-flight — without it two quick settings changes raced, the second
overwrote `_engine`, and the first was left loaded and unreachable with a
gigabyte of model behind it.

**`ConfigureAwait(false)` in Core, and in anything the UI thread might block
on.** `DisposeEngineAndControllerAsync` awaited with the context captured while
`Dispose` blocked the dispatcher waiting for it: the continuations queued onto
the very thread that was waiting. Exiting mid-dictation hung for the full
timeout and then abandoned the whisper context.

Fire-and-forget (`_ = Task.Run(...)`) catches everything inside itself. There is
a `TaskScheduler.UnobservedTaskException` handler, but it is a net, not a plan.

A `CancellationTokenSource` field is cleared with `Volatile.Write` *before* it
is disposed, and read into a local before use — otherwise cancellation and
natural completion race for a window in which `Cancel()` lands on a disposed
object.

## Errors and logging

`AppLog` never throws. If it cannot write, it stops trying rather than failing
while reporting a failure. Messages are Russian, like the comments.

Catch specific exception types with a `when` filter — `catch (Exception ex)
when (ex is IOException or UnauthorizedAccessException)`. A bare `catch` hides
the bugs you want and swallows the cancellation you don't.

**Never let an exception reach unmanaged code.** WASAPI callbacks, the keyboard
hook, and anything invoked from a native library: an exception crossing that
boundary takes the process, and it takes it without a stack you can read.

Where failure is expected rather than exceptional, return the reason instead of
throwing. `ModelStorage.Delete` returns `string?`; `SettingsStore.Save` and
`AutoStart.SetEnabled` return `bool`. A file held open by the engine is not an
exceptional condition, it is Tuesday, and the caller needs to say something
calm about it.

Anything reaching the user goes through `UiStrings`. Anything reaching the log
does not need to.

## Comments

Comments are written in Russian, wrap at 80 columns, and are XML doc comments
(`<summary>`, `<remarks>`) on anything public.

A comment earns its place by saying something the code cannot. The code already
says what it does; a restatement is a second thing to keep in sync and it sits
in the way of the comment that matters. A `<summary>` that expands the
identifier back into a sentence — `/// <summary>Загружает модель.</summary>` on
`LoadAsync` — is the shape to avoid.

The comments worth writing answer a question the code raises but cannot settle.
Why `WithNoContext` is the single most important line in the whole pipeline.
Why `ReadFully = false` on the buffered provider. Why the far channel is
aligned against the near channel's sample count and not a stopwatch. Whoever
asks those next — a maintainer months from now, or an agent told to "clean this
up" — cannot recover the answer from the code, and will helpfully undo it.

Four things make such a comment hold up:

**Say whether it was measured or assumed.** "На RTX 3060 первый прогон стоил
8–12 секунд против 0,55 с у последующих" tells a reader which claims they may
reason from and which to re-check. A guess is fine to write down — label it.

**Name the failure it prevents.** "61 МБ в куче больших объектов НА КАЖДОЕ
нажатие хоткея" turns an arbitrary-looking constant into something nobody
deletes by accident. A rule with no consequence attached reads as a preference.

**Say what not to do.** `Клиппинг обязателен`, `Устройство держим в поле, а не
в using`, `Не $args: это автоматическая переменная PowerShell`. A deliberate
non-obvious choice needs a fence around it or it gets optimised away. This is
the highest-value kind of comment here and the one likeliest to be violated in
its absence.

**Point at the next hop by name.** `см. SetPortable`, `см. TimelineWavWriter`.
A reader who needs more should be told where, in a form that greps.

Long explanations go in one `<remarks>` block above the declaration they
explain — `AudioCapture`, `TimelineWavWriter`, `BleedFilter` and `KeyWatcher`
are the pattern — rather than sprinkled line by line through the body. Fields
carrying a concurrency contract say who owns them and what lock covers them.

On mechanics: `<summary>` on every public member; `<remarks>` for the *why*;
`<param>` and `<returns>` only when they add something. An override that
implements a documented base member inherits its doc and should not repeat it.

Three things go stale silently, so they don't get written at all: file-path
headers, which nothing checks and which outlive a rename; a claim about what
does not exist yet ("call recording is designed but not implemented"), which
still reads as fact long after it stopped being one — describe what the design
allows instead; and any restatement of a constant's value, which the constant
already carries. A comment that has drifted is worse than no comment, because
it is believed.

## Adding things

**A user-visible string:** add a `required` property to `UiStrings` and a value
in both language blocks. The compiler enforces the pair — that is the whole
reason it is a record with `required` members rather than a dictionary or a
`.resx`.

**A setting:** add an `init` property to `AppSettings` with a `<summary>`. If
it is computed from other properties, mark it `[JsonIgnore]` — otherwise it is
written into `settings.json` looking like a real knob that silently does
nothing when edited. If the engine must be rebuilt when it changes, add it to
`SettingsChange.AffectsEngine`; a setting that is saved, displayed and ignored
until restart is worse than one that isn't there. Then add a card to the right
section of `SettingsWindow` with a `Refresh(...)` registration so it re-reads
when something else changes it.

**A message from Core to the user:** add a value to the relevant enum
(`DictationNotice`, `CallStopReason`, `CallTranscriptionStage`), a `required`
string, and a case in the matching `UiStrings.Describe`. Do not return text
from Core.

**A downloadable model:** add a `CatalogModel` to `ModelCatalog.All`. Verify
the URL against the repository listing rather than constructing it by pattern —
the tests check the scheme and that the URL ends in the file name, but nothing
can check that the file exists.

**A recognition language:** add it to `WhisperLanguages.All`. The name stays
English, because that is how the model and every whisper document refer to it.

**An audio source:** derive from `AudioCapture` and implement `CreateDevice`
and `Flow`. Everything else — mono downmix, resampling, level metering,
failure reporting — is already there.

**A settings section:** `AddSection` in `BuildEverything`, with its own icon.
Icons are not shared between sections: they are all the same colour, so the
glyph is the only thing that tells them apart.

**Anything visual:** sizes, spacing and radii come from `Tokens`
(`src/Tapybara.App/Tokens.cs`); text styles, cards and links from
`Resources/Controls.xaml`, or from `Ui` when the element is built in code.
A bare `FontSize = 12.5`, `Opacity = 0.65` or `Margin = new Thickness(14, …)`
is how the windows drifted apart in the first place — every page had its own
idea of a caption. Secondary text uses `TextFillColorSecondaryBrush`, not
opacity, so it follows the theme.

Colour carries meaning, and only a few things have one: accent marks what is
selected or clickable, red means recording, the voice palette tells speakers
apart. Everything else is the theme's neutral text and fill brushes. No
per-section tints, no yellow warning bars for things that are not a problem.

Dates, times, sizes and durations go through `UiStrings` (`Date`, `Time`,
`Size`, `Duration`), which format in the interface language rather than the
system's: an English interface on a Russian Windows otherwise shows
"23 сентября" and "1,6 GB".

## Testing & verification

`build.cmd test`, or `dotnet test tests/Tapybara.Core.Tests`. The suite covers
Core's pure logic: post-processing, the bleed filter, threshold calibration,
track alignment, model storage, settings round-trips. It runs in about a tenth
of a second and there is no excuse for not running it.

Prefer real temporary directories over mocks for anything touching files. The
storage and repair tests create a folder, write real WAVs, corrupt them and
check the repair; that is both simpler than a filesystem abstraction and
actually exercises what ships.

**Never write a test that depends on the developer's machine.**
`ListVadModels_IsEmptyWhenThereIsNoDirectory` passed for exactly as long as it
took someone to download a detector model into `%APPDATA%`, because a
non-existent override path falls back to the default folder by design. Use a
directory the test created.

**Verify assumptions about native libraries with a test, not with
documentation.** Periodic header rewriting only works because NAudio's
`WaveFileWriter.Flush()` rewrites the RIFF sizes, so there is a test that reads
a WAV while it is still open for writing. Similarly, "the WAV header is 44
bytes" was wrong — NAudio writes an 18-byte `fmt` chunk — and `CallRepair`
walks the chunk list instead of trusting an offset.

Test the entry point, not only the helpers underneath it. `Publish` is what
actually decides whether a dictation survives; testing `IsHallucination` alone
would not have caught the filter being applied to the whole transcript.

A regression test has to be watched failing before it is trusted. Reintroduce
the bug, confirm the test goes red, then take it back out.

The UI has no automated tests. It is verified by running the app: dictate into
a real application, cancel with Escape and confirm the target still receives
it, record a short call, switch models, change the models folder, and check the
tray in both light and dark taskbars. Do this before tagging.

## Formatting & CI

CRLF, UTF-8, final newline, four spaces — `.editorconfig` has the rest and the
build enforces it. Zero warnings is the bar; `TreatWarningsAsErrors` makes that
the only possible bar, which is the point.

CI (`.github/workflows/build.yml`) restores, builds, tests and packages a
self-contained `win-x64` zip on every push. Everything runs on
`windows-latest`, because WPF, WASAPI and Win32 interop cannot be built
anywhere else — that is not an inconvenience to work around.

The CUDA backend is opt-in (`build.cmd publish cuda`). It weighs 150 MB and
needs `cublas` from the CUDA Toolkit, which an ordinary machine does not have:
for almost everyone it would be 150 MB that loads, fails to resolve a
dependency and hands over to Vulkan anyway.

`README.md` is hand-written and describes what the code does today. When
behaviour changes, it changes in the same commit.

## Release notes

Release notes are read by people deciding whether to run this, not by whoever
fixed the bug. Lead with what changed *for them*, in plain language — "models
download from inside the app now, and Escape no longer stops working in other
programs while you dictate" — and say whether upgrading costs them anything: a
settings migration, a new download, a changed default. One short paragraph of
context is plenty.

Root-cause detail does not go on the release page at all. It lives in the
commit messages, where the next maintainer actually looks. Whisper decoding
flags, WASAPI callback threading and internal type names mean nothing to
someone choosing a dictation tool, and a release page carrying them reads as a
changelog for the author rather than news for the reader. Commit messages are
the opposite case and stay as technical as they need to be — which is what
makes leaving the detail out here cost nothing.

The workflow publishes a release for any tag matching `v*`. If the tag is
annotated, its message body becomes the release notes; otherwise GitHub
generates them from commits, which is a fallback and not the intent.

```bash
git tag -a --cleanup=verbatim v0.3.0 -F notes.md
git push origin v0.3.0
```

```markdown
Tapybara 0.3.0

Models download from inside the app now, and Escape no longer stops working
in other programs while you dictate. Nothing to migrate — existing settings
are read as they are.
```

The first line above is the subject and will not appear in the release.

`--cleanup=verbatim` is not optional. Git's default cleanup strips every line
beginning with `#` as a comment, which silently eats Markdown headings and
leaves a published release with its structure missing. Measured on this repo:
the same tag file produced a body with the heading and one without it,
depending only on that flag.

The second sharp edge is that `%(contents:body)` is the *body*, and Git decides
where that starts — everything up to the first blank line is the subject and is
dropped. Not just the first line: also measured. So the notes file opens with a
throwaway subject line, a blank line, and then the content that matters.
