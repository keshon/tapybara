using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Whisper.net.Logger;
using Tapybara.Core.Audio;
using Tapybara.Core.Calls;
using Tapybara.Core.Settings;
using Tapybara.Core.Speech;
using Tapybara.Core.Windows;

// Консольный харнесс фазы 0: замерить скорость распознавания на этой машине
// и сравнить модели на ОДНОМ И ТОМ ЖЕ звуке.
//
//   bench rec [секунды]            — записать образец с микрофона в sample.wav
//   bench run <модель> [файл.wav]  — распознать и показать тайминги
//   bench models                   — что лежит в папке models
//
// Файл использует top-level statements: для точки входа в одну задачу класс
// с Main — лишняя церемония.

string repoRoot = FindRepoRoot();
string modelsDir = Path.Combine(repoRoot, "models");
string defaultSample = Path.Combine(repoRoot, "sample.wav");

// Флаги, после которых идёт значение. Знать их обязательно: без этого списка
// текст промпта («Разговор о…») выглядит как обычный аргумент и уезжает в
// позиционные — на этом разбор `run <модель> [файл]` и ломается.
string[] valueFlags = ["--prompt", "--vad-threshold", "--lang", "--beam", "--speakers", "--cluster-threshold"];

List<string> positionalArgs = [];
string? promptOption = null;
float? vadThreshold = null;
string language = "auto";
// null — взять продуктовое умолчание. Замер, подставляющий здесь своё
// значение, мерил бы то, чего у пользователя нет: так уже случилось с
// порогом детектора речи, где бенч тихо ставил 0.5 вместо 0.35.
int? beamSize = null;
// Сколько голосов искать при разделении. null — определять по порогу.
int? speakerCount = null;
float? clusterThreshold = null;
for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    if (!arg.StartsWith("--", StringComparison.Ordinal))
    {
        positionalArgs.Add(arg);
        continue;
    }

    if (valueFlags.Contains(arg) && i + 1 < args.Length)
    {
        if (arg == "--prompt")
        {
            promptOption = args[i + 1];
        }
        else if (arg == "--vad-threshold")
        {
            vadThreshold = float.Parse(args[i + 1], CultureInfo.InvariantCulture);
        }
        else if (arg == "--lang")
        {
            language = args[i + 1];
        }
        else if (arg == "--beam")
        {
            beamSize = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
        }
        else if (arg == "--speakers")
        {
            speakerCount = int.Parse(args[i + 1], CultureInfo.InvariantCulture);
        }
        else if (arg == "--cluster-threshold")
        {
            clusterThreshold = float.Parse(args[i + 1], CultureInfo.InvariantCulture);
        }

        i++; // значение уже забрали — позиционным оно не является
    }
}

string[] positional = [.. positionalArgs];

// --verbose включает нативный лог ggml: там видно, какой бэкенд
// инициализировался и почему не загрузился предыдущий.
if (args.Contains("--verbose"))
{
    LogProvider.AddConsoleLogging(WhisperLogLevel.Debug);
}

string command = positional.Length > 0 ? positional[0].ToLowerInvariant() : "help";

// Верхний перехват на весь разбор команд. Харнесс — инструмент отладки, и
// стек в консоли вместо внятной строки («модель не читается») отладке мешает.
try
{
    return await RunCommandAsync(command);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Ошибка: {ex.Message}");
    return 1;
}

