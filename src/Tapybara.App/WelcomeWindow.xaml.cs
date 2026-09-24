using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Audio;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Models;
using Tapybara.Core.Settings;
using Wpf.Ui.Controls;

using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Tapybara.App;

/// <summary>
/// Первый запуск: скачать модель, проверить микрофон, надиктовать первую фразу.
/// </summary>
/// <remarks>
/// Показывается один раз — пока не пройден или не пропущен, см.
/// <see cref="AppSettings.OnboardingDone"/>. Всё, что здесь делается, делается и
/// в настройках: окно не вводит ничего своего, а только ведёт по порядку.
/// </remarks>
public partial class WelcomeWindow : FluentWindow, IDisposable
{
    private const int ModelStepIndex = 0;
    private const int MicStepIndex = 1;
    private const int TryStepIndex = 2;

    private readonly SettingsHost _settings;
    private readonly Func<IReadOnlyList<string>> _availableModels;
    private readonly Action _modelsChanged;
    private readonly Action _openModelsSettings;
    private readonly List<(RadioButton Button, CatalogModel Model)> _choices = [];

    private int _step;
    private CancellationTokenSource? _download;
    private MicrophoneCapture? _microphone;

    public WelcomeWindow(
        SettingsHost settings,
        Func<IReadOnlyList<string>> availableModels,
        Action modelsChanged,
        Action openModelsSettings)
    {
        InitializeComponent();

        _settings = settings;
        _availableModels = availableModels;
        _modelsChanged = modelsChanged;
        _openModelsSettings = openModelsSettings;

        BuildModelChoices();
        FillMicrophones();

        Closed += (_, _) => Dispose();

        ShowStep(ModelStepIndex);
    }

    /// <summary>Прервать скачивание и отпустить микрофон. Зовётся при закрытии окна.</summary>
    public void Dispose()
    {
        _download?.Cancel();
        _download?.Dispose();
        _download = null;
        StopMicrophone();
        GC.SuppressFinalize(this);
    }

    private string ModelsDirectory =>
        ModelLocator.FindModelsDirectory(_settings.Current.ModelsDirectory) ?? AppPaths.DefaultModelsDirectory;

    // --- шаги ----------------------------------------------------------------

    private void ShowStep(int step)
    {
        _step = step;

        Title = L.S.WelcomeTitle;
        WindowTitleBar.Title = L.S.WelcomeTitle;
        SkipButton.Content = L.S.ButtonSkip;
        BackButton.Content = L.S.ButtonBack;
        BackButton.Visibility = step == ModelStepIndex ? Visibility.Hidden : Visibility.Visible;

        ModelStep.Visibility = step == ModelStepIndex ? Visibility.Visible : Visibility.Collapsed;
        MicStep.Visibility = step == MicStepIndex ? Visibility.Visible : Visibility.Collapsed;
        TryStep.Visibility = step == TryStepIndex ? Visibility.Visible : Visibility.Collapsed;

        BuildStepStrip();

        switch (step)
        {
            case ModelStepIndex:
                HeadingText.Text = L.S.WelcomeModelHeading;
                BodyText.Text = L.S.WelcomeModelBody;
                DetectorNote.Text = L.S.WelcomeModelDetector;
                NextButton.Content = HasRecognitionModel() ? L.S.ButtonNext : L.S.ButtonDownload;
                StopMicrophone();
                break;

            case MicStepIndex:
                HeadingText.Text = L.S.WelcomeMicHeading;
                BodyText.Text = L.S.WelcomeMicBody;
                NextButton.Content = L.S.ButtonNext;
                StartMicrophone();
                break;

            default:
                string hotkey = _settings.Current.Hotkey.ToString();
                HeadingText.Text = L.S.WelcomeTryHeading;
                BodyText.Text = string.Format(CultureInfo.CurrentCulture, L.S.WelcomeTryBody, hotkey);
                TryBox.PlaceholderText = L.S.WelcomeTryPlaceholder;
                TryDone.Text = L.S.WelcomeTryDone;
                NextButton.Content = L.S.ButtonFinish;
                StopMicrophone();
                TryBox.Focus();
                break;
        }
    }

