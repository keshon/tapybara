using System.Text;
using Tapybara.Core.Export;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tapybara.Core.Calls;

/// <summary>TXT, Markdown и внешний JSON: только выбранные текущие данные, без baseline и слепков.</summary>
public static class CallExport
{
    private sealed record ExportLine(string? Speaker, string Text, TimeSpan? Start, TimeSpan? End, bool? Estimated);
    private sealed record ExportDocument(string Schema, int Version, long Revision, IReadOnlyList<ExportLine> Lines);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Внешняя копия не должна подменять исходные данные записи или их резервные версии.</summary>
    public static void ValidateDestination(CallSession session, string destination)
    {
        string path = Path.GetFullPath(destination);
        foreach (string name in new[] { CallSession.MetaFileName, CallSession.TranscriptDataFileName,
            CallSession.TranscriptFileName, CallSession.MicFileName, CallSession.SystemFileName, CallLibrary.NoteFileName })
        {
            string source = Path.GetFullPath(Path.Combine(session.Directory, name));
            if (path.Equals(source, StringComparison.OrdinalIgnoreCase)
                || path.Equals(source + ".previous", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Export destination would replace recording data.", nameof(destination));
            }
        }
    }

    /// <summary>Говорящие и известные упомянутые люди, включая владельца без собственной реплики.</summary>
    public static IReadOnlyList<string> AliasNames(CallSession session, CallTranscript document, IEnumerable<string> knownNames,
        string myName, string fallback, string voiceLabel)
    {
        List<string> names = [.. document.Lines.Select(line => CallSpeakers.Label(line, session,
            document.Voices.Count, myName, fallback, voiceLabel)).Distinct(StringComparer.OrdinalIgnoreCase)];
        IEnumerable<string> candidates = knownNames.Concat(session.Participants).Concat(session.VoiceNames.Values).Append(myName);
        names.AddRange(candidates.Where(n => n != CallSession.Me && !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase)
                && PersonNames.Find([document], n).Count > 0));
        return names;
    }

    public static string Render(CallSession session, CallTranscript document, TranscriptExportOptions options,
        string myName, string fallback, string voiceLabel)
    {
        List<CallLine> chosen = options.Selection is null ? [.. document.Lines] :
            [.. options.Selection.Select(selection =>
            {
                CallLine line = document.Lines.Single(l => l.Id == selection.Id);
                ArgumentOutOfRangeException.ThrowIfNegative(selection.Start);
                ArgumentOutOfRangeException.ThrowIfNegative(selection.Length);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(selection.Start + selection.Length, line.Text.Length);
                return line with { Text = line.Text.Substring(selection.Start, selection.Length),
                    TimingEstimated = line.TimingEstimated || selection.Start != 0 || selection.Length != line.Text.Length };
            })];
        var selected = document with { Lines = chosen };
        var aliases = new Dictionary<string, string>(options.Aliases, StringComparer.OrdinalIgnoreCase);
        if (options.Labels == TranscriptSpeakerLabels.Aliases)
        {
            var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, string alias) in aliases)
            {
                if (string.IsNullOrWhiteSpace(alias) || aliases.ContainsKey(alias.Trim()) || !usedAliases.Add(alias.Trim()))
                {
                    throw new ArgumentException("Aliases must be unique and different from known names.", nameof(options));
                }
            }
            foreach (CallLine line in document.Lines)
            {
                string name = CallSpeakers.Label(line, session, document.Voices.Count, myName, fallback, voiceLabel);
                if (!aliases.TryGetValue(name, out string? alias) || string.IsNullOrWhiteSpace(alias)
                    || name.Equals(alias.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Every speaker requires a separate alias.", nameof(options));
                }
            }
            if (options.ReplaceMentions)
            {
                selected = PersonNames.Anonymize(session, selected, aliases, mentions: true).Transcript;
            }
        }
        List<ExportLine> lines = [.. selected.Lines.OrderBy(l => l.Start).Select(line =>
        {
            string name = CallSpeakers.Label(line, session, document.Voices.Count, myName, fallback, voiceLabel);
            string? speaker = options.Labels switch
            {
                TranscriptSpeakerLabels.None => null,
                TranscriptSpeakerLabels.Aliases => aliases[name].Trim(),
                _ => name,
            };
            return new ExportLine(speaker, line.Text, options.Timestamps ? line.Start : null,
                options.Timestamps ? line.End : null, options.Timestamps ? line.TimingEstimated : null);
        })];
        if (options.Format == TranscriptExportFormat.Json)
        {
            return JsonSerializer.Serialize(new ExportDocument("tapybara.transcript-export", 1, document.Revision, lines), JsonOptions);
        }
        var text = new StringBuilder();
        foreach (ExportLine line in lines)
        {
            if (line.Start is { } start)
            {
                text.Append('[').Append(line.Estimated == true ? "~" : string.Empty)
                    .Append(CallTranscriptRenderer.Stamp(start)).Append("] ");
            }
            if (line.Speaker is { } name)
            {
                text.Append(options.Format == TranscriptExportFormat.Markdown ? "**" + Escape(name) + ":** " : name + ": ");
            }
            text.AppendLine(options.Format == TranscriptExportFormat.Markdown ? Escape(line.Text) : line.Text).AppendLine();
        }
        return text.ToString();
    }

    private static string Escape(string text)
    {
        foreach (char c in "\\`*_{}[]<>#|!")
        {
            text = text.Replace(c.ToString(), "\\" + c, StringComparison.Ordinal);
        }
        return text;
    }
}
