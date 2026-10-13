using System.Windows;
using System.Windows.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Calls;

namespace Tapybara.App;

public partial class CallsPage
{
    private bool NeedsSpeakerCount => _transcript is not null && SpeakerSeparation.NeedsCount(_transcript);

    private async void RetrySpeakerSeparation()
    {
        if (_session is null || _services.SeparateSpeakers is null || _activeEditor?.Flush() == false)
        {
            return;
        }

        string directory = _session.Directory;
        if (_services.LiveState(directory) is not null)
        {
            return;
        }

        var input = new System.Windows.Controls.ComboBox { IsEditable = true, ItemsSource = Enumerable.Range(1, 16).ToList(),
            Text = (_session.SpeakerCountHint ?? _transcript?.ExpectedVoices ?? 0) is > 0 and var count ? count.ToString(L.S.Formatting) : string.Empty,
            Margin = new Thickness(0, 12, 0, 0) };
        Controls.ControlSizing.Apply(input);
        string countHint = _session.ImportedMedia
            ? SpeakerText("Count everyone who speaks in the file, including yourself if audible.", "Считайте всех, кто говорит в файле, включая себя, если вас слышно.")
            : SpeakerText("Count people heard in system audio. Your microphone is a separate track.", "Считайте людей на системной дорожке. Ваш микрофон — отдельная дорожка.");
        var dialog = new ConfirmWindow(SpeakerText("Redo speaker detection", "Повторить определение говорящих"),
            SpeakerText("How many people speak in this audio track? Detection may take several minutes and replace automatic speaker assignments. Your text, names, and manually assigned phrases are preserved.",
                "Сколько людей говорят на этой аудиодорожке? Определение может занять несколько минут и изменить автоматические подписи. Текст, имена и вручную назначенные реплики сохранятся."),
            SpeakerText("Redo detection", "Определить заново"), L.S.ButtonCancel);
        dialog.AddContent(Ui.Caption(countHint));
        dialog.AddContent(input);
        bool Valid() => int.TryParse(input.Text, out int n) && n is > 0 and <= 100;
        dialog.PrimaryEnabled = Valid();
        input.AddHandler(System.Windows.Controls.TextBox.TextChangedEvent,
            new TextChangedEventHandler((_, _) => dialog.PrimaryEnabled = Valid()));
        if (ConfirmWindow.Ask(Window.GetWindow(this), dialog) != ConfirmChoice.Primary || !Valid())
        {
            return;
        }

        if (_services.LiveState(directory) is not null)
        {
            return;
        }

        bool succeeded = await _services.SeparateSpeakers(directory, int.Parse(input.Text, L.S.Formatting));
        if (!succeeded && !_disposed && _session?.Directory == directory)
        {
            BannerText.Text = SpeakerText("Speaker separation did not finish. Your transcript and corrections are unchanged; you can retry.",
                "Разделение не завершено. Текст и исправления не изменились; можно повторить.");
            Banner.Visibility = Visibility.Visible;
        }
    }
}
