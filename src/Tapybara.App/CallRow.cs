using System.IO;
using System.Windows;
using System.Windows.Media;
using Tapybara.Core.Calls;

using Brush = System.Windows.Media.Brush;

namespace Tapybara.App;

/// <summary>Строка списка звонков — то, что видит глаз.</summary>
/// <remarks>
/// Отдельный тип, а не привязка прямо к <see cref="CallEntry"/>: список
/// показывает не поля, а уже сложенные из них фразы («12:04 · Zoom ·
/// Кирилл, Марина»), и собирать их в разметке значило бы разложить логику
/// по XAML, где её не видно и не проверить.
/// </remarks>
public sealed class CallRow
{
    public required CallEntry Entry { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    /// <summary>Группа в списке: «Сегодня», «Вчера», дата.</summary>
    public required string Day { get; init; }

    public required string StateText { get; init; }

    public required Brush StateBackground { get; init; }

    public required Brush StateForeground { get; init; }

    /// <summary>
    /// Значок виден только у состояний, которые что-то значат.
    /// </summary>
    /// <remarks>
    /// «Готово» — обычное состояние звонка. Девятнадцать зелёных «готово»
    /// подряд были шумом, среди которого терялись «назовите голоса» и
    /// «распознаётся» — ради которых значок и заведён.
    /// </remarks>
    public Visibility BadgeVisibility =>
        Entry.State == CallState.Ready ? Visibility.Collapsed : Visibility.Visible;

    public string Directory => Entry.Directory;

    /// <summary>Всё видимое одной строкой — чтобы не пересобирать список без изменений.</summary>
    public string Signature => $"{Directory}|{Title}|{Subtitle}|{Day}|{StateText}";
}
