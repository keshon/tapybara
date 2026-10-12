using Tapybara.Core.Speech;

namespace Tapybara.Core.Calls;

/// <summary>Review-only suggestions based on explicitly confirmed examples in this call.</summary>
public static class SpeakerCorrection
{
    public sealed record Candidate(CallLine Line, double Similarity, double Lead, bool Clear);

    public static IReadOnlyList<CallLine> Examples(CallTranscript transcript) => transcript.Lines
        .Where(line => line.Channel == CallChannel.Theirs && line.SpeechMix == SpeechMix.None && line.End - line.Start >= VoiceCheck.MinLine
            && !transcript.Lines.Any(other => !other.IsSameAs(line) && other.Channel == line.Channel
                && other.Start < line.End && other.End > line.Start))
        .OrderByDescending(line => line.IsDoubtful).ThenBy(line => line.Start).ToList();

    public static CallTranscript Confirm(CallTranscript transcript, CallLine example, string voice)
    {
        if (!Examples(transcript).Contains(example))
            throw new InvalidOperationException("The example changed or is unsuitable; choose another phrase.");
        return CallVoices.Reassign(transcript, example, voice);
    }

    public static IReadOnlyList<Candidate> Find(CallTranscript transcript, CallLine seed,
        Func<CallLine, float[]?> printOf, CancellationToken cancellationToken = default)
    {
        if (seed.Channel != CallChannel.Theirs || seed.Voice is null
            || seed.SpeechMix != SpeechMix.None
            || seed.End - seed.Start < VoiceCheck.MinLine || !transcript.Lines.Contains(seed)
            || transcript.Lines.Any(other => !other.IsSameAs(seed) && other.Channel == seed.Channel
                && other.Start < seed.End && other.End > seed.Start))
        {
            throw new ArgumentException("Choose at least four seconds of one speaker's speech.", nameof(seed));
        }
        var confirmed = transcript.Lines.Where(l => l.Channel == CallChannel.Theirs
            && l.Voice is not null && l.SpeechMix == SpeechMix.None && l.End - l.Start >= VoiceCheck.MinLine
            && (l.IsSameAs(seed) || l.HumanEdits.HasFlag(CallLineEdits.Speaker))
            && !transcript.Lines.Any(other => !other.IsSameAs(l) && other.Channel == l.Channel
                && other.Start < l.End && other.End > l.Start)).ToList();
        var prints = new List<(CallLine Line, float[] Print)>();
        foreach (CallLine line in confirmed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (printOf(line) is { Length: > 0 } print)
                prints.Add((line, VoiceBook.Normalize(print)));
        }
        if (!prints.Any(p => p.Line.IsSameAs(seed)))
            throw new InvalidOperationException("The example does not contain enough clear speech.");
        var centres = prints.GroupBy(p => p.Line.Voice!)
            .ToDictionary(g => g.Key, g => VoiceCheck.Centre(g.Select(p => p.Print).ToList()));
        float[] target = centres[seed.Voice];
        var result = new List<Candidate>();
        foreach (CallLine line in transcript.Lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Channel != CallChannel.Theirs || line.SpeechMix != SpeechMix.None || line.Voice == seed.Voice || line.IsSameAs(seed)
                || line.HumanEdits.HasFlag(CallLineEdits.Speaker) || line.End - line.Start < VoiceCheck.MinLine)
                continue;
            // Overlap cannot provide a reliable single-speaker example.
            if (transcript.Lines.Any(other => !other.IsSameAs(line) && other.Channel == line.Channel
                && other.Start < line.End && other.End > line.Start)) continue;
            if (printOf(line) is not { Length: > 0 } raw) continue;
            float[] print = VoiceBook.Normalize(raw);
            double similarity = VoiceBook.Similarity(print, target);
            double rival = centres.Where(c => c.Key != seed.Voice)
                .Select(c => VoiceBook.Similarity(print, c.Value)).DefaultIfEmpty(0).Max();
            double lead = similarity - rival;
            if (similarity >= VoiceCheck.Sure && lead > 0)
                result.Add(new(line, similarity, lead, centres.Count > 1 && lead >= VoiceCheck.MoveLead));
        }
        return result.OrderByDescending(c => c.Clear).ThenByDescending(c => c.Lead).ToList();
    }

    /// <summary>Reject stale proposals; one caller-owned edit transaction supplies batch undo.</summary>
    public static CallTranscript Apply(CallTranscript transcript, CallLine seed, IReadOnlyList<CallLine> selected)
    {
        if (seed.Voice is null || !transcript.Lines.Contains(seed)
            || selected.Any(line => !transcript.Lines.Contains(line) || line.Channel != CallChannel.Theirs
                || line.HumanEdits.HasFlag(CallLineEdits.Speaker)))
            throw new InvalidOperationException("The transcript changed; find matches again.");
        var lines = selected.Append(seed).ToHashSet();
        return transcript with
        {
            EditedByHand = true,
            Lines = transcript.Lines.Select(line => lines.Contains(line)
                ? line with { Voice = seed.Voice, Fit = null, HumanEdits = line.HumanEdits | CallLineEdits.Speaker }
                : line).ToList(),
        };
    }
}
