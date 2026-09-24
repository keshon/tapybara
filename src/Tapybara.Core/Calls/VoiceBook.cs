using System.Text.Json;
using System.Text.Json.Serialization;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Calls;

/// <summary>Кто, вероятно, говорит этим голосом.</summary>
/// <param name="Name">Имя из книги голосов.</param>
/// <param name="Score">Сходство слепков, 0–1.</param>
public sealed record VoiceMatch(string Name, double Score);

/// <summary>Запомненные голоса одного человека.</summary>
public sealed record PersonVoice
{
    /// <summary>Слепки с разных звонков, свежие последними.</summary>
    public IReadOnlyList<float[]> Prints { get; init; } = [];

    /// <summary>Когда слепок добавлялся последний раз.</summary>
    public DateTimeOffset Updated { get; init; }
}

/// <summary>
/// Книга голосов: как звучат люди, с которыми вы разговариваете.
/// </summary>
/// <remarks>
/// <para>
/// Назвали голос на звонке — его слепок запоминается под этим именем. На
/// следующем звонке голос сравнивается с книгой, и панель голосов говорит
/// «похоже на Кирилла». Именно «похоже», а не подпись: решает по-прежнему
/// человек, и неподтверждённая догадка не попадает в транскрипт. Раньше
/// имена раздавались голосам наугад, и повторять ту же ошибку с машинной
/// уверенностью было бы хуже.
/// </para>
/// <para>
/// Слепков у человека несколько: голос в гарнитуре, с телефона в машине и
/// простуженный — это разные слепки одного человека. Сравниваем с лучшим из
/// них, храним несколько последних.
/// </para>
/// <para>
/// Это биометрия. Файл лежит рядом с настройками и уходит с машины, только
/// если человек сам выгрузит словарь (и то при включённом запоминании);
/// запоминание выключается в настройках, человек забывается по кнопке, вся
/// книга — одной кнопкой.
/// </para>
/// </remarks>
public sealed class VoiceBook
{
    /// <summary>
    /// Сколько слепков помнить на человека.
    /// </summary>
    /// <remarks>
    /// Восемь — несколько разных условий записи (гарнитура, ноутбук, телефон)
    /// с запасом. Больше — книга растёт, а узнавание не улучшается: старые
    /// слепки описывают те же условия, что и новые.
    /// </remarks>
    public const int PrintsPerPerson = 8;

    /// <summary>
    /// С какого сходства предлагать имя.
    /// </summary>
    /// <remarks>
    /// Замерено на CAM++ (multilingual) по записям автора, <c>bench voiceprint</c>:
    /// один человек на двух разных звонках — 0,81, две половины одной записи —
    /// 0,91; разные люди (свой канал против чужого на двух звонках) — 0,25–0,35.
    /// Порог стоит ближе к «разным», чем к «одному», но с запасом от обоих:
    /// пропущенная подсказка стоит человеку одного щелчка, а ложная — чужого
    /// имени в транскрипте, если он её примет не глядя. Выборка маленькая —
    /// перемерить на новых голосах, прежде чем двигать.
    /// </remarks>
    public const double MatchThreshold = 0.62;

    /// <summary>
    /// На сколько лучший кандидат должен обходить второго.
    /// </summary>
    /// <remarks>
    /// Два похожих голоса (братья, коллеги из одного города) дают близкое
    /// сходство с обоими. Подсказывать в таком случае одно из имён — снова
    /// угадывание; лучше промолчать.
    /// </remarks>
    public const double MatchMargin = 0.05;

    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private Dictionary<string, PersonVoice> _people;

    public VoiceBook(string path)
    {
        _path = path;
        _people = Read(path);
    }

    /// <summary>Книга изменилась: кого-то запомнили или забыли.</summary>
    public event Action? Changed;

    /// <summary>Чьи голоса запомнены.</summary>
    public IReadOnlyCollection<string> People
    {
        get
        {
            lock (_gate)
            {
                return [.. _people.Keys];
            }
        }
    }

    /// <summary>Сколько слепков запомнено для человека.</summary>
    public int PrintsOf(string name)
    {
        lock (_gate)
        {
            return _people.TryGetValue(name, out PersonVoice? voice) ? voice.Prints.Count : 0;
        }
    }

    /// <summary>Запомнить слепок под именем.</summary>
    public void Learn(string name, float[] print)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(print);

        lock (_gate)
        {
            List<float[]> prints = _people.TryGetValue(name, out PersonVoice? known) ? [.. known.Prints] : [];

            // Тот же слепок второй раз (человек снял имя и поставил снова) не
            // должен вытеснять из книги другие условия записи.
            if (prints.Any(p => Similarity(p, print) > 0.999))
            {
                return;
            }

            prints.Add(print);
            if (prints.Count > PrintsPerPerson)
            {
                prints.RemoveRange(0, prints.Count - PrintsPerPerson);
            }

            _people[name] = new PersonVoice { Prints = prints, Updated = DateTimeOffset.Now };
            Write();
        }

