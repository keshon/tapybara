using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;

namespace Tapybara.App;

/// <summary>
/// Элементы для экранов, которые строятся кодом, — с теми же стилями, что в разметке.
/// </summary>
/// <remarks>
/// Панель голосов, словарь и карточки настроек собираются в коде. Раньше
/// каждый такой элемент получал кегль, отступ и прозрачность прямо на
/// месте, и код расходился с разметкой соседнего окна. Отсюда — только
/// стили из <c>Resources/Controls.xaml</c>.
/// </remarks>
internal static class Ui
{
    public static TextBlock Caption(string text) => Styled("TextCaption", text);

    public static TextBlock Body(string text) => Styled("TextBody", text);

    public static TextBlock BodySecondary(string text) => Styled("TextBodySecondary", text);

    public static TextBlock BodyStrong(string text) => Styled("TextBodyStrong", text);

    public static TextBlock Subtitle(string text) => Styled("TextSubtitle", text);

    public static TextBlock Title(string text) => Styled("TextTitle", text);

    /// <summary>Карточка: фон, обводка, радиус и отступ — как у всех.</summary>
    public static Border Card(UIElement child) => new() { Child = child, Style = StyleOf("Card") };

    /// <summary>Кнопка-ссылка без рамки.</summary>
    public static Button Link(string text) => new() { Content = text, Style = StyleOf("FlatLinkButton") };

    public static Style StyleOf(string key) => (Style)Application.Current.FindResource(key);

    private static TextBlock Styled(string key, string text) => new() { Text = text, Style = StyleOf(key) };
}

/// <summary>
/// Цвета голосов собеседников — единственные «свои» цвета приложения.
/// </summary>
/// <remarks>
/// <para>
/// Приглушённые, средние по светлоте тона: читаются на светлой и тёмной теме
/// и не спорят ни с акцентом Windows, ни с красным, который значит «идёт
/// запись». Прежние были насыщеннее, и цветные имена в транскрипте делали
/// спокойное окно пёстрым.
/// </para>
/// <para>
/// Цвет — только у точки рядом с именем, а не у самого имени: чтобы
/// различать людей, хватает метки, а текст остаётся текстом.
/// </para>
/// <para>
/// Цвет постоянный — по порядку появления голоса, — чтобы «B» был одного
/// цвета и в транскрипте, и в панели голосов. Собеседник, голоса которого
/// не разделялись, получает цвет первого голоса: это обычный человек, а
/// не «неизвестно кто», и серый цвет читался как выключенный элемент.
/// </para>
/// </remarks>
internal static class VoicePalette
{
    private static readonly Brush[] Voices =
    [
        Frozen(0x3A, 0x93, 0x86),
        Frozen(0x86, 0x6E, 0xC7),
        Frozen(0xC0, 0x82, 0x4A),
        Frozen(0x4A, 0x82, 0xBE),
        Frozen(0xB8, 0x62, 0x8E),
        Frozen(0x7E, 0x93, 0x4E),
    ];

    /// <summary>Владелец микрофона — приглушённый коричневый со значка приложения.</summary>
    public static Brush Me { get; } = Frozen(0xA0, 0x7C, 0x58);

    public static Brush For(int index) => Voices[Math.Max(index, 0) % Voices.Length];

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