async Task<int> RunCommandAsync(string command)
{
switch (command)
{
    case "models":
        ListModels();
        break;

    case "rec":
        await RecordSampleAsync(positional.Length > 1
            ? double.Parse(positional[1], CultureInfo.InvariantCulture)
            : 20);
        break;

    case "run":
        await RunAsync(
            positional.Length > 1 ? positional[1] : null,
            positional.Length > 2 ? positional[2] : defaultSample);
        break;

    case "dictate":
        await DictateAsync(positional.Length > 1 ? positional[1] : null);
        break;

    case "check":
        CheckWindowsIntegration();
        break;

    case "devices":
        ListDevices();
        break;

    case "transcribe":
        if (positional.Length < 2)
        {
            Console.Error.WriteLine("Укажи папку звонка: bench transcribe <папка>");
            return 1;
        }

        await TranscribeCallAsync(positional[1]);
        break;

    case "vad":
        if (positional.Length < 2)
        {
            Console.Error.WriteLine("Укажи файл: bench vad <файл.wav>");
            return 1;
        }

        await InspectVadAsync(positional[1]);
        break;

    case "diarize":
        if (positional.Length < 2)
        {
            Console.Error.WriteLine("Укажи файл: bench diarize <файл.wav> [--speakers N]");
            return 1;
        }

        SplitVoices(positional[1], speakerCount ?? 0, clusterThreshold);
        break;

    case "voiceprint":
        if (positional.Length < 2)
        {
            Console.Error.WriteLine("Укажи файл: bench voiceprint <a.wav> [b.wav]");
            return 1;
        }

        CompareVoices(positional[1], positional.Length > 2 ? positional[2] : null);
        break;

    case "call":
        await RecordCallAsync(positional.Length > 1
            ? double.Parse(positional[1], CultureInfo.InvariantCulture)
            : 15);
        break;

    default:
        Console.WriteLine("""
            Замер распознавания и проверка диктовки.

              bench models                   список моделей в папке models
              bench devices                  звуковые устройства и их идентификаторы
              bench rec [секунды]            записать образец с микрофона (по умолчанию 20 с)
              bench run [модель] [файл.wav]  распознать и показать тайминги
              bench dictate [модель]         живая диктовка по глобальному хоткею
              bench call [секунды]           записать звонок в два канала
              bench transcribe <папка>       собрать транскрипт записанного звонка
              bench vad <файл.wav>           что детектор считает речью в файле
              bench diarize <файл.wav>       разделить голоса в дорожке
              bench voiceprint <a> [b]       сходство слепков голоса: двух файлов или половин одного
              bench check                    проверить хоткей и буфер обмена

            Модель задаётся куском имени файла: `bench run turbo`.
            Без подсказки берётся любая найденная.

            Флаги:
              --prompt "текст"        подсказка словаря (по умолчанию промпта нет)
              --lang <код>            язык распознавания (по умолчанию auto)
              --beam <N>              ширина луча, 1 — жадный поиск
              --speakers <N>          сколько голосов искать при разделении
              --cluster-threshold <x> порог разделения голосов без подсказки
              --vad-threshold <0..1>  порог детектора речи
              --no-vad                не искать речь детектором перед распознаванием
              --verbose               нативный лог ggml: какой бэкенд загрузился
            """);
        break;
}

return 0;
}

// --- команды ---------------------------------------------------------------

void ListModels()
{
    Console.WriteLine($"Папка моделей: {modelsDir}");
    if (!Directory.Exists(modelsDir))
    {
        Console.WriteLine("  папки нет — скачайте модель в приложении или создайте папку вручную");
        return;
    }

    foreach (string path in Directory.EnumerateFiles(modelsDir, "*.bin").Order())
    {
        double mb = new FileInfo(path).Length / 1024.0 / 1024.0;
        Console.WriteLine($"  {Path.GetFileName(path),-40} {mb,8:N0} МБ");
    }
}

async Task RecordSampleAsync(double seconds)
{
    using var capture = new MicrophoneCapture();

    // Простейший индикатор уровня: убеждаемся, что микрофон реально слышит.
    var lastPrint = Stopwatch.StartNew();
    capture.LevelChanged += rms =>
    {
        if (lastPrint.ElapsedMilliseconds < 200)
        {
            return;
        }

        lastPrint.Restart();
        int bars = (int)Math.Clamp(rms * 120, 0, 40);
        Console.Write($"\r  [{new string('#', bars).PadRight(40)}] {capture.Duration.TotalSeconds,5:F1} с");
    };

    Console.WriteLine($"Говори {seconds:F0} секунд…");
    capture.Start();
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    float[] samples = await capture.StopAsync();
    Console.WriteLine();

    AudioFile.WriteWav16k(defaultSample, samples);
    Console.WriteLine($"Записано {samples.Length / (double)MicrophoneCapture.TargetSampleRate:F1} с → {defaultSample}");
}

