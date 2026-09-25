using Tapybara.Core.Settings;

namespace Tapybara.Core.Models;

/// <summary>Строка страницы моделей: модель одного типа — из каталога или своя.</summary>
/// <param name="Kind">Тип модели.</param>
/// <param name="FileName">Имя файла в папке моделей.</param>
/// <param name="Catalog">Запись каталога; <c>null</c> — свой файл, положенный руками.</param>
/// <param name="Installed">Файл на диске; <c>null</c> — не скачана.</param>
/// <param name="InUse">Именно ею сейчас работает приложение.</param>
public sealed record ShelfModel(
    ModelKind Kind,
    string FileName,
    CatalogModel? Catalog,
    InstalledModel? Installed,
    bool InUse)
{
    /// <summary>Файл модели лежит в папке моделей.</summary>
    public bool IsInstalled => Installed is not null;

    /// <summary>Файл положен руками, в каталоге его нет.</summary>
    public bool IsCustom => Catalog is null;
}

/// <summary>
/// Модели одного типа — одним списком, как бы они ни попали на диск.
/// </summary>
/// <remarks>
/// <para>
/// У модели любого типа три состояния: не скачана, скачана, используется.
/// Этого хватает, чтобы показать все типы одинаково и одинаково ими
/// управлять — выбрать, скачать, удалить.
/// </para>
/// <para>
/// Свои файлы — дообученная модель, положенная в папку руками, — стоят в
/// своей группе наравне с каталогом: их тоже выбирают и удаляют.
/// </para>
/// </remarks>
public static class ModelShelf
{
    /// <summary>Модели типа: сначала каталог в его порядке, затем свои файлы.</summary>
    /// <param name="kind">Тип.</param>
    /// <param name="installed">Что лежит в папке моделей (<see cref="ModelStorage.List"/>).</param>
    /// <param name="settings">Какая модель выбрана.</param>
    public static IReadOnlyList<ShelfModel> Of(ModelKind kind, IReadOnlyList<InstalledModel> installed, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(settings);

        List<InstalledModel> own = [.. installed.Where(m => m.Kind == kind)];
        string? used = InUse(kind, own, settings);

        InstalledModel? OnDisk(string fileName) =>
            own.FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        bool Used(string fileName) => string.Equals(fileName, used, StringComparison.OrdinalIgnoreCase);

        var shelf = new List<ShelfModel>();
        foreach (CatalogModel model in ModelCatalog.All.Where(m => m.Kind == kind))
        {
            shelf.Add(new ShelfModel(kind, model.FileName, model, OnDisk(model.FileName), Used(model.FileName)));
        }

        foreach (InstalledModel file in own.Where(m => ModelCatalog.Find(m.FileName) is null))
        {
            shelf.Add(new ShelfModel(kind, file.FileName, null, file, Used(file.FileName)));
        }

        return shelf;
    }

    /// <summary>
    /// Каких типов из <paramref name="kinds"/> нечем выполнить: ни одна модель типа не используется.
    /// </summary>
    /// <remarks>
    /// По тому же правилу, по которому отмечается строка (<see cref="InUse"/>):
    /// если распознавание работает запасной моделью, её не «не хватает», и
    /// предлагать качать другую незачем.
    /// </remarks>
    public static IReadOnlyList<ModelKind> Missing(
        AppSettings settings,
        IReadOnlyList<ModelKind> kinds,
        IReadOnlyList<InstalledModel> installed)
    {
        ArgumentNullException.ThrowIfNull(kinds);

        return [.. kinds.Where(kind => !Of(kind, installed, settings).Any(m => m.InUse))];
    }

    /// <summary>Какой файл этого типа выбран в настройках.</summary>
    public static string Chosen(AppSettings settings, ModelKind kind)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return kind switch
        {
            ModelKind.Recognition => settings.ModelFileName ?? string.Empty,
            ModelKind.SpeechDetector => settings.VadModelFileName ?? string.Empty,
            ModelKind.VoiceSegmentation => settings.VoiceSegmentationModelFileName,
            ModelKind.VoiceEmbedding => settings.VoiceEmbeddingModelFileName,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>Выбрать файл для его типа.</summary>
    public static AppSettings Choose(AppSettings settings, ModelKind kind, string fileName)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return kind switch
        {
            ModelKind.Recognition => settings with { ModelFileName = fileName },
            ModelKind.SpeechDetector => settings with { VadModelFileName = fileName },
            ModelKind.VoiceSegmentation => settings with { VoiceSegmentationModelFileName = fileName },
            ModelKind.VoiceEmbedding => settings with { VoiceEmbeddingModelFileName = fileName },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    /// <summary>
    /// Каким файлом приложение работает на самом деле.
    /// </summary>
    /// <remarks>
    /// Выбранный — если он на диске. Нет — распознавание и детектор берут
    /// первый попавшийся своего типа (<see cref="ModelLocator.ResolveAnyAvailable"/>,
    /// <see cref="ModelLocator.ResolveVadModel"/>), и отмеченным должен быть
    /// он, а не выбранный, которого нет. Голосовым моделям замены нет: не
    /// скачана выбранная — не работает ничего, и отмеченной не будет ни одна.
    /// </remarks>
    private static string? InUse(ModelKind kind, IReadOnlyList<InstalledModel> own, AppSettings settings)
    {
        string chosen = Chosen(settings, kind);
        if (own.Any(m => string.Equals(m.FileName, chosen, StringComparison.OrdinalIgnoreCase)))
        {
            return chosen;
        }

        return kind is ModelKind.Recognition or ModelKind.SpeechDetector
            ? own.Select(m => m.FileName).Order(StringComparer.Ordinal).FirstOrDefault()
            : null;
    }
}
