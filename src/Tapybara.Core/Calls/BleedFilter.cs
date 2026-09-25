using Tapybara.Core.Audio;
using Tapybara.Core.Speech;

namespace Tapybara.Core.Calls;

/// <summary>
/// Отсев чужой речи, просочившейся в микрофонный канал.
/// </summary>
/// <remarks>
/// <para>
/// Без наушников голоса собеседников идут из колонок обратно в микрофон.
/// Whisper распознаёт их как речь владельца микрофона, и в транскрипте
/// появляются реплики, которых человек не говорил, — приписанные ему.
/// </para>
/// <para>
/// Два независимых признака, потому что ни один не ловит всё:
/// </para>
/// <list type="number">
/// <item>
/// <b>Текстовое эхо</b> — одна и та же фраза распозналась в обоих каналах.
/// Надёжно, когда просочившийся звук достаточно разборчив.
/// </item>
/// <item>
/// <b>Энергетика</b> — просочившаяся речь заметно тише собственной, а системный
/// канал в этот момент активен. Ловит случаи, где тексты двух каналов
/// разошлись и сравнение строк бессильно.
/// </item>
/// </list>
/// <para>
/// Логика перенесена из macOS-оригинала: это отлаженное решение, а не вывод
/// из первых принципов.
/// </para>
/// </remarks>
public static class BleedFilter
{
    /// <summary>Насколько тише собственной речи должна быть просочившаяся.</summary>
    private const double DefaultMarginDb = 6.0;

    /// <summary>Порог, выше которого системный канал считается активным.</summary>
    private const double SystemActiveDb = -15.0;

    /// <summary>Доля перекрытия, начиная с которой сегменты считаются одновременными.</summary>
    private const double OverlapShare = 0.5;

    /// <summary>Сходство текстов, начиная с которого это одна и та же фраза.</summary>
    private const double SimilarityThreshold = 0.6;

    /// <summary>Со скольких слов фразу ищем внутри длинной чужой реплики, а не сравниваем целиком.</summary>
    private const int MinWordsForPart = 3;

    /// <summary>Итог отсева: что оставить и сколько чего убрано.</summary>
    public sealed record Result(
        IReadOnlyList<TranscriptSegment> Kept,
        int RemovedByText,
        int RemovedByEnergy)
    {
        public int RemovedTotal => RemovedByText + RemovedByEnergy;
    }

    /// <summary>Убрать из микрофонного канала то, что на самом деле сказали собеседники.</summary>
    /// <param name="micSegments">Распознанное в микрофонном канале.</param>
    /// <param name="systemSegments">Распознанное в системном канале.</param>
    /// <param name="micEnergy">Огибающая громкости микрофонного канала.</param>
    /// <param name="systemEnergy">Огибающая громкости системного канала.</param>
    /// <param name="marginDb">Порог разницы уровней в децибелах.</param>
    public static Result Apply(
        IReadOnlyList<TranscriptSegment> micSegments,
        IReadOnlyList<TranscriptSegment> systemSegments,
        EnergyEnvelope micEnergy,
        EnergyEnvelope systemEnergy,
        double marginDb = DefaultMarginDb)
    {
        if (micSegments.Count == 0)
        {
            return new Result([], 0, 0);
        }

        double[] micLevels = [.. micSegments.Select(s => micEnergy.LevelDb(s.Start, s.End))];
        double[] systemLevels = [.. systemSegments.Select(s => systemEnergy.LevelDb(s.Start, s.End))];

        // Опорная громкость каждого канала — 85-й перцентиль его сегментов.
        // Сравнивать абсолютные уровни нельзя: каналы усилены по-разному, и
        // «тихий» микрофон может быть просто тихо настроенным.
        double micReference = Percentile(micLevels, 85) ?? -40;
        double systemReference = Percentile(systemLevels, 85) ?? -40;

        var kept = new List<TranscriptSegment>(micSegments.Count);
        int byText = 0;
        int byEnergy = 0;

        for (int i = 0; i < micSegments.Count; i++)
        {
            TranscriptSegment segment = micSegments[i];

            if (IsTextEcho(segment, systemSegments))
            {
                byText++;
                continue;
            }

            double micRelative = micLevels[i] - micReference;
            double systemRelative = systemEnergy.LevelDb(segment.Start, segment.End) - systemReference;

            bool systemActive = systemRelative > SystemActiveDb;
            bool muchQuieter = micRelative - systemRelative < -marginDb;

            if (systemActive && muchQuieter)
            {
                byEnergy++;
                continue;
            }

            kept.Add(segment);
        }

        return new Result(kept, byText, byEnergy);
    }

