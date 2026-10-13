using Tapybara.Core.Speech;

namespace Tapybara.Core.Calls;

/// <summary>Reject unusable automatic clustering and preserve human speaker identities on retries.</summary>
public static class SpeakerSeparation
{
    // A safety boundary for unverified automatic counts, not a cap on explicit counts.
    public const int MaxAutomaticSpeakers = 12;
    public static bool NeedsCount(CallTranscript transcript) => transcript.ExpectedVoices == 0
        && transcript.SpeakerIntervals.Select(s => s.Speaker).Distinct().Count() > MaxAutomaticSpeakers;

    public static CallTranscript Apply(CallTranscript transcript, IReadOnlyList<SpeakerSpan> spans, int expected)
    {
        var result = transcript with { SpeakerIntervals = spans, ExpectedVoices = Math.Max(0, expected), VoicePrints = new Dictionary<string, float[]>() };
        if (NeedsCount(result))
            return result with { VoicesSplit = false, VoicesChecked = false,
                Lines = transcript.Lines.Select(l => l.Channel == CallChannel.Theirs && !l.HumanEdits.HasFlag(CallLineEdits.Speaker)
                    ? l with { Voice = null, Fit = null, SuggestedVoice = null, SpeechMix = SpeechMix.None } : l).ToList() };

        var assigned = CallVoices.Assign(transcript.Lines, spans);
        var identities = transcript.Lines.Where(l => l.HumanEdits.HasFlag(CallLineEdits.Speaker) && l.Voice is not null
                && SpeakerAssignment.Mix(l.Start, l.End, spans) == SpeechMix.None)
            .Select(l => (Line: l, Cluster: SpeakerAssignment.For(l.Start, l.End, spans)))
            .Where(a => a.Cluster is not null).GroupBy(a => a.Cluster!.Value)
            .Where(g => g.Select(a => a.Line.Voice).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Line.Voice!);
        return result with { VoicesSplit = true, VoicesChecked = false,
            Lines = assigned.Select(l => !l.HumanEdits.HasFlag(CallLineEdits.Speaker) && l.Channel == CallChannel.Theirs
                && SpeakerAssignment.For(l.Start, l.End, spans) is { } cluster && identities.TryGetValue(cluster, out var identity)
                    ? l with { Voice = identity, Fit = null }
                    : l.HumanEdits.HasFlag(CallLineEdits.Speaker) && l.Channel == CallChannel.Theirs
                        ? l with { SpeechMix = SpeakerAssignment.Mix(l.Start, l.End, spans) } : l).ToList() };
    }

    public static CallTranscript AnchorNames(CallTranscript transcript, IReadOnlyDictionary<string, string> names)
    {
        var anchors = transcript.Lines.Where(l => l.Channel == CallChannel.Theirs && l.Voice is not null && names.ContainsKey(l.Voice)
            && l.SpeechMix == SpeechMix.None).GroupBy(l => l.Voice)
            .Where(g => !g.Any(l => l.HumanEdits.HasFlag(CallLineEdits.Speaker)))
            .Select(g => { var quotes = CallVoices.QuotesOf(g.ToList()); return quotes.Count > 0 ? quotes[0] : null; })
            .Where(l => l is not null).ToHashSet();
        return transcript with { Lines = transcript.Lines.Select(l => anchors.Contains(l)
            ? l with { HumanEdits = l.HumanEdits | CallLineEdits.Speaker } : l).ToList() };
    }
}
