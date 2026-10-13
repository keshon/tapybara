namespace Tapybara.Core.Calls;

/// <summary>Same-call voice matches are suggestions, never automatic identity changes.</summary>
public static class SpeakerNameSuggestions
{
    public static IReadOnlyDictionary<string, VoiceMatch> Find(CallSession session, CallTranscript transcript)
    {
        var named = transcript.VoicePrints.Where(p => CallSpeakers.NameOf(session, p.Key) is not null
            && transcript.Lines.Any(l => l.Voice == p.Key && l.HumanEdits.HasFlag(CallLineEdits.Speaker)))
            .GroupBy(p => CallSpeakers.NameOf(session, p.Key)!, StringComparer.OrdinalIgnoreCase).ToList();
        var result = new Dictionary<string, VoiceMatch>();
        foreach (var (voice, print) in transcript.VoicePrints)
        {
            if (CallSpeakers.NameOf(session, voice) is not null) continue;
            var scores = named.Select(g => new VoiceMatch(g.Key, g.Max(p => VoiceBook.Similarity(p.Value, print))))
                .OrderByDescending(m => m.Score).ToList();
            if (VoiceBook.Pick(scores) is { } match) result[voice] = match;
        }
        return result;
    }
}
