# Calls refactor implementation

Updated: 8 October 2026. The plan and mockup direction are approved. Implementation is staged; the full redesigned Calls workspace has not shipped.

## Reusable foundation

Extend the existing native WPF foundation: `Tokens.cs` owns sizing and spacing, `Resources/Controls.xaml` owns shared styles, and `Ui.cs` builds common elements. Retain the manual composition root in `App.xaml.cs`. No new UI framework, DI container or mandatory MVVM library.

New reusable pieces in this tranche:

- `Tapybara.App/Controls/TextCorrectionEditor`: replacement field, collapsed candidates, explicit bulk operation, explicit dictionary learning, local Enter and Escape. Receives plain text/candidates and emits requests. It knows nothing about recordings, transcript persistence or model inference. Dictation can reuse the same behavior later.
- `Tapybara.App/Controls/ContextMenuBehavior`: first-invocation dynamic menu opening for mouse and keyboard. Menu contents remain the feature owner's responsibility.
- `Tapybara.Core/Concurrency/CancelableOperation`: per-job cancellation versus commit boundary. Cancellation does not block the UI on file writes. Call jobs bind their final persistence to it; other background features can use the same contract.
- `Tapybara.Core/Storage/RecoverableFile`: atomic replacement plus previous bytes, with no-op detection. Used for transcript and call metadata. Append-only journals need a different storage pattern; this component is not forced onto them.

Calls owns speaker identities, source channels, audio timing, document transitions and persistence. Core owns those rules without WPF types or user-facing wording. UI components own interaction and presentation; screens compose them and translate requests into document commands.

For the next editor tranche, introduce stable segment identity, explicit provenance/revision control and an edit session before split/merge UI. A shared command model should give toolbar, menus and shortcuts one implementation of each operation. Extract reusable action bars, filters/chips, export choices and transport controls as they gain actual contracts. Avoid a generic screen base class or one component that embeds all Calls behavior. Do not refactor Dictation or Dictionary wholesale as a side effect of this work.

## First safety tranche

Implemented:

- F01: rebuild/removal of recognition resources waits for an active call job. Queued jobs obtain the current transcriber after the gate.
- F02: voice verification and embedding loops observe cancellation, including the last embedding. Reads can be cancelled. Final job writes use an explicit commit boundary; late Cancel cannot label a saved result cancelled. Delete waits for settlement and blocks requeue during deletion.
- F03: legitimate silence longer than ten minutes is retained; speech returning after a long gap stays at its actual time.
- F04: dictation disposal waits for the active operation before cleanup and waits for model preparation. Late callbacks no longer release a disposed semaphore.
- F05: capture failure schedules recovery off WASAPI, checks capture identity, stops the length fuse and recording state, and attempts to transcribe accumulated speech. StopRecording errors from lost devices do not discard the existing buffer.
- F06: channel decoding and envelope calculation run off the UI thread; audio reads check cancellation between chunks.
- F07 defaults: local correction is primary and Enter applies only here. Similar forms are collapsed and unchecked; dictionary learning is unchecked. Transcript and metadata updates retain previous bytes.
- F13: dynamic context menus explicitly handle and open the first invocation.
- Shutdown protection: active native call inference is cancelled without disposing its models underneath it. Process termination releases those resources.

Cancellation semantics: before commit, TryCancel cancels computation. Once commit begins, TryCancel returns false and the write completes. This is a cancellation boundary, not a multi-file crash transaction. JSON, metadata and Markdown still require the planned document/revision foundation for full crash-consistent recovery.

Recovery copies: `transcript.json.previous` and `meta.json.previous` contain the immediately preceding changed file. Identical writes do not rotate them. They are a bounded safety copy, not persistent Undo, full version history, or an automatic recovery UI. Further changed writes replace the previous copy.

## Verification

- Full solution build: zero warnings and errors.
- Core suite: 327 passed, no failures or skips (317 existing plus 10 new tests).
- Regression coverage: long silence and returning speech position, final-embedding cancellation, no further speaker embedding after cancellation, pre-cancelled reads, previous-file preservation, no-op snapshot preservation, late cancellation without commit/Done, and cancellation versus commit semantics.
- Temporarily restoring the ten-minute padding limit caused both new long-silence tests to fail with the expected ten-minute offset. The fix was restored afterward.
- Bugbot identified the cancellation/delete race and a dictation stop/drain cancellation gap. Both were fixed; the final focused review found no remaining concrete regressions. Dictation publishes cancellation before entering Transcribing and checks it before recognition.