        Changed?.Invoke();
    }

    /// <summary>Забыть голос человека.</summary>
    public void Forget(string name)
    {
        lock (_gate)
        {
            if (!_people.Remove(name))
            {
                return;
            }

            Write();
        }

        Changed?.Invoke();
    }

    /// <summary>Человека переименовали — голос переезжает вместе с именем.</summary>
    public void Rename(string from, string to)
    {
        lock (_gate)
        {
            if (from == to || !_people.Remove(from, out PersonVoice? voice))
            {
                return;
            }

            if (_people.TryGetValue(to, out PersonVoice? existing))
            {
                voice = voice with { Prints = [.. existing.Prints.Concat(voice.Prints).TakeLast(PrintsPerPerson)] };
            }

            _people[to] = voice;
            Write();
        }

        Changed?.Invoke();
    }

    /// <summary>Копия книги — для выгрузки словаря в файл.</summary>
    public IReadOnlyDictionary<string, PersonVoice> Snapshot()
    {
        lock (_gate)
        {
            return new Dictionary<string, PersonVoice>(_people, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Принять голоса из файла словаря.
    /// </summary>
    /// <remarks>
    /// Слепки добавляются к своим, как при <see cref="Learn"/>: голос,
    /// выученный уже на этой машине, не затирается старым с прошлой.
    /// </remarks>
    /// <returns>Скольким людям что-то добавилось.</returns>
    public int Import(IReadOnlyDictionary<string, PersonVoice> people)
    {
        ArgumentNullException.ThrowIfNull(people);

        int touched = 0;
        lock (_gate)
        {
            foreach ((string name, PersonVoice voice) in people)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                PersonVoice? known = _people.GetValueOrDefault(name);
                List<float[]> prints = known is null ? [] : [.. known.Prints];
                int before = prints.Count;

                foreach (float[] print in voice.Prints)
                {
                    if (print.Length > 0 && !prints.Any(p => Similarity(p, print) > 0.999))
                    {
                        prints.Add(print);
                    }
                }

                if (prints.Count == before)
                {
                    continue;
                }

                if (prints.Count > PrintsPerPerson)
                {
                    prints.RemoveRange(0, prints.Count - PrintsPerPerson);
                }

                DateTimeOffset updated = known is null || voice.Updated > known.Updated ? voice.Updated : known.Updated;
                _people[name] = new PersonVoice { Prints = prints, Updated = updated };
                touched++;
            }

            if (touched > 0)
            {
                Write();
            }
        }

        if (touched > 0)
        {
            Changed?.Invoke();
        }

        return touched;
    }

    /// <summary>Забыть все голоса.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _people = new Dictionary<string, PersonVoice>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Не удалось стереть книгу голосов.", ex);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Кто, вероятно, говорит этим голосом.
    /// </summary>
    /// <param name="print">Слепок голоса со звонка.</param>
    /// <param name="exclude">Имена, которые уже заняты другими голосами этого звонка.</param>
    /// <returns><c>null</c>, если уверенного кандидата нет.</returns>
    public VoiceMatch? Match(float[] print, IEnumerable<string>? exclude = null)
    {
        ArgumentNullException.ThrowIfNull(print);

        HashSet<string> taken = new(exclude ?? [], StringComparer.OrdinalIgnoreCase);
        List<VoiceMatch> scores;
        lock (_gate)
        {
            scores =
            [
                .. _people
                    .Where(p => !taken.Contains(p.Key) && p.Value.Prints.Count > 0)
                    .Select(p => new VoiceMatch(p.Key, p.Value.Prints.Max(known => Similarity(known, print))))
                    .OrderByDescending(m => m.Score),
            ];
        }

        return Pick(scores);
    }

    /// <summary>Выбрать кандидата: выше порога и заметно лучше второго.</summary>
    internal static VoiceMatch? Pick(IReadOnlyList<VoiceMatch> scores)
    {
        if (scores.Count == 0 || scores[0].Score < MatchThreshold)
        {
            return null;
        }

        if (scores.Count > 1 && scores[0].Score - scores[1].Score < MatchMargin)
        {
            return null;
        }

        return scores[0];
    }

    /// <summary>Косинусное сходство. Для нормированных слепков — скалярное произведение.</summary>
    public static double Similarity(float[] a, float[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Length != b.Length || a.Length == 0)
        {
            return 0; // слепки разных моделей несравнимы
        }

        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return na > 0 && nb > 0 ? dot / Math.Sqrt(na * nb) : 0;
    }

    /// <summary>Привести вектор к единичной длине.</summary>
    public static float[] Normalize(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        double norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        return norm > 0 ? [.. vector.Select(v => (float)(v / norm))] : vector;
    }

    private static Dictionary<string, PersonVoice> Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new Dictionary<string, PersonVoice>(StringComparer.OrdinalIgnoreCase);
            }

            Dictionary<string, PersonVoice>? people =
                JsonSerializer.Deserialize<Dictionary<string, PersonVoice>>(File.ReadAllText(path), Options);

            return new Dictionary<string, PersonVoice>(people ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось прочитать книгу голосов — начинаю с пустой.", ex);
            return new Dictionary<string, PersonVoice>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Write()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_people, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Не удалось сохранить книгу голосов.", ex);
        }
    }
}