async Task RunAsync(string? modelHint, string wavPath)
{
    string modelPath = ResolveModel(modelHint);
    if (!File.Exists(wavPath))
    {
        Console.Error.WriteLine($"Нет образца {wavPath} — сначала `bench rec`.");
        return;
    }

    float[] samples = AudioFile.ReadMono16k(wavPath);
    var audioDuration = TimeSpan.FromSeconds(samples.Length / (double)MicrophoneCapture.TargetSampleRate);

    Console.WriteLine($"Модель:   {Path.GetFileName(modelPath)}");
    Console.WriteLine($"Образец:  {Path.GetFileName(wavPath)}, {audioDuration.TotalSeconds:F1} с");

    Console.WriteLine($"Промпт:   {promptOption ?? "нет"}");

    Console.WriteLine($"Язык:     {language}");

    await using var engine = new WhisperEngine(new WhisperEngineOptions
    {
        ModelPath = modelPath,
        // Язык берём из --lang. Раньше флаг разбирался, но здесь стояла
        // константа: замер на английском образце всё равно шёл с русским.
        Language = language,
        // Промпта по умолчанию нет намеренно. Подсказка словаря биасит декодер,
        // и НЕРЕЛЕВАНТНЫЙ промпт делает это во вред: он должен быть настройкой
        // пользователя, а не зашитой в замер константой.
        Prompt = promptOption,
        BeamSize = beamSize ?? WhisperEngineOptions.DefaultBeamSize,
    });

    var loadTimer = Stopwatch.StartNew();
    await engine.LoadAsync();
    loadTimer.Stop();

    // Печатаем ПОСЛЕ загрузки: до неё нативная библиотека ещё не выбрана.
    Console.WriteLine($"Рантайм:  {WhisperEngine.LoadedRuntime ?? "ещё не загружен"}");

    var runTimer = Stopwatch.StartNew();
    IReadOnlyList<TranscriptSegment> segments = await engine.TranscribeAsync(samples);
    runTimer.Stop();

    Console.WriteLine();
    foreach (TranscriptSegment segment in segments)
    {
        // Считаем минуты через TotalMinutes, а не форматом "mm": тот
        // обнуляется на 60-й минуте, а звонок бывает и полуторачасовым.
        string stamp = $"{(int)segment.Start.TotalMinutes:D2}:{segment.Start.Seconds:D2}";
        Console.WriteLine($"  [{stamp}] {segment.Text}");
    }

    // Realtime factor: во сколько раз распознавание быстрее реального времени.
    // Это единственная метрика, которую можно сравнивать между моделями.
    double rtf = audioDuration.TotalSeconds / runTimer.Elapsed.TotalSeconds;
    Console.WriteLine();
    Console.WriteLine($"Загрузка + прогрев: {loadTimer.Elapsed.TotalSeconds,6:F2} с");
    Console.WriteLine($"Распознавание:      {runTimer.Elapsed.TotalSeconds,6:F2} с  ({rtf:F1}× реального времени)");
}