Still required before calling Phase 1's exit gate complete: real settings changes during transcription, cancellation during native inference, microphone unplug/recovery, first mouse/keyboard menu on fresh and recycled WPF rows, and correction popup interactions in both languages. These were not exercised with live audio/native UI in this run. No GPU/model quality claims are inferred from the unit suite.

Phase 0's representative real-call corpus and measurements remain open. The editor foundation has now been implemented as described below. The approved header/library/timeline redesign follows it; it is not claimed implemented by this tranche.


## Phase 2 — editor foundation

Implemented on 8 October 2026:

- Schema v2: persistent segment GUIDs, a monotonic document revision, immutable-by-convention original lines, origin links for isolated fragments, per-field human provenance/locks, and explicit estimated timing. Channel remains the physical audio source; changing the speaker does not change the channel.
- Migration of old JSON writes a previous-file backup and persists unique IDs. Already-edited legacy content is marked as an edited baseline and conservatively protected; it is never presented as recovered original recognition. Unknown future schema versions cannot be overwritten.
- Background transcription, resplit, verification and print refresh commit against their starting revision. A newer edit rejects the stale result. Resplitting preserves manually assigned voices and reserves their identifiers; only automatic identities are regenerated.
- Reusable `DocumentHistory<T>` stores bounded snapshot transactions without UI or storage dependencies. `CallEditSession` adapts it to revision-checked persistence and speaker-name changes. Undo/Redo restores text, identity, timing and provenance plus only the metadata keys affected by that command; unrelated names, title and participants survive.
- Reusable native WPF `InlineTextEditor`, `InlineEditScope`, `HistoryBar` and `AudioBoundaryEditor`. Calls composes these and owns document commands; the controls do not know about call files or inference.
- Direct native typing and word selection. A field commits as one transaction when focus leaves or before a document action. Enter inserts a line break; Escape restores the current draft. Ctrl+Z and Ctrl+Shift+Z/Ctrl+Y use document history. History buttons have a reserved area beside the tabs and appear for Transcript only.
- Caret split through Ctrl+Enter or the menu; whitespace boundary only, same speaker on both pieces, no diarization or speaker prompt. Without word alignment, the temporary audio boundary is the interval midpoint, explicitly estimated; `~` and accessible help identify it. It is not represented as measured word timing.
- Menus provide merge with the next compatible segment, delete selection, manual whole-segment assignment, explicit selection assignment with validated audio boundaries, timing adjustment and missing speech. Overlapping intervals are supported. Boundary editing offers bounded playback of the real source channel. Missing speech starts with an empty text field.
- Rejected commits retain a draft and prevent switching rows/calls around it. An explicit Save my draft instead action saves only that segment against the latest revision; Discard draft and reload discards it. Quote playback follows its source channel even after cross-source identity corrections.

Verification: full solution builds with zero warnings/errors; 343 Core tests and 5 STA WPF tests pass (348 total). Coverage includes schema migration/backups, future-schema protection, interruption repair and complete Undo/Redo, neighbor preservation, overlapping segments, stable identity after timing changes, stale writes/Undo, original baseline preservation, speaker locks, metadata Undo with unrelated edits, native binding retention, recycling, Escape, rejected draft retry and rejected row A -> focus B -> leave call. Bugbot found integration defects in resplit locks, name restoration and draft focus; each was fixed and reviewed again.

Limits and native exit gate: Undo is bounded to 100 transactions per opened call and lasts for the current application session. This is not a persistent history browser. JSON and metadata writes are serialized but are not yet one crash-atomic multi-file transaction. Real alignment, boundary dragging and source envelopes were scheduled for Phase 4; see its tranche statuses below. Physical source channels currently support one or more speaker identities, but identity-book learning remains limited to the existing system-audio extraction path.

Still exercise the actual Calls window with real long Russian transcripts: first context menu, word selection, wrapped/multiline typing, right-click caret/selection retention, rapid call changes, recycled rows, Undo/Redo focus and scroll continuity, name assignment, boundary popup keyboard access, source-channel playback, and light/dark themes at compact widths. The STA tests instantiate native controls without running the production app or using audio devices; they do not close this live usability gate. The library, unified Export, header layout, timeline and optional inspector redesign are the next implementation phases.


## Phase 3 — library, header and delivery

Implemented on 8 October 2026:

- `CallLibraryIndex` performs cached file discovery and edited-text indexing on a sequential background worker. Unchanged calls reuse their search entries; changed files invalidate the entry and deleted calls disappear. Live recording/job state is overlaid by the screen. Filtering runs against immutable snapshots in the background; cancellation and request versions prevent old search results from replacing newer criteria. The WPF collection-view callback does no file IO.
- Reusable `SearchFilterBar` keeps Search visible and expands person/state/date controls on demand. Active removable chips remain visible when the controls are collapsed. Local dates are inclusive. Language changes retain filter values and rebuild their labels without reparenting live controls incorrectly.
- Call rows expose their own ellipsis and first-invocation context menu. Rename, export, reveal/open transcript, transcription/cancellation and deletion capture the clicked recording. Recording/job state is rechecked before destructive or processing actions. Requested call selection goes through the editor's rejected-draft guard.
- Header delivery is consolidated under Export; file/folder management and processing live in overflow. The existing People/Review helper is optional and closed initially; its inference runs only while opened. This is a presentation change, not evidence that its suggestions meet Phase 5's quality gate.
- Reusable `TranscriptExportPanel` owns format, labels/aliases, timestamp and selection options. Shared typed options live in `Core/Export`; Calls owns rendering and delivery. An anchored flyout has an excerpt preview, a scrollable options area bounded by screen height and fixed Copy/Save controls. English and Russian labels are included.
- TXT, Markdown and versioned external JSON use current editable text. Delivery reloads the current document; selected substrings retain the parent audio interval explicitly marked estimated. JSON excludes recognition baselines, voiceprints and private metadata. Speaker aliases include known mentioned people, including an owner without recognized lines. Alias values are validated for blanks, duplicates and reuse of known names. Replacement of known name mentions is explicit; arbitrary personal information is not automatically detected or removed. No source files are changed by alias export.
- Save as writes UTF-8 without BOM and rejects the recording's source filenames and their previous-version backups. Old Markdown-only calls retain Markdown delivery; structured export/editing requires recognition data.

Verification: solution build has zero warnings/errors; 361 Core tests and 7 STA WPF control tests pass (368 total). New coverage includes current JSON projections, omitted labels/timestamps, exact selection scope and estimated timing, alias inventory/replacement/collisions, unnamed-speaker preview context, Markdown escaping, protected destinations, cached edited-text search/invalidation/removal, cancellation and combined local-date/person/state filters, usable export defaults and removable collapsed-filter chips. Bugbot findings about requested selection, mention-only alias coverage, unbounded options and preview speaker context were fixed; the focused follow-up found no remaining concrete regressions.

Native exit gate remains open: test large real libraries, rapid search/filter changes, failed drafts while filtering/opening notifications, fresh/recycled-row menus, export flyout keyboard/focus/dismissal and compact displays, many aliases, actual clipboard/file delivery and both languages/themes. The STA suite exercises controls without launching the production application or real audio. Subtitle formats, styled PDF/DOCX, timeline/seek work and the assistance effectiveness decision remain later phases. None of these is inferred complete from the test suite.


## Phase 4 — first transport tranche

Implemented on 8 October 2026; Phase 4 remains in progress:

- Reusable `AudioTransportBar` provides Play/Pause, Stop, five-second rewind, elapsed/total time, native scrubber, Follow and an explicit excerpt range with a Whole call action. It emits requests and owns interaction state without accessing audio files or Calls types. It docks under the editor's scrolling area, independently of library and optional inspector. Range details wrap at compact widths.
- Mouse dragging previews a position and commits once on release/capture exit. Repeated arrow/Home/End/Page keyboard adjustments commit on release/focus exit; UI clock refreshes never issue seek commands. Changing calls resets pending scrub interaction, including descendant mouse capture. Manual wheel scrolling switches Follow off; text editing and unresolved drafts suppress automatic following.
- `CallPlayer` uses the output device byte clock rather than decoder read-ahead. Pause retains the existing output queue; Resume continues it. Seek replaces the output queue and positions every selected source. A shorter source stays at EOF rather than replaying its beginning. Seeking while paused prepares a position without starting audio.
- Prepared Load/Seek retain only position/range metadata and release readers. Resume reads fresh files. This permits stale WAV header recovery between selection and transcription; retranscription also stops active playback of its target before queuing the job. An explicit source excerpt can play even if the unused channel's duration cannot be read.
- Timestamp clicks seek; a separate play button starts a bounded source-channel excerpt with the existing 300 ms padding. The editor's boundary audition uses the exact selected bounds and rejects playback from a stale popup for another call. Main transport controls operate on the visible range; seeking or Whole call explicitly returns to the mixed call. Late callbacks from replaced outputs are ignored.
- Playback interval activity uses half-open intervals. Silence leaves no active speaker; overlap can mark multiple rows, limited to the playing source when auditioning a channel. The misleading full-row progress sweep and its settings toggle are removed. The old settings key remains readable for compatibility. No word timing or isolated-speaker audio is implied.
- Switching calls and hiding the page stop UI timers; returning to a page with active audio restarts clock updates. Playback failure is shown with localized wording. The clock tick avoids recreating playback icons.

