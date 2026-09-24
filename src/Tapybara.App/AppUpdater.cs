using System.Windows.Threading;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Settings;
using Velopack;
using Velopack.Sources;

namespace Tapybara.App;

/// <summary>Где сейчас обновление.</summary>
public enum UpdateStatus
{
    /// <summary>Программа не установлена (zip, сборка из исходников): обновляется руками.</summary>
    Manual,

    /// <summary>Ещё не проверяли.</summary>
    Idle,

    Checking,

    UpToDate,

    Downloading,

    /// <summary>Новая версия скачана и встанет при следующем запуске.</summary>
    Ready,

    Failed,
}

/// <summary>
/// Проверка и скачивание обновлений через Velopack и релизы на GitHub.
/// </summary>
/// <remarks>
/// <para>
/// Скачивает в фоне и ничего не перезапускает само. Скачанная версия ставится
/// при следующем запуске программы (см. <see cref="Program"/>), а раньше —
/// только по кнопке и только когда ничего не пишется. Обновление, которое
/// само перезапускает программу посреди звонка, стоило бы человеку записи.
/// </para>
/// <para>
/// Запрос к GitHub — это список релизов, и больше ничего: ни о человеке, ни о
/// его записях туда не уходит. Но это всё же сетевой запрос, поэтому он
/// выключается в настройках и назван в документации рядом с загрузкой моделей.
/// </para>
/// </remarks>
public sealed class AppUpdater
{
    public const string RepositoryUrl = "https://github.com/keshon/tapybara";

    /// <summary>
    /// Как часто проверять, пока программа работает.
    /// </summary>
    /// <remarks>
    /// Tapybara живёт в трее неделями. Проверка только при запуске означала бы,
    /// что об обновлении узнают после перезагрузки компьютера, то есть редко.
    /// </remarks>
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    /// <summary>
    /// Первая проверка — не сразу при старте: в это время грузится модель, и
    /// сетевому запросу незачем с ней соперничать.
    /// </summary>
    private static readonly TimeSpan FirstAfter = TimeSpan.FromMinutes(1);

    private readonly UpdateManager _manager;
    private readonly Func<AppSettings> _settings;
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _timer;

    public AppUpdater(Func<AppSettings> settings, Dispatcher ui)
    {
        _settings = settings;
        _ui = ui;
        // TAPYBARA_UPDATE_FEED — папка с выходом `build installer`: так
        // обновление проверяется от начала до конца, ничего не публикуя.
        string? feed = Environment.GetEnvironmentVariable("TAPYBARA_UPDATE_FEED");
        _manager = string.IsNullOrWhiteSpace(feed)
            ? new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false))
            : new UpdateManager(feed);

        Status = UpdateStatus.Manual;
        if (_manager.IsInstalled)
        {
            // Скачанное в прошлый раз могло не примениться (программу закрыли
            // до перезапуска) — тогда оно уже ждёт.
            VelopackAsset? pending = _manager.UpdatePendingRestart;
            NewVersion = pending?.Version.ToString();
            Status = pending is null ? UpdateStatus.Idle : UpdateStatus.Ready;
        }

        _timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = FirstAfter };
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = Every;
            _ = CheckAsync(userAsked: false);
        };
    }

    /// <summary>Сменилось состояние или прибавились проценты скачивания.</summary>
    public event Action? Changed;

    public UpdateStatus Status { get; private set; }

    /// <summary>Какая версия найдена или скачана.</summary>
    public string? NewVersion { get; private set; }

    /// <summary>Сколько скачано, 0–100.</summary>
    public int Percent { get; private set; }

    /// <summary>Почему не удалось.</summary>
    public string? Error { get; private set; }

    /// <summary>Установлена ли программа — то есть умеет ли обновляться сама.</summary>
    public bool IsInstalled => _manager.IsInstalled;

    /// <summary>Начать проверять по расписанию. В неустановленной копии — ничего.</summary>
    public void Start()
    {
        if (IsInstalled)
        {
            _timer.Start();
        }
    }

    public void Stop() => _timer.Stop();

    /// <summary>Проверить и, если есть новая версия, скачать её.</summary>
    /// <param name="userAsked">
    /// Нажата кнопка «Проверить». Тогда проверяем, даже если автоматическая
    /// проверка выключена: человек попросил прямо.
    /// </param>
    public async Task CheckAsync(bool userAsked)
    {
        if (!IsInstalled || Status is UpdateStatus.Checking or UpdateStatus.Downloading or UpdateStatus.Ready)
        {
            return;
        }

        if (!userAsked && !_settings().CheckForUpdates)
        {
            return;
        }

        Set(UpdateStatus.Checking);
        try
        {
            UpdateInfo? info = await _manager.CheckForUpdatesAsync().ConfigureAwait(true);
            if (info is null)
            {
                Set(UpdateStatus.UpToDate);
                return;
            }

            NewVersion = info.TargetFullRelease.Version.ToString();
            Percent = 0;
            Set(UpdateStatus.Downloading);

            await _manager.DownloadUpdatesAsync(info, percent => _ui.BeginInvoke(() =>
            {
                Percent = percent;
                Changed?.Invoke();
            })).ConfigureAwait(true);

            AppLog.Info($"Обновление {NewVersion} скачано, встанет при следующем запуске.");
            Set(UpdateStatus.Ready);
        }
        catch (Exception ex)
        {
            // Сеть, GitHub, диск — что бы ни было, обновление не повод падать:
            // программа работает и без него. Пишем в журнал и показываем в
            // настройках, а не во всплывающем окне.
            AppLog.Warn("Не удалось проверить или скачать обновление.", ex);
            Error = ex.Message;
            Set(UpdateStatus.Failed);
        }
    }

    /// <summary>
    /// Поставить скачанное сейчас: запустить установщик, который дождётся
    /// выхода программы, обновит её и запустит снова.
    /// </summary>
    /// <remarks>
    /// Выход — забота вызывающего, и выход обычный, через Shutdown: при нём
    /// дописываются и закрываются файлы. <c>ApplyUpdatesAndRestart</c> не
    /// годится — он завершает процесс сразу, не дав ничего сохранить.
    /// </remarks>
    /// <returns><c>false</c>, если ставить нечего.</returns>
    public bool PrepareRestart()
    {
        VelopackAsset? pending = _manager.UpdatePendingRestart;
        if (pending is null)
        {
            return false;
        }

        _manager.WaitExitThenApplyUpdates(pending, silent: true, restart: true);
        return true;
    }

    private void Set(UpdateStatus status)
    {
        Status = status;
        Changed?.Invoke();
    }
}
