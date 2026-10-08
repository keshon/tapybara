# Tapybara product fix plan

Updated: 8 October 2026.

Current steering: pause the overlap/alignment investigation until the user finds a representative recording. Finish native UI feedback and make current-call Find/Replace discoverable.

Latest UI follow-up: field clipping and menu chrome/placement regressions are addressed in the repository build; 41 native App tests pass. Retest the updated executable after exiting the separately installed copy. See the implementation log for evidence and remaining live validation.

## Next session — 9 October 2026

The user requested a commit/push checkpoint and reported that issues remain. This checkpoint is not a product acceptance or completion of the refactor.

- **First priority:** right-clicking transcript text shows the native Copy/Cut menu instead of the document commands. Investigate routed-event/class-handler precedence and restore the intended first-click menu, including split and selected-text speaker assignment. Verify it in the actual application, not only offscreen layout fixtures.
- Make the split/reassignment flow discoverable. Current keyboard path: caret at whitespace between words, no selection, Ctrl+Enter. Split audio timing is estimated until adjusted. Consecutive lines with the same speaker hide repeated labels, so a newly split part can lack a visible assignment entry point; provide a reliable per-line speaker action.
- Retest session ellipsis menu positioning, control clipping and popover behavior in the real app. Native tests passed, but user acceptance remains open; collect the remaining concrete issues during the next trial.
- Preserve the pause on overlap/alignment investigation until the user supplies a representative recording. Continue using the approved reusable component foundation and saved mockups.

**Status:** the fix plan and revised mockup direction are approved. The safety tranche, reversible editor foundation and library/header/export tranche are implemented with reusable components. Phase 4 transport, source envelopes, speaker activity lanes and boundary gestures are implemented; the subsequent UI consistency pass is also implemented. Native/audio verification, alignment/diarization investigation and the assistance quality decision remain open. See [implementation status and component boundaries](implementation.md).

The detailed 21-finding audit is preserved in `audit.html`, including source references, evidence levels, acceptance criteria and the original build/test snapshot. That snapshot is not current validation of these prototypes or a hardware benchmark.

## Product direction

Keep the native Windows application and its useful Calls workspace. Prioritize trustworthy, reversible transcription correction over additional model features. Take interaction conventions from professional editors without turning call review into a full video/audio workstation.

- **Fix:** lifecycle, cancellation, device loss, audio timeline correctness, first-click context menus, unsafe correction defaults and UI-thread stalls.
- **Remove:** default dictionary learning and bulk similar-word replacement; misleading whole-row playback sweep; duplicate delivery paths; dependence on a permanent inference helper.
- **Add:** Undo/Redo, recoverable revisions, explicit speaker assignment, call row actions, discoverable filters, unified Export and reliable audio navigation.
- **Rework:** transcript editing, speaker reconciliation, stable document identity/provenance and optional People/Review inspector.
- **Defer:** general file-import work, project/tag taxonomy, simultaneous delivery of every export format, source separation, and an ASR engine switch without measured benefits.

## Design revision after the second mockup review

### Library: progressive disclosure

Keep Record and Search immediately available. Put a Filters button beside Search. People, date and status controls expand only when requested; active filters remain visible as removable chips when collapsed. Keep the Filters icon square; expose the active count in its tooltip and accessible name, with active chips carrying the visible criteria. Search covers titles and transcript text, with that scope made clear in accessible help.

Retain newest-first ordering and row ellipsis/right-click menus. Rename, Export and Show in folder form one group; processing actions form another; Delete is last. Commands target the clicked row even if a different call is open. Later investigate a filter popover only if the expandable panel still displaces too much content in real libraries.

### Editing: caret first

Click transcript text to place the caret and edit directly. Native word selection remains native: double-click selects a word instead of unexpectedly opening a modal. Typing, insertion and deletion operate only in that segment. Dictionary and bulk replacement are separate explicit opt-ins, disabled by default.

**Split:** place the caret in whitespace between words, then use Split here, Ctrl+Enter, or the context menu. Immediately create two segments at that exact text boundary; retain the original speaker on both. Split never guesses a text boundary, opens a mandatory speaker dialog, or starts diarization. Assign a speaker to either resulting segment as a separate action.

Use measured word timing when available. Without alignment, mark timing as estimated and offer an explicit audio boundary adjustment. Character-based timing in the prototype is an illustration only; it is never represented as measured timing. True simultaneous speech permits overlapping intervals, rather than pushing one speaker after the other. A split cannot recover speech missing from ASR output: add missing speech or use targeted retranscription separately.

Nonempty text selection exposes Replace/Delete/Assign selection. Assignment can isolate that selection with explicit/validated audio boundaries; a collapsed caret assignment applies to the whole segment. Selection, focused segment, playback range and playhead are separate states. Context menus preserve the selected text and caret. Editing commands share the same implementation across toolbar, menu and shortcut.

