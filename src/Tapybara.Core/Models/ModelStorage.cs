using Tapybara.Core.Diagnostics;

namespace Tapybara.Core.Models;

/// <summary>Модель, лежащая на диске.</summary>
/// <param name="FileName">Имя файла.</param>
/// <param name="Path">Полный путь.</param>
/// <param name="Bytes">Размер.</param>
/// <param name="Kind">Распознавание или детектор речи.</param>
public sealed record InstalledModel(string FileName, string Path, long Bytes, ModelKind Kind);

/// <summary>Итог переноса моделей в другую папку.</summary>
/// <param name="Moved">Сколько файлов перенесено.</param>
/// <param name="Failed">Что перенести не удалось, с причиной.</param>
public sealed record MoveResult(int Moved, IReadOnlyList<string> Failed);

/// <summary>
/// Файлы моделей на диске: перечислить, удалить, перенести.
/// </summary>
/// <remarks>
/// Модели весят сотни мегабайт и накапливаются: попробовал три, оставил одну,
/// а место занимают все три. Без возможности удалить их из программы человек
/// либо лезет в папку руками, либо просто не убирает — и то и другое плохо.
/// </remarks>
public static class ModelStorage
{
    /// <summary>Всё, что лежит в папке моделей.</summary>
    public static IReadOnlyList<InstalledModel> List(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return
            [
                .. Directory.EnumerateFiles(directory, "ggml-*.bin")
                    .Select(path => new InstalledModel(
                        Path.GetFileName(path),
                        path,
                        new FileInfo(path).Length,
                        IsSpeechDetector(path) ? ModelKind.SpeechDetector : ModelKind.Recognition))
                    .OrderBy(m => m.Kind)
                    .ThenBy(m => m.FileName, StringComparer.OrdinalIgnoreCase),
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Не удалось перечислить модели в {directory}.", ex);
            return [];
        }
    }

    /// <summary>
    /// Детектор речи отличается от модели распознавания только именем.
    /// </summary>
    /// <remarks>
    /// Лежат они в одной папке и называются одинаково — ggml-*.bin. Silero
    /// в списке моделей распознавания выглядел бы как вариант, который
    /// ничего не распознаёт.
    /// </remarks>
    public static bool IsSpeechDetector(string fileNameOrPath) =>
        Path.GetFileName(fileNameOrPath).Contains("silero", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Удалить модель.
    /// </summary>
    /// <returns><c>null</c>, если удалось; иначе причина отказа.</returns>
    /// <remarks>
    /// Возвращаем сообщение, а не бросаем: самая частая причина — файл ещё
    /// держит загруженный движок, и это не исключительная ситуация, а то,
    /// о чём надо спокойно сказать.
    /// </remarks>
    public static string? Delete(string path)
    {
        try
        {
            File.Delete(path);
            AppLog.Info($"Модель удалена: {Path.GetFileName(path)}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Не удалось удалить {Path.GetFileName(path)}.", ex);
            return ex.Message;
        }
    }

    /// <summary>
    /// Перенести все модели из одной папки в другую.
    /// </summary>
    /// <param name="from">Откуда.</param>
    /// <param name="to">Куда. Создаётся, если её нет.</param>
    /// <param name="progress">Имя файла, который переносится прямо сейчас.</param>
    /// <param name="cancellationToken">Отмена. Уже перенесённое остаётся на новом месте.</param>
    /// <remarks>
    /// В пределах тома это переименование и происходит мгновенно; между
    /// дисками — настоящее копирование полутора гигабайт, поэтому метод
    /// асинхронный и сообщает о ходе.
    /// </remarks>
    public static async Task<MoveResult> MoveAllAsync(
        string from,
        string to,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InstalledModel> models = List(from);
        if (models.Count == 0)
        {
            return new MoveResult(0, []);
        }

        Directory.CreateDirectory(to);

        int moved = 0;
        var failed = new List<string>();

        foreach (InstalledModel model in models)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(model.FileName);

            string target = Path.Combine(to, model.FileName);
            if (File.Exists(target))
            {
                // Одноимённый файл уже на месте — не трогаем ни тот, ни другой.
                // Молча перезаписать чужую модель было бы худшим вариантом.
                failed.Add(model.FileName);
                continue;
            }

            try
            {
                await Task.Run(() => File.Move(model.Path, target), cancellationToken).ConfigureAwait(false);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"Не удалось перенести {model.FileName}.", ex);
                failed.Add(model.FileName);
            }
        }

        AppLog.Info($"Перенесено моделей: {moved} из {models.Count}, {from} → {to}");
        return new MoveResult(moved, failed);
    }

    /// <summary>Суммарный размер моделей в папке.</summary>
    public static long TotalBytes(string? directory) => List(directory).Sum(m => m.Bytes);
}