/// <summary>
/// Диагностика детектора: уровни до и после нормализации, найденные участки.
/// </summary>
/// <summary>
/// Что разделитель услышал в дорожке.
/// </summary>
/// <remarks>
/// Существует ради одного вопроса, на который иначе пришлось бы отвечать
/// на глаз: какая модель слепков лучше слышит русские голоса. Обе обучены
/// на других языках, и «должно переноситься» — не измерение.
/// </remarks>
/// <summary>
/// Сходство слепков голоса — чтобы проверять порог книги голосов на живых записях.
/// </summary>
/// <remarks>
/// С одним файлом сравниваются его половины: это один и тот же человек, и
/// сходство должно быть заметно выше <see cref="VoiceBook.MatchThreshold"/>.
/// С двумя — два файла, например два разных человека.
/// </remarks>
void CompareVoices(string first, string? second)
{
    string? embedding = ModelLocator.Resolve(new AppSettings().VoiceEmbeddingModelFileName);
    if (embedding is null)
    {
        Console.Error.WriteLine("Нет модели слепков голоса.");
        return;
    }

    float[] a = AudioNormalizer.Normalize(AudioFile.ReadMono16k(first));
    float[] b;
    if (second is null)
    {
        b = a[(a.Length / 2)..];
        a = a[..(a.Length / 2)];
    }
    else
    {
        b = AudioNormalizer.Normalize(AudioFile.ReadMono16k(second));
    }

    using var extractor = new VoiceprintExtractor(embedding);
    var timer = Stopwatch.StartNew();
    float[]? printA = extractor.Embed(a);
    float[]? printB = extractor.Embed(b);
    timer.Stop();

    if (printA is null || printB is null)
    {
        Console.Error.WriteLine("Слишком мало речи для слепка.");
        return;
    }

    double similarity = VoiceBook.Similarity(printA, printB);
    Console.WriteLine($"Модель:    {Path.GetFileName(embedding)}, размерность {printA.Length}");
    Console.WriteLine($"Отрезки:   {a.Length / 16000.0:F1} с и {b.Length / 16000.0:F1} с, {timer.Elapsed.TotalSeconds:F2} с на оба");
    Console.WriteLine($"Сходство:  {similarity:F3} (порог подсказки {VoiceBook.MatchThreshold:F2})");
}

void SplitVoices(string wavPath, int expected, float? threshold)
{
    if (!File.Exists(wavPath))
    {
        Console.Error.WriteLine($"Нет файла {wavPath}");
        return;
    }

    var defaults = new AppSettings();
    string? segmentation = ModelLocator.Resolve(defaults.VoiceSegmentationModelFileName);
    string? embedding = ModelLocator.Resolve(defaults.VoiceEmbeddingModelFileName);

    if (segmentation is null || embedding is null)
    {
        Console.Error.WriteLine(
            $"Нет моделей разделения: {defaults.VoiceSegmentationModelFileName}, {defaults.VoiceEmbeddingModelFileName}");
        return;
    }

    float[] samples = AudioNormalizer.Normalize(AudioFile.ReadMono16k(wavPath));

    Console.WriteLine($"Файл:      {Path.GetFileName(wavPath)}");
    Console.WriteLine($"Длина:     {samples.Length / (double)AudioCapture.TargetSampleRate:F1} с");
    Console.WriteLine($"Сегментация: {Path.GetFileName(segmentation)}");
    Console.WriteLine($"Слепки:      {Path.GetFileName(embedding)}");
    Console.WriteLine($"Подсказка:   {(expected > 0 ? expected.ToString(CultureInfo.InvariantCulture) : "нет")}");
    Console.WriteLine($"Порог:       {threshold ?? (float)defaults.VoiceSplitThreshold:F2}");
    Console.WriteLine();

    var timer = Stopwatch.StartNew();
    using var diarizer = new SpeakerDiarizer(new SpeakerDiarizerOptions
    {
        SegmentationModelPath = segmentation,
        EmbeddingModelPath = embedding,
        ClusterThreshold = threshold ?? (float)defaults.VoiceSplitThreshold,
    });
    TimeSpan load = timer.Elapsed;

    timer.Restart();
    IReadOnlyList<SpeakerSpan> spans = diarizer.Split(samples, expected);
    TimeSpan work = timer.Elapsed;

    foreach (SpeakerSpan span in spans)
    {
        Console.WriteLine(
            $"  [{span.Start.TotalSeconds,6:F2} – {span.End.TotalSeconds,6:F2}]  голос {span.Speaker + 1}");
    }

    if (spans.Count == 0)
    {
        Console.WriteLine("  речи не найдено");
    }

    double audioSeconds = samples.Length / (double)AudioCapture.TargetSampleRate;
    Console.WriteLine();
    Console.WriteLine($"Голосов:   {spans.Select(s => s.Speaker).Distinct().Count()}");
    Console.WriteLine($"Загрузка:  {load.TotalSeconds:F2} с");
    Console.WriteLine($"Работа:    {work.TotalSeconds:F2} с  ({audioSeconds / Math.Max(0.001, work.TotalSeconds):F1}× реального времени)");
}