Avoid shifting transcript text when the editing toolbar appears. A stable toolbar area in the prototype reserves room; final placement must be tested with wrapped Russian text and narrow windows. Group continuous typing into sensible undo transactions. Split, merge, assignment, timing changes and bulk operations are individually reversible. Undo/Redo must restore text, speaker, timing and provenance together.

### Playback and timeline

The target native layout has a dock **across the bottom of the main editor**, separate from the library and optional inspector. It stays available while transcript content scrolls. The timeline expands above the transport. The browser mockup follows a content-sized preview and uses a sticky dock; it is not proof of WPF docking/virtualization behavior.

Transport: Play/Pause, exact resume, elapsed/total time, short rewind, bounded segment/range playback, scrubber and Follow. Timestamp seeks; a distinct playback command plays the excerpt. Do not silently play the remainder of a long call when the user wants a short comparison.

Timeline modes:

- **Sources:** real Microphone and System audio envelopes, with shared time ruler and playhead. Source audio remains the authoritative playback material.
- **Speakers:** time-aligned speech-activity lanes per person. Duration and overlap are spatially represented. Clicking an interval selects it and plays that source-audio interval. Machine suggestions use an explicit uncertain state; human assignments and approximate boundaries remain distinguishable.

Do not draw or label clean per-speaker waveforms when several voices share one recording. Cropping the system mix to Pavel's intervals is still mixed audio. A genuine isolated-speaker waveform requires validated source separation; it is an optional later investigation, not a prerequisite for useful speaker lanes. Do not offer speaker Solo/Mute as if separated audio exists.

Prototype waveform envelopes, candidate speaker spans, playback and recording are illustrative/simulated. Candidate Anna at 27.6–31 seconds demonstrates uncertainty, not a measured inference. After edits, speaker lanes derive from current segment state; they are not a fixed decorative diagram. Range dragging, timing previews, real source audio and keyboard timeline navigation are now implemented; native real-call acceptance remains open.

### Header and export

Keep title/metadata followed by one command rail: Undo/Redo, Export, Notes, optional Review and management overflow. Playback belongs to the dock. Notes expand an inline editor while the transcript stays visible; a truncated note appears in metadata with full text available in its tooltip. Export owns Copy to clipboard and Save as, including names/aliases/no labels, timestamps and selection/whole-call scope. Overflow owns call management and processing, with no duplicate anonymized-copy workflow.

Start with TXT, existing Markdown and versioned JSON. Release SRT/VTT only after defining overlap, missing/estimated timing, line-length and cue policies and verifying target players. The prototype previews basic cues and preserves fractional seconds; it does not prove production subtitle compliance. DOCX/PDF require validated styled output; standalone HTML is deferred unless demand justifies it.

### Optional inspector

People is explicit identity management. Review contains actionable issues with jump/play and a concrete correction. Keep it collapsible; opening it must not be required to split or assign a segment. Keep inference assistance only if it lowers correction time and extra clicks on representative calls. An embedding score is not a calibrated identity probability.

## Critical verdict on the first mockup

- **Keep:** restrained native appearance, transcript-first default, optional workbench, unified Export, Undo/Redo and local call actions.
- **Improve:** dense library controls; modal-first split/assignment; edit toolbar movement; optional inspector width; timing visibility.
- **Reject:** three equal-sized Pavel/Pavel–Anna/Anna buttons below the waveform. They hide duration, suggest a sequence and do not represent overlap.
- **Reject:** guessing a split at a period or sentence midpoint. The user's caret is the text boundary.
- **Reject:** making inferred split timing look measured or making a mixed-source envelope look speaker-isolated.
- **Still validate in the native app:** docking, menus on virtualized rows, selection retention, real playback clocks, large libraries, long multiline edits, keyboard access and both PC configurations. A browser prototype alone cannot establish product quality.

## Implementation sequence and exit gates

### Phase 0 — establish real cases

Create a private representative corpus: fast sequential turns, short interruptions, overlapping remote speakers, microphone/system overlap, silence, noisy audio and similar voices. Record interaction failures. Baseline correction time, missed words, speaker errors, overlap detection, latency and peak memory on both PCs.

### Phase 1 — make current behavior safe

F01–F06, F13 and F07 defaults: coordinate settings with active jobs; settle cancellation before commit; preserve legitimate silence; handle device loss; move decoding/preprocessing off the UI thread; initialize menus correctly; remove unsafe default learning/replacement; introduce recoverable snapshots.

Exit: focused lifecycle/storage tests plus live settings, cancel, device-loss and first-click menu checks. Cancelled jobs neither report success nor commit late.

### Phase 2 — reversible editor foundation

F07, F14, F21: stable IDs; versioned schema; original recognition plus editable document; source channel separate from identity; human locks/provenance; revision-safe background results; Undo/Redo; inline typing; caret split; selection assignment; merge; missing speech; explicit audio timing adjustment.

Exit: repair one interruption and undo it without retranscribing the call. Unrelated content survives. Restart and later background analysis preserve human edits. Old calls migrate with recoverable backups.

