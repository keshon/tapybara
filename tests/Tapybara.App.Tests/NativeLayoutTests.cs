using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using System.Xml.Linq;
using Tapybara.App.Controls;
using Tapybara.Core.Calls;
using Xunit;

namespace Tapybara.App.Tests;

public sealed class NativeLayoutTests
{
    private static void OnSta(Action check)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { check(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }
    private static string Repository()
    {
        for (var directory = new System.IO.DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        { if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Tapybara.slnx"))) { return directory.FullName; } }
        throw new InvalidOperationException("Repository not found.");
    }
    private static XElement Resources(string theme)
    {
        // Пробники меню тоже могут идти первыми: XamlReader должен видеть сборку темы.
        _ = new Wpf.Ui.Controls.Button();
        var app = XDocument.Load(System.IO.Path.Combine(Repository(), "src/Tapybara.App/App.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(app.Root!.Element(ns + "Application.Resources")!.Elements().Single());
        foreach (var attribute in app.Root.Attributes().Where(a => a.IsNamespaceDeclaration))
        { dictionary.SetAttributeValue(attribute.Name, attribute.Value); }
        foreach (var attribute in dictionary.DescendantsAndSelf().Attributes().Where(a => a.IsNamespaceDeclaration && a.Value == "clr-namespace:Tapybara.App"))
        { attribute.Value += ";assembly=Tapybara"; }
        foreach (var element in dictionary.Descendants())
        {
            if (element.Name.LocalName == "ThemesDictionary") { element.SetAttributeValue("Theme", theme); }
            if (element.Attribute("Source")?.Value == "Resources/Controls.xaml")
            { element.SetAttributeValue("Source", "pack://application:,,,/Tapybara;component/Resources/Controls.xaml"); }
        }
        // Пробник не запускает application bootstrap, поэтому задаёт стандартный акцент только для снимка.
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (string key in new[] { "AccentFillColorDefaultBrush", "SystemAccentColorBrush", "SystemAccentColorPrimaryBrush", "SystemAccentColorSecondaryBrush", "SystemAccentColorTertiaryBrush" })
        { dictionary.Add(new XElement(ns + "SolidColorBrush", new XAttribute(x + "Key", key), new XAttribute("Color", "#FF0067C0"))); }
        foreach (string key in new[] { "SystemAccentColor", "SystemAccentColorPrimary", "SystemAccentColorSecondary", "SystemAccentColorTertiary" })
        { dictionary.Add(new XElement(ns + "Color", new XAttribute(x + "Key", key), "#FF0067C0")); }
        return dictionary;
    }
    private static UserControl Page(string theme)
    {
        _ = new AudioTransportBar();
        var source = XDocument.Load(System.IO.Path.Combine(Repository(), "src/Tapybara.App/CallsPage.xaml"));
        XElement root = source.Root!;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        root.Attribute(x + "Class")!.Remove();
        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(a => new[]
        { "Click", "Checked", "Unchecked", "SelectionChanged", "ContextMenuOpening", "Loaded", "GotKeyboardFocus", "KeyDown", "LostKeyboardFocus" }.Contains(a.Name.LocalName)).ToArray())
        { attribute.Remove(); }
        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.Name.NamespaceName.StartsWith("clr-namespace:Tapybara.App", StringComparison.Ordinal))
            { element.Name = XName.Get(element.Name.LocalName, element.Name.NamespaceName + ";assembly=Tapybara"); }
        }
        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(a => a.Name.NamespaceName.StartsWith("clr-namespace:Tapybara.App", StringComparison.Ordinal)).ToArray())
        { var name = XName.Get(attribute.Name.LocalName, attribute.Name.NamespaceName + ";assembly=Tapybara"); var value = attribute.Value; var owner = attribute.Parent!; attribute.Remove(); owner.SetAttributeValue(name, value); }
        root.SetAttributeValue(XNamespace.Xmlns + "app", "clr-namespace:Tapybara.App;assembly=Tapybara");
        root.SetAttributeValue(XNamespace.Xmlns + "components", "clr-namespace:Tapybara.App.Controls;assembly=Tapybara");
        root.SetAttributeValue(XNamespace.Xmlns + "sys", "clr-namespace:System;assembly=mscorlib");
        var wrapper = new XElement(root.Name.Namespace + "Grid",
            new XElement(root.Name.Namespace + "Grid.Resources", Resources(theme)), root);
        return ((Grid)XamlReader.Parse(wrapper.ToString())).Children.OfType<UserControl>().Single();
    }
    private static void Render(FrameworkElement element, int width, int height, string name)
    {
        Visual root = element;
        while (VisualTreeHelper.GetParent(root) is Visual parent) { root = parent; }
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("layout-render")
        { Width = width, Height = height, WindowStyle = 0 });
        source.RootVisual = root;
        ((FrameworkElement)root).Measure(new Size(width, height));
        ((FrameworkElement)root).Arrange(new Rect(0, 0, width, height));
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        if (Environment.GetEnvironmentVariable("TAPYBARA_RENDER_LAYOUTS") == "1")
        {
            string path = System.IO.Path.Combine(Repository(), "docs/product-design/validation", name + ".png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(path); encoder.Save(stream);
        }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ActualCallsXamlHasOneHeaderRailAndAlignedControls(string theme) => OnSta(() =>
    {
        UserControl page = Page(theme);
        page.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        ((FrameworkElement)page.FindName("DetailPane")).Visibility = Visibility.Visible;
        ((FrameworkElement)page.FindName("NothingSelected")).Visibility = Visibility.Collapsed;
        ((ColumnDefinition)page.FindName("VoicesColumn")).Width = new GridLength(0);
        ((TextBox)page.FindName("TitleBox")).Text = "Product review · October";
        ((TextBlock)page.FindName("DetailMeta")).Text = "8 October · 42 min · 74 MB · Note: Release decisions and follow-up";
        var export = (Wpf.Ui.Controls.Button)page.FindName("ExportButton");
        ((TextBlock)page.FindName("ExportCaption")).Text = "Export";
        var search = (SearchFilterBar)page.FindName("LibrarySearch");
        search.Search.PlaceholderText = "Search calls and transcripts";
        ((TextBlock)page.FindName("ListTitle")).Text = "Calls";
        ((AudioTransportBar)page.FindName("Transport")).SetState(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(42), false, true,
            (TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(4)));
        var row = new CallRow
        {
            Entry = new CallEntry { Session = new CallSession { Directory = "probe", StartedAt = DateTimeOffset.Now }, State = CallState.Ready },
            Title = "Product review", Subtitle = "11:02 · 42 min · Pavel, Anna", Day = "Today", StateText = string.Empty,
            StateBackground = Brushes.Transparent, StateForeground = Brushes.Gray,
        };
        ((ListBox)page.FindName("CallList")).ItemsSource = new[] { row };
        ((ListBox)page.FindName("LineList")).ItemsSource = new[]
        {
            new TranscriptLineRow
            {
                Line = new CallLine(CallChannel.Theirs, TimeSpan.Zero, TimeSpan.FromSeconds(8), "We should validate the editing flow on real calls before release.", "A") { Id = Guid.NewGuid() },
                PlayHint = "Play source fragment", Stamp = "0:00", Speaker = "Anna", SpeakerBrush = Brushes.Teal, SpeakerForeground = Brushes.Teal,
                SpeakerFontStyle = FontStyles.Normal, SpeakerVisibility = Visibility.Visible,
            },
            new TranscriptLineRow
            {
                Line = new CallLine(CallChannel.Mine, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15), "Agreed. The controls should feel consistent across sections.", null) { Id = Guid.NewGuid() },
                PlayHint = "Play source fragment", Stamp = "0:08", Speaker = "Me", SpeakerBrush = Brushes.Peru, SpeakerForeground = Brushes.Peru,
                SpeakerFontStyle = FontStyles.Normal, SpeakerVisibility = Visibility.Visible,
            },
        };
        Render(page, 1160, 650, "calls-" + theme.ToLowerInvariant());
        Assert.Null(page.FindName("ListenButton"));
        Assert.Null(page.FindName("TranscriptTab"));
        Assert.Equal(Tokens.ControlHeight, export.ActualHeight);
        var toggle = search.Children.OfType<Grid>().Single().Children.OfType<Wpf.Ui.Controls.Button>().Single();
        Assert.Equal(search.Search.ActualHeight, toggle.ActualHeight);
        Assert.Equal(Tokens.ControlHeight, toggle.ActualWidth);
        Render(page, 780, 650, "calls-compact-" + theme.ToLowerInvariant());
    });

    [Fact]
    public void PopoverCoercesTranslucentBrushAndTracksResourceChanges() => OnSta(() =>
    {
        var surface = new PopoverSurface();
        surface.Resources["SolidBackgroundFillColorBaseBrush"] = new SolidColorBrush(Color.FromArgb(120, 240, 240, 240));
        Assert.Equal(255, Assert.IsType<SolidColorBrush>(surface.Background).Color.A);
        surface.Resources["SolidBackgroundFillColorBaseBrush"] = new SolidColorBrush(Color.FromArgb(40, 32, 32, 32));
        SolidColorBrush brush = Assert.IsType<SolidColorBrush>(surface.Background);
        Assert.Equal(255, brush.Color.A);
        Assert.Equal(32, brush.Color.R);
        Assert.Equal(1, brush.Opacity);
    });

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ExportSurfaceIsOpaqueWithRealThemeTemplates(string theme) => OnSta(() =>
    {
        var surface = new PopoverSurface { Width = 520, Resources = (ResourceDictionary)XamlReader.Parse(Resources(theme).ToString()) };
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Export recording", FontSize = Tokens.Subtitle, Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(new TranscriptExportPanel(["Anna"], new Dictionary<string, string>(), false));
        content.Children.Add(new TextBox { Text = "[0:00] Anna: Current editable transcript", Height = 100, Margin = new Thickness(0, 12, 0, 12) });
        content.Children.Add(new Wpf.Ui.Controls.Button { Content = "Copy to clipboard", Appearance = Wpf.Ui.Controls.ControlAppearance.Primary });
        surface.Child = content;
        Render(surface, 520, 440, "export-" + theme.ToLowerInvariant());
        Assert.Equal(255, Assert.IsType<SolidColorBrush>(surface.Background).Color.A);
    });
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void RenameDialogUsesUnframedInputAndCompactFooter(string theme) => OnSta(() =>
    {
        var source = XDocument.Load(System.IO.Path.Combine(Repository(), "src/Tapybara.App/ConfirmWindow.xaml"));
        XElement root = source.Root!;
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        root.Attribute(x + "Class")!.Remove();
        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName == "Click").ToArray()) { attribute.Remove(); }
        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(a => a.Name.NamespaceName.StartsWith("clr-namespace:Tapybara.App", StringComparison.Ordinal)).ToArray())
        { var owner = attribute.Parent!; var name = XName.Get(attribute.Name.LocalName, attribute.Name.NamespaceName + ";assembly=Tapybara"); var value = attribute.Value; attribute.Remove(); owner.SetAttributeValue(name, value); }
        root.SetAttributeValue(XNamespace.Xmlns + "components", "clr-namespace:Tapybara.App.Controls;assembly=Tapybara");
        foreach (var attribute in root.Attributes().Where(a => a.Value.Contains("StaticResource App", StringComparison.Ordinal))) { attribute.Value = attribute.Value.Replace("StaticResource", "DynamicResource", StringComparison.Ordinal); }
        root.AddFirst(new XElement("{http://schemas.microsoft.com/winfx/2006/xaml/presentation}Window.Resources", Resources(theme)));
        var window = (Window)XamlReader.Parse(source.ToString());
        ((TextBlock)window.FindName("HeadingText")).Text = "Rename recording";
        ((FrameworkElement)window.FindName("HeadingIcon")).Visibility = Visibility.Collapsed;
        ((FrameworkElement)window.FindName("BodyText")).Visibility = Visibility.Collapsed;
        var fields = (StackPanel)window.FindName("ContentRows"); fields.Visibility = Visibility.Visible;
        var field = new Wpf.Ui.Controls.TextBox { Text = "Product review · October" }; ControlSizing.Apply(field); fields.Children.Add(field);
        ((Wpf.Ui.Controls.Button)window.FindName("PrimaryButton")).Content = "Apply";
        ((Wpf.Ui.Controls.Button)window.FindName("CancelButton")).Content = "Cancel";
        var content = (Grid)window.Content;
        content.SetResourceReference(Panel.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        content.Measure(new Size(480, double.PositiveInfinity));
        int height = (int)Math.Ceiling(content.DesiredSize.Height);
        Assert.InRange(height, 140, 250);
        Render(content, 480, height, "rename-" + theme.ToLowerInvariant());
        var footer = content.Children.OfType<Border>().Single();
        Assert.InRange(footer.ActualHeight, 55, 58);
        Assert.Equal(Visibility.Collapsed, ((Border)window.FindName("DetailCard")).Visibility);
        Assert.Equal(Tokens.ControlHeight, field.ActualHeight);
        window.Close();
    });

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void BoundaryAndDateFieldsUseConsistentSpacingAndTimeScale(string theme) => OnSta(() =>
    {
        var editor = new AudioBoundaryEditor(TimeSpan.FromSeconds(862.39), TimeSpan.FromSeconds(866.11), TimeSpan.FromMinutes(30));
        var card = new PopoverSurface { Child = editor, Resources = (ResourceDictionary)XamlReader.Parse(Resources(theme).ToString()) };
        card.Measure(new Size(392, double.PositiveInfinity));
        Render(card, 392, (int)Math.Ceiling(card.DesiredSize.Height), "boundaries-" + theme.ToLowerInvariant());
        Wpf.Ui.Controls.TextBox[] fields = [.. editor.Children.OfType<Wpf.Ui.Controls.TextBox>()];
        Assert.Equal("14:22.390", fields[0].Text);
        Assert.Equal("14:26.110", fields[1].Text);
        Assert.All(fields, field => Assert.Equal(Tokens.ControlHeight, field.ActualHeight));
        var endLabel = editor.Children.OfType<TextBlock>().Last();
        Assert.Equal(Tokens.Space3, endLabel.Margin.Top);
        Assert.Equal(Tokens.Space1, endLabel.Margin.Bottom);
        var actions = editor.Children.OfType<StackPanel>().Single();
        Assert.All(actions.Children.OfType<Wpf.Ui.Controls.Button>(), button => Assert.Equal(Tokens.ControlHeight, button.ActualHeight));
        var dates = new DateRangeFilter { Resources = (ResourceDictionary)XamlReader.Parse(Resources(theme).ToString()) };
        dates.SetResourceReference(Panel.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        dates.SetLabels("From date", "Through date");
        Render(dates, 280, 64, "date-range-" + theme.ToLowerInvariant());
        Assert.Equal(dates.From.ActualHeight, dates.To.ActualHeight);
        Assert.Equal(dates.From.ActualWidth, dates.To.ActualWidth);
        Assert.Equal(dates.From.TranslatePoint(new Point(), dates).Y, dates.To.TranslatePoint(new Point(), dates).Y);
    });

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void StableMenuShellHasNoStoryboardAndCheckedIconsUseAccentText(string theme) => OnSta(() =>
    {
        var menu = new ContextMenu { Resources = (ResourceDictionary)XamlReader.Parse(Resources(theme).ToString()) };
        menu.Items.Add(new MenuItem { Header = "Rename…" });
        menu.Items.Add(new MenuItem { Header = "Export" });
        menu.Measure(new Size(300, 400)); menu.ApplyTemplate();
        Assert.NotNull(menu.Template);
        Assert.Empty(menu.Template.Triggers.Cast<object>());
        UserControl page = Page(theme);
        page.SetResourceReference(Control.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        ((TextBox)page.FindName("TitleBox")).Text = "Checked command contrast";
        ((FrameworkElement)page.FindName("DetailPane")).Visibility = Visibility.Visible;
        ((FrameworkElement)page.FindName("NothingSelected")).Visibility = Visibility.Collapsed;
        ((ColumnDefinition)page.FindName("VoicesColumn")).Width = new GridLength(0);
        var notes = (System.Windows.Controls.Primitives.ToggleButton)page.FindName("NotesToggle");
        var waveform = (System.Windows.Controls.Primitives.ToggleButton)page.FindName("TimelineToggle");
        notes.IsChecked = waveform.IsChecked = true;
        Render(page, 1160, 650, "checked-icons-" + theme.ToLowerInvariant());
        Brush expected = (Brush)notes.FindResource("TextOnAccentFillColorPrimaryBrush");
        Assert.Same(expected, notes.Foreground);
        Assert.Same(expected, ((Wpf.Ui.Controls.SymbolIcon)notes.Content).Foreground);
        Assert.Same(expected, ((System.Windows.Shapes.Path)waveform.Content).Stroke);
    });

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void FindReplaceFitsCompactEditorWithoutClippingItsCommandFields(string theme) => OnSta(() =>
    {
        var bar = new FindReplaceBar { Resources = (ResourceDictionary)XamlReader.Parse(Resources(theme).ToString()) };
        bar.SetResourceReference(Panel.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        bar.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        bar.Open(replace: true, selection: "schedule");
        bar.SetMatches(1, 3);
        Render(bar, 420, 160, "find-replace-" + theme.ToLowerInvariant());
        Grid[] rows = [.. bar.Children.OfType<Grid>()];
        foreach (Grid row in rows)
        {
            var field = row.Children.OfType<Wpf.Ui.Controls.TextBox>().Single();
            Assert.InRange(field.ActualWidth, 80, 420);
            Assert.Equal(Tokens.ControlHeight, field.ActualHeight);
            var commands = row.Children.OfType<StackPanel>().Single();
            Assert.True(field.TranslatePoint(new Point(field.ActualWidth, 0), row).X <= commands.TranslatePoint(new Point(), row).X);
        }
    });

    private static IEnumerable<DependencyObject> Visuals(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { foreach (var child in Visuals(VisualTreeHelper.GetChild(parent, i))) { yield return child; } }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void CompactFieldsLeaveRoomForApplicationFont(string theme) => OnSta(() =>
    {
        var panel = new StackPanel { Resources = (ResourceDictionary)XamlReader.Parse(Resources(theme).ToString()) };
        System.Windows.Documents.TextElement.SetFontFamily(panel, (FontFamily)panel.FindResource("AppFontFamily"));
        System.Windows.Documents.TextElement.SetFontSize(panel, Tokens.Body);
        panel.SetResourceReference(Panel.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        var text = new Wpf.Ui.Controls.TextBox { Text = "погнали · 00:15.399", ClearButtonEnabled = false };
        var combo = new ComboBox { Items = { "All people", "All states" }, SelectedIndex = 0, Margin = new Thickness(0, 8, 0, 0) };
        ControlSizing.Apply(text); ControlSizing.Apply(combo);
        panel.Children.Add(text); panel.Children.Add(combo);
        Render(panel, 300, 80, "compact-fields-" + theme.ToLowerInvariant());
        var metrics = new FormattedText(text.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.Black, 1);
        var host = Assert.IsAssignableFrom<FrameworkElement>(text.Template.FindName("PART_ContentHost", text));
        Assert.True(host.ActualHeight >= metrics.Height, $"Text viewport {host.ActualHeight} < line {metrics.Height}");
        var label = Visuals(combo).OfType<TextBlock>().Single(t => t.Text == "All people");
        Assert.True(label.ActualHeight >= label.DesiredSize.Height);
        Assert.True(label.TranslatePoint(new Point(0, label.ActualHeight), combo).Y <= combo.ActualHeight - 1);
    });

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void MenuRetainsFluentShadowWithoutScrollbarOrMovement(string theme) => OnSta(() =>
    {
        var menu = new ContextMenu { Resources = (ResourceDictionary)XamlReader.Parse(Resources(theme).ToString()),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = Tokens.Body };
        foreach (string caption in new[] { "Rename…", "Export", "Folder", "Open transcript.md", "Transcribe again", "Delete recording" })
        { menu.Items.Add(new MenuItem { Header = caption }); }
        menu.Measure(new Size(400, 600));
        Render(menu, (int)Math.Ceiling(menu.DesiredSize.Width), (int)Math.Ceiling(menu.DesiredSize.Height), "session-menu-" + theme.ToLowerInvariant());
        Assert.Equal(1, menu.Opacity);
        Assert.Equal(Visibility.Visible, menu.Visibility);
        Assert.NotNull(menu.Background);
        Assert.Empty(menu.Template.Triggers.Cast<object>());
        Assert.Contains(Visuals(menu).OfType<Border>(), border => border.Effect is System.Windows.Media.Effects.DropShadowEffect);
        Assert.All(Visuals(menu).OfType<System.Windows.Controls.Primitives.ScrollBar>(), scrollbar => Assert.NotEqual(Visibility.Visible, scrollbar.Visibility));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionMenuPositionRemainsStableAfterPopupLayout(bool nearBottom) => OnSta(() =>
    {
        var anchor = new Wpf.Ui.Controls.Button { Width = 32, Height = 32, Content = "…",
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 24, 0) };
        var root = new Grid { Resources = (ResourceDictionary)XamlReader.Parse(Resources("Light").ToString()), Children = { anchor } };
        using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("menu-position")
        { Width = 440, Height = 500, PositionX = 500,
            PositionY = nearBottom ? System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea.Bottom - 150 : 100, WindowStyle = 0 });
        source.RootVisual = root;
        root.Measure(new Size(440, 500)); root.Arrange(new Rect(0, 0, 440, 500)); root.UpdateLayout();
        var menu = new ContextMenu { Resources = root.Resources };
        foreach (string caption in new[] { "Rename…", "Export", "Folder", "Open transcript.md", "Transcribe again", "Delete recording" })
        { menu.Items.Add(new MenuItem { Header = caption }); }
        static void Pump(int milliseconds)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        try
        {
            AnchoredMenu.Open(menu, anchor);
            Pump(30);
            var shell = Visuals(menu).OfType<Border>().Single(border => border.Effect is System.Windows.Media.Effects.DropShadowEffect);
            Point initial = shell.PointToScreen(new Point());
            Pump(250);
            Point settled = shell.PointToScreen(new Point());
            Assert.InRange(Math.Abs(settled.X - initial.X), 0, 1);
            Assert.InRange(Math.Abs(settled.Y - initial.Y), 0, 1);
            double scale = source.CompositionTarget.TransformToDevice.M22;
            double expected = nearBottom
                ? anchor.PointToScreen(new Point()).Y - (Tokens.Space2 + shell.ActualHeight) * scale
                : anchor.PointToScreen(new Point(0, anchor.ActualHeight)).Y + Tokens.Space2 * scale;
            Assert.InRange(Math.Abs(initial.Y - expected), 0, 2);
        }
        finally { menu.IsOpen = false; }
    });

    [Fact]
    public void TallMenusCanScrollToTheirLastItem() => OnSta(() =>
    {
        var menu = new ContextMenu { Resources = (ResourceDictionary)XamlReader.Parse(Resources("Light").ToString()) };
        for (int i = 0; i < 60; i++) { menu.Items.Add(new MenuItem { Header = $"Speaker {i + 1}" }); }
        Render(menu, 300, 280, "menu-overflow");
        var scroll = Visuals(menu).OfType<ScrollViewer>().Single();
        Assert.True(scroll.ScrollableHeight > 0);
        scroll.ScrollToEnd();
        Render(menu, 300, 280, "menu-overflow-bottom");
        Assert.True(scroll.VerticalOffset > 0);
        var last = Assert.IsType<MenuItem>(menu.Items[^1]);
        double y = last.TranslatePoint(new Point(), scroll).Y;
        Assert.InRange(y, 0, scroll.ActualHeight);
    });
}
