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
        var settings = _preferences.Conversion;
        FormatBox.SelectedItem = settings.Format;
        if (FormatBox.SelectedIndex < 0) FormatBox.SelectedIndex = 0;
        QualitySlider.Value = settings.Quality;
        LosslessBox.IsChecked = settings.Lossless;
        ResizeBox.SelectedIndex = (int)settings.Resize;
        PercentBox.Value = settings.Percent;
        WidthBox.Value = settings.Width;
        HeightBox.Value = settings.Height;
        AspectBox.IsChecked = settings.KeepAspectRatio;
        UpscaleBox.IsChecked = settings.PreventUpscale;
        MetadataBox.IsChecked = settings.StripMetadata;
        StructureBox.IsChecked = settings.KeepFolderStructure;
        BackgroundBox.Text = settings.Background;
        OutputBox.Text = settings.OutputDirectory;
        RecursiveBox.IsChecked = _preferences.IncludeSubfolders;
        _ready = true;
        RefreshFormat();
        RefreshResize();
        AppWindow.Closing += OnClosing;
        Closed += (_, _) => _updateTimer.Stop();
        _updateTimer.Tick += async (_, _) => await CheckAutomatically();
        Root.Loaded += async (_, _) =>
        {
            if (_updates.IsStoreBuild) return;
            _updateTimer.Start();
            await CheckAutomatically();
        };
    }

    private void Tell(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        Notice.Message = message;
        Notice.Severity = severity;
        Notice.IsOpen = true;
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
            !double.IsFinite(HeightBox.Value) || !double.IsFinite(PercentBox.Value))
            throw new ArgumentException("Enter valid resize dimensions and quality.");
        var options = new ConversionOptions
        {
            Format = (OutputKind)FormatBox.SelectedItem,
            Quality = (uint)QualitySlider.Value,
            Lossless = LosslessBox.IsChecked == true,
            Resize = (ResizeKind)ResizeBox.SelectedIndex,
            Percent = PercentBox.Value,
            Width = checked((uint)WidthBox.Value), Height = checked((uint)HeightBox.Value),
            KeepAspectRatio = AspectBox.IsChecked == true, PreventUpscale = UpscaleBox.IsChecked == true,
            StripMetadata = MetadataBox.IsChecked == true, KeepFolderStructure = StructureBox.IsChecked == true,
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
            var output = Path.GetFullPath(OutputBox.Text);
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
                            if (current != root && current.Equals(output, StringComparison.OrdinalIgnoreCase)) continue;
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
        finally { _importing = false; SetPanelEnabled(ImportControls, true); RefreshQueue(); }
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
    { if (_conversion is null && !_importing && sender is Button { Tag: QueueItem item }) { _items.Remove(item); RefreshQueue(); } }
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

    private async void ConvertImages(object sender, RoutedEventArgs e)
    {
        ConversionOptions options;
        try { options = ReadOptions(); }
        catch (Exception error) { Tell(error.Message, InfoBarSeverity.Error); return; }
        SavePreferences();
        using var cancellation = new CancellationTokenSource();
        _conversion = cancellation;
        SetWorking(true);
        int done = 0, errors = 0;
        long original = 0, converted = 0;
        try
        {
            foreach (var item in _items.ToArray())
            {
                cancellation.Token.ThrowIfCancellationRequested();
                item.Status = "Converting…";
                try
                {
                    var result = await Task.Run(() => _engine.Convert(item.Source, options, cancellation.Token));
                    item.OutputPath = result.Path;
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
            Tell($"Finished. Output: {Size(converted)}" + (original > 0 ? $" from {Size(original)}." : "."), errors == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { StatusText.Text = $"Cancelled after {done} files. Completed files are kept."; }
        finally { _conversion = null; SetWorking(false); }
    }
    private void SetWorking(bool working)
    {
        SetPanelEnabled(ImportControls, !working);
        SetPanelEnabled(SettingsPanel, !working);
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
        if (_dialogOpen) return;
        if (QueueList.SelectedItem is not QueueItem item) { Tell("Select an image to compare."); return; }
        PreviewButton.IsEnabled = false;
        try
        {
            var options = ReadOptions();
            var preview = await Task.Run(() => _engine.Preview(item.Source.Path, options));
            var panel = new StackPanel { Spacing = 12, Width = 600 };
            panel.Children.Add(new TextBlock { Text = "Original", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new Image { Source = await Bitmap(preview.Original), MaxHeight = 210, Stretch = Stretch.Uniform });
            panel.Children.Add(new TextBlock { Text = $"{options.Format} · {preview.Width} × {preview.Height} · {Size(preview.EncodedBytes)}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            panel.Children.Add(new Image { Source = await Bitmap(preview.Converted), MaxHeight = 210, Stretch = Stretch.Uniform });
            await Dialog(new ContentDialog { Title = item.Name, Content = new ScrollViewer { Content = panel }, CloseButtonText = "Close" });
        }
        catch (Exception error) { Tell(error.Message, InfoBarSeverity.Error); }
        finally { PreviewButton.IsEnabled = true; }
    }

    private async void ShowAbout(object sender, RoutedEventArgs e)
    {
        bool showLicences = false;
        var about = new ContentDialog { Title = "Akki Image Converter", CloseButtonText = "Close" };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("ms-appx:///Assets/AppLogo.png")) { DecodePixelWidth = 192 },
            Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Left
        });
        panel.Children.Add(new TextBlock { Text = $"Version {_updates.Version} · Windows x64" });
        panel.Children.Add(new TextBlock { Text = "Made by akki.lu" });
        panel.Children.Add(new HyperlinkButton { Content = "Visit akki.lu", NavigateUri = new Uri("https://akki.lu") });
        panel.Children.Add(new TextBlock { Text = "Batch conversion, made simple. Your images are processed locally.", TextWrapping = TextWrapping.Wrap });
        var licenses = new Button { Content = "Third-party licences" };
        licenses.Click += (_, _) => { showLicences = true; about.Hide(); };
        panel.Children.Add(licenses);
        about.Content = panel;
        await Dialog(about);
        if (showLicences) await ShowLicences();
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
