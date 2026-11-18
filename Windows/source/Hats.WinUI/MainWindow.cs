using System.Diagnostics;
using System.Runtime.InteropServices;
using Hats.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace Hats.WinUI;

public sealed class MainWindow : Window
{
    readonly AppSettings settings = AppSettings.Load();
    readonly Grid root = new() { RequestedTheme = ElementTheme.Dark, Background = Brush(9, 15, 28) };
    readonly ComboBox drives = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Select your SD card" };
    readonly ComboBox packs = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a HATS pack" };
    readonly TextBlock driveDetail = Text("Connect an SD card or a Switch in USB mass storage mode.", 12, true);
    readonly TextBlock releaseTitle = Text("Checking GitHub…", 17);
    readonly TextBlock releaseDetail = Text("Latest published release from TR4UNEXUS/Nexus-USB-HATS", 12, true);
    readonly TextBlock backupSummary = Text("Verified backups on this PC", 12, true);
    readonly Button refreshDrive = new() { Content = "Refresh", MinWidth = 92, MinHeight = 26, Height = 26, Padding = new Thickness(10, 1, 10, 1) };
    readonly Button refreshRelease = new() { Content = "Check releases", MinWidth = 120, MinHeight = 26, Height = 26, Padding = new Thickness(10, 1, 10, 1) };
    readonly MenuFlyoutItem browseBackup = new() { Text = "Backup location…" };
    readonly MenuFlyoutItem checkAppUpdate = new() { Text = "Check for app updates…" };
    readonly Button appUpdateAction = new() { Content = "Review update" };
    readonly InfoBar appUpdateNotice = new() { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Informational };
    readonly Button install = new() { Content = "Install HATS", MinWidth = 160, Height = 44, IsEnabled = false };
    readonly Button cancel = new() { Content = "Cancel", Visibility = Visibility.Collapsed, Height = 44 };
    readonly MenuFlyoutItem format = new() { Text = "Format SD card…" };
    readonly Button options = new() { Content = "Options", Height = 44, Padding = new Thickness(12, 4, 12, 4) };
    readonly ProgressBar progressBar = new() { Minimum = 0, Maximum = 100, Height = 4, Visibility = Visibility.Collapsed };
    readonly TextBlock status = Text("Choose a drive and a pack to begin.", 14);
    readonly TextBlock progressText = Text("", 12, true);
    readonly TextBlock activity = new() { Text = "Progress, activity and errors appear here.", IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
    readonly ScrollViewer activityScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, ZoomMode = ZoomMode.Disabled };
    readonly InfoBar notice = new() { IsOpen = false, IsClosable = true };
    readonly DispatcherTimer driveTimer = new() { Interval = TimeSpan.FromSeconds(12) };
    HatsRelease? release;
    CancellationTokenSource? cancellation;
    bool busy, scanning, fetching, dialogOpen, closed, requireDriveSelection, followLogPending, checkingAppUpdate, updatingApp;
    long lastProgress;
    readonly List<string> activityLines = [];
    readonly object progressLogLock = new();
    string? lastLoggedPhase, lastDriveSummary;
    int lastLoggedProgress = -1;