Verification: full solution builds with zero warnings/errors. 372 Core tests and 11 STA WPF control tests pass (383 total). New tests cover output-clock versus decoder read-ahead, same-queue pause/resume, seek and stale output callbacks, shorter-track EOF, bounded completion and endpoint replay, paused seeking, invalid ranges/missing sources, idle readers and stale-header repair, valid selected source with corrupt auxiliary audio, silence/overlap/source filtering, transport update suppression, mouse/keyboard commit-once and cancellation of pending scrub on a call change. The fake output implements the NAudio output/clock interfaces and consumes real synthetic WAV streams; it does not validate a physical output device. Bugbot found the idle-file-lock recovery regression and an auxiliary-duration error-handling gap; both were fixed and the focused lifecycle review passed.

Remaining after this first tranche: cached real source envelopes, aligned speaker activity lanes, range/boundary gestures, the bounded alignment/diarization investigation and validated word timing. Native exit gate: real Pause/Resume and seek while playing/paused, output-device failure, short/unequal source files, audible excerpt boundaries, stopped clocks and Follow, keyboard/screen-reader scrub, rapid call changes, editing with active playback, compact-width/docking layouts, both themes/languages and both PCs. No hardware accuracy, diarization improvement or real-call usability claims are inferred from the automated suite.


## Phase 4 — source envelopes and speaker activity tranche

Implemented on 8 October 2026; Phase 4 remains in progress:

- Optional Timeline opens above the docked transport. Sources shows real Microphone/System peak envelopes on one time ruler; Speakers shows current editable transcript intervals grouped by identity. The reusable `AudioTimelineView` receives plain lanes, opaque interval keys and immutable envelopes, and emits seek/play requests. Calls owns source choice, document lookup, draft flushing and persistence. It introduces no model or UI framework dependency.
- Physical source envelopes stream on background workers with at most 6,000 peaks per file and an eight-source cache. Stereo uses the stronger channel, preserving opposite-polarity audio. Bin duration controls placement, including the partial final bin; shorter sources end at their own time. File size/write time invalidate cached sources. Cancellation and request epochs reject results for another call, hidden page, collapsed timeline or disposed screen.
- A short-lived per-file gate coordinates waveform reads with WAV header repair, including cancelable waiting. Transcription settles its selected timeline task before queuing processing. Prepared playback metadata is reloaded when source files change. Playback itself continues to use its existing output-clock transport.
- Speaker intervals derive from current rows, retain physical microphone/system channels after identity reassignment, and expose estimated timing and doubtful attribution in their outlines/tooltips. Overlap remains visible; simultaneous intervals in the same lane offer a native chooser. Clicking a fragment selects the current interval and auditions its source. Old interval actions re-resolve current keys and cannot play a deleted row.
- Source and blank-timeline clicks seek. Arrow/Home/End adjustments preview and commit once on release/focus exit. A native interval selector and Play action provide a keyboard/screen-reader route to fragments. Data replacement cancels pending keyboard seeks. Drawing caches the static ruler/lanes and updates only the playhead/selection while the clock runs. English/Russian labels and existing theme resources are used.

Verification: full solution builds without warnings/errors; 380 Core tests and 14 STA WPF control tests pass (394 total). New checks cover silence/peaks, partial bins, opposite-polarity stereo, invalid samples, bounded memory, cache reuse/change/deletion/eviction, cancellation, repair waiting and restored tail, stale interval actions, selection retention, overlap rendering, keyboard commit-once/data-change cancellation and native rendering of source envelopes at compact/wide widths. These checks do not launch the production application or validate physical audio hardware. The focused Bugbot review found no concrete bugs in this tranche.

Remaining: range/boundary dragging, alignment/diarization investigation and validated word timing. Speaker lanes are transcript activity, not separated voice audio or improved diarization. Native exit gate still requires representative real calls, rapid switching/cancel/retranscription, long recordings, real device playback, keyboard/focus/screen-reader use, compact docking, both themes/languages and both PCs.


## Phase 4 — range selection and boundary gestures tranche

Implemented on 8 October 2026; Phase 4 remains in progress:

