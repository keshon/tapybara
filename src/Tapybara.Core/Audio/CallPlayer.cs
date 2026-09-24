using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Tapybara.Core.Calls;
using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Audio;

/// <summary>
/// Проигрывание записанного звонка — целиком или кусочком.
/// </summary>
/// <remarks>
/// <para>
/// Нужно, чтобы назвать голос: по тексту цитаты человека узнают не всегда,
/// по голосу — почти всегда. Раньше послушать звонок можно было только
/// открыв папку и найдя нужное место в плеере вручную.
/// </para>
/// <para>
/// Дорожки смешиваются на лету, а не сводятся в третий файл: сведённая
/// копия часового звонка — это ещё сто с лишним мегабайт на диске ради
/// того, чтобы иногда послушать полминуты.
/// </para>
/// <para>
/// Не потокобезопасен: все вызовы — из одного потока, в приложении это
/// поток интерфейса. <see cref="Stopped"/> приходит туда же — WaveOutEvent
/// сам возвращает событие в контекст синхронизации, в котором создан.
/// </para>
/// </remarks>
public sealed class CallPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private List<AudioFileReader> _readers = [];
    private TimeSpan _from;

    /// <summary>Проигрывание закончилось само или его остановили.</summary>
    public event Action? Stopped;

    /// <summary>Играет ли сейчас.</summary>
    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    /// <summary>Папка звонка, который играет. <c>null</c> — ничего не играет.</summary>
    public string? CallDirectory { get; private set; }

    /// <summary>Какую дорожку играем. <c>null</c> — обе.</summary>
    public CallChannel? Channel { get; private set; }

    /// <summary>С какого места начали.</summary>
    public TimeSpan StartedFrom => _from;

    /// <summary>
    /// Где сейчас проигрывание от начала записи.
    /// </summary>
    /// <remarks>
    /// По положению чтения, а не по часам вывода: на величину буфера
    /// (десятые доли секунды) впереди того, что слышно. Для подсветки
    /// реплики этого достаточно, а для монтажа никто этим не пользуется.
    /// </remarks>
    public TimeSpan Position => _readers.Count > 0 ? _readers[0].CurrentTime : _from;

    /// <summary>Играть звонок с места <paramref name="from"/>.</summary>
    /// <param name="callDirectory">Папка звонка.</param>
    /// <param name="from">Откуда.</param>
    /// <param name="to">До куда; <c>null</c> — до конца.</param>
    /// <param name="channel">
    /// Одна дорожка или обе. Голос собеседника слушают по его дорожке: на
    /// общей записи поверх него звучал бы владелец микрофона.
    /// </param>
    /// <returns>Удалось ли начать.</returns>
    public bool Play(string callDirectory, TimeSpan from, TimeSpan? to = null, CallChannel? channel = null)
    {
        Stop();

        var paths = new List<string>();
        if (channel is null or CallChannel.Mine)
        {
            paths.Add(Path.Combine(callDirectory, CallSession.MicFileName));
        }

        if (channel is null or CallChannel.Theirs)
        {
            paths.Add(Path.Combine(callDirectory, CallSession.SystemFileName));
        }

        try
        {
            foreach (string path in paths.Where(File.Exists))
            {
                var reader = new AudioFileReader(path);
                if (from < reader.TotalTime)
                {
                    reader.CurrentTime = from < TimeSpan.Zero ? TimeSpan.Zero : from;
                }

                _readers.Add(reader);
            }

            if (_readers.Count == 0)
            {
                return false;
            }

            ISampleProvider source = _readers.Count == 1
                ? _readers[0]
                : new MixingSampleProvider(_readers);

            if (to is { } end && end > from)
            {
                source = new OffsetSampleProvider(source) { Take = end - from };
            }

            var output = new WaveOutEvent { DesiredLatency = 150 };
            output.PlaybackStopped += OnPlaybackStopped;
            output.Init(source);
            output.Play();

            _output = output;
            _from = from;
            CallDirectory = callDirectory;
            Channel = channel;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ArgumentException or NAudio.MmException)
        {
            // Нет устройства вывода, файл занят, заголовок битый — звонок от
            // этого не пострадал, просто послушать его сейчас не выйдет.
            AppLog.Warn($"Не удалось проиграть звонок {callDirectory}.", ex);
            Release();
            return false;
        }
    }

    /// <summary>Остановить, если играет.</summary>
    public void Stop()
    {
        if (_output is null)
        {
            return;
        }

        // Отписываемся ДО остановки: иначе Stopped пришёл бы от старого
        // проигрывания уже после того, как начато новое, и интерфейс
        // погасил бы кнопку свежего.
        _output.PlaybackStopped -= OnPlaybackStopped;
        _output.Stop();
        Release();
        Stopped?.Invoke();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            AppLog.Warn("Проигрывание звонка оборвалось.", e.Exception);
        }

        Release();
        Stopped?.Invoke();
    }

    private void Release()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Dispose();
            _output = null;
        }

        foreach (AudioFileReader reader in _readers)
        {
            reader.Dispose();
        }

        _readers = [];
        CallDirectory = null;
        Channel = null;
    }

    public void Dispose()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
        }

        Release();
    }
}
