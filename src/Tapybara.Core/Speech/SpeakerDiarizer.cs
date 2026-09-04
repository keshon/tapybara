using SherpaOnnx;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Speech;

/// <summary>Настройки разделения голосов.</summary>
public sealed record SpeakerDiarizerOptions
{
    /// <summary>Модель сегментации: где в записи речь и сколько голосов звучит разом.</summary>
    public required string SegmentationModelPath { get; init; }

    /// <summary>Модель слепков голоса: во что превращается кусок речи, чтобы голоса сравнивать.</summary>
    public required string EmbeddingModelPath { get; init; }

    /// <summary>
    /// Порог, по которому голоса считаются разными, когда их число неизвестно.
    /// </summary>
    /// <remarks>
    /// Работает только без подсказки. Если участники названы, число кластеров
    /// задано точно, и порог не используется вовсе — именно поэтому спросить
    /// имена важнее, чем подобрать это число.
    /// </remarks>
    public float ClusterThreshold { get; init; } = 0.5f;

    /// <summary>Короче этого — не речь, а призвук. Секунды.</summary>
    public float MinDurationOn { get; init; } = 0.3f;

    /// <summary>Пауза короче этой не разрывает реплику. Секунды.</summary>
    public float MinDurationOff { get; init; } = 0.5f;

    /// <summary>Потоков процессора. <c>null</c> — оставить пару ядер системе.</summary>
    public int? Threads { get; init; }

    /// <summary>Фактическое число потоков.</summary>
    public int EffectiveThreads => Threads ?? Math.Max(2, Environment.ProcessorCount - 2);
}

/// <summary>Кусок записи, отданный одному голосу.</summary>
/// <param name="Start">Начало от начала дорожки.</param>
/// <param name="End">Конец.</param>
/// <param name="Speaker">Номер голоса. Имени у него пока нет — только номер.</param>
public sealed record SpeakerSpan(TimeSpan Start, TimeSpan End, int Speaker);

/// <summary>
/// Разделение голосов внутри одной дорожки.
/// </summary>
/// <remarks>
/// <para>
/// Работает по чужому каналу и только по нему. Свой канал — это микрофон
/// владельца, и кто на нём говорит, известно по построению; отдавать его
/// разделителю значило бы просить машину угадать то, что мы и так знаем.
/// Заодно это снимает с задачи самый трудный голос: самый громкий, самый
/// близкий к микрофону и чаще всех перебивающий остальных.
/// </para>
/// <para>
/// Внутри — pyannote-сегментация плюс модель слепков голоса плюс быстрая
/// кластеризация, всё в ONNX и на процессоре. Ничего не уходит с машины:
/// для продукта, который обещает «звук остаётся у вас», это не деталь
/// реализации, а условие задачи.
/// </para>
/// <para>
/// Потокобезопасности нет. Экземпляр держит нативный контекст; распознавание
/// звонков и так идёт по одному за раз.
/// </para>
/// </remarks>
public sealed class SpeakerDiarizer : IDisposable
{
    /// <summary>Частота, на которой работают обе модели.</summary>
    public const int SampleRate = 16_000;

    private readonly SpeakerDiarizerOptions _options;
    private readonly OfflineSpeakerDiarization _native;
    private bool _disposed;

    public SpeakerDiarizer(SpeakerDiarizerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _native = new OfflineSpeakerDiarization(BuildConfig(options, expectedSpeakers: 0));

        if (_native.SampleRate != SampleRate)
        {
            // Не подгоняем молча: несовпадение частоты означает другую модель,
            // и разделение «как-то работало бы», давая тихо неверные границы.
            _native.Dispose();
            throw new InvalidOperationException(
                $"Модель сегментации ждёт {_native.SampleRate} Гц, а дорожки записаны на {SampleRate} Гц.");
        }
    }

    /// <summary>
    /// Собрать конфигурацию под конкретный прогон.
    /// </summary>
    /// <remarks>
    /// <c>NumClusters</c> и <c>Threshold</c> взаимоисключающие: положительное
    /// число кластеров отменяет порог. Задаём ровно одно из двух, иначе
    /// непонятно, что именно сработало.
    /// </remarks>
    private static OfflineSpeakerDiarizationConfig BuildConfig(SpeakerDiarizerOptions options, int expectedSpeakers)
    {
        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = options.SegmentationModelPath;
        config.Segmentation.NumThreads = options.EffectiveThreads;
        config.Embedding.Model = options.EmbeddingModelPath;
        config.Embedding.NumThreads = options.EffectiveThreads;
        config.MinDurationOn = options.MinDurationOn;
        config.MinDurationOff = options.MinDurationOff;

        if (expectedSpeakers > 0)
        {
            config.Clustering.NumClusters = expectedSpeakers;
        }
        else
        {
            config.Clustering.Threshold = options.ClusterThreshold;
        }

        return config;
    }

    /// <summary>
    /// Разделить дорожку на голоса.
    /// </summary>
    /// <param name="samples">Дорожка: 16 кГц, моно, float32.</param>
    /// <param name="expectedSpeakers">
    /// Сколько голосов искать. Ноль или меньше — определять самому по порогу.
    /// </param>
    /// <param name="progress">Доля выполненного, 0–100.</param>
    /// <param name="cancellationToken">Отмена. Проверяется между блоками записи.</param>
    /// <remarks>
    /// Известное число голосов — самый сильный рычаг точности, какой у этой
    /// задачи есть. Кластеризация без подсказки решает сразу две задачи:
    /// разделить голоса И угадать, сколько их; ошибка во второй ломает первую
    /// целиком — два похожих голоса склеиваются в один, а один простуженный
    /// распадается на два. Поэтому мы и спрашиваем участников.
    /// </remarks>
    public IReadOnlyList<SpeakerSpan> Split(
        float[] samples,
        int expectedSpeakers = 0,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Length == 0)
        {
            return [];
        }

        _native.SetConfig(BuildConfig(_options, expectedSpeakers));

        bool cancelled = false;

        // Делегат — в переменную, а не выражением прямо в аргументе. Нативная
        // сторона зовёт его на протяжении всего прогона, и сборщик мусора не
        // обязан считать живым временный объект, чей единственный владелец —
        // уже неуправляемый код.
        OfflineSpeakerDiarizationProgressCallback callback = (done, total, _) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                return 1; // ненулевой ответ просит нативную сторону остановиться
            }

            if (total > 0)
            {
                progress?.Report(Math.Clamp(done * 100 / total, 0, 100));
            }

            return 0;
        };

        OfflineSpeakerDiarizationSegment[] segments =
            _native.ProcessWithCallback(samples, callback, IntPtr.Zero);

        GC.KeepAlive(callback);

        if (cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        AppLog.Info(
            $"Голоса разделены: участков {segments.Length}, " +
            $"голосов {segments.Select(s => s.Speaker).Distinct().Count()}, " +
            $"подсказка {(expectedSpeakers > 0 ? expectedSpeakers.ToString(System.Globalization.CultureInfo.InvariantCulture) : "нет")}.");

        return
        [
            .. segments
                .OrderBy(s => s.Start)
                .Select(s => new SpeakerSpan(
                    TimeSpan.FromSeconds(s.Start),
                    TimeSpan.FromSeconds(s.End),
                    s.Speaker)),
        ];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _native.Dispose();
    }
}
