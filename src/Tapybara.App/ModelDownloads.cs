using System.IO;
using System.Net.Http;
using Tapybara.App.Localization;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Models;
using Tapybara.Core.Settings;

namespace Tapybara.App;

/// <summary>Закачка одной модели: как идёт и чем кончилась.</summary>
public sealed class ModelDownload
{
    public required CatalogModel Model { get; init; }

    public CancellationTokenSource Cancellation { get; } = new();

    public bool Running { get; set; } = true;

    /// <summary>Последний замер хода; <c>null</c> — ещё не было.</summary>
    public DownloadProgress? Progress { get; set; }

    /// <summary>Чем кончилась, если не удачей: «отменено», «не удалось…».</summary>
    public string? Note { get; set; }
}

/// <summary>
/// Закачки моделей — у приложения, а не у окна настроек.
/// </summary>
/// <remarks>
/// <para>
/// Скачать модель просят не только со страницы моделей: из карточки после
/// звонка и из окна звонков, где «не хватает модели». Оттуда открывается
/// окно настроек, и человек его закрывает, возвращаясь туда, откуда пришёл.
/// Пока закачки жили в окне, закрытие молча их отменяло.
/// </para>
/// <para>
/// Окно настроек только показывает, что здесь происходит, и подписывается на
/// <see cref="Changed"/>.
/// </para>
/// </remarks>
public sealed class ModelDownloads(SettingsHost settings)
{
    private readonly Dictionary<string, ModelDownload> _all = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>У закачки этого файла что-то изменилось: ход, конец, отмена.</summary>
    public event Action<string>? Changed;

    /// <summary>Модель легла в папку — движку и спискам пора перечитать.</summary>
    public event Action? Landed;

    /// <summary>Закачка файла — идущая или последняя неудачная; <c>null</c> — не было.</summary>
    public ModelDownload? Of(string fileName) => _all.GetValueOrDefault(fileName);

    public bool IsRunning(string fileName) => Of(fileName) is { Running: true };

    /// <summary>Скачать модель каталога; уже скачанную или скачиваемую — нет.</summary>
    public async Task StartAsync(CatalogModel model)
    {
        string directory = ModelsDirectory();
        if (IsRunning(model.FileName) || File.Exists(Path.Combine(directory, model.FileName)))
        {
            return;
        }

        var download = new ModelDownload { Model = model };
        _all[model.FileName] = download;
        Changed?.Invoke(model.FileName);

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (download.Running)
            {
                download.Progress = p;
                Changed?.Invoke(model.FileName);
            }
        });

        bool landed = false;
        try
        {
            using var downloader = new ModelDownloader();
            await downloader.DownloadAsync(model, directory, progress, download.Cancellation.Token).ConfigureAwait(true);
            _all.Remove(model.FileName);
            landed = true;

            // Скачали первую модель типа — сразу ею и пользуемся: выбирать её
            // отдельным действием было бы пустой формальностью.
            AdoptIfNothingChosen(model);
        }
        catch (OperationCanceledException)
        {
            download.Note = L.S.DownloadCancelled;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            AppLog.Error($"Не удалось скачать {model.FileName}.", ex);
            download.Note = string.Format(L.S.Formatting, L.S.DownloadFailed, ex.Message);
        }
        finally
        {
            download.Running = false;
            download.Cancellation.Dispose();
        }

        Changed?.Invoke(model.FileName);
        if (landed)
        {
            Landed?.Invoke();
        }
    }

    public void Cancel(string fileName)
    {
        if (Of(fileName) is { Running: true } download)
        {
            download.Cancellation.Cancel();
        }
    }

    /// <summary>Остановить всё — при выходе из приложения.</summary>
    public void CancelAll()
    {
        foreach (ModelDownload download in _all.Values.Where(d => d.Running))
        {
            download.Cancellation.Cancel();
        }
    }

    /// <summary>
    /// Сделать модель выбранной, если выбранного файла её типа на диске нет.
    /// </summary>
    public void AdoptIfNothingChosen(CatalogModel model)
    {
        AppSettings current = settings.Current;
        if (ModelLocator.Resolve(ModelShelf.Chosen(current, model.Kind), current.ModelsDirectory) is null)
        {
            settings.Update(s => ModelShelf.Choose(s, model.Kind, model.FileName));
        }
    }

    /// <summary>
    /// Папка, куда качать: спрашивается в момент закачки, а не запоминается —
    /// её меняют прямо на странице моделей.
    /// </summary>
    public string ModelsDirectory() =>
        ModelLocator.FindModelsDirectory(settings.Current.ModelsDirectory) ?? AppPaths.DefaultModelsDirectory;
}