async Task InspectVadAsync(string wavPath)
{
    if (!File.Exists(wavPath))
    {
        Console.Error.WriteLine($"Нет файла {wavPath}");
        return;
    }

    float[] raw = AudioFile.ReadMono16k(wavPath);
    float[] normalized = AudioNormalizer.Normalize(raw);

    Console.WriteLine($"Файл: {Path.GetFileName(wavPath)}");
    Console.WriteLine($"Длительность: {raw.Length / (double)AudioCapture.TargetSampleRate:F2} с");
    Console.WriteLine($"Пик исходный:      {raw.Max(Math.Abs):F4}");
    Console.WriteLine($"Пик после нормализации: {normalized.Max(Math.Abs):F4}");

    string? vadPath = ModelLocator.ResolveVadModel(new AppSettings().VadModelFileName);
    if (vadPath is null)
    {
        Console.Error.WriteLine("Модель детектора не найдена.");
        return;
    }

    // Одна модель на все пороги: порог живёт в прогоне, а не в модели.
    await using var detector = new SpeechDetector(new SpeechDetectorOptions { ModelPath = vadPath });

    foreach (float threshold in new[] { 0.5f, 0.35f, 0.2f, 0.1f })
    {
        IReadOnlyList<SpeechRegion> regions = await detector.DetectAsync(normalized, threshold);
        string found = regions.Count == 0
            ? "ничего"
            : string.Join(", ", regions.Select(r => $"{r.Start.TotalSeconds:F1}-{r.End.TotalSeconds:F1}"));

        Console.WriteLine($"  порог {threshold:F2}: {regions.Count} уч. — {found}");
    }
}