    private void BuildStepStrip()
    {
        StepStrip.Children.Clear();
        string[] names = [L.S.WelcomeStepModel, L.S.WelcomeStepMicrophone, L.S.WelcomeStepTry];

        for (int i = 0; i < names.Length; i++)
        {
            if (i > 0)
            {
                StepStrip.Children.Add(new TextBlock { Text = "·", Opacity = 0.4, Margin = new Thickness(8, 0, 8, 0) });
            }

            StepStrip.Children.Add(new TextBlock
            {
                Text = $"{i + 1} {names[i]}",
                FontSize = 12.5,
                FontWeight = i == _step ? FontWeights.SemiBold : FontWeights.Normal,
                Opacity = i == _step ? 1 : 0.6,
                Foreground = i == _step ? TryFindResource("AccentTextFillColorPrimaryBrush") as System.Windows.Media.Brush : null,
            });
        }
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case ModelStepIndex when !HasRecognitionModel():
                await DownloadAsync();
                if (HasRecognitionModel())
                {
                    ShowStep(MicStepIndex);
                }

                break;

            case ModelStepIndex:
                ShowStep(MicStepIndex);
                break;

            case MicStepIndex:
                ShowStep(TryStepIndex);
                break;

            default:
                Finish();
                break;
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => ShowStep(Math.Max(_step - 1, ModelStepIndex));

    private void OnSkipClick(object sender, RoutedEventArgs e) => Finish();

    private void Finish()
    {
        _settings.Update(s => s with { OnboardingDone = true });
        Close();
    }

    // --- модель --------------------------------------------------------------

    /// <summary>
    /// Три модели на выбор, а не весь каталог.
    /// </summary>
    /// <remarks>
    /// Человеку, который видит программу впервые, нужен ответ, а не таблица:
    /// рекомендуемая, поменьше для слабой машины и самая быстрая. Весь
    /// каталог — в настройках.
    /// </remarks>
    private void BuildModelChoices()
    {
        CatalogModel recommended = ModelCatalog.DefaultRecognitionModel;
        CatalogModel?[] picks =
        [
            recommended,
            ModelCatalog.Find("ggml-small-q5_1.bin"),
            ModelCatalog.Find("ggml-base-q5_1.bin"),
        ];

        foreach (CatalogModel model in picks.OfType<CatalogModel>())
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = model.DisplayName, FontWeight = FontWeights.SemiBold });
            content.Children.Add(new TextBlock
            {
                Text = $"{L.S.Describe(model.Tier)} · {FormatSize(model.ApproximateBytes)}",
                FontSize = 12,
                Opacity = 0.7,
            });

            var button = new RadioButton
            {
                Content = content,
                GroupName = "WelcomeModel",
                IsChecked = model == recommended,
                Margin = new Thickness(0, 0, 0, 8),
            };

