namespace Tapybara.Core.Calls;

/// <summary>Команды документа; меню, кнопки и клавиатура используют одни правила.</summary>
public static class CallDocumentCommands
{
    /// <summary>Можно ли разделить текст именно у этого пробела, оставив две непустые части.</summary>
    public static bool CanSplit(string text, int caret) => caret > 0 && caret < text.Length
        && (char.IsWhiteSpace(text[caret]) || char.IsWhiteSpace(text[caret - 1]))
        && !string.IsNullOrWhiteSpace(text[..caret]) && !string.IsNullOrWhiteSpace(text[caret..]);

    /// <summary>Разделить у каретки; без выравнивания временная граница честно отмечена приблизительной.</summary>
    public static CallTranscript Split(CallTranscript document, Guid id, int caret, TimeSpan? boundary = null)
    {
        CallLine line = Find(document, id);
        if (!CanSplit(line.Text, caret))
        {
            throw new ArgumentException("Каретка должна стоять между словами.", nameof(caret));
        }
        TimeSpan at = boundary ?? line.Start + ((line.End - line.Start) / 2);
        ValidateRange(line.Start, at, line.End);
        CallLine first = line with
        {
            Text = line.Text[..caret].TrimEnd(), End = at, TimingEstimated = true,
            SpeechMix = Tapybara.Core.Speech.SpeechMix.None,
            HumanEdits = line.HumanEdits | CallLineEdits.Structure | CallLineEdits.Text | CallLineEdits.Timing,
            Fit = null,
        };
        CallLine second = first with
        {
            Id = Guid.NewGuid(), OriginId = line.OriginId ?? line.Id,
            Text = line.Text[caret..].TrimStart(), Start = at, End = line.End,
        };
        return Replace(document, id, [first, second]);
    }

    /// <summary>Выделить выбранный текст с проверенными по аудио границами и назначить голос.</summary>
    public static CallTranscript AssignSelection(CallTranscript document, Guid id, int start, int length,
        TimeSpan from, TimeSpan to, string voice)
    {
        CallLine line = Find(document, id);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + length, line.Text.Length);
        ArgumentException.ThrowIfNullOrWhiteSpace(voice);
        if (from < line.Start || to > line.End || from < TimeSpan.Zero || from >= to)
        {
            throw new ArgumentOutOfRangeException(nameof(from));
        }
        List<CallLine> pieces = [];
        string prefix = line.Text[..start].Trim();
        string selected = line.Text.Substring(start, length).Trim();
        string suffix = line.Text[(start + length)..].Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(selected);
        CallLine Piece(string text, TimeSpan begin, TimeSpan end) => line with
        {
            Id = Guid.NewGuid(), OriginId = line.OriginId ?? line.Id,
            Text = text, Start = begin, End = end, Fit = null,
            SpeechMix = Tapybara.Core.Speech.SpeechMix.None,
            TimingEstimated = line.TimingEstimated,
            HumanEdits = line.HumanEdits | CallLineEdits.Structure | CallLineEdits.Text | CallLineEdits.Timing,
        };
        if (prefix.Length > 0)
        {
            ValidateRange(line.Start, from, line.End);
            pieces.Add(Piece(prefix, line.Start, from));
        }
        pieces.Add(Piece(selected, from, to) with
        {
            Voice = voice, TimingEstimated = false,
            HumanEdits = line.HumanEdits | CallLineEdits.Structure | CallLineEdits.Text | CallLineEdits.Timing | CallLineEdits.Speaker,
        });
        if (suffix.Length > 0)
        {
            ValidateRange(line.Start, to, line.End);
            pieces.Add(Piece(suffix, to, line.End));
        }
        return Replace(document, id, pieces);
    }

    /// <summary>Склеить соседние реплики одного источника и голоса, сохранив охватывающий интервал.</summary>
    public static CallTranscript Merge(CallTranscript document, Guid firstId, Guid secondId)
    {
        CallLine first = Find(document, firstId);
        CallLine second = Find(document, secondId);
        int index = document.Lines.ToList().FindIndex(l => l.Id == firstId);
        if (index + 1 >= document.Lines.Count || document.Lines[index + 1].Id != secondId
            || first.Channel != second.Channel || first.Voice != second.Voice)
        {
            throw new ArgumentException("Склеиваются соседние реплики одного источника и голоса.");
        }
        CallLine merged = first with
        {
            Text = first.Text.TrimEnd() + " " + second.Text.TrimStart(),
            Start = first.Start < second.Start ? first.Start : second.Start,
            End = first.End > second.End ? first.End : second.End,
            HumanEdits = first.HumanEdits | second.HumanEdits | CallLineEdits.Structure | CallLineEdits.Text | CallLineEdits.Timing,
            TimingEstimated = first.TimingEstimated || second.TimingEstimated, Fit = null,
        };
        CallTranscript updated = Replace(document, firstId, [merged]);
        return updated with
        {
            Lines = [.. updated.Lines.Where(l => l.Id != secondId)],
        };
    }

    /// <summary>Добавить отсутствующую речь; соседние интервалы не сдвигаются.</summary>
    public static CallTranscript Insert(CallTranscript document, CallChannel channel, TimeSpan from, TimeSpan to,
        string text, string? voice)
    {
        if (from < TimeSpan.Zero || from >= to)
        {
            throw new ArgumentOutOfRangeException(nameof(from));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var line = new CallLine(channel, from, to, text.Trim(), voice)
        {
            Id = Guid.NewGuid(),
            HumanEdits = CallLineEdits.Structure | CallLineEdits.Text | CallLineEdits.Timing | CallLineEdits.Speaker,
        };
        return Changed(document, [.. document.Lines.Append(line).OrderBy(l => l.Start)]);
    }

    /// <summary>Уточнить интервал без запрета реальных перекрытий.</summary>
    public static CallTranscript SetTiming(CallTranscript document, Guid id, TimeSpan from, TimeSpan to)
    {
        if (from < TimeSpan.Zero || from >= to)
        {
            throw new ArgumentOutOfRangeException(nameof(from));
        }
        CallLine line = Find(document, id);
        return Replace(document, id, [line with
        {
            Start = from, End = to, TimingEstimated = false, Fit = null,
            HumanEdits = line.HumanEdits | CallLineEdits.Timing,
        }]);
    }

    private static CallLine Find(CallTranscript document, Guid id) => id == Guid.Empty
        ? throw new ArgumentException("Нужен постоянный ключ реплики.", nameof(id))
        : document.Lines.Single(l => l.Id == id);

    private static void ValidateRange(TimeSpan from, TimeSpan at, TimeSpan to)
    {
        if (from < TimeSpan.Zero || at <= from || at >= to)
        {
            throw new ArgumentOutOfRangeException(nameof(at));
        }
    }

    private static CallTranscript Replace(CallTranscript document, Guid id, IReadOnlyList<CallLine> pieces) =>
        Changed(document, [.. document.Lines.SelectMany(l => l.Id == id ? pieces : [l]).OrderBy(l => l.Start)]);

    private static CallTranscript Changed(CallTranscript document, IReadOnlyList<CallLine> lines) => document with
    {
        Lines = lines, EditedByHand = true, VoicePrints = new Dictionary<string, float[]>(),
    };
}