/// <summary>
/// Собрать транскрипт ранее записанного звонка.
/// </summary>
async Task TranscribeCallAsync(string callDirectory)
{
    CallSession? session = CallMeta.Load(callDirectory);
    if (session is null)
    {
        Console.Error.WriteLine($"В {callDirectory} нет meta.json — это не папка звонка.");
        return;
    }

    string modelPath = ResolveModel(null);
    await using var engine = new WhisperEngine(new WhisperEngineOptions
    {
        ModelPath = modelPath,
        Language = language,
        Prompt = promptOption,
        BeamSize = beamSize ?? WhisperEngineOptions.DefaultBeamSize,
    });

    Console.WriteLine($"Модель: {Path.GetFileName(modelPath)}, язык: {language}");
    Console.Write("Прогреваю движок… ");
    await engine.LoadAsync();
    Console.WriteLine(WhisperEngine.LoadedRuntime ?? "ещё не загружен");

    // --no-vad позволяет сравнить с детектором и без него на одной записи.
    SpeechDetector? detector = null;
    if (!args.Contains("--no-vad"))
    {
        string? vadPath = ModelLocator.ResolveVadModel(new AppSettings().VadModelFileName);
        if (vadPath is null)
        {
            Console.WriteLine("Модель детектора речи не найдена — иду без неё.");
        }
        else
        {
            Console.WriteLine($"Детектор речи: {Path.GetFileName(vadPath)}");
            var vadOptions = new SpeechDetectorOptions { ModelPath = vadPath };
            if (vadThreshold is { } threshold)
            {
                vadOptions = vadOptions with { Threshold = threshold };
            }

            Console.WriteLine($"Порог детектора: {vadOptions.Threshold:F2}");
            detector = new SpeechDetector(vadOptions);
        }
    }
    else
    {
        Console.WriteLine("Детектор речи выключен (--no-vad)");
    }

    // Разделение голосов, если модели на месте. Без него звонок с тремя
    // участниками собирается с одной подписью на всех — то есть ровно так,
    // как выглядела задача до того, как её решили.
    var defaults = new AppSettings();
    SpeakerDiarizer? diarizer = null;
    string? segmentation = ModelLocator.Resolve(defaults.VoiceSegmentationModelFileName);
    string? embedding = ModelLocator.Resolve(defaults.VoiceEmbeddingModelFileName);

    if (segmentation is not null && embedding is not null)
    {
        Console.WriteLine($"Разделение голосов: {Path.GetFileName(embedding)}");
        diarizer = new SpeakerDiarizer(new SpeakerDiarizerOptions
        {
            SegmentationModelPath = segmentation,
            EmbeddingModelPath = embedding,
            ClusterThreshold = (float)defaults.VoiceSplitThreshold,
        });
    }
    else
    {
        Console.WriteLine("Моделей разделения голосов нет — собеседники останутся без имён.");
    }

    var transcriber = new CallTranscriber(
        new SpeechTranscriber(engine, detector),
        () => new AppSettings
        {
            MyName = "Я",
            OtherSideName = "Собеседник",
            Language = language,
        },
        labels: null,
        diarizer: () => diarizer);

    var timer = Stopwatch.StartNew();
    var progress = new Progress<CallTranscriptionProgress>(p => Console.WriteLine($"  {p.Stage} {p.Percent}%"));
    string path = await transcriber.TranscribeAsync(session, progress);
    timer.Stop();

    Console.WriteLine();
    Console.WriteLine($"Транскрипт: {path}  ({timer.Elapsed.TotalSeconds:F1} с)");
    Console.WriteLine();
    Console.WriteLine(await File.ReadAllTextAsync(path));

    diarizer?.Dispose();

    if (detector is not null)
    {
        await detector.DisposeAsync();
    }
}

/// <summary>
/// Запись звонка в два канала: проверка захвата и выравнивания дорожек.
/// </summary>
async Task RecordCallAsync(double seconds)
{
    string callsRoot = Path.Combine(repoRoot, "temp", "calls");
    using var recorder = new CallRecorder();

    var lastPrint = Stopwatch.StartNew();
    recorder.MicLevel += level =>
    {
        if (lastPrint.ElapsedMilliseconds < 200)
        {
            return;
        }

        lastPrint.Restart();
        double db = 20 * Math.Log10(Math.Max(level, 1e-7));
        int bars = (int)Math.Clamp((db + 50) / 40 * 30, 0, 30);
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write("  микрофон ["
                      + new string('#', bars).PadRight(30)
                      + $"] {recorder.Elapsed.TotalSeconds,5:F1} с");
    };

    Console.WriteLine($"Пишу {seconds:F0} секунд. Говори и включи что-нибудь со звуком.");
    CallSession session = recorder.Start(new CallRecordingOptions(callsRoot));
    await Task.Delay(TimeSpan.FromSeconds(seconds));
    CallSession? finished = await recorder.StopAsync();
    Console.WriteLine();

    if (finished is null)
    {
        Console.Error.WriteLine("Запись не состоялась.");
        return;
    }

    Console.WriteLine($"Папка: {finished.Directory}");
    Console.WriteLine($"Длительность по часам: {finished.Duration.TotalSeconds:F2} с");
    Console.WriteLine();

    // Главная проверка: дорожки обязаны совпадать по длине. Системный канал
    // не отдаёт данных в тишине, и без добивания он оказался бы короче.
    foreach ((string label, string path) in new[]
             {
                 ("микрофон", finished.MicPath),
                 ("система ", finished.SystemPath),
             })
    {
        float[] samples = AudioFile.ReadMono16k(path);
        double duration = samples.Length / (double)AudioCapture.TargetSampleRate;
        double peak = samples.Length == 0 ? 0 : samples.Max(Math.Abs);
        double mb = new FileInfo(path).Length / 1024.0 / 1024.0;
        Console.WriteLine($"  {label}: {duration,6:F2} с, пик {peak:F3}, {mb:F2} МБ");
    }
}

