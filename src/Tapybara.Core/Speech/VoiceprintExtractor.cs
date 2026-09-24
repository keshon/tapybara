using SherpaOnnx;

namespace Tapybara.Core.Speech;

/// <summary>
/// Слепок голоса: отрезок речи → вектор, который можно сравнивать.
/// </summary>
/// <remarks>
/// <para>
/// Та же модель слепков, что работает внутри разделителя голосов (CAM++),
/// только вызванная отдельно: разделитель сравнивает голоса внутри одного
/// звонка и наружу слепки не отдаёт, а чтобы узнать Кирилла на следующем
/// звонке, слепок нужно сохранить.
/// </para>
/// <para>
/// Всё на процессоре и на этой машине. Слепок — это биометрия, и он не
/// покидает папку с настройками; см. <see cref="Calls.VoiceBook"/>.
/// </para>
/// <para>
/// Не потокобезопасен, как и разделитель: работа над звонками идёт по одной.
/// </para>
/// </remarks>
public sealed class VoiceprintExtractor : IDisposable
{
    private readonly SpeakerEmbeddingExtractor _native;
    private bool _disposed;

    public VoiceprintExtractor(string embeddingModelPath)
    {
        _native = new SpeakerEmbeddingExtractor(new SpeakerEmbeddingExtractorConfig
        {
            Model = embeddingModelPath,
            NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            Provider = "cpu",
        });
    }

    /// <summary>
    /// Слепок голоса по отрезку речи, нормированный к единичной длине.
    /// </summary>
    /// <param name="samples">16 кГц, моно, float32.</param>
    /// <returns><c>null</c>, если речи слишком мало, чтобы модель что-то сказала.</returns>
    /// <remarks>
    /// Нормируем сразу: сравнение слепков — косинус, а у единичных векторов
    /// косинус — просто скалярное произведение. Хранить ненормированные
    /// значило бы нормировать при каждом сравнении.
    /// </remarks>
    public float[]? Embed(float[] samples)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Length == 0)
        {
            return null;
        }

        using OnlineStream stream = _native.CreateStream();
        stream.AcceptWaveform(SpeakerDiarizer.SampleRate, samples);
        stream.InputFinished();

        if (!_native.IsReady(stream))
        {
            return null;
        }

        return Calls.VoiceBook.Normalize(_native.Compute(stream));
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
