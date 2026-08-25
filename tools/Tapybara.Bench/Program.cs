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
string[] valueFlags = ["--prompt", "--vad-threshold", "--lang"];

List<string> positionalArgs = [];
string? promptOption = null;
float? vadThreshold = null;
string language = "ru";
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
        if (positional.Length < 2)
        {
            Console.Error.WriteLine("Укажи модель: bench run large-v3-turbo");
            return 1;
        }

        await RunAsync(positional[1], positional.Length > 2 ? positional[2] : defaultSample);
        break;

    case "dictate":
        await DictateAsync(positional.Length > 1 ? positional[1] : "podlodka");
        break;

    case "check":
        CheckWindowsIntegration();
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

    case "call":
        await RecordCallAsync(positional.Length > 1
            ? double.Parse(positional[1], CultureInfo.InvariantCulture)
            : 15);
        break;

    default:
        Console.WriteLine("""
            Замер распознавания и проверка диктовки.

              bench models                   список моделей в папке models
              bench rec [секунды]            записать образец с микрофона (по умолчанию 20 с)
              bench run <модель> [файл.wav]  распознать и показать тайминги
              bench dictate [модель]         живая диктовка по глобальному хоткею
              bench call [секунды]           записать звонок в два канала
              bench transcribe <папка>       собрать транскрипт записанного звонка
              bench vad <файл.wav>           что детектор считает речью в файле

            Модель задаётся куском имени файла: `bench run podlodka`.

            Флаги:
              --prompt "текст"   подсказка словаря (по умолчанию промпта нет)
              --no-vad           не искать речь детектором перед распознаванием
              --verbose          нативный лог ggml: какой бэкенд загрузился
            """);
        break;
}

return 0;

// --- команды ---------------------------------------------------------------

void ListModels()
{
    Console.WriteLine($"Папка моделей: {modelsDir}");
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

async Task RunAsync(string modelHint, string wavPath)
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

    await using var engine = new WhisperEngine(new WhisperEngineOptions
    {
        ModelPath = modelPath,
        Language = "ru",
        // Промпта по умолчанию нет намеренно. Подсказка словаря биасит декодер,
        // и НЕРЕЛЕВАНТНЫЙ промпт делает это во вред: он должен быть настройкой
        // пользователя, а не зашитой в замер константой.
        Prompt = promptOption,
    });

    var loadTimer = Stopwatch.StartNew();
    await engine.LoadAsync();
    loadTimer.Stop();

    // Печатаем ПОСЛЕ загрузки: до неё нативная библиотека ещё не выбрана.
    Console.WriteLine($"Рантайм:  {WhisperEngine.LoadedRuntime}");

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

    string? vadPath = ModelLocator.ResolveVadModel("ggml-silero-v6.2.0.bin");
    if (vadPath is null)
    {
        Console.Error.WriteLine("Модель детектора не найдена.");
        return;
    }

    foreach (float threshold in new[] { 0.5f, 0.35f, 0.2f, 0.1f })
    {
        await using var detector = new SpeechDetector(new SpeechDetectorOptions
        {
            ModelPath = vadPath,
            Threshold = threshold,
        });

        IReadOnlyList<SpeechRegion> regions = await detector.DetectAsync(normalized);
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

    string modelPath = ResolveModel("podlodka");
    await using var engine = new WhisperEngine(new WhisperEngineOptions
    {
        ModelPath = modelPath,
        Language = language,
        Prompt = promptOption,
    });

    Console.WriteLine($"Модель: {Path.GetFileName(modelPath)}, язык: {language}");
    Console.Write("Прогреваю движок… ");
    await engine.LoadAsync();
    Console.WriteLine(WhisperEngine.LoadedRuntime);

    // --no-vad позволяет сравнить с детектором и без него на одной записи.
    SpeechDetector? detector = null;
    if (!args.Contains("--no-vad"))
    {
        string? vadPath = ModelLocator.ResolveVadModel("ggml-silero-v6.2.0.bin");
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

    var transcriber = new CallTranscriber(
        new SpeechTranscriber(engine, detector),
        () => new AppSettings
        {
            MyName = "Я",
            OtherSideName = "Собеседник",
        });

    var timer = Stopwatch.StartNew();
    var progress = new Progress<string>(step => Console.WriteLine($"  {step}"));
    string path = await transcriber.TranscribeAsync(session, progress);
    timer.Stop();

    Console.WriteLine();
    Console.WriteLine($"Транскрипт: {path}  ({timer.Elapsed.TotalSeconds:F1} с)");
    Console.WriteLine();
    Console.WriteLine(await File.ReadAllTextAsync(path));

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
    CallSession session = recorder.Start(callsRoot);
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
async Task DictateAsync(string modelHint)
{
    string modelPath = ResolveModel(modelHint);

    await using var engine = new WhisperEngine(new WhisperEngineOptions
    {
        ModelPath = modelPath,
        Language = "ru",
        Prompt = promptOption,
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

string ResolveModel(string hint)
{
    string[] matches = [.. Directory.EnumerateFiles(modelsDir, "*.bin")
        .Where(p => Path.GetFileName(p).Contains(hint, StringComparison.OrdinalIgnoreCase))];

    return matches.Length switch
    {
        1 => matches[0],
        0 => throw new FileNotFoundException($"Нет модели по фрагменту «{hint}» в {modelsDir}"),
        _ => throw new InvalidOperationException(
            $"Под «{hint}» подходит несколько: {string.Join(", ", matches.Select(Path.GetFileName))}"),
    };
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
