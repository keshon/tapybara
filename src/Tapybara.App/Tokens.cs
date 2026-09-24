using System.Windows;

namespace Tapybara.App;

/// <summary>
/// Размеры интерфейса: шрифты, отступы, скругления — в одном месте.
/// </summary>
/// <remarks>
/// <para>
/// Раньше каждое окно подбирало цифры на глаз: в новых экранах набралось
/// одиннадцать кеглей (11, 11.5, 12, 12.5, 13, 15, 18, 20, 21, 22, 24),
/// семнадцать разных отступов и семь радиусов скругления. По отдельности
/// каждая цифра была «почти правильной», а вместе интерфейс выглядел
/// небрежно — левые края в карточке звонка расходились на 13 пикселей.
/// </para>
/// <para>
/// Шкала — та же, что у Windows 11: подпись 12, текст 14, подзаголовок 20,
/// заголовок 28; отступы кратны четырём; у контролов радиус 4, у карточек 8.
/// Новое значение, которого здесь нет, — повод остановиться и подумать, а не
/// дописать ещё одно число по месту.
/// </para>
/// <para>
/// XAML берёт эти значения через <c>x:Static</c>, код — напрямую. Источник
/// один: если размер поменять здесь, поменяется везде.
/// </para>
/// </remarks>
public static class Tokens
{
    // --- шрифт ----------------------------------------------------------------

    /// <summary>Подписи, метки времени, вспомогательный текст.</summary>
    public const double Caption = 12;

    /// <summary>Основной текст.</summary>
    public const double Body = 14;

    /// <summary>Заголовок карточки или звонка.</summary>
    public const double Subtitle = 20;

    /// <summary>Заголовок страницы.</summary>
    public const double Title = 28;

    // --- отступы: шаг 4 ------------------------------------------------------

    public const double Space1 = 4;
    public const double Space2 = 8;
    public const double Space3 = 12;
    public const double Space4 = 16;
    public const double Space5 = 24;

    /// <summary>Внутренний отступ карточки.</summary>
    public static Thickness CardPadding { get; } = new(Space4, Space3, Space4, Space3);

    /// <summary>Отступ содержимого страницы от краёв окна.</summary>
    public static Thickness PagePadding { get; } = new(Space2, 0, Space4, Space3);

    // --- скругления ----------------------------------------------------------

    /// <summary>Кнопки, поля, строки списков.</summary>
    public static CornerRadius ControlRadius { get; } = new(4);

    /// <summary>Карточки и панели.</summary>
    public static CornerRadius CardRadius { get; } = new(8);

    /// <summary>Капсулы: чипы, значки состояний.</summary>
    public static CornerRadius PillRadius { get; } = new(12);
}
