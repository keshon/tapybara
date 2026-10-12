namespace Tapybara.Core.Calls;

/// <summary>Group voice embeddings independently of existing machine-assigned labels.</summary>
public static class SpeakerReanalysis
{
    // A conservative starting threshold, not a probability or a count of people.
    public const double GroupSimilarity = .72;
    public sealed record Group(IReadOnlyList<CallLine> Lines, IReadOnlyList<CallLine> Examples, string? SuggestedName);
    public sealed record Assignment(IReadOnlyList<CallLine> Lines, string Voice);
    public sealed record Report(IReadOnlyList<Group> Groups, int Skipped)
    {
        public IReadOnlyList<CallLine> MixedLines { get; init; } = [];
    }

    public static CallTranscript Scope(CallTranscript transcript, IReadOnlyList<string> voices) => transcript with
    {
        Lines = transcript.Lines.Where(l => l.HumanEdits.HasFlag(CallLineEdits.Speaker)
            || (l.Channel == CallChannel.Theirs && (voices.Contains(l.Voice ?? CallVoices.WholeOtherSide)
                || transcript.Lines.Any(source => source.Channel == l.Channel
                    && voices.Contains(source.Voice ?? CallVoices.WholeOtherSide)
                    && source.Start < l.End && source.End > l.Start)))).ToList(),
    };

    public static bool NeedsAttention(Group group, Func<string, string?> nameOf) =>
        string.IsNullOrWhiteSpace(group.SuggestedName) || group.Lines.Any(line => line.IsDoubtful
            || line.Voice is null || !string.Equals(nameOf(line.Voice), group.SuggestedName, StringComparison.OrdinalIgnoreCase));

    public static Report Analyse(CallTranscript transcript, Func<CallLine, float[]?> printOf,
        Func<string, string?> nameOf, Func<float[], string?> rememberedName,
        IProgress<int>? progress = null, CancellationToken token = default)
    {
        var eligible = SpeakerCorrection.Examples(transcript);
        var measured = new List<(CallLine Line, float[] Print)>();
        for (int i = 0; i < eligible.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            if (printOf(eligible[i]) is { Length: > 0 } raw)
            {
                var print = VoiceBook.Normalize(raw);
                if (print.All(float.IsFinite) && print.Any(v => v != 0)) measured.Add((eligible[i], print));
            }
            progress?.Report((i + 1) * 80 / Math.Max(eligible.Count, 1));
        }
        var references = measured.Where(m => m.Line.HumanEdits.HasFlag(CallLineEdits.Speaker)
                && m.Line.Voice is not null && nameOf(m.Line.Voice) is not null)
            .GroupBy(m => nameOf(m.Line.Voice!)!)
            .ToDictionary(g => g.Key, g => VoiceCheck.Centre(g.Select(m => m.Print).ToList()));
        var clusters = new List<List<(CallLine Line, float[] Print)>>();
        // Complete-link membership prevents an intermediate voice from joining two dissimilar voices.
        foreach (var item in measured.Where(m => !m.Line.HumanEdits.HasFlag(CallLineEdits.Speaker)))
        {
            token.ThrowIfCancellationRequested();
            var nearest = clusters.Select(c => (Cluster: c, Score: c.Min(m => VoiceBook.Similarity(m.Print, item.Print))))
                .Where(c => c.Score >= GroupSimilarity).OrderByDescending(c => c.Score).FirstOrDefault();
            if (nearest.Cluster is null) clusters.Add([item]);
            else nearest.Cluster.Add(item);
        }
        var groups = new List<Group>();
        foreach (var cluster in clusters)
        {
            token.ThrowIfCancellationRequested();
            float[] centre = VoiceCheck.Centre(cluster.Select(c => c.Print).ToList());
            var matches = references.Select(r => (Name: r.Key, Score: VoiceBook.Similarity(centre, r.Value)))
                .OrderByDescending(r => r.Score).ToList();
            string? suggestion = matches.Count > 0 && matches[0].Score >= GroupSimilarity
                && (matches.Count == 1 || matches[0].Score - matches[1].Score >= VoiceCheck.MoveLead)
                ? matches[0].Name : references.Count == 0 ? rememberedName(centre) : null;
            // First, middle and last give the listener a chance to notice a mixed group.
            var ordered = cluster.Select(c => c.Line).OrderBy(l => l.Start).ToList();
            groups.Add(new(ordered, new[] { ordered[0], ordered[ordered.Count / 2], ordered[^1] }.Distinct().ToList(), suggestion));
        }
        progress?.Report(100);
        return new(groups.OrderByDescending(g => g.Lines.Any(l => l.IsDoubtful))
            .ThenByDescending(g => g.Lines.Count).ToList(), transcript.Lines.Count(l => l.Channel == CallChannel.Theirs
                && !l.HumanEdits.HasFlag(CallLineEdits.Speaker)) - groups.Sum(g => g.Lines.Count))
        { MixedLines = transcript.Lines.Where(l => l.SpeechMix != Tapybara.Core.Speech.SpeechMix.None).ToList() };
    }

    public static CallTranscript Apply(CallTranscript transcript, IReadOnlyList<Assignment> assignments)
    {
        var selected = new Dictionary<CallLine, string>();
        foreach (var assignment in assignments)
        foreach (var line in assignment.Lines)
        {
            if (string.IsNullOrWhiteSpace(assignment.Voice) || !transcript.Lines.Contains(line)
                || line.Channel != CallChannel.Theirs || line.HumanEdits.HasFlag(CallLineEdits.Speaker)
                || !selected.TryAdd(line, assignment.Voice))
                throw new InvalidOperationException("These proposals changed; analyse again.");
        }
        return transcript with { EditedByHand = true, Lines = transcript.Lines.Select(l => selected.TryGetValue(l, out var voice)
            ? l with { Voice = voice, Fit = null, HumanEdits = l.HumanEdits | CallLineEdits.Speaker } : l).ToList() };
    }
}
