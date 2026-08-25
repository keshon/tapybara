using System.Windows.Threading;
using Tapybara.Core.Settings;

namespace Tapybara.App;

/// <summary>Что именно изменилось — чтобы пересобирать только затронутое.</summary>
/// <param name="Previous">Как было.</param>
/// <param name="Current">Как стало.</param>
public sealed record SettingsChange(AppSettings Previous, AppSettings Current)
{
    /// <summary>Нужна ли пересборка движка распознавания.</summary>
    /// <remarks>
    /// Список полный, включая детектор речи. Раньше настройки детектора в него
    /// не входили: подобранный порог сохранялся в файл, показывался в поле — и
    /// не действовал до следующего запуска. Ошибка, которую невозможно
    /// заметить, потому что интерфейс выглядит так, будто всё применилось.
    /// </remarks>
    public bool AffectsEngine =>
        Previous.ModelFileName != Current.ModelFileName
        || Previous.ModelsDirectory != Current.ModelsDirectory
        || Previous.Language != Current.Language
        || Previous.Prompt != Current.Prompt
        || Previous.IdleUnloadMinutes != Current.IdleUnloadMinutes
        || Previous.UseVoiceActivityDetection != Current.UseVoiceActivityDetection
        || Previous.VadModelFileName != Current.VadModelFileName
        || Math.Abs(Previous.VadThreshold - Current.VadThreshold) > 1e-6
        || Previous.NormalizeAudio != Current.NormalizeAudio;

    public bool AffectsHotkey => !Equals(Previous.Hotkey, Current.Hotkey);

    public bool AffectsUiLanguage => Previous.UiLanguage != Current.UiLanguage;

    public bool AffectsTheme => Previous.Theme != Current.Theme;

    public bool AffectsOverlay =>
        Previous.ShowOverlay != Current.ShowOverlay
        || Previous.OverlayLeft != Current.OverlayLeft
        || Previous.OverlayTop != Current.OverlayTop;
}

/// <summary>
/// Единственный владелец настроек.
/// </summary>
/// <remarks>
/// <para>
/// Раньше настройки жили в двух местах сразу: поле в приложении и снимок
/// внутри окна настроек. Синхронизация была односторонней, и это давало
/// тихую потерю изменений — сменить модель из меню трея при открытом окне
/// настроек, а потом щёлкнуть в окне любой переключатель, и модель
/// возвращалась к прежней: окно отправляло обратно свой устаревший снимок.
/// </para>
/// <para>
/// Теперь копия одна. Все правки идут через <see cref="Update"/>, все
/// заинтересованные слушают <see cref="Changed"/>.
/// </para>
/// </remarks>
public sealed class SettingsHost : IDisposable
{
    /// <summary>
    /// Пауза перед записью на диск.
    /// </summary>
    /// <remarks>
    /// Каждое движение переключателя писало весь файл заново. Само по себе это
    /// дёшево, но настройки правят пачками, а запись — это ещё и переименование
    /// файла: складывать их в одну незачем.
    /// </remarks>
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(700);

    private readonly DispatcherTimer _saveTimer;
    private AppSettings _current;
    private bool _savePending;

    public SettingsHost(AppSettings initial)
    {
        _current = initial;
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            Flush();
        };
    }

    /// <summary>Действующие настройки.</summary>
    public AppSettings Current => _current;

    /// <summary>Настройки изменились.</summary>
    public event Action<SettingsChange>? Changed;

    /// <summary>Сохранить не удалось — интерфейс должен об этом сказать.</summary>
    public event Action? SaveFailed;

    /// <summary>Изменить настройки.</summary>
    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        AppSettings previous = _current;
        AppSettings updated = mutate(previous);

        if (updated == previous)
        {
            return; // record сравнивается по значению — лишних событий не будет
        }

        _current = updated;
        _savePending = true;
        _saveTimer.Stop();
        _saveTimer.Start();

        Changed?.Invoke(new SettingsChange(previous, updated));
    }

    /// <summary>Записать на диск немедленно — при выходе.</summary>
    public void Flush()
    {
        if (!_savePending)
        {
            return;
        }

        _savePending = false;
        if (!SettingsStore.Save(_current))
        {
            SaveFailed?.Invoke();
        }
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        Flush();
    }
}