    /// <summary>
    /// Совпала ли фраза с тем, что в то же время звучало в другом канале.
    /// </summary>
    /// <remarks>
    /// Эхо повторяет не всю чужую реплику, а её кусок: у собеседника сегмент
    /// на двадцать секунд, в микрофон из колонок разборчиво долетела одна фраза
    /// из середины. Поэтому фраза сравнивается с каждым отрезком чужого текста
    /// той же длины в словах, а не с сегментом целиком. Чужой текст — всё, что
    /// звучало одновременно, даже если это конец одного сегмента и начало
    /// следующего.
    /// </remarks>
    private static bool IsTextEcho(TranscriptSegment segment, IReadOnlyList<TranscriptSegment> others)
    {
        double duration = Math.Max((segment.End - segment.Start).TotalSeconds, 0.1);
        double overlapped = 0;
        var heard = new List<string>();

        foreach (TranscriptSegment other in others)
        {
            // Дальние по времени отсеиваем ДО сравнения строк: расстояние
            // Левенштейна стоит квадрат длины, а на часовом разговоре пар
            // «каждый с каждым» набираются десятки тысяч.
            if (other.Start > segment.End)
            {
                break;
            }

            double overlap = Math.Min(segment.End.TotalSeconds, other.End.TotalSeconds)
                             - Math.Max(segment.Start.TotalSeconds, other.Start.TotalSeconds);

            if (overlap > 0)
            {
                overlapped += overlap;
                heard.AddRange(Words(other.Text));
            }
        }

        if (overlapped / duration < OverlapShare)
        {
            return false;
        }

        string[] said = Words(segment.Text);
        if (said.Length == 0)
        {
            return false;
        }

        // Короткое «да» или «вот» найдётся в любой длинной реплике — такие
        // фразы считаются эхом, только если чужой текст ими и исчерпывается.
        if (said.Length < MinWordsForPart)
        {
            return Similarity(string.Join(' ', said), string.Join(' ', heard)) >= SimilarityThreshold;
        }

        // Меньше половины слов фразы вообще не звучало в чужом канале — дальше
        // не считаем: это отсекает почти всю свою речь до дорогого сравнения.
        var heardSet = new HashSet<string>(heard, StringComparer.Ordinal);
        if (said.Count(heardSet.Contains) * 2 < said.Length)
        {
            return false;
        }

        // Распознавание в двух каналах режет слова по-разному, поэтому окно
        // берётся на слово короче и на слово длиннее фразы.
        string phrase = string.Join(' ', said);
        for (int size = said.Length - 1; size <= said.Length + 1; size++)
        {
            for (int from = 0; from + size <= Math.Max(heard.Count, size); from++)
            {
                string part = string.Join(' ', heard.Skip(from).Take(size));
                if (Similarity(phrase, part) >= SimilarityThreshold)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static double? Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
        {
            return null;
        }

        double[] sorted = [.. values.Order()];
        double position = (percentile / 100.0) * (sorted.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);

        return lower == upper
            ? sorted[lower]
            : sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }

    /// <summary>Слова фразы: только буквы и цифры, нижний регистр.</summary>
    private static string[] Words(string text) =>
        new string([.. text.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).Select(char.ToLowerInvariant)])
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Схожесть двух строк, 0..1.
    /// </summary>
    /// <remarks>
    /// Нормализованное расстояние Левенштейна. Точного аналога питоновского
    /// SequenceMatcher в .NET нет, но для порога «это одна и та же фраза»
    /// разница между метриками несущественна.
    /// </remarks>
    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        int distance = previous[b.Length];
        return 1.0 - ((double)distance / Math.Max(a.Length, b.Length));
    }
}