- Dragging a source waveform selects a listening range on that physical source; dragging the ruler or blank speaker area selects mixed audio. Reverse drags are ordered, bounds are clamped, and source ranges stop at their actual decoded duration. Play selection and Clear selection are explicit; selecting audio never changes transcript text or timing. A native Select range flyout offers seconds fields and a source selector for keyboard/precise work.
- A selected speaker interval exposes start/end handles. Dragging previews the new boundary, with a minimum one-millisecond positive interval and no neighboring ripple. Release opens the existing boundary editor with the proposed bounds. Listen auditions the original physical source; Apply persists one timing transaction through the existing revision checks and Undo/Redo. Numeric Adjust timing remains available through the native interval selector, including when drawn intervals are too small to manipulate.
- `AudioTimelineSelection` is UI-independent selection/preview state. It knows no Calls documents, persistence, identity model or audio devices. `AudioTimelineView` renders and dispatches gestures; Calls re-resolves current IDs and expected original bounds before opening the editor. The boundary editor also rejects Apply after a call change or conflicting timing/selection-text changes.
- Context/duration/availability changes clear selections; removed source keys invalidate ranges. Data replacement, collapse/unload and Escape cancel pending interaction. Losing pointer capture abandons a partial drag and restores the prior listening selection. Range flyouts close on data replacement. Native fields accept their own millisecond-rounded endpoint while emitting the exact audio limit.

Verification: 386 Core tests and 17 STA WPF tests pass (403 total). Coverage includes reversed/clamped/source-preserving ranges, context/duration/availability invalidation, positive boundary limits, stale/unchanged/canceled previews, source-specific range requests, current-key timing requests, selected-handle/range rendering, millisecond endpoint rounding and one-transaction timing Undo/Redo with unchanged text, channels and overlapping neighbors. The solution builds cleanly; the focused Bugbot review found no concrete defects. Automated checks do not establish physical mouse-capture behavior or real audible boundaries in the production app.

Remaining: bounded alignment/diarization investigation, validated word timing and real-call acceptance. Native gate: forward/reverse/out-of-bounds drags, capture loss/Escape during drag, same-lane overlap handles, precise fields/source choice, failed drafts/conflicting edits during Apply, keyboard focus/dismissal, rapid call switching and processing, long calls on compact displays, both themes/languages and both PCs.


## UI consistency tranche — native usage feedback

Implemented on 8 October 2026:

- Square library row actions and equal-height Search/Filters. Shared control sizing uses attached behavior to retain native Fluent templates, keyboard focus and hover states. Active filter counts remain accessible without squeezing labels into a square button.
- Ellipsis/header menus measure before opening, use custom anchor candidates and disable popup animation. Screen-edge placement and the reported delayed jump still require live desktop confirmation.
- Shared opaque `PopoverSurface` now serves Export, text correction, boundary adjustment and range selection. Export uses a vector arrow instead of the broken font glyph, with bounded width and inherited theme foreground.
- One Calls header rail replaces duplicate Play and Transcript/Note tab rows. Notes expand above the transcript and appear truncated in metadata. History targets notes while editing them and switches to document history after a successful transcript command.
- Timestamp play/time/uncertainty controls share a centered inline rail. Transport controls, clock, seek, waveform toggle and overflow occupy one row at ordinary widths; a compact editor uses two rows. Follow and excerpt detail commands move into overflow, with visible whole-call exit.
- Shared confirmation dialog now has one heading, separate unframed editable fields, a compact action footer, initial field focus/selection and bounded scrollable content. Consent, onboarding, voice-review and VAD windows share solid surfaces and action sizing while retaining their distinct flows.

Verification: 386 Core tests and 24 STA WPF tests pass (410 total); solution build has zero warnings/errors. New native layout checks parse the actual Calls and confirmation XAML with real theme dictionaries, check search/filter and row action dimensions, render compact/wide light/dark layouts, verify opaque surfaces and ensure the rename footer stays compact. Bugbot found a note-history routing defect after timeline edits; successful document commits and call changes now reset routing, and its focused follow-up found no additional concrete regressions.

Saved layout probes are in `validation/`. They use a hidden native visual host and seeded theme/accent resources, without application bootstrap, live services or audio devices. They are not screenshots of a running production session; primary-action accent appearance, menu motion, focus, DPI and real audio remain native acceptance work. Approved historical browser mocks remain preserved.


## Phase 4 — bounded speaker-attribution investigation tooling

Implemented on 8 October 2026:

