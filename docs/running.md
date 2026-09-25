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

Two ways, both from the
[latest release](https://github.com/keshon/tapybara/releases/latest):

- **`Tapybara-win-Setup.exe`** installs for your user, without administrator
  rights, into `%LocalAppData%\Tapybara`, with a Start menu shortcut and an
  entry in Settings › Apps for removing it. This version updates itself (see
  below).
- **`Tapybara-*-win-x64.zip`** is the portable copy: unzip it anywhere and run
  `Tapybara.exe`. Nothing is written outside the data folder, and it is
  updated by hand — download the new zip and replace the folder.

Until the builds are code-signed, Windows may say it protected your PC on the
first run; *More info › Run anyway* starts it.

### Updates

The installed version checks the GitHub releases at start and every six hours
and downloads a new version in the background — usually a delta of well under
a megabyte. It never restarts on its own: the new version installs the next
time Tapybara starts, or at once from Settings › About › **Restart now** or the
tray menu. Either refuses while a dictation or a call is being recorded or
transcribed, so an update cannot cut a recording short.

The check is a request to GitHub for the list of releases, carrying nothing
about you or your recordings. Settings › About switches it off; the **Check
now** button still works when you want it.

Settings, history, voices, models and calls live outside the program folder,
so updates and uninstalling leave them alone.

A dot appears in the system tray, and on the very first launch a short window
walks you through downloading a model, checking the microphone and dictating a
first sentence — nothing works until a model is there. Later, click the tray
icon to open the Tapybara window — dictations, calls and the dictionary;
right-click for the menu. `Tapybara.exe --welcome` opens the first-run window
again.

Every dictation is kept in Tapybara › Dictations, grouped by day and
searchable, so text that went into the wrong window can be copied again. The
history lives next to the settings as `dictations.jsonl`; Settings › General
switches it off or clears it.

The **Dictate** button on that page, and **Record a call** on the Calls page,
do the same as their hotkeys. Tapybara never pastes into its own window unless
the caret is in a text field there, such as a call note: dictate from the
button and the text lands in the list and in the clipboard.

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
dictation, so the Models page keeps each in its own group and nothing prompts
you to fetch them until they would help.

Every model on the page is one row, whatever its kind. A filled circle is the
model in use; an empty one is downloaded — click it to switch; a pale one is
not downloaded yet, with **Download** beside it. **⋯ › Delete** removes a
downloaded model; the one currently loaded is unloaded first, and another of
its kind takes over if there is one.

Models you fetch yourself go in the models folder and appear straight away in
their group, marked *your file* — the folder is watched, so no restart is
needed.

Downloads resume if the connection drops, and a file only takes its real name
once the length checks out, so an interrupted download can never leave a
half-written model that looks valid.

## Settings

The window opens from the tray or from the Tapybara window, and is organised by
the question you arrived with:

| Section | What is there |
|---|---|
| **General** | Interface language, theme, floating indicator, start with Windows, dictation history |
| **Dictation** | Hotkey, microphone, language, where the text goes, paragraphs, maximum length |
| **Calls** | Hotkey, output device to record, your name, telling voices apart, folder, limits |
| **Models** | Every model by kind: which is in use, download, delete, where they live |
| **Advanced** | Decoding effort, idle unload, speech detection, voice threshold, portable mode |
| **About** | Version and updates, compute backend, where data lives, diagnostics, log |

Everything that needs knowing how recognition works is under **Advanced**;
sensible values are already set. Replacements and the prompt live in the
Tapybara window under **Dictionary**, because they are added to all the time.

Settings live in `%APPDATA%\Tapybara\settings.json` and are written
atomically, but you should not need to touch the file. A log sits next to it in
`logs\`, and Settings › About can open it.

### Storage and portable mode

By default, settings and models live in `%APPDATA%\Tapybara\`. Creating an
empty file named **`portable.txt`** next to the executable switches the
application to a `Data` folder beside itself instead — useful on a USB stick or
when you would rather leave nothing behind in the system. The older
`Tapybara.portable` name still works. There is also a toggle in
Settings › Advanced, which creates the same file.

Portable mode belongs to the zip. In the installed version the toggle is off
and disabled: an update replaces the program folder, and a `Data` folder kept
beside the program would go with it.

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
replacements in Tapybara › Dictionary fix them after the fact, matching whole
words case-insensitively — what was heard on the left, what it should be on the
right:

| Heard | Correct |
|---|---|
| хакинг фейс | Hugging Face |
| си шарп | C# |

A replacement fixes new dictations and calls. For calls transcribed before it
was added, **Apply to past calls** on the Dictionary page corrects their text in
place — nothing is transcribed again.

Names that end in punctuation — `C#`, `C++`, `.NET` — work as replacement
targets and as keys.

The Dictionary page shows one row per correct word, with every way it was
heard as chips on the left: type a variant and press Enter, or paste several
separated by commas. A search field appears once there are more rows than fit
on screen.

### Moving the dictionary to another computer

Tapybara › Dictionary › **Export…** saves the replacements, the prompt, the
people and — if remembering voices is on — their voiceprints into one JSON
file. **Import…** on the other computer merges it in rather than replacing:
replacements already added there stay, a replacement present in both takes the
file's version, and the prompt is taken only if there is none yet or it is
still the default. Voiceprints are imported only when both computers use the
same voice model, since prints from different models cannot be compared.
Hotkeys, devices and folders are not part of the file: they belong to the
machine.

### Speech detection

The detector is measured, not guessed. Settings › Advanced has a **Measure**
button: count from one to ten at your usual pace, and the detector runs at
several sensitivities so you can see where it starts losing speech. The
recommendation is deliberately one step softer than the strictest setting that
still keeps everything, because the sample is recorded in better conditions
than real speech.

## Recording calls

A call is recorded as two separate tracks — your microphone and your system
audio — and merged into one chronological `transcript.md` with timestamps and
names.

Start and stop a recording with `Ctrl`+`Alt`+`C` (changeable in Settings ›
Calls), from the tray menu or from the Calls page. While it runs, the floating indicator shows a red
ring and a ■ button that stops it; clicking the rest of the indicator does
nothing, so a stray click cannot start a dictation on top of the call.

Pick the output device the call actually plays on in Settings › Calls. If you
talk through a headset while your speakers are the system default, recording
"the default device" captures silence.

### Who was on the call

When a recording stops, transcription starts straight away and a card appears
by the tray: how far transcription has got, a name for the call, and who was
there. Names are chips — the people you talked to most recently come first —
and there is a field for anyone new. The card does not take focus, because
calls usually end on top of something you are still typing into, and
transcription does not wait for it: the answer is read when transcription
reaches the point of telling voices apart, which is the end.

Answering is worth the two seconds. One other person needs no voice analysis at
all: whoever spoke is settled by which track the speech landed on, and the name
goes straight into the transcript. Several people make the answer a hint about
how many voices to look for, and that hint does more for accuracy than any
setting on the page — on a two-voice check file, knowing the count put the
boundary exactly where it belonged, and the same audio without it came apart
into four speakers.

What the answer does not do is say which voice is whom. Voices are labelled
`Voice A`, `Voice B` until you name them, and a call waiting for names is
marked so in the list. Ticking the participants after transcription has
finished is fine too: the transcript is rebuilt from the stored lines, and if
the count changed, only the voice split runs again.

Nothing is lost if you close the card: the name and the participants are saved
however it is closed. When the transcript is ready, the card says so and offers
to open the call; if it is already closed, a notification does, and clicking it
opens that call. The Calls page of the Tapybara window lists everything recorded,
what state each is in, and lets you fix names or transcribe again later.

### Naming the voices

The calls window shows the transcript itself, not a link to a file. Beside it,
**People** lists everyone on the call — you first, then the others — with how
long each spoke and their share of the whole call. Each person has a colour,
the same as the dot beside their lines.

A voice without a name is a **Who is this?** card: three quotes, a ▶ that plays
each from the far track alone, and chips with names. The quotes are the lines
that sound most surely like that voice, one from each part of the call — not
the longest ones, because a long line is exactly where a change of speaker
hides. Pick a name and it goes into every one of its lines at once —
`transcript.md` is rebuilt in the same moment.

A name is a person. The chips with a coloured dot are people already on the
call: pick one and the voice joins them — the usual fix when the split tore one
person into two voices. **That's me** is for your own voice leaking into the
far track through speakers. A named person is one line; open it to see the
quotes, the voices it is made of and **Detach** by each, and **Change** to
rename. Two people with one name are one person to Tapybara, so give namesakes
different names — "Sasha K.", "Sasha M.".

**Change** renames a person on this call only. **Rename everywhere…** beside it
— or editing the name under Dictionary › People — renames them in every call,
the voice book and the list of people, and offers the mentions in the text:
each form with what it becomes, "кириллу → Шерифу ×5", so that "tell Kirill"
follows the new name in the right case. Nothing is transcribed again.

To show a call to someone without showing who was on it, **⋯ › Copy without
names…** copies the transcript with an alias for each person — yourself and
anyone mentioned included — and replaces the mentions too. Only the copy
changes; the call keeps the real names. Aliases are remembered, so the same
person is the same "Sheriff" in the next copy.

When a quote is not that person, **Not this person** under it gives the line
to someone else, and the next best quote takes its place. When a line in the
transcript landed on the wrong person, click the name above it. When the split
found the wrong number of people, **⋯ › Split the voices again…** takes the
right number and runs only the split.

### Listening

Point at a line and a ▶ appears by its time: click it to play the call from
there. The playing line is marked with a bar on the left and fills as it
sounds, and the transcript scrolls along — unless you have scrolled away to
read something else. The ▶ of the playing line turns into ■, which stops it.
Click a line while nothing plays and the bar moves there: **Listen** starts
from it. Settings › Calls › **Show what is playing** turns the
fill off and keeps only the bar.

### Fixing words

Double-click a misheard word. The card that opens lists how else the same word
came out in this call — misspelled or declined, "битре", "битра", "битры" —
all ticked: type the right word and **Replace** changes them at once, or
**Only here** changes just that one place. **Remember in the dictionary** (on by
default) adds the ticked spellings as replacements, so later calls come out
right.

Recognition often cuts an unfamiliar word in two — "рек стат" — and spells it
whole elsewhere in the same call. The card finds those too. When a double-click
catches only half, select both words and right-click › **Fix "рек стат"…**.

To rewrite a whole line, right-click it › **Edit the line**: Enter saves,
Esc cancels. A line that is not speech at all — a sigh read as words, a
phrase Whisper invented on silence — goes with right-click › **Delete the
line**.

Without headphones the other side's voice reaches your microphone from the
speakers. Tapybara removes that echo from your track when it matches what
was said on the other side, a phrase out of a longer line included; what
came through too garbled to match stays, and is deleted the same way.

Edits change the text only — nothing is transcribed again. Transcribing an
edited call again would lose them, so Tapybara asks first.

### Checking every line

Once voices are split, every line of four seconds or more is compared with
the voice it was given. A line that clearly sounds like another voice moves
there by itself; one that sounds like none of them gets a `?` beside its
time — click it to say who it was. Your own track is never checked; it is
your microphone.

When many lines sound like nobody found — usually because one person was
ticked and three spoke — the voices panel says someone else was probably on
the call and offers to split again with one more voice. On the author's
recordings a call ticked with one person and spoken by three had 39 such
lines; split into three voices, none. Calls recorded before this check are
checked once, in the background, the first time they are opened.

The check runs whether or not voices are remembered: it compares the lines of
one call with each other and keeps nothing. `bench linecheck <call folder>`
prints the numbers for a recording.

Calls transcribed before this version kept only the markdown, so their voices
cannot be named; the window says so and offers to transcribe them again.

### Remembering voices

When you name a voice, its voiceprint — a few hundred numbers from the same
model that tells voices apart — is kept under that name. On the next call each
unnamed voice is compared with the ones you have named before, and the panel
says "Sounds like Kirill · 84%" with a button to accept it; when every voice is
recognised, one button accepts them all. Nothing is named without you: a
suggestion that is wrong costs a click, a label that is wrong would put
someone else's name in the transcript.

A call with a single ticked participant teaches the book on its own — the
whole far track is that person by construction — so one-to-one calls are
enough for Tapybara to recognise people later on group calls. Unless the line
check hears someone else on that side: then nothing is learned, because the
print would carry other people's voices under that name. The print itself is
taken from the lines that most surely belong to the voice.

A voiceprint is biometric data. It is stored in `voices.json` next to the
settings and never leaves the computer unless you export the dictionary
yourself (and with remembering off, the export leaves voices out). Settings ›
Calls turns remembering off
and has a button that forgets every voice; Tapybara › Dictionary › People
forgets one person, and renaming a person there carries the voice along.

On the author's recordings the same person on two different calls scored
0.81, two halves of one recording 0.91, and different people 0.25–0.35; a
suggestion needs 0.62 and a clear lead over the next candidate.
`bench voiceprint a.wav b.wav` measures it on your own recordings.

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

**Recognition is nonsense.** Check the language in Settings › Dictation. A
language forced onto the wrong speech does not degrade the result gently; it
produces confident nonsense. `auto` is the safe setting.

**Something crashed.** The log is in `logs\` next to the settings file, and
Settings › About has both an "Open log" button and a "Copy diagnostics" button
that includes the last 30 lines.
