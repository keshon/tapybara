using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Tapybara.App.Localization;
using Tapybara.Core.Audio;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Speech;
using Wpf.Ui.Controls;

using TextBlock = System.Windows.Controls.TextBlock;

namespace Tapybara.App;

/// <summary>
/// Подбор порога детектора речи по образцу голоса.
/// </summary>
/// <remarks>
/// Порог зависит от микрофона, комнаты и голоса, поэтому единственного
/// правильного значения не существует. Вместо совета «попробуй подвигать
/// ползунок» окно измеряет: пользователь произносит фразу, детектор
/// прогоняется на нескольких порогах, и видно, где он начинает терять речь.
/// </remarks>
public partial class VadTestWindow : FluentWindow
{
    /// <summary>Сколько писать образец.</summary>
    /// <remarks>
    /// Восьми секунд хватает на счёт до десяти с естественными паузами —
    /// именно паузы и делают образец пригодным для подбора.
    /// </remarks>
    private static readonly TimeSpan SampleDuration = TimeSpan.FromSeconds(8);

    private readonly string _vadModelPath;
    private readonly bool _normalize;
    private readonly string? _microphoneDeviceId;

    private CancellationTokenSource? _run;

    public VadTestWindow(string vadModelPath, bool normalize, string? microphoneDeviceId)
    {
        _vadModelPath = vadModelPath;
        _normalize = normalize;
        _microphoneDeviceId = microphoneDeviceId;

        InitializeComponent();

        Title = L.S.VadTestTitle;
        WindowTitleBar.Title = L.S.VadTestTitle;
        IntroText.Text = L.S.VadTestIntro;
        StateText.Text = L.S.VadTestReady;
        StartButton.Content = L.S.ButtonStartTest;
        ApplyButton.Content = L.S.ButtonApplyRecommended;
    }

    /// <summary>Подобранный порог, если пользователь его принял.</summary>
    public double? AcceptedThreshold { get; private set; }

    private bool IsBusy => _run is not null;

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        // Повторное нажатие во время работы — это остановка. Раньше кнопка
        // просто блокировалась, и восемь секунд записи было нечем прервать.
        if (_run is { } running)
        {
            running.Cancel();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _run = cancellation;

        StartButton.Content = L.S.ButtonStopTest;
        ApplyButton.IsEnabled = false;
        ResultsList.Items.Clear();
        ResultsHeader.Visibility = Visibility.Collapsed;
        RecommendationText.Visibility = Visibility.Collapsed;

        try
        {
            float[] samples = await RecordSampleAsync(cancellation.Token);

            StateText.Text = L.S.VadTestAnalyzing;
            LevelBar.Value = 0;

            IReadOnlyList<VadProbe> probes = await VadCalibrator.ProbeAsync(
                samples, _vadModelPath, _normalize, cancellation.Token);

            ShowResults(probes);
        }
        catch (OperationCanceledException)
        {
            StateText.Text = L.S.VadTestReady;
        }
        catch (Exception ex)
        {
            AppLog.Error("Подбор порога детектора не удался.", ex);
            StateText.Text = ex.Message;
        }
        finally
        {
            _run = null;
            cancellation.Dispose();
            StartButton.Content = L.S.ButtonRepeatTest;
            StartButton.IsEnabled = true;
        }
    }

    private async Task<float[]> RecordSampleAsync(CancellationToken cancellationToken)
    {
        using var capture = new MicrophoneCapture { DeviceId = _microphoneDeviceId };
        capture.LevelChanged += level => Dispatcher.BeginInvoke(() =>
        {
            // Та же шкала в децибелах, что у индикатора диктовки: линейная
            // на тихом микрофоне показывает пустоту даже при реальной речи.
            double db = 20 * Math.Log10(Math.Max(level, 1e-7));
            LevelBar.Value = Math.Clamp((db + 50) / 40, 0, 1);
        });

        capture.Start();

        try
        {
            var started = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow - started < SampleDuration)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TimeSpan left = SampleDuration - (DateTimeOffset.UtcNow - started);
                StateText.Text = string.Format(
                    CultureInfo.CurrentCulture, L.S.VadTestSpeakNow, Math.Max(0, (int)left.TotalSeconds + 1));
                await Task.Delay(200, cancellationToken);
            }

            return await capture.StopAsync();
        }
        catch (OperationCanceledException)
        {
            // Микрофон закрываем в любом случае: окно могли просто закрыть,
            // и оставленное открытым устройство висело бы до конца процесса.
            await capture.StopAsync();
            throw;
        }
    }

    private void ShowResults(IReadOnlyList<VadProbe> probes)
    {
        ResultsHeader.Text = L.S.VadTestResultsHeader;
        ResultsHeader.Visibility = Visibility.Visible;

        float? recommended = VadCalibrator.Recommend(probes);

        foreach (VadProbe probe in probes)
        {
            bool isRecommended = recommended is { } value && Math.Abs(value - probe.Threshold) < 0.001f;

            var row = new Grid { Margin = new Thickness(2, 0, 2, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var threshold = new TextBlock
            {
                Text = probe.Threshold.ToString("F2", CultureInfo.CurrentCulture),
                FontWeight = isRecommended ? FontWeights.SemiBold : FontWeights.Normal,
            };

            var detail = new TextBlock
            {
                Text = string.Format(
                    CultureInfo.CurrentCulture,
                    L.S.VadTestRow,
                    probe.SpeechDuration.TotalSeconds,
                    probe.Regions),
                Opacity = isRecommended ? 1 : 0.7,
                FontWeight = isRecommended ? FontWeights.SemiBold : FontWeights.Normal,
            };

            Grid.SetColumn(detail, 1);
            row.Children.Add(threshold);
            row.Children.Add(detail);
            ResultsList.Items.Add(row);
        }

        if (recommended is { } best)
        {
            AcceptedThreshold = best;
            RecommendationText.Text = string.Format(
                CultureInfo.CurrentCulture, L.S.VadTestRecommended, best);
            ApplyButton.IsEnabled = true;
            StateText.Text = L.S.VadTestDone;
        }
        else
        {
            AcceptedThreshold = null;
            RecommendationText.Text = L.S.VadTestNoSpeech;
            StateText.Text = L.S.VadTestNoSpeech;
        }

        RecommendationText.Visibility = Visibility.Visible;
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    /// <summary>Закрытие посреди записи не должно оставлять микрофон открытым.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        _run?.Cancel();
        base.OnClosing(e);
    }

    /// <summary>Пока идёт измерение, закрывать окно кнопкой «Применить» нечего.</summary>
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        ApplyButton.IsEnabled = false;
        _ = IsBusy;
    }
}
