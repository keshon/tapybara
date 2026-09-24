using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Tapybara.App.Localization;
using Tapybara.Core.Diagnostics;
using Tapybara.Core.Windows;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using WinFormsScreen = System.Windows.Forms.Screen;

namespace Tapybara.App;

/// <summary>Что сейчас показывает пилюля — от этого зависит, что значит щелчок по ней.</summary>
public enum OverlayMode
{
    /// <summary>Диктовка: запись, распознавание, вспышка результата.</summary>
    Dictation,

    /// <summary>Идёт запись звонка.</summary>
    CallRecording,

    /// <summary>Распознаётся записанный звонок.</summary>
    CallTranscribing,
}

/// <summary>Плавающая пилюля-индикатор диктовки поверх всех окон.</summary>
public partial class OverlayWindow : Window
{
    /// <summary>Сколько столбиков в бегущей волне уровня.</summary>
    private const int BarCount = 22;

    /// <summary>Смещение мыши, после которого клик считается перетаскиванием.</summary>
    private const double DragThreshold = 4;

    private readonly Rectangle[] _bars = new Rectangle[BarCount];

    /// <summary>Идёт восстановление фона — не реагировать на собственную же правку.</summary>
    private bool _restoringBackground;

    /// <summary>О перекрашенном фоне пишем в журнал один раз за запуск.</summary>
    private bool _backgroundIncidentLogged;

    private System.Windows.Threading.DispatcherTimer? _hideTimer;
    private Point _pressedAt;
    private Point _pressedWindowAt;
    private bool _pressed;
    private bool _dragged;


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

    /// <summary>
    /// Фон окна обязан оставаться прозрачным — и возвращается, если его меняют.
    /// </summary>
    /// <remarks>
    /// Пилюля круглая только потому, что окно попиксельно прозрачно, а форму
    /// рисует лежащий внутри <c>Border</c>. Стоит окну получить непрозрачный
    /// фон, и вокруг пилюли появляется белый прямоугольник.
    /// <para>
    /// Виновник известен и найден по стеку: наш же <c>SystemTheme.Apply</c>
    /// зовёт <c>ApplicationThemeManager.Apply</c>, а тот обходит ВСЕ окна
    /// приложения и через <c>WindowBackdrop.RestoreContentBackground</c>
    /// возвращает каждому непрозрачный фон. Библиотеке неоткуда знать, что
    /// одно из окон живёт попиксельной прозрачностью, а тема применяется не
    /// только по кнопке в настройках: Windows шлёт уведомление о смене
    /// оформления по множеству поводов, поэтому и «пропадает иногда».
    /// </para>
    /// <para>
    /// Восстановление ОТЛОЖЕННОЕ. Присваивание прямо здесь роняло приложение:
    /// WPF не даёт переприсвоить свойство, пока сам его меняет, — падало
    /// с «The provided DependencyObject is not a context for this Freezable».
    /// Приоритет <c>Send</c> означает, что правка успевает до отрисовки, и белого
    /// прямоугольника не видно даже кадром.
    /// </para>
    /// </remarks>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property != BackgroundProperty || _restoringBackground || IsTransparent(e.NewValue))
        {
            return;
        }

        if (!_backgroundIncidentLogged)
        {
            _backgroundIncidentLogged = true;
            AppLog.Warn($"Фон пилюли перекрасили в {Describe(e.NewValue)} — возвращаю прозрачный.");
        }

