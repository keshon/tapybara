using Tapybara.Core.Dictation;

namespace Tapybara.App;

/// <summary>Почему диктовка сейчас не начнётся.</summary>
public enum DictationBlock
{
    None,

    /// <summary>Модели распознавания нет вовсе.</summary>
    NoModel,

    /// <summary>Модель ещё загружается.</summary>
    Loading,

    /// <summary>Модель не загрузилась; причина — в <see cref="LiveActivity.BlockDetail"/>.</summary>
    Failed,
}

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

    /// <summary>
    /// Диктовку попросили, а начать её нечем.
    /// </summary>
    /// <remarks>
    /// Раньше об этом говорила только подсказка значка в трее. Нажавший
    /// «Диктовать» в окне видел, что не происходит ничего, — и не узнавал
    /// почему.
    /// </remarks>
    public DictationBlock Block { get; private set; }

    /// <summary>Текст ошибки для <see cref="DictationBlock.Failed"/>.</summary>
    public string? BlockDetail { get; private set; }

    /// <summary>Сообщить, почему диктовка не начнётся, или снять сообщение.</summary>
    internal void ReportBlock(DictationBlock block, string? detail = null)
    {
        if (block == Block && detail == BlockDetail)
        {
            return;
        }

        Block = block;
        BlockDetail = detail;
        Changed?.Invoke();
    }

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