- Confirmed the structural loss point: `CallVoices.Assign` calls `SpeakerAssignment.For` once per ASR line, reducing all intersecting speaker spans to a single majority-duration winner. Sequential turn changes and simultaneous voices can therefore both be hidden inside one text segment. `VoiceCheck` compares/reassigns whole-line embeddings; it does not split text or recover a missing voice's words.
- Added UI-independent `SpeakerCoverage` evidence measurement. It clips spans to a requested window, unions duplicates per cluster, retains short secondary turns, distinguishes sequential activity from simultaneous activity and exposes chronological active-speaker intervals. Evidence durations are not confidence probabilities.
- Added a bounded read-only `speaker-audit` command to the existing bench harness. It decodes at most 120 seconds of original system audio, restores call offsets, reports intersecting transcript lines and partial-window/human-lock flags, and emits JSON without transcript text or speaker names. It reads JSON without the migration-writing production loader. Production recognition and assignment are unchanged.
- Exercised the native ONNX pipeline on 0–30 and 25–55 second excerpts from an existing recording. The nonzero-offset probe reported two partial lines and one complete line with two detected clusters, including about 0.34 seconds of simultaneous activity. This is a candidate for listening review, not confirmed ground truth or proof of corrected diarization. Source file sizes and modification times stayed unchanged. Raw evidence remains in ignored `temp/`.

Verification: 391 Core tests plus the previously verified 24 STA App tests (415 total); clean solution build. New tests cover majority-assignment loss of secondary turns, simultaneous versus sequential voices, duplicate/self-overlapping spans, clipped coverage, touching boundaries, invalid spans and empty windows. Native command checks exercise offset restoration, partial-line reporting and rejection of excerpts longer than 120 seconds. Bugbot found no concrete defects in this tranche.

