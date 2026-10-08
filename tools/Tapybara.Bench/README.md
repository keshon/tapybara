# Speaker attribution diagnostics

Run from the repository after building the solution:

```powershell
dotnet tools/Tapybara.Bench/bin/Debug/net10.0-windows/Tapybara.Bench.dll speaker-audit "E:/path/to/call" 25 30 --speakers 2 > temp/speaker-audit.json
```

Positional numbers are start and duration in seconds, using a decimal point. Duration must be positive and at most 120 seconds. The source must be the captured 16 kHz mono `system.wav`, with a readable schema 1 or 2 `transcript.json`. Invalid input returns a nonzero exit code. Create the output directory first.

The command reads transcript JSON directly, avoiding the normal loader's migration writes. It does not repair WAV headers, persist inference, alter speaker names, or change manual corrections. Output omits transcript text and speaker names; GUIDs, times and model evidence remain local diagnostic data.

The diagnostic uses the installed segmentation/embedding model names resolved by the existing model locator, production option defaults and normalization. It does not read user-specific diarizer settings. `--speakers` supplies a cluster count; omit it for threshold clustering, optionally with `--cluster-threshold 0.6`. A short excerpt has less identity context than full-call processing, and cluster numbers cannot be compared between runs or mapped directly to the call's voice letters.

JSON records raw spans with restored call offsets, overlapping transcript lines, partial-window flags, human-speaker locks, the current majority-overlap assignment, per-cluster union durations, covered duration, simultaneous duration and interval sets. Durations are TimeSpan strings; top-level and line bounds are seconds. Duplicate spans of one cluster never count as two simultaneous speakers. Multiple clusters across a sentence can be sequential turns without any simultaneous speech. These are model outputs, not confirmed identities, probabilities or ground truth.

A partial line is measured only within the excerpt. The original line bounds remain visible, but its majority winner describes only the intersecting window. Short turns remain visible without a reporting threshold.

The next experiment is word timing on a confirmed interruption excerpt, followed by a comparison of audio, recognized words and speaker spans. The current production recognizer returns segment-level text; this diagnostic does not invent text splits or recover missing speech.