/// <summary>
/// Диагностика интеграции с Windows: занимается ли хоткей и пишется ли буфер.
/// Вставку (SendInput) здесь не проверяем — она вслепую нажала бы Ctrl+V
/// в том окне, которое сейчас активно.
/// </summary>
void CheckWindowsIntegration()
{
    HotkeyCombo combo = HotkeyCombo.Default;
    try
    {
        using var listener = new HotkeyListener(combo);
        listener.Start();
        Console.WriteLine($"  хоткей {combo}: занят успешно");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  хоткей {combo}: НЕ занят — {ex.Message}");
    }

    string marker = $"Tapybara check {DateTime.Now:HH:mm:ss}";
    try
    {
        ClipboardWriter.SetText(marker);
        Console.WriteLine($"  буфер обмена: записано «{marker}»");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  буфер обмена: ошибка — {ex.Message}");
    }
}

/// <summary>
/// Живая диктовка из консоли: хоткей → запись → распознавание → вставка.
/// Это вертикальный срез всей будущей фазы 1, но без интерфейса — чтобы
/// проверить рискованное (глобальный хоткей и вставку в чужое окно) до того,
/// как вкладываться в WPF.
/// </summary>
async Task DictateAsync(string? modelHint)
{
    string modelPath = ResolveModel(modelHint);

    await using var engine = new WhisperEngine(new WhisperEngineOptions
    {
        ModelPath = modelPath,
        Language = language,
        Prompt = promptOption,
        BeamSize = beamSize ?? WhisperEngineOptions.DefaultBeamSize,
    });

    Console.WriteLine($"Модель:   {Path.GetFileName(modelPath)}");
    Console.Write("Прогреваю движок… ");
    var warmTimer = Stopwatch.StartNew();
    await engine.LoadAsync();
    Console.WriteLine($"{warmTimer.Elapsed.TotalSeconds:F1} с, рантайм {WhisperEngine.LoadedRuntime}");

    HotkeyCombo combo = HotkeyCombo.Default;
    using var listener = new HotkeyListener(combo);

    // Ноль-таймаут: если предыдущее нажатие ещё обрабатывается, новое просто
    // игнорируется. Иначе двойное нажатие во время распознавания запустило бы
    // вторую запись поверх первой.
    var busy = new SemaphoreSlim(1, 1);
    MicrophoneCapture? capture = null;

    listener.Pressed += () => _ = ToggleAsync();
    listener.Start();

    Console.WriteLine();
    Console.WriteLine($"  {combo} — начать и закончить диктовку.");
    Console.WriteLine("  Переключись в любое приложение, поставь курсор в поле ввода и жми хоткей.");
    Console.WriteLine("  Ctrl+C — выход.");
    Console.WriteLine();

    var exit = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true; // не убивать процесс жёстко: дадим освободить хоткей
        exit.TrySetResult();
    };

    // ЗАКРЫТИЕ ОКНА КОНСОЛИ — НЕ Ctrl+C. Windows шлёт CTRL_CLOSE_EVENT, который
    // .NET показывает как SIGTERM, а CancelKeyPress на него не срабатывает.
    // Без этой подписки процесс переживал своё окно и продолжал держать
    // глобальный хоткей: приложение потом не могло его занять, а пользователь
    // видел «горячая клавиша занята» без единого видимого виновника.
    using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => exit.TrySetResult());

    await exit.Task;
    capture?.Dispose();

    async Task ToggleAsync()
    {
        if (!await busy.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (capture is null)
            {
                var started = new MicrophoneCapture();
                started.Start();
                capture = started;
                Console.WriteLine("● запись… (хоткей ещё раз — закончить)");
                return;
            }

            float[] samples = await capture.StopAsync();
            capture.Dispose();
            capture = null;

            var duration = TimeSpan.FromSeconds(samples.Length / (double)MicrophoneCapture.TargetSampleRate);
            if (duration < TimeSpan.FromSeconds(0.5))
            {
                Console.WriteLine("  слишком коротко — пропускаю");
                return;
            }

            var timer = Stopwatch.StartNew();
            IReadOnlyList<TranscriptSegment> segments = await engine.TranscribeAsync(samples);
            timer.Stop();

            string text = string.Join(' ', segments.Select(s => s.Text)).Trim();
            if (text.Length == 0)
            {
                Console.WriteLine("  пусто — речь не распознана");
                return;
            }

            Console.WriteLine($"  {duration.TotalSeconds:F1} с речи → {timer.Elapsed.TotalSeconds:F2} с: {text}");
            TextInserter.PasteViaClipboard(text);
        }
        catch (Exception ex)
        {
            // Ловим всё: диктовка не должна ронять процесс из-за занятого
            // буфера обмена или окна с правами администратора.
            Console.WriteLine($"  ошибка: {ex.Message}");
        }
        finally
        {
            busy.Release();
        }
    }
}