    static SolidColorBrush Brush(byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(255, r, g, b));
    static TextBlock Text(string value, double size, bool muted = false) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, Foreground = muted ? Brush(255, 255, 255) : Brush(255, 255, 255) };
    static StackPanel Stack(params UIElement[] children) { var panel = new StackPanel { Spacing = 8 }; foreach (var child in children) panel.Children.Add(child); return panel; }
    static Border Card(UIElement child) => new() { Background = Brush(19, 29, 48), BorderBrush = Brush(43, 60, 88), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 7, 12, 7), Child = child };
    static Image Artwork(string file, double width, double height) => new() { Source = new BitmapImage(new Uri("ms-appx:///Assets/" + file)), Width = width, Height = height, Stretch = Stretch.Uniform, IsHitTestVisible = false };
    static Grid Row(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid { ColumnSpacing = 12 }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); grid.Children.Add(left); Grid.SetColumn(right, 1); grid.Children.Add(right); return grid;
    }
    public MainWindow()
    {
        Title = "Nexus · HATS Installer";
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96d;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)(680 * scale), (int)(area.Width * .86)), height = Math.Min((int)(620 * scale), (int)(area.Height * .9));
        bool compact = height / scale < 760;
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "app_icon.ico")); } catch { }
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min((int)(500 * scale), width);
            presenter.PreferredMinimumHeight = Math.Min((int)(480 * scale), height);
        }
        AppWindow.TitleBar.BackgroundColor = Windows.UI.Color.FromArgb(255, 9, 15, 28);
        AppWindow.TitleBar.ForegroundColor = Colors.White;
        AppWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(255, 9, 15, 28);
        AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 42, 53, 86);
        AppWindow.TitleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 9, 15, 28);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(255, 9, 15, 28);
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { Height = compact ? 64 : 96, Margin = new Thickness(0, -4, 0, 2) };
        // Fit the complete Nexus logo inside the header using the original artwork's aspect ratio.
        var art = Artwork("nexus-background.png", header.Height * 2 * 16 / 9, header.Height * 2);
        art.HorizontalAlignment = HorizontalAlignment.Right; art.VerticalAlignment = VerticalAlignment.Top; art.Margin = new Thickness(0, -header.Height * .12, 0, 0);
        header.Children.Add(art);
        var fade = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0) };
        fade.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb(255, 9, 15, 28), Offset = 0 });
        fade.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb(255, 9, 15, 28), Offset = .48 });
        fade.GradientStops.Add(new() { Color = Windows.UI.Color.FromArgb(0, 9, 15, 28), Offset = .76 });
        header.Children.Add(new Border { Background = fade, IsHitTestVisible = false });
        var brand = Text("NEXUS", compact ? 22 : 25); brand.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; brand.CharacterSpacing = 30; brand.VerticalAlignment = VerticalAlignment.Center;
        var edition = Text("HATS INSTALLER", compact ? 10 : 12); edition.CharacterSpacing = 120; edition.Foreground = Brush(162, 171, 255); edition.VerticalAlignment = VerticalAlignment.Center;
        var brandText = Stack(brand, edition); brandText.Orientation = compact ? Orientation.Horizontal : Orientation.Vertical; brandText.Spacing = compact ? 12 : 5; brandText.VerticalAlignment = VerticalAlignment.Center; brandText.HorizontalAlignment = HorizontalAlignment.Left; brandText.Margin = new Thickness(2, 0, 0, 0);
        header.Children.Add(brandText);
        header.SizeChanged += (_, e) => header.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        UpdateBackupSummary();
        var driveContents = Stack(Row(Text("SD card", 16), refreshDrive), drives, driveDetail); driveContents.Spacing = 4;
        var driveCard = Card(driveContents);
        var packHeading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        packHeading.Children.Add(Artwork("fetch-hats.png", 24, 24)); releaseTitle.VerticalAlignment = VerticalAlignment.Center; packHeading.Children.Add(releaseTitle);
        var releaseContents = Stack(Row(packHeading, refreshRelease), packs, releaseDetail); releaseContents.Spacing = 4;
        var releaseCard = Card(releaseContents);
        var info = new Button { Content = "Info", MinWidth = 60, Height = 44, Padding = new Thickness(12, 4, 12, 4) };
        info.Click += async (_, _) => await ShowProtectionDetails();
        var menu = new MenuFlyout();
        menu.Items.Add(browseBackup);
        var github = new MenuFlyoutItem { Text = "View GitHub releases" }; github.Click += (_, _) => OpenPath("https://github.com/TR4UNEXUS/Nexus-USB-HATS/releases");
        menu.Items.Add(github); menu.Items.Add(checkAppUpdate); menu.Items.Add(new MenuFlyoutSeparator()); menu.Items.Add(format); options.Flyout = menu;
        checkAppUpdate.Click += async (_, _) => await CheckAppUpdates(true);
        appUpdateAction.Click += async (_, _) => await CheckAppUpdates(true);
        appUpdateNotice.ActionButton = appUpdateAction;
        var body = Stack(header, appUpdateNotice, notice, driveCard, releaseCard, backupSummary);
        body.Margin = new Thickness(20, compact ? 6 : 14, 20, compact ? 6 : 14);
        if (compact) body.Spacing = 4;
        var setupScroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(setupScroll);
        var openLog = new Button { Content = "Open saved log", Padding = new Thickness(10, 4, 10, 4) };
        openLog.Click += (_, _) => OpenPath(AppSettings.LogPath);
        ToolTipService.SetToolTip(openLog, AppSettings.LogPath);
        var logContents = new Grid { RowSpacing = 6 };
        logContents.RowDefinitions.Add(new() { Height = GridLength.Auto });
        logContents.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        logContents.Children.Add(Row(Text("Activity log", 14), openLog));
        activityScroll.Content = activity;
        var logViewport = new Border { Background = Brush(15, 24, 42), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Child = activityScroll };
        Grid.SetRow(logViewport, 1); logContents.Children.Add(logViewport);
        // Wait for the new text to be measured before using its final scrollable height.
        activityScroll.LayoutUpdated += (_, _) => {
            if (!followLogPending) return;
            followLogPending = false;
            activityScroll.ChangeView(null, activityScroll.ScrollableHeight, null, true);
        };
        activityScroll.SizeChanged += (_, _) => followLogPending = true;
        var activityPanel = Card(logContents); activityPanel.Margin = new Thickness(20, 0, 20, 10);
        Grid.SetRow(activityPanel, 1); root.Children.Add(activityPanel);
        install.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        install.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center }; buttons.Children.Add(cancel); buttons.Children.Add(options); buttons.Children.Add(info); buttons.Children.Add(install);
        progressText.Visibility = Visibility.Collapsed;
        var footer = new Border { Background = Brush(19, 15, 29), BorderBrush = Brush(43, 60, 88), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 14, 20, 14), Child = Stack(progressBar, Row(Stack(status, progressText), buttons)) };
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
        // Let the log fill spare height; scroll setup on short screens to keep both log and actions reachable.
        void FitSetup() => setupScroll.MaxHeight = Math.Max(0, root.ActualHeight - footer.ActualHeight - 144);
        root.SizeChanged += (_, _) => FitSetup();
        footer.SizeChanged += (_, _) => FitSetup();
        refreshDrive.Click += async (_, _) => await ScanDrives(true);
        refreshRelease.Click += async (_, _) => await FetchRelease();
        drives.SelectionChanged += (_, _) => {
            if (drives.SelectedItem is SdVolume volume) {
                driveDetail.Text = $"{volume.FreeBytes / 1073741824d:N1} GB free of {volume.TotalBytes / 1073741824d:N1} GB. {volume.DeviceDescription}";
                ToolTipService.SetToolTip(drives, $"{volume.Root}\nVolume label: {(string.IsNullOrWhiteSpace(volume.Identity.Label) ? "No volume label" : volume.Identity.Label)}\nDevice: {volume.Device?.HardwareName ?? "Unavailable"}");
                if (!scanning) Log("Selected card: " + volume);
                if (!scanning) requireDriveSelection = false;
            }
            UpdateControls();
        };
        packs.SelectionChanged += (_, _) => UpdateControls();
        browseBackup.Click += async (_, _) => await BrowseBackup();
        install.Click += async (_, _) => await Install();
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.IsEnabled = false; status.Text = updatingApp ? "Cancelling app download…" : "Cancelling preparation…"; };
        format.Click += async (_, _) => await Format();
        driveTimer.Tick += async (_, _) => { if (!busy && !dialogOpen) await ScanDrives(false); };
        AppWindow.Closing += (_, e) => { if (busy) { e.Cancel = true; notice.Title = "An operation is still running"; notice.Message = cancel.IsEnabled ? "Cancel preparation first, then close the app." : "Keep the app and SD card connected until writing or recovery finishes."; notice.Severity = InfoBarSeverity.Warning; notice.IsOpen = true; } };
        Closed += (_, _) => { closed = true; driveTimer.Stop(); };
        root.Loaded += async (_, _) => { Log($"Nexus HATS Installer {Program.AppVersion} ready."); Log("Backups: " + settings.BackupFolder); driveTimer.Start(); await Task.WhenAll(ScanDrives(true), FetchRelease(), CheckAppUpdates(false)); };
    }
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
    void UpdateControls()
    {
        install.IsEnabled = !busy && !dialogOpen && !fetching && !scanning && drives.SelectedItem is SdVolume && packs.SelectedItem is PackAsset;
        drives.IsEnabled = packs.IsEnabled = browseBackup.IsEnabled = options.IsEnabled = !busy && !dialogOpen;
        refreshDrive.IsEnabled = !busy && !dialogOpen && !scanning;
        refreshRelease.IsEnabled = !busy && !dialogOpen && !fetching;
        format.IsEnabled = !busy && !dialogOpen && drives.SelectedItem is SdVolume;
        checkAppUpdate.IsEnabled = appUpdateAction.IsEnabled = !busy && !dialogOpen && !checkingAppUpdate;
    }
    void Ui(Action action) { if (!closed) DispatcherQueue.TryEnqueue(() => { if (!closed) action(); }); }
    void Log(string message)
    {
        AppSettings.Log(message);
        Ui(() => { activityLines.Add($"{DateTime.Now:HH:mm:ss}  {message}"); if (activityLines.Count > 150) activityLines.RemoveAt(0); followLogPending = true; activity.Text = string.Join("\r\n", activityLines); });
    }
    void Report(JobProgress p)
    {
        lock (progressLogLock)
        {
            int bucket = (int)p.Percent / 10;
            if (p.Phase != lastLoggedPhase || bucket > lastLoggedProgress)
            {
                lastLoggedPhase = p.Phase; lastLoggedProgress = bucket;
                Log($"{p.Phase}: {p.Percent:N0}%");
            }
        }
        long now = Environment.TickCount64;
        if (p.CanCancel && p.Percent < 100 && now - Interlocked.Read(ref lastProgress) < 100) return;
        Interlocked.Exchange(ref lastProgress, now);
        Ui(() => { progressBar.IsIndeterminate = false; progressBar.Value = p.Percent; status.Text = p.Phase; progressText.Text = $"{p.Percent:N0}%  ·  " + (p.CanCancel ? "You can cancel before SD card changes begin." : "Keep the app open and the card connected."); cancel.IsEnabled = p.CanCancel && cancellation?.IsCancellationRequested != true; });
    }
    void SetBusy(bool value)
    {
        if (value) { lock (progressLogLock) { lastLoggedPhase = null; lastLoggedProgress = -1; } }
        busy = value; cancel.Visibility = value ? Visibility.Visible : Visibility.Collapsed; cancel.IsEnabled = value; UpdateControls();
        progressBar.Visibility = value || progressBar.Value > 0 ? Visibility.Visible : Visibility.Collapsed;
        progressText.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        if (!value) { progressBar.IsIndeterminate = false; progressText.Text = ""; cancellation?.Dispose(); cancellation = null; }
    }
    void Error(Exception e)
    {
        Log("ERROR: " + e); notice.Title = "Couldn’t complete that step"; notice.Message = e.Message; notice.Severity = InfoBarSeverity.Error; notice.IsOpen = true; status.Text = "Review the message above.";
    }
    async Task ScanDrives(bool announce)
    {
        if (scanning || busy) return; scanning = true; UpdateControls();
        if (announce) Log("Scanning removable drives…");
        try
        {
            var selected = drives.SelectedItem as SdVolume; var found = await Task.Run(WindowsVolumes.Scan);
            if (closed) return;
            if (!announce && drives.ItemsSource is SdVolume[] current && current.SequenceEqual(found)) return;
            var retained = found.FirstOrDefault(v => selected != null && v.Root == selected.Root && v.Identity == selected.Identity && v.TotalBytes == selected.TotalBytes);
            if (selected != null && retained == null) requireDriveSelection = true;
            drives.ItemsSource = found;
            drives.SelectedItem = retained ?? (!requireDriveSelection && found.Length == 1 ? found[0] : null);
            if (selected != null && drives.SelectedItem == null) { driveDetail.Text = "The selected card was removed or changed. Select it again."; if (announce) Log(driveDetail.Text); }
            else if (found.Length == 0) driveDetail.Text = "No removable card found. Connect your SD card, then refresh.";
            else if (drives.SelectedItem == null) driveDetail.Text = requireDriveSelection ? "The selected card was removed or changed. Select it again." : $"{found.Length} removable drives found. Select your SD card above.";
            var summary = found.Length == 0 ? "No ready removable drives found." : "Detected: " + string.Join("; ", found.Select(v => v.ToString()));
            if (announce || summary != lastDriveSummary) { lastDriveSummary = summary; Log(summary); }
        }
        catch (Exception e) { if (announce) Error(e); else Log("ERROR scanning drives: " + e.Message); }
        finally { scanning = false; if (!closed) UpdateControls(); }
    }
    async Task FetchRelease()
    {
        if (fetching || busy) return; fetching = true; UpdateControls();
        Log("Checking the latest GitHub release…");
        var selected = packs.SelectedItem as PackAsset; packs.ItemsSource = null; release = null; releaseTitle.Text = "Checking GitHub…";
        try
        {
            using var service = new ReleaseService(); release = await service.LatestAsync(); if (closed) return;
            releaseTitle.Text = release.Title;
            releaseDetail.Text = $"{release.Assets.Length} ZIP packs available  ·  Checked {DateTime.Now:t}";
            packs.ItemsSource = release.Assets; packs.SelectedItem = release.Assets.FirstOrDefault(a => a == selected) ?? (release.Assets.Length == 1 ? release.Assets[0] : null);
            Log($"Latest release: {release.Title} (tag {release.Tag}). Found {release.Assets.Length} ZIP assets.");
        }
        catch (Exception e) { releaseTitle.Text = "Release check unavailable"; releaseDetail.Text = "Check your internet connection and try again."; Error(e); }
        finally { fetching = false; if (!closed) UpdateControls(); }
    }
    async Task BrowseBackup()
    {
        try
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync(); if (folder == null) return;
            settings.BackupFolder = folder.Path; settings.Save(); UpdateBackupSummary(); Log("Backup location: " + folder.Path);
        }
        catch (Exception e) { Error(e); }
    }
    async Task CheckAppUpdates(bool interactive)
    {
        if (busy || dialogOpen || checkingAppUpdate) return;
        checkingAppUpdate = true; UpdateControls();
        Log("Checking for installer app updates…");
        try
        {
            using var service = new AppUpdateService();
            var latest = await service.LatestAsync();
            if (closed) return;
            if (latest == null || latest.Version <= AppUpdateService.ParseVersion(Program.AppVersion))
            {
                appUpdateNotice.IsOpen = false; checkAppUpdate.Text = "Check for app updates…";
                var message = latest == null ? "No stable Windows app update has been published yet." : $"Installed app: {Program.AppVersion}. Latest stable release: {latest.DisplayVersion}. No newer stable app update is available.";
                Log(message);
                if (interactive && !busy && !dialogOpen) await Dialog("App updates", Text(message, 14), "", "Close");
                return;
            }
            Log($"App update available: {latest.DisplayVersion} (installed {Program.AppVersion}).");
            checkAppUpdate.Text = $"Update app to {latest.DisplayVersion}…";
            appUpdateNotice.Title = "App update available";
            appUpdateNotice.Message = $"Version {latest.DisplayVersion} is ready.";
            appUpdateNotice.IsOpen = true;
            if (!interactive || busy || dialogOpen) return;
            var notes = new HyperlinkButton { Content = "View release notes", NavigateUri = new Uri(latest.PageUrl), Padding = new Thickness(0) };
            if (await Dialog("Update the installer app?", Stack(Text($"{Program.AppVersion} → {latest.DisplayVersion}", 20),
                Text($"Download: {latest.Asset.Size / 1048576d:N1} MB", 14),
                Text("The download will be verified, then the app will restart. A copy of the previous app will be kept beside it. Your settings and SD card files stay in place.", 14), notes), "Update & restart", "Later") != ContentDialogResult.Primary) return;
            await InstallAppUpdate(latest);
        }
        catch (Exception e)
        {
            Log("ERROR checking app updates: " + e);
            if (interactive && !closed && !busy && !dialogOpen) await Dialog("App update unavailable", Text(e.Message, 14), "", "Close");
        }
        finally { checkingAppUpdate = false; if (!closed) UpdateControls(); }
    }
    async Task InstallAppUpdate(AppRelease selected)
    {
        if (busy) return;
        bool restarting = false;
        updatingApp = true; SetBusy(true); cancellation = new(); notice.IsOpen = false;
        progressBar.Value = 0;
        try
        {
            var target = Environment.ProcessPath ?? throw new IOException("Cannot locate the installed app.");
            using var service = new AppUpdateService();
            status.Text = "Preparing app update…";
            AppUpdateService.Confirm(selected, await service.LatestAsync(cancellation.Token));
            var stage = AppUpdateInstaller.CreateWorkspace(target, selected.Asset.Size);
            Log($"Downloading app {selected.DisplayVersion} from {AppUpdateService.Repository}.");
            await service.DownloadAsync(selected, Path.Combine(stage, "download.exe"), p => Report(new("Downloading app update", p, true)), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Report(new("Verifying app update", 100, false)); cancel.IsEnabled = false;
            var request = await Task.Run(() => AppUpdateInstaller.Prepare(stage, target, selected));
            cancellation.Token.ThrowIfCancellationRequested();
            Log("App update verified. Restarting the installer…");
            using var worker = AppUpdateInstaller.StartWorker(request);
            restarting = true; busy = false; Close(); Application.Current.Exit();
        }
        catch (OperationCanceledException) { status.Text = "App update cancelled. The installed app was not changed."; Log(status.Text); }
        catch (Exception e) { Error(e); }
        finally { updatingApp = false; if (!restarting) SetBusy(false); }
    }
    void UpdateBackupSummary()
    {
        var label = Path.TrimEndingDirectorySeparator(settings.BackupFolder).Equals(AppSettings.DefaultBackupFolder, StringComparison.OrdinalIgnoreCase) ? @"Desktop\NexusHATSBackups" : settings.BackupFolder;
        backupSummary.Text = "Backups: " + label;
        ToolTipService.SetToolTip(backupSummary, settings.BackupFolder);
    }
    async Task<ContentDialogResult> Dialog(string title, UIElement content, string primary = "Continue", string close = "Cancel")
    {
        if (dialogOpen) return ContentDialogResult.None;
        dialogOpen = true; UpdateControls();
        try { return await new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = ElementTheme.Dark, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = ContentDialogButton.Close }.ShowAsync(); }
        finally { dialogOpen = false; UpdateControls(); }
    }
    async Task Install()
    {
        if (busy || drives.SelectedItem is not SdVolume volume || packs.SelectedItem is not PackAsset asset || release == null) return;
        var selectedRelease = release;
        try
        {
            WindowsVolumes.Validate(volume); var preserved = InstallPolicy.LoadPreserved(AppSettings.ProtectionFile);
            var backup = Path.Combine(settings.BackupFolder, $"HATS-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}-{Guid.NewGuid().ToString("N")[..6]}");
            if (Paths.Overlap(backup, volume.Root)) throw new IOException("Choose a backup location outside the selected SD card.");
            if (await Dialog("Install HATS on this card?", Stack(Text(volume.ToString(), 18), Text(asset.Name, 15), Text($"Backup: {backup}", 13, true), Text("Atmosphere core, bootloader and recognised HATS utilities will update. Protected games, ports and mods stay in place.", 14)), "Back up & install") != ContentDialogResult.Primary) return;
            WindowsVolumes.Validate(volume); notice.IsOpen = false; progressBar.Value = 0; SetBusy(true); cancellation = new();
            Log($"Installing {asset.Name} on {volume}. Backup: {backup}");
            // Prevent two WinUI processes/elevated sessions updating one volume.
            await Task.Run(async () =>
            {
                using var jobLock = new Semaphore(1, 1, "Local\\HATS-Volume-" + volume.Identity.Serial.ToString("X8"));
                if (!jobLock.WaitOne(0)) throw new IOException("Another HATS operation is using this card.");
                try { await new Installer().RunAsync(selectedRelease, asset, volume.Root, backup, preserved, Log, Report, cancellation.Token, () => WindowsVolumes.Validate(volume)); }
                finally { jobLock.Release(); }
            });
            notice.Title = "HATS is ready"; notice.Message = "Your update is complete. Safely eject the SD card before disconnecting it."; notice.Severity = InfoBarSeverity.Success; notice.IsOpen = true; status.Text = "Installation complete"; progressBar.Value = 100;
        }
        catch (OperationCanceledException) { status.Text = "Preparation cancelled. No installation was performed."; Log(status.Text); }
        catch (Exception e) { Error(e); }
        finally { SetBusy(false); }
    }
    async Task ShowProtectionDetails()
    {
        try
        {
            await Dialog("Automatic protection", Stack(Text("Games & ports", 17), Text("Ordinary switch folders are checked too. Only recognised HATS utilities update. Unknown apps, games and ports stay untouched.", 14, true), Text("Mods & sysmodules", 17), Text("Game mods and cheats stay protected. HOC and other identified sysmodules update with the pack, including their resource files. Nexus USB library indexes and Manager settings stay in place.", 14, true), Text("Backups", 17), Text("Windows and Mac use the same Nexus protection list. Protected content is excluded from backups and cleanup. Files being replaced get a verified backup on this PC.", 14, true), Text("Backup location: " + settings.BackupFolder, 12, true)), "", "Close");
        }
        catch (Exception e) { Error(e); }
    }
    async Task Format()
    {
        if (busy || drives.SelectedItem is not SdVolume volume) return;
        try
        {
            WindowsVolumes.Validate(volume);
            var warning = Text("!!! CAUTION !!!\nThis erases EVERYTHING on the selected card, including games, ports, mods and backups. This cannot be undone.\n\nBack up anything you need first. Formatting does not create a backup, and installation protections do not apply.", 14);
            warning.Foreground = Brush(255, 130, 144); warning.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            var permission = Text(WindowsVolumes.IsUserAnAdmin() ? "Check the drive shown above before continuing." : "Windows will ask for administrator permission after you choose Erase card. The app will stay open.", 13, true);
            if (await Dialog("Erase and format as FAT32?", Stack(Text(volume.ToString(), 18), warning, permission), "Erase card") != ContentDialogResult.Primary) { Log("Format cancelled. No format was performed."); return; }
            WindowsVolumes.Validate(volume); SetBusy(true); cancel.IsEnabled = false; notice.IsOpen = false;
            Log("Format confirmed for " + volume);
            status.Text = WindowsVolumes.IsUserAnAdmin() ? "Preparing to format…" : "Waiting for Windows administrator permission…";
            Log(status.Text); progressBar.IsIndeterminate = true;
            await FormatElevation.RunAsync(volume, p => Report(new(p >= 95 ? "Finalising FAT32" : "Formatting FAT32", p, false)));
            status.Text = "Format complete. Safely eject and reconnect the card."; Log(status.Text); drives.SelectedItem = null;
        }
        catch (OperationCanceledException) { status.Text = "Administrator permission was cancelled. No format was performed."; Log(status.Text); }
        catch (Exception e) { Error(e); }
        finally { SetBusy(false); }
    }
    void OpenPath(string path)
    {
        try { if (!path.StartsWith("https://github.com/", StringComparison.Ordinal) && !Paths.Exists(path)) { notice.Title = "Nothing saved here yet"; notice.Message = path; notice.Severity = InfoBarSeverity.Informational; notice.IsOpen = true; return; } Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Error(e); }
    }
}