        _restoringBackground = true;
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Send,
            () =>
            {
                try
                {
                    if (!IsTransparent(Background))
                    {
                        Background = Brushes.Transparent;
                    }
                }
                finally
                {
                    _restoringBackground = false;
                }
            });
    }

    /// <summary>Прозрачна ли кисть настолько, что окно остаётся невидимым.</summary>
    /// <remarks>
    /// <c>null</c> тоже годится: окно без кисти не рисует фон вовсе.
    /// </remarks>
    private static bool IsTransparent(object? brush) =>
        brush is null || (brush is SolidColorBrush solid && solid.Color.A == 0);

    private static string Describe(object? brush) => brush switch
    {
        SolidColorBrush solid => solid.Color.ToString(CultureInfo.InvariantCulture),
        null => "null",
        _ => brush.GetType().Name,
    };

    /// <summary>Клик по пилюле диктовки — то же, что нажать хоткей.</summary>
    /// <remarks>
    /// По пилюле звонка щелчок не приходит вовсе: у звонка своя кнопка
    /// остановки, см. <see cref="StopCallRequested"/>.
    /// </remarks>
    public event Action? Clicked;

    /// <summary>Нажата кнопка ■ на пилюле звонка.</summary>
    public event Action? StopCallRequested;

    /// <summary>Щелчок по пилюле распознающегося звонка — показать звонок.</summary>
    public event Action? OpenCallsRequested;

    /// <summary>Что сейчас на пилюле.</summary>
    public OverlayMode Mode { get; private set; }

    /// <summary>Пользователь перетащил пилюлю. Координаты логические.</summary>
    public event Action<double, double>? Moved;

    /// <summary>Куда пользователь однажды поставил пилюлю. <c>null</c> — по умолчанию.</summary>
    public (double Left, double Top)? PinnedPosition { get; set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Стили, которых нет в WPF: окно не забирает фокус и не появляется
        // в Alt+Tab. Ставить их можно только когда HWND уже существует.
        WindowChrome.MakeNonActivating(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Показать пилюлю в состоянии записи диктовки.</summary>
    public void ShowRecording(string hotkeyHint)
    {
        ShowPanel(recording: true);
        SetCallMode(false);
        HotkeyHint.Text = string.Format(CultureInfo.CurrentCulture, L.S.PillHintStop, hotkeyHint);
        ElapsedText.Text = "0:00";
        ResetLevels();
        Reveal();
    }

    /// <summary>Показать пилюлю в состоянии записи звонка.</summary>
    /// <param name="hotkeyHint">Сочетание, которым звонок останавливается.</param>
    /// <remarks>
    /// Раньше запись звонка не показывалась вовсе — только точка в трее, и та
    /// того же цвета, что у диктовки. Запись, идущую часами, было буквально
    /// нечем заметить.
    /// </remarks>
    public void ShowCallRecording(string hotkeyHint)
    {
        ShowPanel(recording: true);
        SetCallMode(true);
        HotkeyHint.Text = string.Format(CultureInfo.CurrentCulture, L.S.PillRecordingCall, hotkeyHint);
        ElapsedText.Text = "0:00";
        ResetLevels();
        Reveal();
    }

    /// <summary>
    /// Показать, что записанный звонок распознаётся.
    /// </summary>
    /// <remarks>
    /// Раньше это было видно только во всплывающей подсказке иконки в трее:
    /// часовой звонок распознавался минутами, и понять, идёт ли работа, можно
    /// было, лишь наведя мышь на точку размером в восемь пикселей.
    /// </remarks>
    public void ShowCallTranscribing(string text)
    {
        ShowPanel(recording: false);
        Mode = OverlayMode.CallTranscribing;
        TranscribingPanel.Visibility = Visibility.Visible;
        TranscribingText.Text = text;
        CancelHint.Visibility = Visibility.Collapsed;
        Reveal();
    }

    private void SetCallMode(bool call)
    {
        Mode = call ? OverlayMode.CallRecording : OverlayMode.Dictation;
        RecordingRing.Visibility = call ? Visibility.Visible : Visibility.Collapsed;
        RecordingDot.Width = call ? 5 : 9;
        RecordingDot.Height = call ? 5 : 9;
        StopCallButton.Visibility = call ? Visibility.Visible : Visibility.Collapsed;
        StopCallButton.ToolTip = L.S.PillStopCall;
    }

    /// <summary>Переключить пилюлю в состояние распознавания.</summary>
    public void ShowTranscribing(int percent)
    {
        ShowPanel(recording: false);
        TranscribingPanel.Visibility = Visibility.Visible;
        TranscribingText.Text = string.Format(CultureInfo.CurrentCulture, L.S.PillTranscribing, percent);
        CancelHint.Visibility = Visibility.Visible;
        CancelHint.Text = L.S.PillHintCancel;
    }

    /// <summary>Показать короткое сообщение и спрятать пилюлю.</summary>
    public void FlashAndHide(string note)
    {
        ShowPanel(recording: false);
        NoteText.Visibility = Visibility.Visible;
        NoteText.Text = note;

        // Один таймер на окно, а не новый на каждую вспышку: прежний вариант
        // плодил по объекту на каждую диктовку и держал их до срабатывания.
        _hideTimer ??= CreateHideTimer();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>Спрятать немедленно.</summary>
    public void HideNow()
    {
        _hideTimer?.Stop();
        Hide();
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

    private System.Windows.Threading.DispatcherTimer CreateHideTimer()
    {
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

        return timer;
    }

    private void ShowPanel(bool recording)
    {
        _hideTimer?.Stop();
        if (!recording)
        {
            SetCallMode(false);
        }

        RecordingPanel.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        TranscribingPanel.Visibility = Visibility.Collapsed;
        NoteText.Visibility = Visibility.Collapsed;
    }

    private void Reveal()
    {
        // Дешёвая перестраховка на случай, если фон перекрасили, пока пилюля
        // была скрыта: тогда OnPropertyChanged уже отработал, но проверить
        // ещё раз ничего не стоит.
        if (!IsTransparent(Background))
        {
            Background = Brushes.Transparent;
        }

        MoveIntoPlace();

        // Show(), а не Activate(): активация увела бы фокус с приложения,
        // в которое пользователь собирается диктовать.
        Show();
        Topmost = true; // переутверждаем: полноэкранные окна умеют перекрывать
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
    /// Поставить пилюлю туда, где её ждут.
    /// </summary>
    /// <remarks>
    /// Если пользователь однажды перетащил её — туда, куда поставил, но с
    /// проверкой, что это место всё ещё на каком-то экране: монитор могли
    /// отключить, и окно оказалось бы за пределами видимого.
    /// </remarks>
    private void MoveIntoPlace()
    {
        if (PinnedPosition is { } pinned && IsOnSomeScreen(pinned.Left, pinned.Top))
        {
            Left = pinned.Left;
            Top = pinned.Top;
            return;
        }

        MoveToActiveScreen();
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
        System.Drawing.Point cursor = System.Windows.Forms.Control.MousePosition;
        System.Drawing.Rectangle area = WinFormsScreen.FromPoint(cursor).WorkingArea;

        // WinForms отдаёт физические пиксели, WPF работает в аппаратно-
        // независимых единицах. Масштаб берём У ЦЕЛЕВОГО монитора, а не у
        // того, на котором окно висит сейчас: при разном масштабе на двух
        // мониторах это разные числа, и пилюля уезжала мимо.
        (double scaleX, double scaleY) = ScaleOfMonitorAt(cursor);

        // Ширина у окна плавающая (SizeToContent), и на момент первого показа
        // она может быть ещё не посчитана — пересчитываем принудительно.
        if (double.IsNaN(Width) || Width <= 0)
        {
            Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        double width = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;

        Left = ((area.Left + (area.Width / 2.0)) / scaleX) - (width / 2);
        Top = (area.Top / scaleY) + 14;
    }

    private static bool IsOnSomeScreen(double left, double top)
    {
        foreach (WinFormsScreen screen in WinFormsScreen.AllScreens)
        {
            (double scaleX, double scaleY) = ScaleOfMonitorAt(
                new System.Drawing.Point(screen.WorkingArea.Left + 1, screen.WorkingArea.Top + 1));

            double l = screen.WorkingArea.Left / scaleX;
            double t = screen.WorkingArea.Top / scaleY;
            double r = screen.WorkingArea.Right / scaleX;
            double b = screen.WorkingArea.Bottom / scaleY;

            // Достаточно, чтобы левый верхний угол попадал на экран: пилюля
            // маленькая, и требовать полного вхождения незачем.
            if (left >= l - 40 && left <= r - 40 && top >= t - 10 && top <= b - 20)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Масштаб конкретного монитора.
    /// </summary>
    /// <remarks>
    /// <c>VisualTreeHelper.GetDpi</c> отвечает про монитор, на котором окно
    /// находится СЕЙЧАС, а нам нужен тот, куда мы собираемся его поставить.
    /// На смешанной конфигурации (100% и 150%) это разные числа.
    /// </remarks>
    private static (double X, double Y) ScaleOfMonitorAt(System.Drawing.Point point)
    {
        const int MonitorDefaultToNearest = 2;
        const int MdtEffectiveDpi = 0;

        try
        {
            nint monitor = MonitorFromPoint(new NativePoint { X = point.X, Y = point.Y }, MonitorDefaultToNearest);
            if (monitor != nint.Zero && GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out uint dpiY) == 0)
            {
                return (dpiX / 96.0, dpiY / 96.0);
            }
        }
        catch (DllNotFoundException)
        {
            // Windows 8 и старше — там масштаб общий на систему.
        }
        catch (EntryPointNotFoundException)
        {
            // То же самое.
        }

        return (1.0, 1.0);
    }

    // --- перетаскивание ----------------------------------------------------

    private void OnPillPressed(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragged = false;
        _pressedAt = PointToScreen(e.GetPosition(this));
        _pressedWindowAt = new Point(Left, Top);
        Pill.CaptureMouse();
    }

    private void OnPillMoved(object sender, MouseEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        Point now = PointToScreen(e.GetPosition(this));
        double dx = now.X - _pressedAt.X;
        double dy = now.Y - _pressedAt.Y;

        if (!_dragged && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
        {
            return;
        }

        _dragged = true;
        Left = _pressedWindowAt.X + dx;
        Top = _pressedWindowAt.Y + dy;
    }

    private void OnPillReleased(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        _pressed = false;
        Pill.ReleaseMouseCapture();

        if (_dragged)
        {
            PinnedPosition = (Left, Top);
            Moved?.Invoke(Left, Top);
            return;
        }

        // Не перетаскивание — значит клик. Но не по пилюле звонка: там
        // щелчок раньше запускал диктовку, и человек, решивший, что так
        // останавливают звонок, получал вторую запись вместо остановки первой.
        switch (Mode)
        {
            case OverlayMode.Dictation:
                Clicked?.Invoke();
                break;
            case OverlayMode.CallTranscribing:
                OpenCallsRequested?.Invoke();
                break;
        }
    }

    // --- кнопка остановки звонка -------------------------------------------

    private void OnStopCallPressed(object sender, MouseButtonEventArgs e) =>
        e.Handled = true; // иначе нажатие начнёт перетаскивание пилюли

    private void OnStopCallReleased(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        StopCallRequested?.Invoke();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);
}
