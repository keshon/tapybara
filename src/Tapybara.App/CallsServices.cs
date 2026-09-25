using Tapybara.Core.Calls;

namespace Tapybara.App;

/// <summary>Что странице звонков нужно от приложения.</summary>
/// <param name="Settings">Настройки: имя владельца, знакомые имена, словарь замен.</param>
/// <param name="CallsDirectory">Где лежат звонки.</param>
/// <param name="LiveState">Что приложение прямо сейчас делает со звонком.</param>
/// <param name="LivePercent">Сколько процентов распознано, если распознаётся.</param>
/// <param name="Transcribe">Распознать звонок.</param>
/// <param name="Reconcile">Привести транскрипт к участникам — перерисовка или разделение заново.</param>
/// <param name="Resplit">Разделить голоса заново на указанное число.</param>
/// <param name="Verify">Сверить реплики с голосами у звонка, который ещё не сверяли.</param>
/// <param name="RefreshPrints">Снять слепки голосов заново после ручной правки.</param>
/// <param name="Render">Перерисовать transcript.md. Мгновенно.</param>
/// <param name="Delete">Удалить звонок, отменив работу над ним.</param>
/// <param name="Cancel">Остановить распознавание звонка или убрать его из очереди.</param>
/// <param name="ToggleRecording">Начать или закончить запись звонка.</param>
/// <param name="CanSplitVoices">Есть ли чем разделять голоса.</param>
/// <param name="FetchModel">Скачать недостающую модель этого типа.</param>
/// <param name="Voices">Книга голосов — для подсказок «похоже на…» и чтобы запоминать названные.</param>
/// <param name="Live">Что сейчас пишется — для кнопки записи.</param>
public sealed record CallsServices(
    SettingsHost Settings,
    Func<string> CallsDirectory,
    Func<string, CallState?> LiveState,
    Func<string, int?> LivePercent,
    Func<CallSession, Task> Transcribe,
    Func<string, Task> Reconcile,
    Func<string, int, Task> Resplit,
    Func<string, Task> Verify,
    Func<string, Task> RefreshPrints,
    Action<string> Render,
    Action<string> Delete,
    Action<string> Cancel,
    Action ToggleRecording,
    Func<bool> CanSplitVoices,
    Action<Tapybara.Core.Models.ModelKind> FetchModel,
    VoiceBook Voices,
    LiveActivity Live);
