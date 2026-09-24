using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Tapybara.App;

/// <summary>Что выбрал пользователь.</summary>
public enum ConfirmChoice
{
    /// <summary>Главное действие — то, что на акцентной кнопке.</summary>
    Primary,

    /// <summary>Второй вариант: тоже продолжить, но иначе.</summary>
    Secondary,

    /// <summary>Ничего не делать.</summary>
    Cancel,
}

/// <summary>
/// Вопрос с понятными кнопками и, если надо, с путями.
/// </summary>
/// <remarks>
/// Заменяет системный <c>MessageBox</c> там, где «Да» и «Нет» ничего не
/// объясняют. Вопрос «перенести модели в новую папку или оставить в старой»
/// на кнопках «Да/Нет» читается как загадка, а цена неверного ответа —
/// полтора гигабайта не там, где нужно.
/// </remarks>
public partial class ConfirmWindow : FluentWindow
{
    public ConfirmWindow(
        string title,
        string message,
        string primaryButton,
        string cancelButton,
        string? secondaryButton = null,
        SymbolRegular icon = SymbolRegular.QuestionCircle24,
        bool danger = false)
    {
        InitializeComponent();

        Title = title;
        WindowTitleBar.Title = title;
        HeadingText.Text = title;
        BodyText.Text = message;
        PrimaryButton.Content = primaryButton;
        CancelButton.Content = cancelButton;
        HeadingIcon.Symbol = icon;

        if (danger)
        {
            HeadingIcon.Foreground = TryFindResource("SystemFillColorCautionBrush") as Brush
                                     ?? new SolidColorBrush(Colors.OrangeRed);
        }

        if (secondaryButton is not null)
        {
            SecondaryButton.Content = secondaryButton;
            SecondaryButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Ответ пользователя.</summary>
    public ConfirmChoice Choice { get; private set; } = ConfirmChoice.Cancel;

    /// <summary>
    /// Добавить строку «подпись — значение» в карточку под текстом.
    /// </summary>
    /// <remarks>
    /// Для путей. Моноширинный шрифт и перенос по символам: путь — это не
    /// фраза, и разрывать его по пробелам неоткуда.
    /// </remarks>
    public void AddDetail(string label, string value)
    {
        DetailCard.Visibility = Visibility.Visible;

        var row = new StackPanel { Margin = new Thickness(0, DetailRows.Children.Count == 0 ? 0 : Tokens.Space3, 0, 0) };

        TextBlock caption = Ui.Caption(label);
        caption.Margin = new Thickness(0, 0, 0, 2);
        row.Children.Add(caption);

        row.Children.Add(new TextBlock
        {
            Text = value,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = Tokens.Caption,
            TextWrapping = TextWrapping.Wrap,
        });

        DetailRows.Children.Add(row);
    }

    /// <summary>
    /// Добавить в карточку под текстом произвольный элемент — например, поле ввода.
    /// </summary>
    /// <remarks>
    /// Короткий вопрос с парой полей — «что услышано, как правильно» — не
    /// стоит отдельного окна со своей разметкой, заголовком и кнопками,
    /// которые разошлись бы с этими.
    /// </remarks>
    public void AddContent(UIElement element)
    {
        DetailCard.Visibility = Visibility.Visible;
        if (element is FrameworkElement framed && DetailRows.Children.Count > 0)
        {
            framed.Margin = new Thickness(0, 12, 0, 0);
        }

        DetailRows.Children.Add(element);
    }

    /// <summary>Показать и дождаться ответа.</summary>
    public static ConfirmChoice Ask(Window? owner, ConfirmWindow window)
    {
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        return window.Choice;
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => Finish(ConfirmChoice.Primary);

    private void OnSecondaryClick(object sender, RoutedEventArgs e) => Finish(ConfirmChoice.Secondary);

    private void OnCancelClick(object sender, RoutedEventArgs e) => Finish(ConfirmChoice.Cancel);

    private void Finish(ConfirmChoice choice)
    {
        Choice = choice;
        DialogResult = choice != ConfirmChoice.Cancel;
        Close();
    }
}