Decision: investigate word timing with the existing runtime before switching ASR/diarization engines. The existing `WhisperEngine` retains only segment Start/End/Text. Upstream [whisper.cpp word timestamps](https://github.com/ggml-org/whisper.cpp#word-level-timestamp-experimental) and [Whisper.net token timestamps](https://github.com/sandrohanea/whisper.net/blob/main/Whisper.net/WhisperProcessorBuilder.cs) are explicitly experimental; their availability is not a validated Russian word alignment result. A matched real interruption example, listening labels and correction-effort measurements remain necessary. No automatic text segmentation, speaker separation, subtitle release or word-sweep UI is enabled by this investigation.

Usage and interpretation: [bench diagnostics](../../tools/Tapybara.Bench/README.md). The user is locating a clear example; preparation and synthetic/native diagnostics progressed independently while that real-case gate remains open.


## UI usage feedback — second refinement pass

Implemented on 8 October 2026:

- Replaced the animated ContextMenu shell with a shared theme-aware shell that has no movement storyboard. Disabling WPF popup animation had not addressed the library's independent border animation. Menu item styling and keyboard navigation remain native; anchored menus prefer an adjacent below/above placement.
- Added shared `AnchoredPopup` placement with an eight-DIP caller gap. Audio-range, timing, correction and Export flyouts use it. Timeline timing commands anchor to the actual timing button, rather than the entire waveform panel. Boundary fields have explicit caption/help/action gaps and shared 32-DIP sizing.
- Boundary fields now display `mm:ss.fff`, matching the timeline's time scale. `AudioTime` also accepts pasted numeric seconds and hour-based clocks, validates component ranges, and retains exact source endpoints when the displayed millisecond value rounds them. The waveform time readout is drawn at the moving pointer rather than left to a stale native tooltip position.
- Forward five seconds mirrors Back through the existing clamped seek/resume path. Follow is a direct toggle, replacing the one-item overflow menu. Timeline mode selection moves into the compact command area; explanatory text is available through a help icon with tooltip and clickable flyout. Native interval selectors, source choices and adjacent commands use the same height.
- Selected Notes/Waveform icons bind to their button's checked accent-text foreground. The shared toggle style preserves the installed native implicit template. Date-range filters use one reusable equal-column row; changing language retains the same native picker instances.
- Added a visible Find/Replace header command plus Ctrl+F and Ctrl+H. The reusable bar supports literal case-insensitive query, counts, previous/next navigation, current replacement, counted Replace All and Escape dismissal. Scope is the current call's text; replacement does not learn dictionary entries or expand similar forms. Empty replacement deletes the literal match. Existing word/selection correction remains in the context menu.
- `TextSearch` is UI-independent literal range matching. `TranscriptSearch` preserves IDs, source channels, speakers, timing and recognition baselines while marking changed text as human-edited. Replace All uses one existing revision-checked document transaction and Undo/Redo. Temporary inactive highlights are owned by search, removed on navigation/clear/close/call change, and relinquished when the user selects text or focuses an editor.

Validation: 394 Core tests pass. Native App tests cover actual-theme menu template triggers, checked icon foregrounds, field/action sizing and spacing, date columns, compact search layout, clock parsing, Forward/Follow, native Enter behavior and temporary highlight ownership. All 34 STA App tests pass (428 tests total with Core); the full solution builds without warnings or errors. Source-reading synthetic/native tests do not close the production mouse/focus/DPI/long-recording usability gate. Bugbot's search highlighting, Enter routing and leftover-selection findings were fixed; its focused follow-up found no additional concrete defects.

The user explicitly stopped the overlap investigation while locating a representative failure. The diagnostic tooling is preserved; further model/word-timing investigation is on hold. Calls UI refinement and existing safe editing remain active.

## Clipping and menu regression follow-up

Implemented on 8 October 2026:

- Compact fields now use explicit vertical padding that fits the app's Segoe UI Variable body font while keeping the common 32-DIP command height. Native TextBox/ComboBox/DatePicker templates remain intact.
- Restored the theme's menu colors, rounded shell and drop shadow without its movement storyboard. Standard scrolling appears only when menu content overflows; the short session menu no longer inherits the decorative scrollbar treatment.
- Button-opened menus inherit the anchor font and are measured against the target monitor's work area and DPI before choosing a position once. Relative placement replaces the callback that could choose another candidate after popup layout. Cursor/keyboard menus compensate for the transparent shadow margin.
- Added text viewport and ComboBox label fit assertions using the application font, live native popup position checks below and above an anchor, and a 60-item overflow test reaching the last item. Updated light/dark probes are saved under `validation/`.

Validation: all 41 STA App tests pass. Core is unchanged by this UI follow-up; its last verified count remains 394. Bugbot's overflow finding was addressed and focused follow-up found no remaining actionable regressions. Offscreen/native popup fixtures do not replace the user's real installed-app/DPI check. The currently running installed copy is separate from the repository test build; exit it before launching the updated test executable.

## End-of-day checkpoint — 8 October 2026

The updated Release test copy was launched after the user exited the installed app. The user reports remaining issues and specifically that right-clicking transcript text exposes only native Copy/Cut commands, obscuring split and speaker assignment. The intended Ctrl+Enter whitespace split exists, but the complete interaction is not accepted. Hidden repeated speaker labels also limit assignment access after a split. These are recorded as next-session priorities in `plan.md`; no speculative last-minute interaction changes were made for this checkpoint.

## Playback and document-action refinement — 9 October 2026

The user reported persistent session-menu movement, transcript row alignment issues, confusion between Follow and bounded playback, a broken split entry point, clock width jitter, redundant playback/range tools, missing waveform zoom/pan and the session scrollbar's placement. The running process was the separately installed AppData copy; the repository Release executable is a distinct test artifact.

Implemented:

- Library filtering skips collection-view refresh when the matching directory set is unchanged. This removes periodic container recreation under a menu anchor. Anchored menus close when their row is unloaded instead of following an invalid target toward the screen origin.
- `InlineTextEditor` exposes document-menu requests through a most-derived class handler registered before TextBox's own handler. This prevents the native Cut/Copy menu from opening first. The relevant native sequence is documented in [WPF's TextEditorContextMenu source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Documents/TextEditorContextMenu.cs).
- Reusable `TranscriptLineActions` separates line-only playback, playback continuing through the call, timestamp seeking and line actions. Timestamps have horizontal padding; all command buttons share a 32-DIP height with the text row. Explicit row actions include speaker assignment and selection/split commands. Manually split lines retain visible speaker labels even when consecutive parts initially share one speaker.
- Follow now responds to paused seeks and explicit re-enabling, with reusable `SmoothScroll` for pixel-based virtualized lists. It changes neither selection nor keyboard focus; editor focus, drafts and open editor menus suspend following. Transcript mouse scrolling cancels the current animation without silently changing Follow's toggle state.
- The main transport plays a selected waveform range, including its physical-source scope, and its accessible label distinguishes selection playback. Removed the waveform interval dropdown, separate excerpt/selection Play commands and Select range form. Temporary drag selection remains separate from the explicit undoable line-timing editor/handles, whose label now identifies the line being edited.
- Core `AudioViewport` owns bounded time windows. Timeline zoom anchors to the pointer; buttons/Ctrl+wheel zoom, wheel/middle drag/scrollbar pan and Fit call resets. View state survives same-call waveform refreshes, resets on context changes and does not change audio or document state. Offscreen interval boundaries neither render nor hit-test as false viewport-edge timing handles.
- Transport clock uses a monospaced font and a reserved width measured from the duration/scope. Session-list right inset aligns the scrollbar with the library content edge.