            _choices.Add((button, model));
            ModelChoices.Children.Add(button);
        }

        var haveOne = new Wpf.Ui.Controls.Button
        {
            Content = L.S.WelcomeModelHave,
            Appearance = ControlAppearance.Transparent,
            Margin = new Thickness(-8, 2, 0, 0),
        };
        haveOne.Click += (_, _) => _openModelsSettings();
        ModelChoices.Children.Add(haveOne);
    }

    private bool HasRecognitionModel() => _availableModels().Count > 0;

    /// <summary>
    /// Скачать выбранную модель и, если его нет, детектор речи.
    /// </summary>
    /// <remarks>
    /// Детектор качается сам, без отдельного вопроса: он меньше мегабайта, и
    /// без него первые же диктовки хуже по таймингам и с галлюцинациями на
    /// тишине. Спрашивать новичка про детектор речи — значит спрашивать о
    /// том, чего он ещё не может знать.
    /// </remarks>
    private async Task DownloadAsync()
    {
        CatalogModel model = _choices.FirstOrDefault(c => c.Button.IsChecked == true).Model
                             ?? ModelCatalog.DefaultRecognitionModel;

        var queue = new List<CatalogModel> { model };
        if (ModelLocator.ResolveVadModel(_settings.Current.VadModelFileName, _settings.Current.ModelsDirectory) is null)
        {
            queue.Add(ModelCatalog.DefaultSpeechDetectorModel);
        }

        _download = new CancellationTokenSource();
        NextButton.IsEnabled = false;
        foreach ((RadioButton button, _) in _choices)
        {
            button.IsEnabled = false;
        }

        DownloadProgress.Visibility = Visibility.Visible;

        try
        {
            using var downloader = new ModelDownloader();
            foreach (CatalogModel item in queue)
            {
                var reporter = new Progress<DownloadProgress>(p =>
                {
                    DownloadProgress.IsIndeterminate = p.Fraction is null;
                    DownloadProgress.Value = p.Fraction ?? 0;
                    DownloadText.Text = string.Format(
                        CultureInfo.CurrentCulture,
                        L.S.DownloadProgress,
                        FormatSize(p.ReceivedBytes),
                        p.TotalBytes is { } total ? FormatSize(total) : "?",
                        FormatSize((long)p.BytesPerSecond));
                    DownloadPercent.Text = p.Fraction is { } f
                        ? $"{Math.Round(f * 100).ToString(CultureInfo.CurrentCulture)}%"
                        : string.Empty;
                });

                await downloader.DownloadAsync(item, ModelsDirectory, reporter, _download.Token);
            }

            _settings.Update(s => s with
            {
                ModelFileName = model.FileName,
                VadModelFileName = queue.Count > 1 ? ModelCatalog.DefaultSpeechDetectorModel.FileName : s.VadModelFileName,
            });
            _modelsChanged();
        }
        catch (OperationCanceledException)
        {
            DownloadText.Text = L.S.DownloadCancelled;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException or InvalidDataException)
        {
            AppLog.Warn("Не удалось скачать модель в окне первого запуска.", ex);
            DownloadText.Text = string.Format(CultureInfo.CurrentCulture, L.S.DownloadFailed, ex.Message);
        }
        finally
        {
            _download?.Dispose();
            _download = null;
            NextButton.IsEnabled = true;
            NextButton.Content = HasRecognitionModel() ? L.S.ButtonNext : L.S.ButtonDownload;
            foreach ((RadioButton button, _) in _choices)
            {
                button.IsEnabled = true;
            }
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:F1} GB",
        >= 1_000_000 => $"{bytes / 1_000_000.0:F0} MB",
        >= 1_000 => $"{bytes / 1_000.0:F0} KB",
        _ => $"{bytes} B",
    };

    // --- микрофон ------------------------------------------------------------

    private void FillMicrophones()
    {
        MicBox.Items.Clear();
        MicBox.Items.Add(new ComboBoxItem { Content = L.S.DeviceSystemDefault, Tag = null });
        foreach (AudioDeviceInfo device in AudioDevices.Inputs())
        {
            MicBox.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Id });
        }

        string? current = _settings.Current.MicrophoneDeviceId;
        MicBox.SelectedItem = MicBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == current)
                              ?? MicBox.Items[0];
    }

    private void OnMicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        string? id = item.Tag as string;
        _settings.Update(s => s with { MicrophoneDeviceId = id });

        if (_step == MicStepIndex)
        {
            StartMicrophone();
        }
    }

    /// <summary>
    /// Слушать микрофон ради полоски уровня — и только её.
    /// </summary>
    /// <remarks>
    /// Звук никуда не пишется и в памяти не копится: нужна одна цифра —
    /// громкость, чтобы человек увидел, что его слышат, до первой диктовки,
    /// а не после неё.
    /// </remarks>
    private void StartMicrophone()
    {
        StopMicrophone();

        try
        {
            var microphone = new MicrophoneCapture
            {
                DeviceId = _settings.Current.MicrophoneDeviceId,
                KeepInMemory = false,
            };

            microphone.LevelChanged += level =>
                Dispatcher.BeginInvoke(() => MicLevel.Value = Math.Clamp(level, 0, 1));
            microphone.Start();
            _microphone = microphone;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            AppLog.Warn("Не удалось включить микрофон в окне первого запуска.", ex);
            MicLevel.Value = 0;
        }
    }

    private void StopMicrophone()
    {
        if (_microphone is not { } microphone)
        {
            return;
        }

        _microphone = null;
        microphone.Dispose();
    }

    // --- проба ---------------------------------------------------------------

    private void OnTryChanged(object sender, TextChangedEventArgs e) =>
        TryDone.Visibility = TryBox.Text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}
