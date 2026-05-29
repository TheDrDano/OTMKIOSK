using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const int SwHide = 0;
    private const int SwRestore = 9;
    private const int SwShow = 5;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkTab = 0x09;
    private const int VkEscape = 0x1B;
    private const int VkSpace = 0x20;
    private const int VkF4 = 0x73;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int LlkhfAltdown = 0x20;
    private const uint AbmGetState = 0x00000004;
    private const uint AbmSetState = 0x0000000A;
    private const int AbsAutoHide = 0x0000001;

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
    private string _autoLaunchedKioskId = "";
    private DateTimeOffset _yieldFocusUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastViolationPoll = DateTimeOffset.UtcNow.AddMinutes(-5);
    private bool _appOwnsDisplays;
    private bool _restoreWebWorkspaceAfterAdmin;
    private bool _restoreBrowserAfterAdmin;
    private bool _allowClose;
    private bool _webFocusMode;
    private bool _suppressSystemShortcuts;
    private bool _taskbarHidden;
    private int? _originalTaskbarState;
    private string _adminSessionSecret = "";
    private IntPtr _keyboardHook;
    private LowLevelKeyboardProc? _keyboardProc;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            PlaceOnPrimaryScreen();
            CreateSecondaryDisplayCovers();
            InstallKeyboardHook();
            SetSystemLockdownActive(true);
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

    private async void AdminSignIn_Click(object sender, RoutedEventArgs e) => await SignInAdminAsync();

    private async void PinBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SignInAdminAsync();
        }
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (await AdminPostAsync("/api/unlock", JsonSerializer.Serialize(new { minutes = 15 })))
        {
            _yieldFocusUntil = DateTimeOffset.UtcNow.AddMinutes(15);
            SetSystemLockdownActive(false);
            Topmost = false;
            SetSecondaryCoversVisible(false);
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

    private void RestartSystem_Click(object sender, RoutedEventArgs e)
    {
        StartLocalSystemCommand("/r /t 0", "Restart requested", "Windows is restarting.");
    }

    private void ShutdownSystem_Click(object sender, RoutedEventArgs e)
    {
        StartLocalSystemCommand("/s /t 0", "Shutdown requested", "Windows is shutting down.");
    }

    private void SignOutSystem_Click(object sender, RoutedEventArgs e)
    {
        StartLocalSystemCommand("/l", "Sign out requested", "Windows is signing out.");
    }

    private async void CheckStationUpdate_Click(object sender, RoutedEventArgs e)
    {
        await AdminUpdatePostAsync("/api/updates/check", "Update check completed");
    }

    private async void DownloadStationUpdate_Click(object sender, RoutedEventArgs e)
    {
        await AdminUpdatePostAsync("/api/updates/download", "Update download completed");
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

    private void ToggleWebFullscreen_Click(object sender, RoutedEventArgs e)
    {
        SetWebFocusMode(!_webFocusMode);
    }

    private void ForceShellLock(bool clearManagedApps)
    {
        _yieldFocusUntil = DateTimeOffset.MinValue;
        _appOwnsDisplays = false;
        _autoLaunchedKioskId = "";
        SetWebFocusMode(false);
        SetSystemLockdownActive(true);
        if (clearManagedApps)
        {
            _managedApps.Clear();
            RefreshManagedTaskbar();
        }

        AdminPanel.Visibility = Visibility.Collapsed;
        _adminSessionSecret = "";
        SetAdminSignedIn(false);
        AdminTaskbarButton.Visibility = Visibility.Visible;
        AdminCornerButton.Visibility = Visibility.Collapsed;
        Show();
        PlaceOnPrimaryScreen();
        WindowState = WindowState.Maximized;
        Topmost = true;
        SetSecondaryCoversVisible(true);
        Activate();
        Focus();
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
                : $"{state.PolicyName}: managed mode {(state.EnforcementEnabled ? "on" : "off")}";
            SafeTestBanner.Visibility = state?.EnforcementEnabled == false ? Visibility.Visible : Visibility.Collapsed;
            LaunchersList.ItemsSource = _launchers;
            WorkspaceSubtitle.Text = _launchers.Count == 0
                ? "No launchers are configured. Open admin controls to add apps or websites."
                : "Choose an approved app, website, or workspace from the launcher.";
            await AutoLaunchDedicatedKioskAsync(state);
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

    private async Task AutoLaunchDedicatedKioskAsync(RuntimeState? state)
    {
        if (state?.EnforcementEnabled != true || AdminPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        var launcher = _launchers.FirstOrDefault(IsDedicatedKiosk);
        if (launcher is null || string.Equals(_autoLaunchedKioskId, launcher.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (WebWorkspace.Visibility == Visibility.Visible || _managedApps.Count > 0 || _appOwnsDisplays)
        {
            return;
        }

        var approved = await RequestLaunchAsync(launcher);
        if (approved is null)
        {
            return;
        }

        _autoLaunchedKioskId = approved.Id;
        if (string.Equals(approved.Type, KioskLauncherTypes.Web, StringComparison.OrdinalIgnoreCase))
        {
            await StartWebWorkspaceAsync(approved);
        }
        else
        {
            StartAppWorkspace(approved);
        }
    }

    private static bool IsDedicatedKiosk(KioskLauncher launcher)
    {
        return string.Equals(launcher.WorkspaceMode, KioskWorkspaceModes.DedicatedKiosk, StringComparison.OrdinalIgnoreCase)
            || string.Equals(launcher.Id, "dedicated-kiosk", StringComparison.OrdinalIgnoreCase);
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
        WorkspaceSubtitle.Text = IsDedicatedKiosk(launcher) ? "Dedicated fullscreen kiosk" : "Embedded web workspace";
        IdleWorkspace.Visibility = Visibility.Collapsed;
        AppWorkspace.Visibility = Visibility.Collapsed;
        WebWorkspace.Visibility = Visibility.Visible;
        Topmost = true;
        SetSecondaryCoversVisible(true);
        ExamBrowser.CoreWebView2?.Navigate(launcher.Url);
        if (IsDedicatedKiosk(launcher))
        {
            SetWebFocusMode(true);
        }
    }

    private void SetWebFocusMode(bool enabled)
    {
        _webFocusMode = enabled;
        LeftRail.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        LeftRailColumn.Width = enabled ? new GridLength(0) : new GridLength(292);
        WorkspaceHeader.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        ShellTaskbar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        WebFullscreenText.Text = enabled ? "Exit full" : "Fullscreen";
        AdminTaskbarButton.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        AdminCornerButton.Visibility = enabled && AdminPanel.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
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
                AddManagedApp(launcher.DisplayName, process);
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(900);
                    TrackLauncherProcess(launcher, process);
                    FocusManagedApp(_managedApps.LastOrDefault(app => app.Process.Id == process.Id));
                });
            }
            else
            {
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(900);
                    TrackLauncherProcess(launcher, null);
                });
            }

            WorkspaceTitle.Text = "Lab Workspace";
            WorkspaceSubtitle.Text = "Approved apps can stay open together.";
            AppWorkspaceTitle.Text = $"{launcher.DisplayName} started";
            AppWorkspaceText.Text = launcher.AllowMultiMonitorOwnership
                ? "This workspace can use all connected displays until it exits."
                : "Use the launcher or taskbar to open and switch between workspaces.";
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
        ShowBlockedWebsitePage(e.Uri, "This website is not included in the allowed list for this workspace.");
        ShowNotice("Website blocked", "That page is not allowed in this workspace.", NoticeKind.Warning);
    }

    private void Browser_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        _ = ReportViolationAsync("BlockedWebsite", $"Blocked new browser window: {e.Uri}", e.Uri);
        ShowBlockedWebsitePage(e.Uri, "Opening new browser windows is not allowed in this workspace.");
        ShowNotice("New window blocked", "Opening additional browser windows is disabled in workspace mode.", NoticeKind.Warning);
    }

    private void Browser_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
        _ = ReportViolationAsync("DownloadDeleted", $"Blocked browser download: {e.DownloadOperation.Uri}", e.DownloadOperation.Uri);
        ShowBlockedWebsitePage(e.DownloadOperation.Uri, "Downloads are turned off for this workspace.");
        ShowNotice("Download blocked", "Downloads are disabled in this workspace.", NoticeKind.Warning);
    }

    private bool IsUrlAllowed(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var candidate))
        {
            return false;
        }

        if (candidate.Scheme is "about" or "data")
        {
            return true;
        }

        return _activeWebAllowedSites.Any(pattern => MatchesSitePattern(candidate, pattern));
    }

    private void ShowBlockedWebsitePage(string blockedUri, string reason)
    {
        var html = BuildBlockedWebsiteHtml(blockedUri, reason);
        _ = Dispatcher.InvokeAsync(() => ExamBrowser.CoreWebView2?.NavigateToString(html));
    }

    private static string BuildBlockedWebsiteHtml(string blockedUri, string reason)
    {
        var safeUri = WebUtility.HtmlEncode(blockedUri);
        var safeReason = WebUtility.HtmlEncode(reason);
        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Website blocked</title>
  <style>
    * { box-sizing: border-box; }
    html, body { height: 100%; margin: 0; }
    body {
      font-family: Segoe UI, Arial, sans-serif;
      background: #e8edf2;
      color: #111827;
      display: grid;
      place-items: center;
      padding: 32px;
    }
    .panel {
      width: min(760px, 100%);
      background: #ffffff;
      border: 1px solid #cbd5e1;
      border-radius: 14px;
      padding: 34px;
      box-shadow: 0 22px 55px rgba(15, 23, 42, .16);
      text-align: center;
    }
    .mark {
      width: 112px;
      height: 112px;
      border-radius: 56px;
      display: inline-grid;
      place-items: center;
      background: #fff7ed;
      border: 1px solid #fed7aa;
      margin-bottom: 18px;
      color: #ff6b1a;
    }
    svg { width: 54px; height: 54px; }
    h1 { margin: 0; font-size: 36px; line-height: 1.12; }
    p { margin: 13px auto 0; color: #526171; font-size: 17px; line-height: 1.45; max-width: 620px; }
    .url {
      margin-top: 22px;
      padding: 13px 14px;
      background: #f8fafc;
      border: 1px solid #d8e1ea;
      border-radius: 8px;
      color: #334155;
      overflow-wrap: anywhere;
      font-family: Consolas, monospace;
      font-size: 13px;
      text-align: left;
    }
    .brand { margin-top: 24px; color: #64748b; font-size: 13px; }
  </style>
</head>
<body>
  <main class="panel">
    <div class="mark" aria-hidden="true">
      <svg viewBox="0 0 16 16" fill="currentColor">
        <path d="M8 1.133 2.5 3.36v4.198c0 3.06 2.062 5.93 5.5 7.31 3.438-1.38 5.5-4.25 5.5-7.31V3.36L8 1.133Zm0 1.08 4.5 1.823v3.522c0 2.47-1.58 4.82-4.5 6.21-2.92-1.39-4.5-3.74-4.5-6.21V4.036L8 2.214Z"/>
        <path d="M5.354 5.646a.5.5 0 0 0-.708.708L6.793 8.5l-2.147 2.146a.5.5 0 0 0 .708.708L7.5 9.207l2.146 2.147a.5.5 0 0 0 .708-.708L8.207 8.5l2.147-2.146a.5.5 0 0 0-.708-.708L7.5 7.793 5.354 5.646Z"/>
      </svg>
    </div>
    <h1>Website blocked</h1>
    <p>{{safeReason}}</p>
    <div class="url">{{safeUri}}</div>
    <div class="brand">SIMPLEKIOSKOS Launchpad</div>
  </main>
</body>
</html>
""";
    }

    private static bool MatchesSitePattern(Uri candidate, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        pattern = pattern.Trim();
        if (pattern == "*")
        {
            return true;
        }

        if (!pattern.Contains("://", StringComparison.Ordinal))
        {
            var host = pattern.TrimStart('*', '.').TrimEnd('*', '/');
            return string.Equals(candidate.Host, host, StringComparison.OrdinalIgnoreCase)
                || candidate.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.Contains('*', StringComparison.Ordinal))
        {
            var expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(candidate.ToString(), expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return candidate.ToString().StartsWith(pattern.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
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
        var secret = !string.IsNullOrWhiteSpace(_adminSessionSecret) ? _adminSessionSecret : PinBox.Password;
        if (string.IsNullOrWhiteSpace(secret))
        {
            ShowNotice("Sign in required", "Enter the admin PIN before using admin actions.", NoticeKind.Info);
            SetAdminSignedIn(false);
            return false;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-OTM-Admin-PIN", secret);

        var response = await _client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            ShowNotice("Admin action blocked", await ReadApiErrorAsync(response), NoticeKind.Warning);
            return false;
        }

        return true;
    }

    private async Task AdminUpdatePostAsync(string url, string title)
    {
        try
        {
            var secret = !string.IsNullOrWhiteSpace(_adminSessionSecret) ? _adminSessionSecret : PinBox.Password;
            if (string.IsNullOrWhiteSpace(secret))
            {
                ShowNotice("Sign in required", "Enter the admin PIN before using updates.", NoticeKind.Info);
                SetAdminSignedIn(false);
                return;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-OTM-Admin-PIN", secret);

            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                ShowNotice("Update request failed", await ReadApiErrorAsync(response), NoticeKind.Warning);
                return;
            }

            var message = await ReadApiMessageAsync(response);
            ShowNotice(title, string.IsNullOrWhiteSpace(message) ? "Update request completed." : message, NoticeKind.Info);
        }
        catch (Exception ex)
        {
            ShowNotice("Update request failed", ex.Message, NoticeKind.Error);
        }
    }

    private async Task SignInAdminAsync()
    {
        if (string.IsNullOrWhiteSpace(PinBox.Password))
        {
            ShowNotice("PIN required", "Enter the admin PIN or recovery key.", NoticeKind.Warning);
            PinBox.Focus();
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/policy");
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _adminSessionSecret = "";
                SetAdminSignedIn(false);
                ShowNotice("Sign in failed", await ReadApiErrorAsync(response), NoticeKind.Warning);
                PinBox.Focus();
                return;
            }

            _adminSessionSecret = PinBox.Password;
            PinBox.Clear();
            SetAdminSignedIn(true);
            ShowNotice("Admin signed in", "Local admin controls are available.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            ShowNotice("Service unavailable", ex.Message, NoticeKind.Error);
        }
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
            _restoreBrowserAfterAdmin = ExamBrowser.Visibility == Visibility.Visible;
            ExamBrowser.Visibility = Visibility.Hidden;
        }
        else if (!open && _restoreWebWorkspaceAfterAdmin)
        {
            WebWorkspace.Visibility = Visibility.Visible;
            ExamBrowser.Visibility = _restoreBrowserAfterAdmin ? Visibility.Visible : Visibility.Collapsed;
            _restoreWebWorkspaceAfterAdmin = false;
            _restoreBrowserAfterAdmin = false;
        }

        AdminPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        AdminTaskbarButton.Visibility = open || _webFocusMode ? Visibility.Collapsed : Visibility.Visible;
        AdminCornerButton.Visibility = open || !_webFocusMode ? Visibility.Collapsed : Visibility.Visible;
        if (open)
        {
            if (string.IsNullOrWhiteSpace(_adminSessionSecret))
            {
                SetAdminSignedIn(false);
                PinBox.Focus();
            }
        }
    }

    private void SetAdminSignedIn(bool signedIn)
    {
        AdminSignInPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        AdminActionsPanel.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
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
        if (AdminPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        EnforceFullscreen();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            RestoreSystemUi();
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
        if (AdminPanel.Visibility == Visibility.Visible)
        {
            SetSystemLockdownActive(true);
            Topmost = true;
            RefreshManagedTaskbar();
            return;
        }

        if (_appOwnsDisplays || _managedApps.Count > 0 || _yieldFocusUntil > DateTimeOffset.UtcNow)
        {
            RefreshManagedTaskbar();
            return;
        }

        SetSystemLockdownActive(true);
        PlaceOnPrimaryScreen();
        Topmost = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Activate();
        SetSecondaryCoversVisible(true);
    }

    private void CleanupExitedProcesses()
    {
        PruneExitedManagedApps();

        RefreshManagedTaskbar();
        if (_managedApps.Count == 0 && WebWorkspace.Visibility != Visibility.Visible)
        {
            _autoLaunchedKioskId = "";
        }

        if (_managedApps.Count == 0 && _appOwnsDisplays)
        {
            _appOwnsDisplays = false;
            _autoLaunchedKioskId = "";
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
        SetSystemLockdownActive(false);
        Topmost = false;
        SetSecondaryCoversVisible(false);
        ShowNotice("Admin tool opening", "SimpleKioskOS is making room for local administration.", NoticeKind.Info);
    }

    private async Task EmergencyUnlockAsync()
    {
        try
        {
            var response = await _client.PostAsync("/api/recovery/disable-enforcement", new StringContent("{}", Encoding.UTF8, "application/json"));
            if (response.IsSuccessStatusCode)
            {
                _yieldFocusUntil = DateTimeOffset.UtcNow.AddHours(24);
                SetSystemLockdownActive(false);
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
            SetSystemLockdownActive(false);
            SetSecondaryCoversVisible(false);
            ShowNotice("Service unavailable", $"The shell yielded focus, but the service did not accept emergency unlock: {ex.Message}", NoticeKind.Warning);
        }
    }

    private void ExitShell()
    {
        _allowClose = true;
        RestoreSystemUi();
        Topmost = false;
        SetSecondaryCoversVisible(false);
        Close();
    }

    private void StartLocalSystemCommand(string arguments, string title, string message)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            RestoreSystemUi();
            ShowNotice(title, message, NoticeKind.Warning);
        }
        catch (Exception ex)
        {
            ShowNotice("Power command failed", ex.Message, NoticeKind.Error);
        }
    }

    private void YieldDisplaysToApp(Process? process)
    {
        if (process is not null && !_managedApps.Any(app => app.Process.Id == process.Id))
        {
            AddManagedApp(process.ProcessName, process);
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
        PruneExitedManagedApps();
        ManagedAppsList.ItemsSource = null;
        ManagedAppsList.ItemsSource = _managedApps.OrderBy(app => app.StartedAt).ToList();
        OpenAppsCountText.Text = _managedApps.Count.ToString();
    }

    private void AddManagedApp(string displayName, Process process)
    {
        if (_managedApps.Any(app => app.Process.Id == process.Id))
        {
            return;
        }

        _managedApps.Add(new ManagedApp(displayName, process));
        RefreshManagedTaskbar();
    }

    private void TrackLauncherProcess(KioskLauncher launcher, Process? startedProcess)
    {
        if (startedProcess is not null)
        {
            try
            {
                startedProcess.Refresh();
                if (!startedProcess.HasExited)
                {
                    AddManagedApp(launcher.DisplayName, startedProcess);
                    return;
                }
            }
            catch
            {
                // Some launchers hand off to a child process; fall through and search by policy.
            }
        }

        var processName = Path.GetFileNameWithoutExtension(launcher.ProcessName);
        if (string.IsNullOrWhiteSpace(processName) && !string.IsNullOrWhiteSpace(launcher.Path))
        {
            processName = Path.GetFileNameWithoutExtension(launcher.Path);
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            return;
        }

        var process = Process.GetProcessesByName(processName)
            .Where(candidate =>
            {
                try
                {
                    return !candidate.HasExited;
                }
                catch
                {
                    return false;
                }
            })
            .OrderByDescending(GetProcessStartTimeSafe)
            .FirstOrDefault();

        if (process is not null)
        {
            AddManagedApp(launcher.DisplayName, process);
        }
    }

    private static DateTime GetProcessStartTimeSafe(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private void PruneExitedManagedApps()
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
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            return;
        }

        try
        {
            _keyboardProc = KeyboardHookCallback;
            using var currentProcess = Process.GetCurrentProcess();
            using var currentModule = currentProcess.MainModule;
            _keyboardHook = SetWindowsHookEx(
                WhKeyboardLl,
                _keyboardProc,
                currentModule is null ? IntPtr.Zero : GetModuleHandle(currentModule.ModuleName),
                0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void RestoreSystemUi()
    {
        _suppressSystemShortcuts = false;
        SetTaskbarVisible(true);
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }

    private void SetSystemLockdownActive(bool active)
    {
        _suppressSystemShortcuts = active;
        SetTaskbarVisible(!active);
    }

    private void SetTaskbarVisible(bool visible)
    {
        if (visible)
        {
            RestoreTaskbarAutoHideState();
        }
        else
        {
            SetTaskbarAutoHide(true);
        }

        if (_taskbarHidden == !visible)
        {
            return;
        }

        ShowTaskbarWindow("Shell_TrayWnd", visible);
        ShowTaskbarWindow("Shell_SecondaryTrayWnd", visible);
        if (visible)
        {
            EnsureExplorerRunning();
        }
        _taskbarHidden = !visible;
    }

    private void SetTaskbarAutoHide(bool enabled)
    {
        try
        {
            var data = CreateAppBarData();
            var currentState = unchecked((int)SHAppBarMessage(AbmGetState, ref data).ToUInt32());
            _originalTaskbarState ??= currentState;

            var nextState = enabled
                ? currentState | AbsAutoHide
                : currentState & ~AbsAutoHide;

            if (nextState == currentState)
            {
                return;
            }

            data = CreateAppBarData();
            data.LParam = new IntPtr(nextState);
            SHAppBarMessage(AbmSetState, ref data);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void RestoreTaskbarAutoHideState()
    {
        if (_originalTaskbarState is not { } originalState)
        {
            return;
        }

        try
        {
            var data = CreateAppBarData();
            data.LParam = new IntPtr(originalState);
            SHAppBarMessage(AbmSetState, ref data);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _originalTaskbarState = null;
        }
    }

    private static AppBarData CreateAppBarData()
    {
        return new AppBarData
        {
            CbSize = Marshal.SizeOf<AppBarData>()
        };
    }

    private static void EnsureExplorerRunning()
    {
        try
        {
            if (!Process.GetProcessesByName("explorer").Any())
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
        }
        catch
        {
            // Explorer restoration is best-effort during recovery/admin handoff.
        }
    }

    private static void ShowTaskbarWindow(string className, bool visible)
    {
        var handle = FindWindow(className, null);
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, visible ? SwShow : SwHide);
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        var message = wParam.ToInt32();
        if (nCode >= 0
            && _suppressSystemShortcuts
            && (message == WmKeyDown || message == WmKeyUp || message == WmSysKeyDown || message == WmSysKeyUp))
        {
            var data = Marshal.PtrToStructure<KeyboardHookStruct>(lParam);
            if (ShouldSuppressGlobalKey(data))
            {
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static bool ShouldSuppressGlobalKey(KeyboardHookStruct data)
    {
        var vkCode = data.VkCode;
        var alt = (data.Flags & LlkhfAltdown) == LlkhfAltdown;
        var ctrl = IsKeyDown(VkControl);
        var shift = IsKeyDown(VkShift);
        var win = vkCode is VkLWin or VkRWin || IsKeyDown(VkLWin) || IsKeyDown(VkRWin);

        return win
            || vkCode is VkLWin or VkRWin
            || (alt && (vkCode is VkTab or VkF4 or VkEscape or VkSpace))
            || (ctrl && vkCode == VkEscape)
            || (ctrl && shift && vkCode == VkEscape);
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref AppBarData pData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookStruct
    {
        public int VkCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int CbSize;
        public IntPtr HWnd;
        public uint UCallbackMessage;
        public uint UEdge;
        public RectData Rc;
        public IntPtr LParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectData
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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

    private static async Task<string> ReadApiMessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? "";
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
