using System.Windows;
using Tapybara.App.Localization;
using Wpf.Ui.Controls;

namespace Tapybara.App;

/// <summary>
/// Предупреждение перед первой записью звонка.
/// </summary>
/// <remarks>
/// Один раз за всё время. Не диалог согласия с юридической силой и не попытка
/// снять с себя ответственность — просто честный рассказ о том, что именно
/// сейчас начнёт записываться, пока человек не начал это вслепую.
/// </remarks>
public partial class CallConsentWindow : FluentWindow
{
    public CallConsentWindow()
    {
        InitializeComponent();

        Title = L.S.CallConsentTitle;
        WindowTitleBar.Title = L.S.CallConsentTitle;
        HeadingText.Text = L.S.CallConsentTitle;
        BodyText.Text = L.S.CallConsentBody;
        PointsText.Text = L.S.CallConsentPoints;
        AcceptButton.Content = L.S.CallConsentAccept;
        CancelButton.Content = L.S.CallConsentCancel;
    }

    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