Validation: 399 Core tests and 49 native App tests pass (448 total). The Release solution builds with zero warnings/errors. Native fixtures verify class routing before the default menu, already-handled requests, separated row commands, stable timer geometry, anchor removal, smooth reveal without selection changes, viewport context isolation and invisible-edge hit testing. Bugbot's viewport-boundary finding was fixed; focused follow-up found no additional actionable regressions. Updated layout images are saved under `validation/`. These fixtures do not constitute installed-app mouse/audio/DPI acceptance; the user still needs to test the updated copy. Overlap/alignment investigation remains paused.


## Editing discoverability refinement — 9 October 2026

Missing-speech insertion now names its action and speaker, explains that it creates a separate line, labels the multiline missing-words field, and offers an Add line action. Timing edits retain a separate heading and omit the text field. Both continue to use the reusable validated audio-boundary component.

Row menus expose merge with previous and next; both require adjacent lines of the same voice and physical source, with a tooltip explaining disabled actions. Timestamp click seeks; Alt+vertical drag proposes a timing shift (10 pixels per second, upward earlier pointer movement moves timing later) in the existing review form. Duration is retained, negative starts clamp to zero, and saving remains an explicit undoable operation. Estimated timestamps keep the ~ marker. Recycling or unloading cancels the gesture.

Loop range is independent of Follow and only enabled for a bounded range. Natural completion resumes from range start, preserving the audio source. Explicit Stop and output errors do not restart. Selecting another call resets Loop. Expanded library filters hide their duplicate removable chips and offer Clear filters; collapsed filters retain removable summaries.

Validation: 400 Core tests and 52 App tests, including range completion versus Stop, insertion requirements, duplicate filter suppression, and gesture cancellation. Bugbot caught the recycled-row gesture issue; fixed and re-reviewed. Native previews are saved under validation. Actual mouse, audio-loop and installed-app acceptance remain for the next user test.


## Shared capture header and compact row controls — 9 October 2026

Calls and Dictations now use SectionHeader, a reusable title/action rail. Calls library width is 340 DIPs. RecordPill retains its native rounded template and inline hotkey; the shared header hides only the idle hotkey when the title and full action cannot fit, restoring it on widening. Both screens retain the full shortcut in the tooltip. Recording elapsed time stays visible and monospaced.

Transcript rows have one bounded Play action, timestamp, and More. Free playback from the row is available in More; the tiny lock icon and duplicate Play have been removed. The transcript gutter is reduced from 140 to 110 DIPs while leaving room for long/estimated timestamps.

TimestampScrubber uses native WPF Thumb capture instead of custom ButtonBase routing. Alt+vertical drag shows a signed offset and proposes a timing edit on release; click and Space/Enter seek. Escape, recycled context, unloading and lost capture cancel without applying. Follow does not scroll while the transcript owns mouse capture. Thumb deltas for a stationary control are displacement from the initial point, so multiple moves do not accumulate that displacement repeatedly (checked against the [official WPF Thumb source](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/Primitives/Thumb.cs)).

Validation: 54 App tests pass, including actual Calls/Dictations XAML, light/dark previews, shortcut collapse/restore, native drag events and recycling cancellation. The isolated Release build is clean. Bugbot found no further actionable regressions in the shared header and compact controls. Real Alt+mouse acceptance remains open; tests do not claim installed-app or physical mouse validation.


## Theme colors and shared section geometry — 9 October 2026

Status badges previously resolved theme brushes while building immutable CallRow snapshots; a later theme change could leave old light-theme foreground colors in dark mode. CallRow now carries resource keys, and the reusable StatusBadge binds dynamic background/text resources. The name-review label uses the primary text brush for readable contrast. Calls banners and the Dictations root foreground also reference active theme resources.

Both sections use the same 340-DIP library/header lane, title/action insets and search width. Dictations keeps its wide history body for long text; its count moves to the footer, matching the Calls summary role. No changes to playback or transcript editing.

Validation: 55 App tests pass, including changing badge resources without recreating rows and actual light/dark XAML previews containing pending/name-review badges. Isolated Release build is clean; focused Bugbot review found no concrete regressions. Real application theme switching remains for user acceptance.