/// <summary>
/// Найти модель по куску имени.
/// </summary>
/// <remarks>
/// Без подсказки берётся любая доступная. Раньше в двух командах стояло имя
/// личной модели автора: у всех остальных они падали с «нет модели», хотя
/// модели в папке лежали.
/// </remarks>
string ResolveModel(string? hint)
{
    if (string.IsNullOrWhiteSpace(hint))
    {
        return ModelLocator.ResolveAnyAvailable()
               ?? throw new FileNotFoundException($"В {modelsDir} нет ни одной модели распознавания.");
    }

    string[] matches = [.. Directory.EnumerateFiles(modelsDir, "*.bin")
        .Where(p => Path.GetFileName(p).Contains(hint, StringComparison.OrdinalIgnoreCase))
        .Where(p => !Path.GetFileName(p).Contains("silero", StringComparison.OrdinalIgnoreCase))];

    return matches.Length switch
    {
        1 => matches[0],
        0 => throw new FileNotFoundException($"Нет модели по фрагменту «{hint}» в {modelsDir}"),
        _ => throw new InvalidOperationException(
            $"Под «{hint}» подходит несколько: {string.Join(", ", matches.Select(Path.GetFileName))}"),
    };
}

/// <summary>Звуковые устройства и их идентификаторы — чтобы вписать в настройки.</summary>
void ListDevices()
{
    Console.WriteLine("Входы (микрофоны):");
    foreach (AudioDeviceInfo device in AudioDevices.Inputs())
    {
        Console.WriteLine($"  {(device.IsDefault ? "*" : " ")} {device.Name}");
        Console.WriteLine($"      {device.Id}");
    }

    Console.WriteLine();
    Console.WriteLine("Выходы (с них снимается системный звук):");
    foreach (AudioDeviceInfo device in AudioDevices.Outputs())
    {
        Console.WriteLine($"  {(device.IsDefault ? "*" : " ")} {device.Name}");
        Console.WriteLine($"      {device.Id}");
    }
}

// Утилита ищет папку models относительно корня репозитория, а не рабочей
// директории: тогда `bench` одинаково работает и из bin/Debug, и из корня.
static string FindRepoRoot()
{
    // .slnx — новый XML-формат солюшена в .NET 10, .sln — классический.
    // Проверяем оба: иначе поиск молча провалится в фолбэк на текущую папку
    // и `bench` начнёт работать только при запуске из корня репозитория.
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !dir.EnumerateFiles("Tapybara.sln*").Any())
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