### Phase 3 — coherent call library and delivery

F08, F18–F20: asynchronous library work; progressive filters; row actions; domain-specific header/overflow; unified exports with privacy options.

Exit: a large real library stays responsive; all actions target the intended call; exports reflect edited state. Aliases/privacy rules and Cyrillic/multiline output are tested. Do not release subtitle formats ahead of timing policy.

### Phase 4 — accurate navigation and boundary repair

F12, F15, F16: reliable transport/seek first; cached source envelopes and range playback next; aligned speaker activity lanes and word highlighting after timing validation. Run a bounded alignment/diarization investigation without committing to a model switch. Evaluate targeted retranscription/separation only on difficult overlap excerpts.

Implementation status: reliable transport, cached real microphone/system envelopes, editable-transcript speaker activity lanes, listening-range selection and preview/apply boundary gestures are implemented. Read-only bounded speaker evidence diagnostics and the assignment-loss investigation are implemented. Validated word timing and real-case model comparisons remain open; native real-call acceptance is still required. See `implementation.md` for verification and limits.

Exit: representative calls require less correction work. Seek, heard audio clock and highlighting agree. Silence highlights nothing; overlapping intervals can show multiple speakers; uncertain or untimed words remain visible.

### Phase 5 — assistance go/no-go

F17: compare optional inspector assistance with direct editing on matched real cases. Measure useful suggestions, false flags, missed short interruptions, total correction time and added interaction cost. Avoid identity learning from mixed or low-quality snippets.

Exit: keep suggestions only if they reduce net effort. Otherwise remove inference assistance from the default flow and retain explicit People management.

## Finding index

- F01: settings rebuild can abort a call transcript.
- F02: late cancellation can still commit an overwrite.
- F03: long silence moves system speech to the wrong time.
- F04: settings rebuild races dictation cleanup.
- F05: microphone loss can leave recording state active.
- F06: initial call preprocessing runs on the UI thread.
- F07: a local correction defaults to broad permanent changes; recovery is missing.
- F08: synchronous library work/polling limits responsiveness at scale.
- F09: scope the work around the mature daily-use Calls product.
- F10: general file-import expansion is deferred.
- F11: whole-audio arrays increase processing memory demand.
- F12: a single-speaker segment model loses rapid turn changes/overlap.
- F13: transcript context menu requires a second right-click.
- F14: editing needs a caret/selection command model.
- F15: playback lacks exact seek/resume and bounded range navigation.
- F16: row-width sweep does not represent spoken text.
- F17: voice helper must earn its screen space.
- F18: library needs local actions and explicit filters.
- F19: Export must unify files, clipboard and privacy choices.
- F20: header and overflow mix action domains.
- F21: stable identity, provenance and revisions are prerequisites for safe editing.

Full evidence and detailed acceptance criteria: `audit.html`. This plan refines interaction decisions without replacing the technical audit.


## UI consistency revision after native usage feedback

The nine screenshot findings are incorporated into the current implementation. Use a small consistent desktop control vocabulary: 32 DIP commands and inputs, square icon targets, centered 24 DIP inline timestamp controls, solid transient surfaces, and compact dialog action footers. Preserve Fluent control templates and theme/focus behavior; sizing is reusable attached behavior rather than a replacement visual theme.

Menus measure before opening and use stable anchor placements without popup animation. Export uses a vector arrow and opaque bounded flyout. Dialog fields are separate from informational detail cards; input dialogs have one heading, an unframed field and a content-sized action footer. Consent/onboarding/diagnostic windows retain their distinct purposes while sharing surface/action rules.

The transport stays on one row at normal editor widths, with a deliberate two-row compact fallback. Follow and excerpt details live in transport overflow; a visible exit action restores whole-call playback. The waveform expands above the dock. Header Notes and document actions explicitly select their corresponding Undo/Redo context, including after a timeline timing commit.

Native acceptance still includes screen-edge menu placement, per-monitor DPI, keyboard/focus traversal, long translated dialog actions, clipboard/file delivery and audible playback on representative calls. Offscreen layout probes are evidence of layout and theme composition, not a completed production-app trial.


## Second native UI feedback revision

Use a stable, nonanimated context-menu shell: WPF PopupAnimation alone does not disable a library template's independent motion. Anchor flyouts to their actual invoking controls with a consistent gap. Audio inputs use `mm:ss.fff`; moving waveform readouts stay with the pointer. Space captions, inputs and action rows explicitly.

Keep paired Back/Forward five-second controls and a direct Follow toggle. Consolidate timeline mode/commands, with explanations behind the help icon. Checked icon foregrounds inherit checked-state contrast. Date filters share one equal-column row.

Find/Replace has a visible header command and Ctrl+F/Ctrl+H. Keep literal search, navigation/count, replacement scope and one-transaction bulk Undo clear. Preserve native keyboard activation and user selections; dictionary and similar-form changes stay explicit in the separate correction workflow.
