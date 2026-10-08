namespace Tapybara.Core.Export;

public enum TranscriptExportFormat { Text, Markdown, Json }
public enum TranscriptSpeakerLabels { Names, Aliases, None }

/// <summary>Настройки внешней копии, не меняющие документ и исходное распознавание.</summary>
public sealed record TranscriptExportOptions
{
    public TranscriptExportFormat Format { get; init; }
    public TranscriptSpeakerLabels Labels { get; init; }
    public bool Timestamps { get; init; } = true;
    public bool ReplaceMentions { get; init; } = true;
    public IReadOnlyDictionary<string, string> Aliases { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<TranscriptExportSelection>? Selection { get; init; }
}

public sealed record TranscriptExportSelection(Guid Id, int Start, int Length);
