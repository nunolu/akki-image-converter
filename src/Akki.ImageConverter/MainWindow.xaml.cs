using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Akki.ImageConverter.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Engine = Akki.ImageConverter.Core.ImageConverter;
using ImageSource = Akki.ImageConverter.Core.ImageSource;
using FileAttributes = System.IO.FileAttributes;

namespace Akki.ImageConverter;

public sealed partial class MainWindow : Window
{
    private readonly ObservableCollection<QueueItem> _items = [];
    private readonly Preferences _preferences = Preferences.Load();
    private readonly Engine _engine = new();
    private readonly UpdateService _updates = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    private CancellationTokenSource? _conversion;
    private bool _importing;
    private bool _ready;
    private bool _dialogOpen;
    private bool _updateAvailable;
    private ContentDialog? _activeDialog;
    private int _previewVersion;
    private bool _draggingComparison;
    private bool _panningPreview;
    private Windows.Foundation.Point _lastPanPoint;
    private double _panX, _panY;
    private Window? _compactWindow;
    private TextBlock? _compactStatus;
    private Windows.Graphics.RectInt32 _compactWorkArea;
    private Action? _compactRefresh;
    private Action<bool>? _compactExpand;
    private bool _compactExpanded;
    private bool _compactMini;
    private readonly Queue<string[]> _pendingLaunchImports = new();
    private bool _launchImportRunning;
    private bool _windowLoaded;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleArea);
        SystemBackdrop = new MicaBackdrop();
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int initialWidth = Math.Min((int)(1260 * scale), (int)(workArea.Width * .94));
        int initialHeight = Math.Min((int)(960 * scale), (int)(workArea.Height * .94));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(workArea.X + (workArea.Width - initialWidth) / 2,
            workArea.Y + (workArea.Height - initialHeight) / 2, initialWidth, initialHeight));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"));
        Root.ActualThemeChanged += (_, _) => UpdateTitleBarTheme();
        ApplyTheme();
        QueueList.ItemsSource = _items;
        FormatBox.ItemsSource = Engine.SupportedOutputs;
        ApplyOptions(_preferences.Conversion);
        RecursiveBox.IsChecked = _preferences.IncludeSubfolders;
        _ready = true;
        RefreshFormat();
        RefreshResize();
        RefreshDestinationMode();
        AppWindow.Closing += OnClosing;
        Closed += (_, _) => _updateTimer.Stop();
        _updateTimer.Tick += async (_, _) => await CheckAutomatically();
        Root.Loaded += async (_, _) =>
        {
            _windowLoaded = true;
            _ = DrainLaunchImports();
            if (_updates.IsStoreBuild) return;
            _updateTimer.Start();
            await CheckAutomatically();
        };
    }

    private void ApplyOptions(ConversionOptions settings)
    {
        FormatBox.SelectedItem = settings.Format;
        if (FormatBox.SelectedIndex < 0) FormatBox.SelectedIndex = 0;
        QualitySlider.Value = settings.Quality;
        TargetSizeBox.Value = settings.TargetKilobytes;
        LosslessBox.IsChecked = settings.Lossless;
        ResizeBox.SelectedIndex = (int)settings.Resize;
        RotationBox.SelectedIndex = Enum.IsDefined(settings.Rotation) ? (int)settings.Rotation : 0;
        FlipHorizontalBox.IsChecked = settings.FlipHorizontal;
        FlipVerticalBox.IsChecked = settings.FlipVertical;
        CropBox.SelectedIndex = Enum.IsDefined(settings.Crop) ? (int)settings.Crop : 0;
        WatermarkTextBox.Text = settings.WatermarkText;
        WatermarkImageBox.Text = settings.WatermarkImage;
        WatermarkPositionBox.SelectedIndex = Enum.IsDefined(settings.WatermarkPosition) ? (int)settings.WatermarkPosition : 0;
        WatermarkSizeBox.Value = settings.WatermarkSize;
        WatermarkOpacityBox.Value = settings.WatermarkOpacity;
        PercentBox.Value = settings.Percent;
        WidthBox.Value = settings.Width;
        HeightBox.Value = settings.Height;
        AspectBox.IsChecked = settings.KeepAspectRatio;
        UpscaleBox.IsChecked = settings.PreventUpscale;
        MetadataBox.IsChecked = settings.StripMetadata;
        StructureBox.IsChecked = settings.KeepFolderStructure;
        SourceFolderBox.IsChecked = settings.OutputToSourceFolder;
        FilenamePrefixBox.Text = settings.FilenamePrefix;
        FilenameSuffixBox.Text = settings.FilenameSuffix;
        NumberFilesBox.IsChecked = settings.NumberFiles;
        BackgroundBox.Text = settings.Background;
        OutputBox.Text = settings.OutputDirectory;
        if (_ready) { RefreshFormat(); RefreshResize(); RefreshDestinationMode(); }
    }

    private async void SavePreset(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        ConversionOptions options;
        try { options = ReadOptions(); }
        catch (Exception error) { Tell(error.Message, InfoBarSeverity.Error); return; }
        var name = new TextBox { Header = "Preset name", MaxLength = 60 };
        var dialog = new ContentDialog { Title = "Save preset", Content = name, PrimaryButtonText = "Save", CloseButtonText = "Cancel" };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var key = name.Text.Trim();
            if (key.Length == 0 || _preferences.Presets.ContainsKey(key)) { args.Cancel = true; name.Header = "Enter a new, unique preset name"; return; }
            try { _preferences.Presets.Add(key, options); _preferences.Save(); }
            catch (Exception error) { _preferences.Presets.Remove(key); args.Cancel = true; Tell("Couldn't save preset: " + error.Message, InfoBarSeverity.Error); }
        };
        await Dialog(dialog);
    }

    private async void LoadPreset(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        var names = _preferences.Presets.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length == 0) { Tell("Save a preset first."); return; }
        var selector = new ComboBox { Header = "Preset", ItemsSource = names, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var dialog = new ContentDialog { Title = "Load preset", Content = selector, PrimaryButtonText = "Load", CloseButtonText = "Cancel" };
        dialog.PrimaryButtonClick += (_, _) => { if (selector.SelectedItem is string key) ApplyOptions(_preferences.Presets[key]); };
        await Dialog(dialog);
    }

    private void Tell(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        Notice.Message = message;
        Notice.Severity = severity;
        Notice.IsOpen = true;
        if (_compactStatus is not null) _compactStatus.Text = message;
        _compactRefresh?.Invoke();
    }

    private void ShowCompactDropZone(object sender, RoutedEventArgs e)
    {
        if (_compactWindow is not null || _dialogOpen || _conversion is not null || _importing) return;
        _compactWorkArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var compact = new Window { Title = "Akki Image Tools" };
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var bg = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 35, 42, 54) : Windows.UI.Color.FromArgb(255, 243, 247, 253));
        var surface = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 43, 61, 84) : Windows.UI.Color.FromArgb(255, 220, 234, 250));
        var blue = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 100, 225));
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(10, 8, 10, 8), RequestedTheme = Root.ActualTheme };
        var content = new Grid();
        var frame = new Border { Background = bg, Child = content, AllowDrop = true, RequestedTheme = Root.ActualTheme };
        var compactRoot = new Grid { Background = bg, RequestedTheme = Root.ActualTheme };
        compactRoot.Children.Add(frame);
        content.Children.Add(panel);
        var header = new Grid { ColumnSpacing = 4 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = new Image { Width = 28, Height = 28, Source = new BitmapImage(new Uri("ms-appx:///Assets/AppLogo.png")), VerticalAlignment = VerticalAlignment.Center };
        var headText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var heading = new TextBlock { Text = "Drop images", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 14 };
        var count = new TextBlock { Text = "0 queued", FontSize = 11 };
        headText.Children.Add(heading); headText.Children.Add(count);
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var toggle = new Button { Content = new FontIcon { Glyph = "\uE8A7", FontSize = 14 }, Padding = new Thickness(4), MinWidth = 28, MinHeight = 28, Background = transparent, BorderThickness = new Thickness(0) };
        var collapse = new Button { Content = new FontIcon { Glyph = "\uE76C", FontSize = 12 }, Padding = new Thickness(4), MinWidth = 28, MinHeight = 28, Background = transparent, BorderThickness = new Thickness(0) };
        var close = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, Padding = new Thickness(4), MinWidth = 28, MinHeight = 28, Background = transparent, BorderThickness = new Thickness(0) };
        var miniCount = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var miniContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        miniContent.Children.Add(new Image { Width = 24, Height = 24, Source = new BitmapImage(new Uri("ms-appx:///Assets/AppLogo.png")) }); miniContent.Children.Add(miniCount);
        var mini = new Button { Content = miniContent, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, Background = transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Visibility = Visibility.Collapsed };
        content.Children.Add(mini);
        ToolTipService.SetToolTip(mini, "Expand drop zone"); ToolTipService.SetToolTip(collapse, "Collapse to edge tab");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mini, "Expand drop zone"); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(collapse, "Collapse to edge tab");
        ToolTipService.SetToolTip(toggle, "Expand drop zone"); ToolTipService.SetToolTip(close, "Return to full app");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, "Expand or collapse drop zone");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(close, "Return to full app");
        Grid.SetColumn(headText, 1); Grid.SetColumn(toggle, 2); Grid.SetColumn(collapse, 3); Grid.SetColumn(close, 4);
        header.Children.Add(logo); header.Children.Add(headText); header.Children.Add(toggle); header.Children.Add(collapse); header.Children.Add(close);
        panel.Children.Add(header);
        var details = new StackPanel { Spacing = 8, Visibility = Visibility.Collapsed };
        var summaryPanel = new StackPanel { Spacing = 3 };
        var summary = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        _compactStatus = new TextBlock { Text = "Ready to open in Akki", FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
        summaryPanel.Children.Add(summary); summaryPanel.Children.Add(_compactStatus);
        var thumbnails = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var summaryGrid = new Grid { ColumnSpacing = 8 };
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(thumbnails, 1); summaryGrid.Children.Add(summaryPanel); summaryGrid.Children.Add(thumbnails);
        details.Children.Add(new Border { Background = surface, CornerRadius = new CornerRadius(6), Padding = new Thickness(10), Child = summaryGrid });
        var presets = new ComboBox { Header = "Preset", HorizontalAlignment = HorizontalAlignment.Stretch };
        presets.Items.Add("Current settings");
        foreach (var name in _preferences.Presets.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) presets.Items.Add(name);
        presets.SelectedIndex = 0;
        presets.SelectionChanged += (_, _) => { if (presets.SelectedIndex > 0 && presets.SelectedItem is string name && _preferences.Presets.TryGetValue(name, out var preset)) { ApplyOptions(preset); _compactRefresh?.Invoke(); } };
        details.Children.Add(presets);
        var destination = new TextBlock { Text = OutputBox.Text, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var destinationButton = new Button { Content = destination, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
        ToolTipService.SetToolTip(destinationButton, "Choose destination folder");
        destinationButton.Click += async (_, _) =>
        {
            try { var picker = new FolderPicker(); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(compact)); picker.FileTypeFilter.Add("*"); var folder = await picker.PickSingleFolderAsync(); if (folder is not null) { OutputBox.Text = folder.Path; SourceFolderBox.IsChecked = false; destination.Text = folder.Path; } }
            catch (Exception error) { if (_compactStatus is not null) _compactStatus.Text = error.Message; }
        };
        details.Children.Add(new TextBlock { Text = "Destination", Margin = new Thickness(0, 2, 0, -6) });
        var sameFolder = new CheckBox { Content = "Same folder as original", IsChecked = SourceFolderBox.IsChecked, Margin = new Thickness(0, -4, 0, -4) };
        sameFolder.Checked += (_, _) => { SourceFolderBox.IsChecked = true; destinationButton.IsEnabled = false; destination.Text = "Same folder as original"; };
        sameFolder.Unchecked += (_, _) => { SourceFolderBox.IsChecked = false; destinationButton.IsEnabled = true; destination.Text = OutputBox.Text; };
        destinationButton.IsEnabled = sameFolder.IsChecked != true;
        details.Children.Add(sameFolder);
        details.Children.Add(destinationButton);
        var footer = new Grid { ColumnSpacing = 8 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var edges = new ComboBox { ItemsSource = new[] { "Left", "Right", "Top", "Bottom" }, SelectedIndex = Enum.IsDefined(_preferences.CompactEdge) ? (int)_preferences.CompactEdge : (int)CompactEdge.Right, HorizontalAlignment = HorizontalAlignment.Left };
        var open = new Button { Content = "Open Akki", Background = blue, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Padding = new Thickness(16, 6, 16, 6) };
        Grid.SetColumn(open, 1); footer.Children.Add(edges); footer.Children.Add(open); details.Children.Add(footer); panel.Children.Add(details);
        _compactRefresh = () =>
        {
            count.Text = $"{_items.Count} queued"; summary.Text = $"{_items.Count} {(_items.Count == 1 ? "image" : "images")} queued";
            miniCount.Text = _items.Count.ToString(); miniCount.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mini, $"Expand drop zone, {_items.Count} images queued");
            sameFolder.IsChecked = SourceFolderBox.IsChecked; destinationButton.IsEnabled = sameFolder.IsChecked != true;
            destination.Text = sameFolder.IsChecked == true ? "Same folder as original" : OutputBox.Text; thumbnails.Children.Clear();
            foreach (var item in _items.Take(3)) thumbnails.Children.Add(new Image { Source = item.Thumbnail, Width = 30, Height = 30, Stretch = Stretch.UniformToFill });
            if (_items.Count > 3) thumbnails.Children.Add(new TextBlock { Text = $"+{_items.Count - 3}", VerticalAlignment = VerticalAlignment.Center });
        };
        _compactExpand = expanded => { _compactMini = false; panel.Visibility = Visibility.Visible; mini.Visibility = Visibility.Collapsed; _compactExpanded = expanded; details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed; heading.Text = expanded ? "Akki Image Tools" : "Drop images"; count.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible; ToolTipService.SetToolTip(toggle, expanded ? "Collapse drop zone" : "Expand drop zone"); PositionCompactWindow(compact, edges.SelectedIndex); };
        frame.DragOver += OnDragOver;
        frame.Drop += async (_, args) => { var deferral = args.GetDeferral(); try { if (args.DataView.Contains(StandardDataFormats.StorageItems)) await ImportPaths((await args.DataView.GetStorageItemsAsync()).Select(item => item.Path)); } catch (Exception error) { Tell(error.Message, InfoBarSeverity.Error); } finally { _compactRefresh?.Invoke(); deferral.Complete(); } };
        headText.PointerPressed += (_, _) => _compactExpand?.Invoke(true);
        compact.Content = compactRoot;
        compact.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico"));
        if (compact.AppWindow.Presenter is OverlappedPresenter presenter)
        { presenter.IsAlwaysOnTop = true; presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.SetBorderAndTitleBar(false, false); }
        var compactHandle = WinRT.Interop.WindowNative.GetWindowHandle(compact);
        var roundCorners = 2; // DWMWCP_ROUND: Windows can apply native rounding when supported.
        _ = DwmSetWindowAttribute(compactHandle, 33, ref roundCorners, sizeof(int));
        edges.SelectionChanged += (_, _) =>
        {
            if (edges.SelectedIndex < 0) return;
            _preferences.CompactEdge = (CompactEdge)edges.SelectedIndex;
            try { _preferences.Save(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { if (_compactStatus is not null) _compactStatus.Text = "Couldn't save edge preference: " + error.Message; }
            PositionCompactWindow(compact, edges.SelectedIndex);
        };
        toggle.Click += (_, _) => _compactExpand?.Invoke(!_compactExpanded);
        collapse.Click += (_, _) => { _compactMini = true; _compactExpanded = false; panel.Visibility = Visibility.Collapsed; mini.Visibility = Visibility.Visible; PositionCompactWindow(compact, edges.SelectedIndex); };
        mini.Click += (_, _) => _compactExpand?.Invoke(false);
        close.Click += (_, _) => compact.Close();
        open.Click += (_, _) => compact.Close();
        compact.Closed += (_, _) =>
        {
            _compactWindow = null; _compactStatus = null; _compactRefresh = null; _compactExpand = null;
            AppWindow.Show(); Activate();
        };
        _compactWindow = compact;
        _compactRefresh(); _compactExpand(false);
        compact.Activate();
        AppWindow.Hide();
    }

    private void PositionCompactWindow(Window compact, int edge)
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(compact)) / 96.0;
        var width = Math.Min(_compactWorkArea.Width, _compactMini ? Math.Max(160, (int)(48 * scale)) : (int)((_compactExpanded ? 360 : 280) * scale));
        var height = Math.Min(_compactWorkArea.Height, (int)((_compactExpanded ? 370 : 64) * scale));
        var x = edge switch { 0 => _compactWorkArea.X, 1 => _compactWorkArea.X + _compactWorkArea.Width - width, _ => _compactWorkArea.X + (_compactWorkArea.Width - width) / 2 };
        var y = edge switch { 2 => _compactWorkArea.Y, 3 => _compactWorkArea.Y + _compactWorkArea.Height - height, _ => _compactWorkArea.Y + (_compactWorkArea.Height - height) / 2 };
        compact.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
    }

    private void ShowConvertOptions(object sender, RoutedEventArgs e) => SelectOptions(0);
    private void FocusQueue(object sender, RoutedEventArgs e) => QueueList.Focus(FocusState.Programmatic);
    private void ShowEditOptions(object sender, RoutedEventArgs e) => SelectOptions(1);
    private void ShowOutputOptions(object sender, RoutedEventArgs e) => SelectOptions(2);
    private void SelectOptions(int selected)
    {
        ConvertTab.IsChecked = selected == 0;
        EditTab.IsChecked = selected == 1;
        OutputTab.IsChecked = selected == 2;
        ConvertOptions.Visibility = selected == 0 ? Visibility.Visible : Visibility.Collapsed;
        EditOptions.Visibility = selected == 1 ? Visibility.Visible : Visibility.Collapsed;
        OutputOptions.Visibility = selected == 2 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ToggleOptions(object sender, RoutedEventArgs e)
    {
        bool show = OptionsBorder.Visibility != Visibility.Visible;
        OptionsBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        OptionsColumn.Width = new GridLength(show ? 360 : 0);
        ToggleOptionsButton.Content = show ? "Hide options" : "Show options";
        ToolTipService.SetToolTip(ToggleOptionsButton, show ? "Hide options" : "Show options");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ToggleOptionsButton, show ? "Hide options" : "Show options");
    }
    private void QueueSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        RefreshImageNavigation();
        _previewVersion++;
        OriginalPreview.Source = null;
        ConvertedPreview.Source = null;
        PreviewPlaceholder.Visibility = Visibility.Visible;
        CompareDecoration.Visibility = Visibility.Collapsed;
        _panX = _panY = 0;
        ResultFormat.Text = ResultDimensions.Text = ResultSize.Text = "—";
        PreviewTitle.Text = (QueueList.SelectedItem as QueueItem)?.Name ?? "Select an image to compare";
        PreviewInfo.Text = "Choose Compare selected image to generate a preview with the current settings.";
        if (!_importing && _conversion is null && QueueList.SelectedItem is QueueItem) ShowPreview(sender, e);
    }

    private void RefreshImageNavigation()
    {
        var index = QueueList.SelectedIndex;
        ImageNavigation.Visibility = _items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ImagePosition.Text = $"{(index >= 0 ? index + 1 : 0)} of {_items.Count}";
        PreviousImageButton.IsEnabled = !_importing && _conversion is null && index > 0;
        NextImageButton.IsEnabled = !_importing && _conversion is null && index >= 0 && index < _items.Count - 1;
    }

    private void PreviousImage(object sender, RoutedEventArgs e) => NavigateImage(-1);
    private void NextImage(object sender, RoutedEventArgs e) => NavigateImage(1);
    private void NavigateImage(int offset)
    {
        var target = QueueList.SelectedIndex + offset;
        if (_importing || _conversion is not null || QueueList.SelectedIndex < 0 || target < 0 || target >= _items.Count) return;
        QueueList.SelectedIndex = target;
        QueueList.ScrollIntoView(_items[target]);
    }

    private void CompareSizeChanged(object sender, SizeChangedEventArgs e) { UpdateComparisonClip(); UpdateZoom(); }
    private void CompareSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => UpdateComparisonClip();
    private void UpdateComparisonClip()
    {
        if (!_ready || CompareCanvas.ActualWidth <= 0 || CompareCanvas.ActualHeight <= 0) return;
        double x = CompareCanvas.ActualWidth * CompareSlider.Value / 100;
        ConvertedClip.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(x, 0, Math.Max(0, CompareCanvas.ActualWidth - x), CompareCanvas.ActualHeight) };
        CompareCanvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, CompareCanvas.ActualWidth, CompareCanvas.ActualHeight) };
        CompareDivider.Height = CompareCanvas.ActualHeight;
        Canvas.SetLeft(CompareDivider, x - 1);
        Canvas.SetLeft(CompareHandle, Math.Clamp(x - 18, 0, Math.Max(0, CompareCanvas.ActualWidth - 36)));
        Canvas.SetTop(CompareHandle, Math.Max(0, (CompareCanvas.ActualHeight - 36) / 2));
    }
    private void ComparePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ConvertedPreview.Source is null) return;
        var point = e.GetCurrentPoint(CompareCanvas).Position;
        var divider = CompareCanvas.ActualWidth * CompareSlider.Value / 100;
        if (Math.Abs(point.X - divider) <= 24) { _draggingComparison = CompareCanvas.CapturePointer(e.Pointer); MoveComparison(e); }
        else if (ZoomBox.SelectedIndex > 0) { _panningPreview = CompareCanvas.CapturePointer(e.Pointer); _lastPanPoint = point; }
    }
    private void ComparePointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_draggingComparison) MoveComparison(e);
        else if (_panningPreview)
        {
            var point = e.GetCurrentPoint(CompareCanvas).Position;
            _panX += point.X - _lastPanPoint.X; _panY += point.Y - _lastPanPoint.Y;
            _lastPanPoint = point; UpdateZoom();
        }
    }
    private void ComparePointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_draggingComparison && !_panningPreview) return;
        _draggingComparison = false;
        _panningPreview = false;
        CompareCanvas.ReleasePointerCapture(e.Pointer);
    }

    private void ZoomChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        _panX = _panY = 0;
        UpdateZoom();
    }
    private void ZoomFit(object sender, RoutedEventArgs e) { ZoomBox.SelectedIndex = 0; _panX = _panY = 0; UpdateZoom(); }
    private void ZoomActual(object sender, RoutedEventArgs e) { ZoomBox.SelectedIndex = 2; _panX = _panY = 0; UpdateZoom(); }
    private void UpdateZoom()
    {
        if (!_ready || ConvertedPreview.Source is not BitmapImage bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0 || CompareCanvas.ActualWidth <= 0 || CompareCanvas.ActualHeight <= 0) return;
        var fit = Math.Min(CompareCanvas.ActualWidth / bitmap.PixelWidth, CompareCanvas.ActualHeight / bitmap.PixelHeight);
        var dpi = Root.XamlRoot?.RasterizationScale ?? 1.0;
        var desired = ZoomBox.SelectedIndex switch { 1 => 0.5, 2 => 1.0, 3 => 2.0, 4 => 4.0, _ => 0.0 };
        var scale = desired == 0 ? 1 : desired / (fit * dpi);
        var maxX = Math.Max(0, (bitmap.PixelWidth * fit * scale - CompareCanvas.ActualWidth) / 2);
        var maxY = Math.Max(0, (bitmap.PixelHeight * fit * scale - CompareCanvas.ActualHeight) / 2);
        _panX = Math.Clamp(_panX, -maxX, maxX); _panY = Math.Clamp(_panY, -maxY, maxY);
        foreach (var transform in new[] { OriginalZoomTransform, ConvertedZoomTransform })
        {
            transform.ScaleX = transform.ScaleY = scale;
            transform.TranslateX = _panX; transform.TranslateY = _panY;
        }
    }
    private void MoveComparison(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (CompareCanvas.ActualWidth > 0)
            CompareSlider.Value = Math.Clamp(e.GetCurrentPoint(CompareCanvas).Position.X / CompareCanvas.ActualWidth * 100, 0, 100);
    }

    private void ApplyTheme()
    {
        Root.RequestedTheme = _preferences.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        if (_activeDialog is not null) _activeDialog.RequestedTheme = Root.RequestedTheme;
        UpdateTitleBarTheme();
    }

    private void UpdateTitleBarTheme()
    {
        var dark = Root.ActualTheme == ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = dark ? Microsoft.UI.Colors.LightGray : Microsoft.UI.Colors.DimGray;
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    private ConversionOptions ReadOptions()
    {
        if (!double.IsFinite(QualitySlider.Value) || !double.IsFinite(WidthBox.Value) ||
            !double.IsFinite(HeightBox.Value) || !double.IsFinite(PercentBox.Value) || !double.IsFinite(TargetSizeBox.Value) || TargetSizeBox.Value < 0 || TargetSizeBox.Value != Math.Truncate(TargetSizeBox.Value) || !double.IsFinite(WatermarkSizeBox.Value) || !double.IsFinite(WatermarkOpacityBox.Value))
            throw new ArgumentException("Enter valid resize dimensions and quality.");
        var options = new ConversionOptions
        {
            Format = (OutputKind)FormatBox.SelectedItem,
            Quality = (uint)QualitySlider.Value,
            TargetKilobytes = checked((uint)TargetSizeBox.Value),
            Lossless = LosslessBox.IsChecked == true,
            Resize = (ResizeKind)ResizeBox.SelectedIndex,
            Rotation = (RotationKind)RotationBox.SelectedIndex,
            FlipHorizontal = FlipHorizontalBox.IsChecked == true, FlipVertical = FlipVerticalBox.IsChecked == true,
            Crop = (CropKind)CropBox.SelectedIndex,
            WatermarkText = WatermarkTextBox.Text, WatermarkImage = WatermarkImageBox.Text.Trim(), WatermarkPosition = (WatermarkPosition)WatermarkPositionBox.SelectedIndex,
            WatermarkSize = checked((uint)WatermarkSizeBox.Value), WatermarkOpacity = checked((uint)WatermarkOpacityBox.Value),
            Percent = PercentBox.Value,
            Width = checked((uint)WidthBox.Value), Height = checked((uint)HeightBox.Value),
            KeepAspectRatio = AspectBox.IsChecked == true, PreventUpscale = UpscaleBox.IsChecked == true,
            StripMetadata = MetadataBox.IsChecked == true, KeepFolderStructure = StructureBox.IsChecked == true, OutputToSourceFolder = SourceFolderBox.IsChecked == true,
            FilenamePrefix = FilenamePrefixBox.Text, FilenameSuffix = FilenameSuffixBox.Text, NumberFiles = NumberFilesBox.IsChecked == true,
            Background = BackgroundBox.Text.Trim(), OutputDirectory = OutputBox.Text.Trim()
        };
        options.Validate();
        return options;
    }

    private void SavePreferences()
    {
        try
        {
            _preferences.Conversion = ReadOptions();
            _preferences.IncludeSubfolders = RecursiveBox.IsChecked == true;
            _preferences.Save();
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or OverflowException)
        { Tell("Couldn't save preferences: " + e.Message, InfoBarSeverity.Warning); }
    }

    private void RefreshQueue()
    {
        RefreshImageNavigation();
        QueueCount.Text = $"{_items.Count} files";
        ConvertButton.Content = $"Convert {_items.Count} images";
        ConvertButton.IsEnabled = _items.Count > 0 && _conversion is null && !_importing;
        if (_conversion is null) StatusText.Text = $"{_items.Count} images queued";
    }

    private nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
    private void InitPicker(object picker) => WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);

    private async void AddImages(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitPicker(picker);
            foreach (var extension in Engine.InputExtensions) picker.FileTypeFilter.Add(extension);
            var files = await picker.PickMultipleFilesAsync();
            await ImportPaths(files.Select(f => f.Path));
        }
        catch (Exception error) { Tell(error.Message, InfoBarSeverity.Error); }
    }

    private async void AddFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker(); InitPicker(picker); picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) await ImportPaths([folder.Path]);
        }
        catch (Exception error) { Tell(error.Message, InfoBarSeverity.Error); }
    }

    private async Task ImportPaths(IEnumerable<string> paths)
    {
        if (_conversion is not null || _importing) return;
        _importing = true;
        SetPanelEnabled(ImportControls, false);
        RefreshQueue();
        StatusText.Text = "Scanning images…";
        try
        {
            var recursive = RecursiveBox.IsChecked == true;
            var output = SourceFolderBox.IsChecked == true || string.IsNullOrWhiteSpace(OutputBox.Text) ? null : Path.GetFullPath(OutputBox.Text);
            var found = await Task.Run(() =>
            {
                var result = new List<ImageSource>();
                foreach (var path in paths)
                {
                    if (Directory.Exists(path))
                    {
                        var root = Path.GetFullPath(path);
                        var folders = new Stack<string>(); folders.Push(root);
                        while (folders.TryPop(out var current))
                        {
                            // Don't accidentally reimport last run's output or follow directory junctions.
                            if (output is not null && current != root && current.Equals(output, StringComparison.OrdinalIgnoreCase)) continue;
                            try
                            {
                                foreach (var file in Directory.EnumerateFiles(current))
                                    if (Engine.InputExtensions.Contains(Path.GetExtension(file))) result.Add(new(file, root));
                                if (recursive) foreach (var child in Directory.EnumerateDirectories(current))
                                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) folders.Push(child);
                            }
                            catch (UnauthorizedAccessException) { }
                            catch (IOException) { }
                        }
                    }
                    else if (File.Exists(path) && Engine.InputExtensions.Contains(Path.GetExtension(path))) result.Add(new(Path.GetFullPath(path)));
                }
                return result;
            });
            var known = _items.Select(i => i.Source.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int added = 0;
            foreach (var source in found)
                if (known.Add(source.Path))
                {
                    _items.Add(new(source)); added++;
                    if (added % 100 == 0) await Task.Delay(1);
                }
            if (_items.Count > 0 && QueueList.SelectedIndex < 0) QueueList.SelectedIndex = 0;
            Tell(added == 0 ? "No new supported images found. Duplicate files are skipped." : $"Added {added} images. Animated and multipage files will be flagged during conversion.");
        }
        catch (Exception e) { Tell("Couldn't import: " + e.Message, InfoBarSeverity.Error); }
        finally { _importing = false; SetPanelEnabled(ImportControls, true); RefreshQueue(); if (QueueList.SelectedItem is QueueItem) ShowPreview(QueueList, null!); _ = DrainLaunchImports(); }
    }

    internal void ImportOnLaunch(IEnumerable<string> paths)
    {
        var launchPaths = paths.Where(path => !string.IsNullOrWhiteSpace(path) && !path.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (launchPaths.Length > 0) _pendingLaunchImports.Enqueue(launchPaths);
        _ = DrainLaunchImports();
    }

    internal void ReceiveActivation(IEnumerable<string> paths)
    {
        ImportOnLaunch(paths);
        if (_compactWindow is not null) _compactWindow.Activate();
        else { if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore(); AppWindow.Show(); Activate(); }
    }

    private async Task DrainLaunchImports()
    {
        if (!_windowLoaded || _launchImportRunning || _importing || _conversion is not null) return;
        _launchImportRunning = true;
        try { while (_pendingLaunchImports.TryDequeue(out var paths)) await ImportPaths(paths); }
        finally { _launchImportRunning = false; }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _conversion is null && !_importing && e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = "Add to conversion queue";
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
                await ImportPaths((await e.DataView.GetStorageItemsAsync()).Select(item => item.Path));
        }
        catch (Exception error) { Tell(error.Message, InfoBarSeverity.Error); }
        finally { deferral.Complete(); }
    }

    private void ClearQueue(object sender, RoutedEventArgs e) { if (_conversion is null) { _items.Clear(); RefreshQueue(); } }
    private void RemoveItem(object sender, RoutedEventArgs e)
    {
        if (_conversion is null && !_importing && sender is Button { Tag: QueueItem item })
        {
            var index = _items.IndexOf(item);
            var wasSelected = QueueList.SelectedItem == item;
            _items.Remove(item);
            if (wasSelected && _items.Count > 0) QueueList.SelectedIndex = Math.Min(index, _items.Count - 1);
            RefreshQueue();
        }
    }
    private void TogglePin(object sender, RoutedEventArgs e)
    { if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = PinButton.IsChecked == true; }

    private void FormatChanged(object sender, SelectionChangedEventArgs e) { if (_ready) RefreshFormat(); }
    private void CompressionChanged(object sender, RoutedEventArgs e) { if (_ready) RefreshFormat(); }
    private void RefreshFormat()
    {
        var format = (OutputKind)FormatBox.SelectedItem;
        bool losslessSupported = format is OutputKind.WebP or OutputKind.Avif;
        LosslessBox.Visibility = losslessSupported ? Visibility.Visible : Visibility.Collapsed;
        QualityPanel.Visibility = format is OutputKind.Jpeg || (losslessSupported && LosslessBox.IsChecked != true) ? Visibility.Visible : Visibility.Collapsed;
        BackgroundBox.Visibility = format is OutputKind.Jpeg or OutputKind.Bmp ? Visibility.Visible : Visibility.Collapsed;
        FormatHint.Text = format switch
        {
            OutputKind.WebP => "Compact files with transparency. Choose lossless for pixel-perfect images.",
            OutputKind.Jpeg => "Best for photos. Transparent areas use the background colour below.",
            OutputKind.Avif => "Efficient compression. Encoding can take longer than other formats.",
            OutputKind.Png => "Lossless output with transparency. Quality does not reduce image fidelity.",
            OutputKind.Tiff => "Single-page TIFF output. Suitable for high-quality image workflows.",
            OutputKind.Ico => "Creates a Windows icon, fitted within 256 × 256 pixels.",
            _ => "Uncompressed bitmap. Transparent areas use the background colour below."
        };
    }
    private void ResizeChanged(object sender, SelectionChangedEventArgs e) { if (_ready) RefreshResize(); }
    private void DestinationModeChanged(object sender, RoutedEventArgs e) { if (_ready) RefreshDestinationMode(); }
    private void RefreshDestinationMode()
    {
        var chooseFolder = SourceFolderBox.IsChecked != true && _conversion is null;
        OutputBox.IsEnabled = OutputBrowseButton.IsEnabled = chooseFolder;
        StructureBox.IsEnabled = chooseFolder;
    }
    private void RefreshResize()
    {
        PercentBox.Visibility = ResizeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        DimensionsPanel.Visibility = ResizeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        AspectBox.Visibility = ResizeBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void ChooseOutput(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker(); InitPicker(picker); picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null) OutputBox.Text = folder.Path;
        }
        catch (Exception error) { Tell(error.Message, InfoBarSeverity.Error); }
    }

    private async void ChooseWatermark(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker(); InitPicker(picker);
            foreach (var extension in Engine.InputExtensions) picker.FileTypeFilter.Add(extension);
            var file = await picker.PickSingleFileAsync();
            if (file is not null) WatermarkImageBox.Text = file.Path;
        }
        catch (Exception error) { Tell(error.Message, InfoBarSeverity.Error); }
    }

    private async void ConvertImages(object sender, RoutedEventArgs e)
    {
        ConversionOptions options;
        try { options = ReadOptions(); }
        catch (Exception error) { Tell(error.Message, InfoBarSeverity.Error); return; }
        SavePreferences();
        using var cancellation = new CancellationTokenSource();
        _conversion = cancellation;
        SetWorking(true);
        int done = 0, errors = 0, missedTargets = 0;
        long original = 0, converted = 0;
        try
        {
            foreach (var item in _items.ToArray())
            {
                cancellation.Token.ThrowIfCancellationRequested();
                item.Status = "Converting…";
                try
                {
                    var sequence = done + 1;
                    var result = await Task.Run(() => _engine.Convert(item.Source, options, cancellation.Token, sequence));
                    item.OutputPath = result.Path;
                    if (options.TargetKilobytes > 0 && result.OutputBytes > (long)options.TargetKilobytes * 1024)
                        missedTargets++;
                    original += result.OriginalBytes; converted += result.OutputBytes;
                    var difference = (1 - (double)result.OutputBytes / Math.Max(1, result.OriginalBytes)) * 100;
                    item.Status = $"Done · {result.Width} × {result.Height} · {Size(result.OutputBytes)} · " + (difference >= 0 ? $"{difference:F0}% smaller" : $"{-difference:F0}% larger");
                }
                catch (OperationCanceledException) { item.Status = "Cancelled · original kept"; throw; }
                catch (Exception error) { errors++; item.Status = "Error · " + error.Message; }
                done++;
                Progress.Value = 100.0 * done / _items.Count;
                StatusText.Text = $"Processed {done} of {_items.Count} images";
            }
            StatusText.Text = $"{done - errors} converted · {errors} errors";
            Tell($"Finished. Output: {Size(converted)}" + (original > 0 ? $" from {Size(original)}." : ".") + (missedTargets > 0 ? $" {missedTargets} images exceeded the target size." : ""), errors == 0 && missedTargets == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { StatusText.Text = $"Cancelled after {done} files. Completed files are kept."; }
        finally { _conversion = null; SetWorking(false); _ = DrainLaunchImports(); }
    }
    private void SetWorking(bool working)
    {
        RefreshImageNavigation();
        SetPanelEnabled(ImportControls, !working);
        SetPanelEnabled(SettingsPanel, !working);
        RefreshDestinationMode();
        PreviewButton.IsEnabled = !working;
        ConvertButton.IsEnabled = !working && _items.Count > 0;
        CancelButton.Visibility = Progress.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = 0;
    }
    private void CancelConversion(object sender, RoutedEventArgs e)
    {
        _conversion?.Cancel();
        StatusText.Text = "Cancelling after the current encoder operation…";
    }
    private static void SetPanelEnabled(Panel panel, bool enabled)
    {
        foreach (var element in panel.Children)
        {
            if (element is Control control) control.IsEnabled = enabled;
            else if (element is Panel child) SetPanelEnabled(child, enabled);
        }
    }
    private static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:F2} MB" : $"{bytes / 1024.0:F1} KB";

    private async Task Dialog(ContentDialog dialog)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            _activeDialog = dialog;
            dialog.RequestedTheme = Root.RequestedTheme;
            dialog.XamlRoot = Root.XamlRoot;
            dialog.Resources["ContentDialogMaxWidth"] = 820.0;
            await dialog.ShowAsync();
        }
        finally { _activeDialog = null; _dialogOpen = false; }
    }

    private static async Task<BitmapImage> Bitmap(byte[] data)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream)) { writer.WriteBytes(data); await writer.StoreAsync(); writer.DetachStream(); }
        stream.Seek(0);
        var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); return bitmap;
    }

    private async void ShowPreview(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen || _conversion is not null) return;
        if (QueueList.SelectedItem is not QueueItem item) { Tell("Select an image to compare."); return; }
        var request = ++_previewVersion;
        PreviewButton.IsEnabled = false;
        PreviewInfo.Text = "Generating preview...";
        ResultFormat.Text = ResultDimensions.Text = ResultSize.Text = "—";
        try
        {
            var options = ReadOptions();
            var preview = await Task.Run(() => _engine.Preview(item.Source.Path, options));
            var before = await Bitmap(preview.Original);
            var after = await Bitmap(preview.Converted);
            if (request != _previewVersion || QueueList.SelectedItem != item) return;
            OriginalPreview.Source = before;
            ConvertedPreview.Source = after;
            UpdateZoom();
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            CompareDecoration.Visibility = Visibility.Visible;
            UpdateComparisonClip();
            var originalBytes = new FileInfo(item.Source.Path).Length;
            var difference = originalBytes > 0 ? (1 - (double)preview.EncodedBytes / originalBytes) * 100 : 0;
            ResultFormat.Text = options.Format.ToString();
            ResultDimensions.Text = $"{preview.Width} × {preview.Height} px";
            ResultSize.Text = Size(preview.EncodedBytes) + (originalBytes > 0 ? difference >= 0 ? $"  ({difference:F0}% smaller)" : $"  ({-difference:F0}% larger)" : "");
            PreviewInfo.Text = $"{options.Format} - {preview.Width} x {preview.Height} - {Size(preview.EncodedBytes)} - " +
                Engine.OutputName(item.Source.Path, options, _items.IndexOf(item) + 1) +
                (options.TargetKilobytes > 0 && preview.EncodedBytes > (long)options.TargetKilobytes * 1024 ? " - Target size not reached" : "") +
                " - Existing filenames are renamed automatically." +
                (options.Crop != CropKind.None || options.Rotation is RotationKind.Clockwise90 or RotationKind.Clockwise270 ? " Images are centred; transformed boundaries may differ." : "");
        }
        catch (Exception error) { if (request == _previewVersion) { PreviewInfo.Text = "Could not generate a preview."; Tell(error.Message, InfoBarSeverity.Error); } }
        finally { PreviewButton.IsEnabled = _conversion is null; }
    }

    private async void ShowAbout(object sender, RoutedEventArgs e)
    {
        bool showLicences = false;
        bool showChangelog = false;
        var about = new ContentDialog { Title = "Akki Image Tools", CloseButtonText = "Close" };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/AppLogo.png")) { DecodePixelWidth = 192 },
            Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left
        });
        panel.Children.Add(new TextBlock { Text = $"Version {_updates.Version} · Windows x64" });
        panel.Children.Add(new TextBlock { Text = "Made by Nuno Rodrigues / akki.lu" });
        panel.Children.Add(new HyperlinkButton { Content = "Visit akki.lu", NavigateUri = new Uri("https://akki.lu") });
        var licenses = new Button { Content = "Third-party licences" };
        licenses.Click += (_, _) => { showLicences = true; about.Hide(); };
        panel.Children.Add(licenses);
        var changelog = new Button { Content = "Changelogs" };
        changelog.Click += (_, _) => { showChangelog = true; about.Hide(); };
        panel.Children.Add(changelog);
        about.Content = panel;
        await Dialog(about);
        if (showLicences) await ShowLicences();
        if (showChangelog) await ShowChangelog();
    }

    private async Task ShowChangelog()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "changelogs");
        var files = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "release-notes-*.md")
                .Select(path => (Path: path, Text: Path.GetFileNameWithoutExtension(path)["release-notes-".Length..]))
                .Where(item => Version.TryParse(item.Text, out _))
                .OrderByDescending(item => Version.Parse(item.Text)).ToArray()
            : [];
        if (files.Length == 0) { Tell("No changelogs are available in this installation.", InfoBarSeverity.Warning); return; }
        var selector = new ComboBox { Header = "Version", ItemsSource = files.Select(item => item.Text).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch };
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 13, MinHeight = 260, MaxHeight = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(text, "Changelog text");
        int selectionVersion = 0;
        selector.SelectionChanged += async (_, _) =>
        {
            var request = ++selectionVersion;
            if (selector.SelectedIndex < 0) return;
            var path = files[selector.SelectedIndex].Path;
            text.Text = "Loading...";
            try { var content = await File.ReadAllTextAsync(path); if (request == selectionVersion) text.Text = content; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { if (request == selectionVersion) text.Text = "Couldn't load this changelog: " + error.Message; }
        };
        var panel = new StackPanel { Width = 680, Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Choose a version to view its release notes. Available offline.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(selector);
        panel.Children.Add(text);
        selector.SelectedIndex = 0;
        await Dialog(new ContentDialog { Title = "Changelogs", Content = panel, CloseButtonText = "Close" });
    }

    private async Task ShowLicences()
    {
        var documents = new List<LicenceDocument>
        {
            new("Third-party software overview", Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md")),
            new("Akki Image Converter — MIT licence", Path.Combine(AppContext.BaseDirectory, "LICENSE"))
        };
        var directory = Path.Combine(AppContext.BaseDirectory, "licenses");
        if (Directory.Exists(directory))
        {
            documents.AddRange(Directory.EnumerateFiles(directory, "*.txt")
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => new LicenceDocument(LicenceTitle(Path.GetFileNameWithoutExtension(path)), path)));
        }
        var panel = new Grid { Width = 680, RowSpacing = 12 };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.Children.Add(new TextBlock
        {
            Text = "Select a document to read its full licence. All documents are included with the app and available offline.",
            TextWrapping = TextWrapping.Wrap
        });
        var selector = new ComboBox
        {
            Header = "Licence document", ItemsSource = documents.Select(document => document.Name).ToArray(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var text = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontSize = 13, MinHeight = 260, MaxHeight = 420, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(text, "Licence text");
        // The selection can change while a file is loading. Only display the latest selection.
        int selectionVersion = 0;
        selector.SelectionChanged += async (_, _) =>
        {
            var requestVersion = ++selectionVersion;
            if (selector.SelectedIndex < 0) return;
            var document = documents[selector.SelectedIndex];
            text.Text = "Loading…";
            try
            {
                var content = await File.ReadAllTextAsync(document.Path);
                if (requestVersion == selectionVersion) text.Text = content;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (requestVersion == selectionVersion) text.Text = "Couldn't load this licence: " + error.Message;
            }
        };
        Grid.SetRow(selector, 1);
        Grid.SetRow(text, 2);
        panel.Children.Add(selector);
        panel.Children.Add(text);
        selector.SelectedIndex = 0;
        await Dialog(new ContentDialog
        {
            Title = "Third-party licences", Content = panel, CloseButtonText = "Close"
        });
    }

    private sealed record LicenceDocument(string Name, string Path);

    private static string LicenceTitle(string name) => name switch
    {
        "Magick.NET-15.0.0-NOTICE" => "Magick.NET and image codecs — full notices",
        "Magick.NET-Apache-2.0" => "Magick.NET — Apache License 2.0",
        "Velopack-MIT" => "Velopack — MIT licence",
        "WindowsAppSDK-MIT" => "Windows App SDK — upstream MIT licence",
        "dotnet-runtime-10.0.14-LICENSE" => ".NET runtime — licence",
        "dotnet-runtime-10.0.14-NOTICES" => ".NET runtime — third-party notices",
        _ => name.Replace('_', ' ')
    };

    private async Task CheckAutomatically()
    {
        if (_preferences.Updates == UpdatePolicy.Manual || _updates.Busy || !_updates.IsInstalled) return;
        if (_preferences.LastUpdateCheck is { } last && DateTimeOffset.Now - last < TimeSpan.FromHours(6)) return;
        var available = await _updates.CheckAsync(_preferences, _preferences.Updates == UpdatePolicy.Automatic);
        if (available) { _updateAvailable = true; Tell(_updates.Status, InfoBarSeverity.Success); }
    }

    private async void ShowSettings(object sender, RoutedEventArgs e)
    {
        // Fit the viewport and reserve space for WinUI's overlay scrollbar.
        var panel = new StackPanel { Spacing = 14, MaxWidth = 460, Margin = new Thickness(0, 0, 24, 0) };
        panel.Children.Add(new TextBlock { Text = "Appearance", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var theme = new ComboBox
        {
            Header = "App theme", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Use Windows settings", "Light", "Dark" },
            SelectedIndex = Enum.IsDefined(_preferences.Theme) ? (int)_preferences.Theme : 0
        };
        theme.SelectionChanged += (_, _) =>
        {
            _preferences.Theme = (AppTheme)theme.SelectedIndex;
            ApplyTheme();
            try { _preferences.Save(); }
            catch (Exception error) { Tell("Couldn't save theme: " + error.Message, InfoBarSeverity.Warning); }
        };
        panel.Children.Add(theme);
        panel.Children.Add(new TextBlock { Text = "Choose a theme or follow Windows automatically.", FontSize = 12, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Microsoft.UI.Colors.Gray), Opacity = .25, Margin = new Thickness(0, 6, 0, 6) });
        panel.Children.Add(new TextBlock { Text = "Updates", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = $"Installed version: {_updates.Version}" });
        if (_updates.IsStoreBuild)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Updates are managed by Microsoft Store. You can check for updates and choose automatic updates in the Store app.",
                TextWrapping = TextWrapping.Wrap
            });
            var store = new Button { Content = "Open Microsoft Store updates" };
            store.Click += async (_, _) =>
            {
                try
                {
                    if (!await Launcher.LaunchUriAsync(new Uri("ms-windows-store://downloadsandupdates")))
                        Tell("Open Microsoft Store and check Downloads for updates.", InfoBarSeverity.Warning);
                }
                catch (Exception error) { Tell("Couldn't open Microsoft Store: " + error.Message, InfoBarSeverity.Warning); }
            };
            panel.Children.Add(store);
        }
        else
        {
            var policy = new ComboBox { Header = "Update preference", HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new[] { "Notify me automatically", "Download and install automatically on exit", "Manual checks only" }, SelectedIndex = (int)_preferences.Updates };
            panel.Children.Add(policy);
            var status = new TextBlock { Text = _updates.Status, TextWrapping = TextWrapping.Wrap };
            var lastCheck = new TextBlock { Text = _preferences.LastUpdateCheck is { } last ? $"Last checked: {last.LocalDateTime:g}" : "Last checked: never", FontSize = 12 };
            panel.Children.Add(lastCheck);
            var check = new Button { Content = "Check for updates" };
            var install = new Button { Content = "Download update", Visibility = _updateAvailable && !_updates.HasDownload ? Visibility.Visible : Visibility.Collapsed };
            var restart = new Button { Content = "Update and restart", Visibility = _updates.HasDownload ? Visibility.Visible : Visibility.Collapsed, IsEnabled = _conversion is null };
            // Let the outer Settings ScrollViewer measure the full release notes.
            var notes = new TextBlock { Text = _updates.ReleaseNotes ?? "", TextWrapping = TextWrapping.Wrap };
            var downloadProgress = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
            policy.SelectionChanged += (_, _) =>
            {
                _preferences.Updates = (UpdatePolicy)policy.SelectedIndex;
                try { _preferences.Save(); } catch (Exception error) { status.Text = "Couldn't save preference: " + error.Message; }
            };
            check.Click += async (_, _) =>
            {
                check.IsEnabled = false; status.Text = "Checking for updates…";
                _updateAvailable = await _updates.CheckAsync(_preferences, false);
                status.Text = _updates.Status; notes.Text = _updates.ReleaseNotes ?? "";
                lastCheck.Text = _preferences.LastUpdateCheck is { } last ? $"Last checked: {last.LocalDateTime:g}" : "Last checked: never";
                install.Visibility = _updateAvailable && !_updates.HasDownload ? Visibility.Visible : Visibility.Collapsed;
                check.IsEnabled = true;
            };
            install.Click += async (_, _) =>
            {
                install.IsEnabled = check.IsEnabled = false; status.Text = "Downloading update…";
                downloadProgress.Visibility = Visibility.Visible;
                await _updates.CheckAsync(_preferences, true, p => DispatcherQueue.TryEnqueue(() => downloadProgress.Value = p));
                status.Text = _updates.Status;
                restart.Visibility = _updates.HasDownload ? Visibility.Visible : Visibility.Collapsed;
                install.IsEnabled = check.IsEnabled = true;
            };
            restart.Click += (_, _) => { if (_conversion is null) { SavePreferences(); _updates.Restart(); } };
            panel.Children.Add(check); panel.Children.Add(install); panel.Children.Add(downloadProgress); panel.Children.Add(restart);
            panel.Children.Add(status); panel.Children.Add(notes);
            panel.Children.Add(new TextBlock { Text = "Checks run on launch and every 6 hours while the app is open. Updates never interrupt conversion.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
        var scroll = new ScrollViewer
        {
            Content = panel,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        await Dialog(new ContentDialog { Title = "Settings", Content = scroll, CloseButtonText = "Close" });
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_conversion is not null || _importing)
        {
            args.Cancel = true;
            Tell("Finish or cancel the current operation before closing.", InfoBarSeverity.Warning);
            return;
        }
        SavePreferences();
        _updates.InstallOnExit();
    }
}
