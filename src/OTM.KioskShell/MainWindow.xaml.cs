using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Otm.Kiosk.Shared.Models;
using Otm.Kiosk.Shared.Storage;
using Forms = System.Windows.Forms;

namespace Otm.Kiosk.Shell;

public partial class MainWindow : Window
{
    private const int SwRestore = 9;
    private const int SwShow = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _client = new() { BaseAddress = new Uri("http://localhost:47821") };
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _focusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer _violationTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly List<SecondaryDisplayWindow> _secondaryWindows = [];
    private readonly List<ManagedApp> _managedApps = [];
    private List<KioskLauncher> _launchers = [];
    private List<string> _activeWebAllowedSites = [];
    private DateTimeOffset _yieldFocusUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastViolationPoll = DateTimeOffset.UtcNow.AddMinutes(-5);
    private bool _appOwnsDisplays;
    private bool _restoreWebWorkspaceAfterAdmin;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            PlaceOnPrimaryScreen();
            CreateSecondaryDisplayCovers();
            EnforceFullscreen();
            await RefreshAsync();
        };
        _clockTimer.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("dddd, MMM d  h:mm tt");
        _clockTimer.Start();
        _focusTimer.Tick += (_, _) => EnforceFullscreen();
        _focusTimer.Start();
        _violationTimer.Tick += async (_, _) => await PollViolationsAsync();
        _violationTimer.Start();
        _noticeTimer.Tick += (_, _) =>
        {
            NoticePanel.Visibility = Visibility.Collapsed;
            _noticeTimer.Stop();
        };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (await AdminPostAsync("/api/unlock", JsonSerializer.Serialize(new { minutes = 15 })))
        {
            ShowNotice("Temporary unlock active", "Kiosk restrictions are paused for 15 minutes.", NoticeKind.Success);
            await RefreshAsync();
        }
    }

    private async void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (await AdminPostAsync("/api/lock", "{}"))
        {
            ForceShellLock(clearManagedApps: true);
            ShowNotice("Locked", "Kiosk restrictions are active.", NoticeKind.Success);
            await RefreshAsync();
        }
    }

    private async void EmergencyUnlock_Click(object sender, RoutedEventArgs e)
    {
        await EmergencyUnlockAsync();
    }

    private void ExitShell_Click(object sender, RoutedEventArgs e)
    {
        ExitShell();
    }

    private void ManagedApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ManagedApp app })
        {
            FocusManagedApp(app);
        }
    }

    private void ShowShell_Click(object sender, RoutedEventArgs e)
    {
        ForceShellLock(clearManagedApps: false);
    }

    private void ForceShellLock(bool clearManagedApps)
    {
        _yieldFocusUntil = DateTimeOffset.MinValue;
        _appOwnsDisplays = false;
        if (clearManagedApps)
        {
            _managedApps.Clear();
            RefreshManagedTaskbar();
        }

        AdminPanel.Visibility = Visibility.Collapsed;
        AdminCornerButton.Visibility = Visibility.Visible;
        Show();
        PlaceOnPrimaryScreen();
        WindowState = WindowState.Maximized;
        Topmost = true;
        SetSecondaryCoversVisible(true);
        Activate();
        Focus();
    }

    private void Manager_Click(object sender, RoutedEventArgs e)
    {
        YieldFocusToAdminTool();
        Process.Start(new ProcessStartInfo("http://localhost:47821") { UseShellExecute = true });
    }

    private void ControlPanel_Click(object sender, RoutedEventArgs e)
    {
        YieldFocusToAdminTool();
        var controlPanelPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "ControlPanel", "OTM.ControlPanel.exe"));
        if (System.IO.File.Exists(controlPanelPath))
        {
            Process.Start(new ProcessStartInfo(controlPanelPath) { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo("OTM.ControlPanel.exe") { UseShellExecute = true });
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: KioskLauncher launcher })
        {
            return;
        }

        try
        {
            var approved = await RequestLaunchAsync(launcher);
            if (approved is null)
            {
                return;
            }

            if (string.Equals(approved.Type, KioskLauncherTypes.Web, StringComparison.OrdinalIgnoreCase))
            {
                await StartWebWorkspaceAsync(approved);
            }
            else
            {
                StartAppWorkspace(approved);
            }
        }
        catch (Exception ex)
        {
            ShowNotice("Launcher failed", ex.Message, NoticeKind.Error);
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var state = await _client.GetFromJsonAsync<RuntimeState>("/api/status", JsonOptions);
            _launchers = await _client.GetFromJsonAsync<List<KioskLauncher>>("/api/kiosk/launchers", JsonOptions) ?? [];
            StatusText.Text = state is null
                ? "Service status unavailable"
                : $"{state.PolicyName}: enforcement {(state.EnforcementEnabled ? "on" : "off")}";
            SafeTestBanner.Visibility = state?.EnforcementEnabled == false ? Visibility.Visible : Visibility.Collapsed;
            LaunchersList.ItemsSource = _launchers;
            WorkspaceSubtitle.Text = _launchers.Count == 0
                ? "No launchers are configured. Open admin controls to apply a template."
                : "Choose an approved app or exam site from the left.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Waiting for kiosk service";
            LaunchersList.ItemsSource = Array.Empty<KioskLauncher>();
            ShowNotice("Service unavailable", "The kiosk service is not responding yet. Local controls will reconnect automatically.", NoticeKind.Warning);
            Debug.WriteLine(ex);
        }
    }

    private async Task<KioskLauncher?> RequestLaunchAsync(KioskLauncher launcher)
    {
        var response = await _client.PostAsJsonAsync("/api/kiosk/launch", new { launcher.Id, launcher.DisplayName }, JsonOptions);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<KioskLauncher>(JsonOptions);
        }

        ShowNotice("Launch blocked", await ReadApiErrorAsync(response), NoticeKind.Warning);
        return null;
    }

    private async Task StartWebWorkspaceAsync(KioskLauncher launcher)
    {
        if (string.IsNullOrWhiteSpace(launcher.Url))
        {
            ShowNotice("Exam site missing", "This web launcher does not have a URL configured.", NoticeKind.Warning);
            return;
        }

        try
        {
            await InitializeBrowserAsync();
        }
        catch (Exception ex)
        {
            ShowNotice("WebView2 unavailable", GetWebView2StartupError(ex), NoticeKind.Error);
            return;
        }
        _activeWebAllowedSites = launcher.AllowedSites.Count > 0 ? launcher.AllowedSites : [launcher.Url];
        WorkspaceTitle.Text = launcher.DisplayName;
        WorkspaceSubtitle.Text = "Secure embedded exam browser";
        IdleWorkspace.Visibility = Visibility.Collapsed;
        AppWorkspace.Visibility = Visibility.Collapsed;
        WebWorkspace.Visibility = Visibility.Visible;
        Topmost = true;
        SetSecondaryCoversVisible(true);
        ExamBrowser.CoreWebView2?.Navigate(launcher.Url);
    }

    private void StartAppWorkspace(KioskLauncher launcher)
    {
        var target = !string.IsNullOrWhiteSpace(launcher.Path) ? launcher.Path : launcher.ProcessName;
        if (string.IsNullOrWhiteSpace(target))
        {
            ShowNotice("Launcher not configured", "This app does not have a launch path or process name configured.", NoticeKind.Warning);
            return;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                Arguments = launcher.Arguments ?? "",
                UseShellExecute = true
            });

            if (process is not null)
            {
                _managedApps.Add(new ManagedApp(launcher.DisplayName, process));
                RefreshManagedTaskbar();
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(900);
                    FocusManagedApp(_managedApps.LastOrDefault(app => app.Process.Id == process.Id));
                });
            }

            WorkspaceTitle.Text = "Lab Workspace";
            WorkspaceSubtitle.Text = "Approved apps can stay open together.";
            AppWorkspaceTitle.Text = $"{launcher.DisplayName} started";
            AppWorkspaceText.Text = launcher.AllowMultiMonitorOwnership
                ? "This approved app can use all connected displays until it exits."
                : "Use the launcher to open additional approved apps.";
            IdleWorkspace.Visibility = Visibility.Collapsed;
            WebWorkspace.Visibility = Visibility.Collapsed;
            AppWorkspace.Visibility = Visibility.Visible;

            if (launcher.AllowMultiMonitorOwnership || string.Equals(launcher.WorkspaceMode, KioskWorkspaceModes.AppOwner, StringComparison.OrdinalIgnoreCase))
            {
                YieldDisplaysToApp(process);
            }
            else
            {
                YieldFocusToManagedApps();
            }

            ShowNotice("Application started", $"{launcher.DisplayName} is opening.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            ShowNotice($"Could not start {launcher.DisplayName}", ex.Message, NoticeKind.Error);
        }
    }

    private async Task InitializeBrowserAsync()
    {
        if (ExamBrowser.CoreWebView2 is not null)
        {
            return;
        }

        Directory.CreateDirectory(KioskPaths.WebView2UserDataDirectory);
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: KioskPaths.WebView2UserDataDirectory);
        await ExamBrowser.EnsureCoreWebView2Async(environment);
        var webView = ExamBrowser.CoreWebView2;
        if (webView is null)
        {
            throw new InvalidOperationException("WebView2 failed to initialize.");
        }

        var settings = webView.Settings;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = true;
        webView.NavigationStarting += Browser_NavigationStarting;
        webView.NewWindowRequested += Browser_NewWindowRequested;
        webView.DownloadStarting += Browser_DownloadStarting;
    }

    private static string GetWebView2StartupError(Exception ex)
    {
        if (ex is UnauthorizedAccessException
            || ex.HResult == unchecked((int)0x80070005)
            || ex.Message.Contains("0x80070005", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("E_ACCESSDENIED", StringComparison.OrdinalIgnoreCase))
        {
            return $"WebView2 could not access its profile folder at {KioskPaths.WebView2UserDataDirectory}. Run the installer as Administrator or make sure the current user can write to this folder. Details: {ex.Message}";
        }

        return $"Install Microsoft Edge WebView2 Runtime to use embedded exam sites. Details: {ex.Message}";
    }

    private void Browser_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsUrlAllowed(e.Uri))
        {
            return;
        }

        e.Cancel = true;
        _ = ReportViolationAsync("BlockedWebsite", $"Blocked navigation to {e.Uri}", e.Uri);
        ShowNotice("Website blocked", "That page is not allowed in this kiosk policy.", NoticeKind.Warning);
    }

    private void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        _ = ReportViolationAsync("BlockedWebsite", $"Blocked new browser window: {e.Uri}", e.Uri);
        ShowNotice("New window blocked", "Opening additional browser windows is disabled in kiosk mode.", NoticeKind.Warning);
    }

    private void Browser_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        _ = ReportViolationAsync("DownloadDeleted", $"Blocked browser download: {e.DownloadOperation.Uri}", e.DownloadOperation.Uri);
        ShowNotice("Download blocked", "Downloads are disabled in this kiosk policy.", NoticeKind.Warning);
    }

    private bool IsUrlAllowed(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var candidate))
        {
            return false;
        }

        return _activeWebAllowedSites.Any(pattern => MatchesSitePattern(candidate, pattern));
    }

    private static bool MatchesSitePattern(Uri candidate, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        if (!pattern.Contains("://", StringComparison.Ordinal))
        {
            return candidate.Host.EndsWith(pattern.TrimStart('*', '.').TrimEnd('*', '/'), StringComparison.OrdinalIgnoreCase);
        }

        var normalized = pattern.TrimEnd('*');
        return candidate.ToString().StartsWith(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private async Task PollViolationsAsync()
    {
        try
        {
            var since = Uri.EscapeDataString(_lastViolationPoll.ToString("O"));
            var logs = await _client.GetFromJsonAsync<List<LogEntry>>($"/api/kiosk/violations?since={since}", JsonOptions) ?? [];
            _lastViolationPoll = DateTimeOffset.UtcNow;
            var latest = logs.LastOrDefault();
            if (latest is not null)
            {
                ShowNotice("Blocked by SimpleKioskOS", latest.Message, NoticeKind.Warning);
            }
        }
        catch
        {
            // Violation polling is best-effort; the service status path covers connectivity.
        }
    }

    private async Task ReportViolationAsync(string eventType, string message, string? path)
    {
        try
        {
            await _client.PostAsJsonAsync("/api/kiosk/violation", new { eventType, message, path }, JsonOptions);
        }
        catch
        {
            // Local logging should never interrupt kiosk browsing.
        }
    }

    private async Task<bool> AdminPostAsync(string url, string body)
    {
        if (string.IsNullOrWhiteSpace(PinBox.Password))
        {
            ShowNotice("Admin PIN required", "Enter the admin PIN before using admin actions.", NoticeKind.Info);
            return false;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);

        var response = await _client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            ShowNotice("Admin action blocked", await ReadApiErrorAsync(response), NoticeKind.Warning);
            return false;
        }

        return true;
    }

    private void ToggleAdmin_Click(object sender, RoutedEventArgs e)
    {
        var open = AdminPanel.Visibility != Visibility.Visible;
        SetAdminPanelOpen(open);
    }

    private void SetAdminPanelOpen(bool open)
    {
        if (open && WebWorkspace.Visibility == Visibility.Visible)
        {
            _restoreWebWorkspaceAfterAdmin = true;
            WebWorkspace.Visibility = Visibility.Collapsed;
        }
        else if (!open && _restoreWebWorkspaceAfterAdmin)
        {
            WebWorkspace.Visibility = Visibility.Visible;
            _restoreWebWorkspaceAfterAdmin = false;
        }

        AdminPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        AdminCornerButton.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        if (open)
        {
            PinBox.Focus();
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)
            && e.Key == Key.End)
        {
            e.Handled = true;
            ExitShell();
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)
            && e.Key == Key.U)
        {
            e.Handled = true;
            _ = EmergencyUnlockAsync();
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)
            && e.Key == Key.A)
        {
            e.Handled = true;
            SetAdminPanelOpen(AdminPanel.Visibility != Visibility.Visible);
            return;
        }

        if (e.Key == Key.Escape && AdminPanel.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            SetAdminPanelOpen(false);
            return;
        }

        if (ShouldSuppressKey(e))
        {
            e.Handled = true;
            ShowNotice("Shortcut blocked", "This station is in kiosk mode.", NoticeKind.Info);
            return;
        }

        if (e.Key == Key.F5)
        {
            _ = RefreshAsync();
        }
    }

    private static bool ShouldSuppressKey(System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        return key is Key.LWin or Key.RWin
            || (alt && (key == Key.F4 || key == Key.Tab || key == Key.Escape || key == Key.Space))
            || (ctrl && key == Key.Escape);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        EnforceFullscreen();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            foreach (var window in _secondaryWindows)
            {
                window.CloseFromOwner();
            }

            return;
        }

        e.Cancel = true;
        ShowNotice("Close blocked", "Use the admin controls to unlock or manage this station.", NoticeKind.Info);
    }

    private void EnforceFullscreen()
    {
        CleanupExitedProcesses();
        if (_appOwnsDisplays || _managedApps.Count > 0 || _yieldFocusUntil > DateTimeOffset.UtcNow)
        {
            RefreshManagedTaskbar();
            return;
        }

        PlaceOnPrimaryScreen();
        Topmost = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Activate();
        SetSecondaryCoversVisible(true);
    }

    private void CleanupExitedProcesses()
    {
        _managedApps.RemoveAll(app =>
        {
            try
            {
                return app.Process.HasExited;
            }
            catch
            {
                return true;
            }
        });

        RefreshManagedTaskbar();

        if (_managedApps.Count == 0 && _appOwnsDisplays)
        {
            _appOwnsDisplays = false;
            Show();
            PlaceOnPrimaryScreen();
            SetSecondaryCoversVisible(true);
        }
    }

    private void YieldFocusToManagedApps()
    {
        _yieldFocusUntil = DateTimeOffset.UtcNow.AddSeconds(20);
        Topmost = false;
    }

    private void YieldFocusToAdminTool()
    {
        _yieldFocusUntil = DateTimeOffset.UtcNow.AddMinutes(5);
        Topmost = false;
        SetSecondaryCoversVisible(false);
        ShowNotice("Admin tool opening", "The kiosk shell is yielding focus for local administration.", NoticeKind.Info);
    }

    private async Task EmergencyUnlockAsync()
    {
        try
        {
            var response = await _client.PostAsync("/api/recovery/disable-enforcement", new StringContent("{}", Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
            {
                _yieldFocusUntil = DateTimeOffset.UtcNow.AddHours(24);
                Topmost = false;
                SetSecondaryCoversVisible(false);
                ShowNotice("Emergency unlock active", "Enforcement is disabled locally for recovery/testing. You can now uninstall or open admin tools.", NoticeKind.Warning);
                await RefreshAsync();
                return;
            }

            ShowNotice("Emergency unlock failed", await ReadApiErrorAsync(response), NoticeKind.Error);
        }
        catch (Exception ex)
        {
            Topmost = false;
            _yieldFocusUntil = DateTimeOffset.UtcNow.AddMinutes(10);
            SetSecondaryCoversVisible(false);
            ShowNotice("Service unavailable", $"The shell yielded focus, but the service did not accept emergency unlock: {ex.Message}", NoticeKind.Warning);
        }
    }

    private void ExitShell()
    {
        _allowClose = true;
        Topmost = false;
        SetSecondaryCoversVisible(false);
        Close();
    }

    private void YieldDisplaysToApp(Process? process)
    {
        if (process is not null && !_managedApps.Any(app => app.Process.Id == process.Id))
        {
            _managedApps.Add(new ManagedApp(process.ProcessName, process));
            RefreshManagedTaskbar();
        }

        _appOwnsDisplays = true;
        Topmost = false;
        SetSecondaryCoversVisible(false);
        Hide();
    }

    private void PlaceOnPrimaryScreen()
    {
        var primary = Forms.Screen.PrimaryScreen?.Bounds;
        if (primary is null)
        {
            WindowState = WindowState.Maximized;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowState = WindowState.Normal;
        Left = primary.Value.Left;
        Top = primary.Value.Top;
        Width = primary.Value.Width;
        Height = primary.Value.Height;
        WindowState = WindowState.Maximized;
    }

    private void CreateSecondaryDisplayCovers()
    {
        foreach (var window in _secondaryWindows)
        {
            window.CloseFromOwner();
        }

        _secondaryWindows.Clear();
        var primary = Forms.Screen.PrimaryScreen;
        foreach (var screen in Forms.Screen.AllScreens)
        {
            if (primary is not null && screen.DeviceName == primary.DeviceName)
            {
                continue;
            }

            var cover = new SecondaryDisplayWindow(screen.Bounds);
            _secondaryWindows.Add(cover);
            cover.Show();
        }
    }

    private void SetSecondaryCoversVisible(bool visible)
    {
        foreach (var window in _secondaryWindows)
        {
            if (visible)
            {
                window.Show();
                window.Activate();
            }
            else
            {
                window.Hide();
            }
        }
    }

    private void RefreshManagedTaskbar()
    {
        ManagedAppsList.ItemsSource = _managedApps
            .Where(app =>
            {
                try
                {
                    return !app.Process.HasExited;
                }
                catch
                {
                    return false;
                }
            })
            .OrderBy(app => app.StartedAt)
            .ToList();
        OpenAppsCountText.Text = _managedApps.Count.ToString();
    }

    private void FocusManagedApp(ManagedApp? app)
    {
        if (app is null)
        {
            return;
        }

        try
        {
            if (app.Process.HasExited)
            {
                CleanupExitedProcesses();
                return;
            }

            _yieldFocusUntil = DateTimeOffset.UtcNow.AddSeconds(25);
            Topmost = false;
            SetSecondaryCoversVisible(false);
            app.Process.Refresh();
            var handle = app.Process.MainWindowHandle;
            if (handle == IntPtr.Zero)
            {
                handle = Process.GetProcessesByName(app.Process.ProcessName)
                    .FirstOrDefault(process =>
                    {
                        try
                        {
                            return process.MainWindowHandle != IntPtr.Zero;
                        }
                        catch
                        {
                            return false;
                        }
                    })?.MainWindowHandle ?? IntPtr.Zero;
            }

            if (handle == IntPtr.Zero)
            {
                ShowNotice("App is starting", "The app window is not ready yet. Try the taskbar button again in a moment.", NoticeKind.Info);
                return;
            }

            ShowWindow(handle, SwRestore);
            ShowWindow(handle, SwShow);
            SetForegroundWindow(handle);
        }
        catch (Exception ex)
        {
            ShowNotice("Could not focus app", ex.Message, NoticeKind.Warning);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private static async Task<string> ReadApiErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return $"{(int)response.StatusCode} {response.ReasonPhrase}";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString() ?? "Request failed.";
            }
        }
        catch
        {
            // Fall back to plain response text below.
        }

        return body.Trim();
    }

    private void ShowNotice(string title, string message, NoticeKind kind)
    {
        NoticeTitle.Text = title;
        NoticeText.Text = message;
        NoticeAccent.Background = kind switch
        {
            NoticeKind.Success => new SolidColorBrush(System.Windows.Media.Color.FromRgb(31, 138, 112)),
            NoticeKind.Warning => new SolidColorBrush(System.Windows.Media.Color.FromRgb(191, 120, 38)),
            NoticeKind.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(159, 45, 45)),
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 107, 135))
        };
        NoticePanel.Visibility = Visibility.Visible;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private enum NoticeKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    private sealed record ManagedApp(string DisplayName, Process Process)
    {
        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    }
}
