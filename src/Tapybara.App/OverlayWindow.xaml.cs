using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Tapybara.App.Localization;
using Tapybara.Core.Windows;
using Color = System.Windows.Media.Color;
using Rectangle = System.Windows.Shapes.Rectangle;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace Tapybara.App;

/// <summary>Плавающая пилюля-индикатор диктовки поверх всех окон.</summary>
public partial class OverlayWindow : Window
{
    /// <summary>Сколько столбиков в бегущей волне уровня.</summary>
    private const int BarCount = 22;

    private readonly Rectangle[] _bars = new Rectangle[BarCount];

    public OverlayWindow()
    {
        InitializeComponent();
        BuildLevelBars();

        // Создаём HWND заранее, не дожидаясь первого Show().
        // Без этого окно до первого показа не имеет источника представления,
        // а значит и сведений о масштабе экрана — и расчёт позиции берёт
        // масштаб 1. На мониторе со 150% пилюля из-за этого уезжала вправо.
        // Заодно WS_EX_NOACTIVATE успевает примениться до первого показа.
        new WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>Клик по пилюле — то же, что нажать хоткей.</summary>
    public event Action? Clicked;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Стили, которых нет в WPF: окно не забирает фокус и не появляется
        // в Alt+Tab. Ставить их можно только когда HWND уже существует.
        WindowChrome.MakeNonActivating(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Показать пилюлю в состоянии записи.</summary>
    public void ShowRecording(string hotkeyHint)
    {
        RecordingPanel.Visibility = Visibility.Visible;
        TranscribingText.Visibility = Visibility.Collapsed;
        NoteText.Visibility = Visibility.Collapsed;
        HotkeyHint.Text = hotkeyHint;
        ElapsedText.Text = "0:00";
        ResetLevels();

        MoveToActiveScreen();

        // Show(), а не Activate(): активация увела бы фокус с приложения,
        // в которое пользователь собирается диктовать.
        Show();
        Topmost = true; // переутверждаем: полноэкранные окна умеют перекрывать
    }

    /// <summary>Переключить пилюлю в состояние распознавания.</summary>
    public void ShowTranscribing(int percent)
    {
        RecordingPanel.Visibility = Visibility.Collapsed;
        NoteText.Visibility = Visibility.Collapsed;
        TranscribingText.Visibility = Visibility.Visible;
        TranscribingText.Text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture, L.S.PillTranscribing, percent);
    }

    /// <summary>Показать короткое сообщение и спрятать пилюлю.</summary>
    public void FlashAndHide(string note)
    {
        RecordingPanel.Visibility = Visibility.Collapsed;
        TranscribingText.Visibility = Visibility.Collapsed;
        NoteText.Visibility = Visibility.Visible;
        NoteText.Text = note;

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1200),
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();

            // Пользователь мог начать новую диктовку, пока висела вспышка, —
            // тогда прятать нельзя.
            if (NoteText.Visibility == Visibility.Visible)
            {
                Hide();
            }
        };

        timer.Start();
    }

    public void UpdateElapsed(TimeSpan elapsed) =>
        ElapsedText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";

    /// <summary>Добавить свежий уровень справа, сдвинув остальные влево.</summary>
    public void PushLevel(float level)
    {
        for (int i = 0; i < BarCount - 1; i++)
        {
            _bars[i].Height = _bars[i + 1].Height;
        }

        _bars[^1].Height = 3 + (Math.Clamp(level, 0, 1) * 18);
    }

    private void BuildLevelBars()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        brush.Freeze(); // общая замороженная кисть дешевле 22 отдельных

        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = 3,
                Height = 3,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = brush,
                Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            _bars[i] = bar;
            LevelBars.Children.Add(bar);
        }
    }

    private void ResetLevels()
    {
        foreach (Rectangle bar in _bars)
        {
            bar.Height = 3;
        }
    }

    /// <summary>
    /// Поставить пилюлю сверху по центру того экрана, где сейчас курсор.
    /// </summary>
    /// <remarks>
    /// Экран выбирается по курсору, а не «главный»: у пользователя с двумя
    /// мониторами индикатор должен быть там, куда он смотрит.
    /// </remarks>
    private void MoveToActiveScreen()
    {
        System.Drawing.Rectangle area = WinFormsScreen
            .FromPoint(System.Windows.Forms.Control.MousePosition)
            .WorkingArea;

        // WinForms отдаёт физические пиксели, WPF работает в аппаратно-
        // независимых единицах. Без пересчёта на мониторе со 150% масштабом
        // пилюля уезжает вправо: 2560/2 логических единиц — это далеко за
        // серединой экрана шириной 1706 логических единиц.
        //
        // VisualTreeHelper.GetDpi, а не PresentationSource.TransformToDevice:
        // он возвращает осмысленный масштаб и тогда, когда окно ещё не
        // показано, а не молчаливую единицу.
        DpiScale dpi = VisualTreeHelper.GetDpi(this);

        Left = ((area.Left + (area.Width / 2.0)) / dpi.DpiScaleX) - (Width / 2);
        Top = (area.Top / dpi.DpiScaleY) + 14;
    }

    private void OnPillClick(object sender, MouseButtonEventArgs e) => Clicked?.Invoke();
}
