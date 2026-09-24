using Tapybara.Core.Dictation;

namespace Tapybara.App;

/// <summary>
/// Что сейчас пишется — диктовка, звонок — и сколько уже идёт.
/// </summary>
/// <remarks>
/// Кнопки записи в окне Tapybara смотрят сюда, а не каждая в свой источник.
/// Состояние публикует приложение из того же места, где решает про таймер
/// пилюли: там сходятся все переходы и диктовки, и звонка, и кнопка не
/// может разойтись с пилюлей на экране.
/// </remarks>
public sealed class LiveActivity
{
    /// <summary>Что-то началось, закончилось или прошла секунда записи.</summary>
    public event Action? Changed;

    public DictationState Dictation { get; private set; }

    public TimeSpan DictationElapsed { get; private set; }

    public bool CallRecording { get; private set; }

    public TimeSpan CallElapsed { get; private set; }

    /// <summary>Обновить состояние.</summary>
    /// <remarks>
    /// Время — с точностью до секунды: таймер приложения тикает пять раз в
    /// секунду ради плавной пилюли, а кнопке столько перерисовок ни к чему.
    /// </remarks>
    internal void Publish(DictationState dictation, TimeSpan dictationElapsed, bool call, TimeSpan callElapsed)
    {
        TimeSpan dictated = WholeSeconds(dictationElapsed);
        TimeSpan called = WholeSeconds(callElapsed);

        if (dictation == Dictation && dictated == DictationElapsed && call == CallRecording && called == CallElapsed)
        {
            return;
        }

        Dictation = dictation;
        DictationElapsed = dictated;
        CallRecording = call;
        CallElapsed = called;
        Changed?.Invoke();
    }

    private static TimeSpan WholeSeconds(TimeSpan value) => TimeSpan.FromSeconds(Math.Floor(Math.Max(0, value.TotalSeconds)));
}
