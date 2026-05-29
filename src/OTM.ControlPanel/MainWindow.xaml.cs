using System.Net.Http;
using System.Net.Http.Json;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Otm.Kiosk.Shared.Models;

namespace Otm.Kiosk.ControlPanel;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _client = new() { BaseAddress = new Uri("http://localhost:47821") };
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private KioskPolicy? _currentPolicy;
    private bool _updateNoticeShown;
    private bool _startupUpdateChecked;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
        _noticeTimer.Tick += (_, _) =>
        {
            NoticePanel.Visibility = Visibility.Collapsed;
            _noticeTimer.Stop();
        };
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Unlock_Click(object sender, RoutedEventArgs e) => await RunAdminActionAsync("/api/unlock", HttpMethod.Post, "{\"minutes\":15}");
    private async void Lock_Click(object sender, RoutedEventArgs e) => await RunAdminActionAsync("/api/lock", HttpMethod.Post, "{}");
    private async void SaveProtectionMode_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing protection mode.", NoticeKind.Info);
            return;
        }

        _currentPolicy.Enforcement.Enabled = EnableEnforcementCheckBox.IsChecked == true;
        _currentPolicy.Enforcement.StrictApplicationWhitelist = StrictWhitelistCheckBox.IsChecked == true;
        await SaveCurrentPolicyAsync("Workspace mode saved.");
    }

    private async void SavePolicy_Click(object sender, RoutedEventArgs e)
    {
        await RunAdminActionAsync("/api/policy", HttpMethod.Put, PolicyTextBox.Text);
    }

    private async void ChangePin_Click(object sender, RoutedEventArgs e)
    {
        var newPin = NewPinBox.Password;
        if (newPin.Length < 6)
        {
            ShowNotice("PIN too short", "PIN must be at least 6 characters.", NoticeKind.Warning);
            return;
        }

        var body = JsonSerializer.Serialize(new { newPassword = newPin });
        await RunAdminActionAsync("/api/admin/password", HttpMethod.Post, body);
        NewPinBox.Clear();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var state = await _client.GetFromJsonAsync<RuntimeState>("/api/status", JsonOptions);
            StatusText.Text = state is null
                ? "Service status unavailable."
                : $"{state.PolicyName}: managed mode {(state.EnforcementEnabled ? "ON" : "OFF")}"
                    + (state.TemporaryUnlockActive ? $"{Environment.NewLine}Unlocked until {state.TemporaryUnlockUntil:yyyy-MM-dd HH:mm:ss zzz}" : "");
            SafeTestBanner.Visibility = state?.EnforcementEnabled == false ? Visibility.Visible : Visibility.Collapsed;

            var policy = await GetAdminJsonAsync<KioskPolicy>("/api/policy");
            var logs = await GetAdminJsonAsync<List<LogEntry>>("/api/logs?count=300") ?? [];
            if (policy is not null)
            {
                policy.Updates ??= new UpdatePolicy();
            }

            _currentPolicy = policy;
            EnableEnforcementCheckBox.IsChecked = policy?.Enforcement.Enabled == true;
            StrictWhitelistCheckBox.IsChecked = policy?.Enforcement.StrictApplicationWhitelist == true;
            BrowserEnabledCheckBox.IsChecked = policy?.Browser.Enabled == true;
            WhitelistOnlyCheckBox.IsChecked = policy?.Browser.WhitelistOnly == true;
            BrowserBlockDownloadsCheckBox.IsChecked = policy?.Browser.BlockDownloads == true;
            BindUpdateSettings(policy);
            PolicyTextBox.Text = JsonSerializer.Serialize(policy, JsonOptions);
            BindAppRules();
            BindWebsiteRules();
            BindDedicatedKiosk();
            LogsGrid.ItemsSource = logs.OrderByDescending(log => log.Timestamp).ToList();
            ShowUpdateNoticeIfNeeded(policy);
            _ = CheckForStartupUpdateAsync(policy);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            StatusText.Text += $"{Environment.NewLine}Enter the admin PIN and refresh to view or change policy.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not reach the SimpleKioskOS service at localhost:47821.{Environment.NewLine}{ex.Message}";
        }
    }

    private void BrowseApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose app EXE",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        AppPathBox.Text = dialog.FileName;
        AppProcessNameBox.Text = System.IO.Path.GetFileName(dialog.FileName);
        if (string.IsNullOrWhiteSpace(AppDisplayNameBox.Text))
        {
            AppDisplayNameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private async void AllowApp_Click(object sender, RoutedEventArgs e)
    {
        await AddAppRuleAsync(allow: true);
    }

    private async void BlockApp_Click(object sender, RoutedEventArgs e)
    {
        await AddAppRuleAsync(allow: false);
    }

    private async void RemoveAllowedApp_Click(object sender, RoutedEventArgs e)
    {
        if (AllowedAppsGrid.SelectedItem is AppRule rule)
        {
            await RemoveAppRuleAsync(_currentPolicy?.AllowedApps, rule, "Allowed app removed.");
        }
    }

    private async void RemoveBlockedApp_Click(object sender, RoutedEventArgs e)
    {
        if (BlockedAppsGrid.SelectedItem is AppRule rule)
        {
            await RemoveAppRuleAsync(_currentPolicy?.BlockedApps, rule, "Blocked app removed.");
        }
    }

    private async void AllowWebsite_Click(object sender, RoutedEventArgs e)
    {
        await AddWebsiteRuleAsync(allow: true);
    }

    private async void BlockWebsite_Click(object sender, RoutedEventArgs e)
    {
        await AddWebsiteRuleAsync(allow: false);
    }

    private async void RemoveAllowedWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (AllowedSitesList.SelectedItem is string site)
        {
            await RemoveWebsiteRuleAsync(_currentPolicy?.Browser.AllowedSites, site, "Allowed website removed.");
        }
    }

    private async void RemoveBlockedWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (BlockedSitesList.SelectedItem is string site)
        {
            await RemoveWebsiteRuleAsync(_currentPolicy?.Browser.BlockedSites, site, "Blocked website removed.");
        }
    }

    private async void SaveBrowserMode_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing website mode.", NoticeKind.Info);
            return;
        }

        _currentPolicy.Browser.Enabled = BrowserEnabledCheckBox.IsChecked == true;
        _currentPolicy.Browser.WhitelistOnly = WhitelistOnlyCheckBox.IsChecked == true;
        _currentPolicy.Browser.BlockDownloads = BrowserBlockDownloadsCheckBox.IsChecked == true;
        await SaveCurrentPolicyAsync("Website mode saved.");
    }

    private async void SaveDedicatedKiosk_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing kiosk mode.", NoticeKind.Info);
            return;
        }

        var type = GetDedicatedKioskType();
        _currentPolicy.DedicatedKiosk ??= new DedicatedKioskPolicy();
        _currentPolicy.DedicatedKiosk.Enabled = DedicatedKioskEnabledCheckBox.IsChecked == true;
        _currentPolicy.DedicatedKiosk.Type = type;
        _currentPolicy.DedicatedKiosk.DisplayName = string.IsNullOrWhiteSpace(DedicatedKioskNameBox.Text) ? "Kiosk" : DedicatedKioskNameBox.Text.Trim();
        _currentPolicy.DedicatedKiosk.Url = string.IsNullOrWhiteSpace(DedicatedKioskUrlBox.Text) ? null : ToWorkspaceUrl(DedicatedKioskUrlBox.Text);
        _currentPolicy.DedicatedKiosk.ProcessName = DedicatedKioskProcessBox.Text.Trim();
        _currentPolicy.DedicatedKiosk.Path = string.IsNullOrWhiteSpace(DedicatedKioskPathBox.Text) ? null : DedicatedKioskPathBox.Text.Trim();
        _currentPolicy.DedicatedKiosk.Arguments = string.IsNullOrWhiteSpace(DedicatedKioskArgumentsBox.Text) ? null : DedicatedKioskArgumentsBox.Text.Trim();

        if (type == KioskLauncherTypes.Web)
        {
            if (string.IsNullOrWhiteSpace(_currentPolicy.DedicatedKiosk.Url))
            {
                ShowNotice("Website required", "Enter the website URL for dedicated website kiosk mode.", NoticeKind.Warning);
                return;
            }

            var site = NormalizeSite(_currentPolicy.DedicatedKiosk.Url);
            UpsertSite(_currentPolicy.Browser.AllowedSites, site);
            _currentPolicy.Browser.Enabled = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_currentPolicy.DedicatedKiosk.ProcessName) && string.IsNullOrWhiteSpace(_currentPolicy.DedicatedKiosk.Path))
            {
                ShowNotice("App required", "Enter an app process name or EXE path for dedicated app kiosk mode.", NoticeKind.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentPolicy.DedicatedKiosk.ProcessName) && !string.IsNullOrWhiteSpace(_currentPolicy.DedicatedKiosk.Path))
            {
                _currentPolicy.DedicatedKiosk.ProcessName = System.IO.Path.GetFileName(_currentPolicy.DedicatedKiosk.Path);
            }

            UpsertRule(_currentPolicy.AllowedApps, new AppRule
            {
                DisplayName = _currentPolicy.DedicatedKiosk.DisplayName,
                ProcessName = _currentPolicy.DedicatedKiosk.ProcessName,
                Path = _currentPolicy.DedicatedKiosk.Path,
                Arguments = _currentPolicy.DedicatedKiosk.Arguments
            });
        }

        _currentPolicy.Enforcement.Enabled = true;
        await SaveCurrentPolicyAsync("Dedicated kiosk saved.");
    }

    private async void DisableDedicatedKiosk_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing kiosk mode.", NoticeKind.Info);
            return;
        }

        _currentPolicy.DedicatedKiosk ??= new DedicatedKioskPolicy();
        _currentPolicy.DedicatedKiosk.Enabled = false;
        await SaveCurrentPolicyAsync("Dedicated kiosk disabled.");
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before importing a profile.", NoticeKind.Info);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import SimpleKioskOS profile",
            Filter = "SimpleKioskOS profile (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var imported = JsonSerializer.Deserialize<KioskPolicy>(System.IO.File.ReadAllText(dialog.FileName), JsonOptions);
            if (imported is null)
            {
                ShowNotice("Import failed", "The selected file is not a valid SimpleKioskOS policy.", NoticeKind.Warning);
                return;
            }

            imported.Admin = _currentPolicy.Admin;
            imported.PolicyId = string.IsNullOrWhiteSpace(imported.PolicyId) ? Guid.NewGuid().ToString("N") : imported.PolicyId;
            imported.UpdatedAt = DateTimeOffset.UtcNow;
            _currentPolicy = imported;
            if (await RunAdminActionAsync("/api/policy", HttpMethod.Put, JsonSerializer.Serialize(imported, JsonOptions)))
            {
                ShowNotice("Profile imported", "Imported profile saved. This PC's admin PIN and recovery key were kept.", NoticeKind.Success);
            }
        }
        catch (Exception ex)
        {
            ShowNotice("Import failed", ex.Message, NoticeKind.Error);
        }
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before exporting a profile.", NoticeKind.Info);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export SimpleKioskOS profile",
            Filter = "SimpleKioskOS profile (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"{CreateLauncherId(_currentPolicy.Name)}.json",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await System.IO.File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(_currentPolicy, JsonOptions));
            ShowNotice("Profile exported", dialog.FileName, NoticeKind.Success);
        }
        catch (Exception ex)
        {
            ShowNotice("Export failed", ex.Message, NoticeKind.Error);
        }
    }

    private async void ApplyBrowserPolicy_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPolicy is not null)
        {
            _currentPolicy.Browser.Enabled = BrowserEnabledCheckBox.IsChecked == true;
            _currentPolicy.Browser.WhitelistOnly = WhitelistOnlyCheckBox.IsChecked == true;
            _currentPolicy.Browser.BlockDownloads = BrowserBlockDownloadsCheckBox.IsChecked == true;
            if (!await SaveCurrentPolicyAsync("Website mode saved before applying browser policy."))
            {
                return;
            }
        }

        if (await RunAdminActionAsync("/api/browser/apply-policy", HttpMethod.Post, "{}"))
        {
            ShowNotice("Browser policy applied", "Edge/Chrome policy was updated. Restart browsers for changes to apply.", NoticeKind.Success);
        }
    }

    private async void SaveUpdateSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing update settings.", NoticeKind.Info);
            return;
        }

        _currentPolicy.Updates ??= new UpdatePolicy();
        _currentPolicy.Updates.Enabled = UpdateEnabledCheckBox.IsChecked == true;
        _currentPolicy.Updates.ManifestUrl = UpdateManifestUrlBox.Text.Trim();
        _currentPolicy.Updates.Channel = string.IsNullOrWhiteSpace(UpdateChannelBox.Text) ? "stable" : UpdateChannelBox.Text.Trim();
        await SaveCurrentPolicyAsync("Update settings saved.");
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentPolicy is not null)
            {
                _currentPolicy.Updates ??= new UpdatePolicy();
                _currentPolicy.Updates.Enabled = UpdateEnabledCheckBox.IsChecked == true;
                _currentPolicy.Updates.ManifestUrl = UpdateManifestUrlBox.Text.Trim();
                _currentPolicy.Updates.Channel = string.IsNullOrWhiteSpace(UpdateChannelBox.Text) ? "stable" : UpdateChannelBox.Text.Trim();
                if (!await SaveCurrentPolicyAsync("Update settings saved before checking."))
                {
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(PinBox.Password))
            {
                ShowNotice("Admin PIN required", "Enter the admin PIN before checking for updates.", NoticeKind.Info);
                return;
            }

            var result = await SendUpdateCommandAsync("/api/updates/check", "Update check failed");
            if (result is null)
            {
                return;
            }

            UpdateStatusText.Text = result?.Message ?? "Update check completed.";
            ShowNotice("Update check completed", UpdateStatusText.Text, result?.Available == true ? NoticeKind.Success : NoticeKind.Info);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowNotice("Update check failed", ex.Message, NoticeKind.Error);
        }
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        var result = await SendUpdateCommandAsync("/api/updates/download", "Update download failed");
        if (result is null)
        {
            return;
        }

        UpdateStatusText.Text = result.Message;
        ShowNotice(result.Downloaded ? "Update downloaded" : "Update not downloaded", result.Message, result.Downloaded ? NoticeKind.Success : NoticeKind.Info);
        await RefreshAsync();
    }

    private void OpenUpdateFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OTM Kiosk", "Updates");
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowNotice("Could not open update folder", ex.Message, NoticeKind.Error);
        }
    }

    private async Task<UpdateOperationResponse?> SendUpdateCommandAsync(string path, string failureTitle)
    {
        if (string.IsNullOrWhiteSpace(PinBox.Password))
        {
            ShowNotice("Admin PIN required", "Enter the admin PIN before using updates.", NoticeKind.Info);
            return null;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
        var response = await _client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            ShowNotice(failureTitle, await ReadApiErrorAsync(response), NoticeKind.Warning);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<UpdateOperationResponse>(JsonOptions);
    }

    private async Task AddAppRuleAsync(bool allow)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing app rules.", NoticeKind.Info);
            return;
        }

        var rule = BuildAppRuleFromFields();
        if (rule is null)
        {
            ShowNotice("App details required", "Enter a process name or choose an EXE path.", NoticeKind.Warning);
            return;
        }

        if (allow)
        {
            UpsertRule(_currentPolicy.AllowedApps, rule);
            RemoveMatchingRule(_currentPolicy.BlockedApps, rule);

            if (AddLauncherCheckBox.IsChecked == true)
            {
                UpsertLauncher(rule);
            }

            await SaveCurrentPolicyAsync("Allowed app saved.");
        }
        else
        {
            UpsertRule(_currentPolicy.BlockedApps, rule);
            RemoveMatchingRule(_currentPolicy.AllowedApps, rule);
            RemoveMatchingLauncher(rule);
            await SaveCurrentPolicyAsync("Blocked app saved.");
        }
    }

    private async Task AddWebsiteRuleAsync(bool allow)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing website rules.", NoticeKind.Info);
            return;
        }

        var site = NormalizeSite(WebsiteBox.Text);
        if (string.IsNullOrWhiteSpace(site))
        {
            ShowNotice("Website required", "Enter a domain or URL like testing.example.edu.", NoticeKind.Warning);
            return;
        }

        if (allow)
        {
            UpsertSite(_currentPolicy.Browser.AllowedSites, site);
            RemoveSite(_currentPolicy.Browser.BlockedSites, site);
            if (AddWebsiteLauncherCheckBox.IsChecked == true)
            {
                UpsertWebLauncher(site);
            }
            await SaveCurrentPolicyAsync("Allowed website saved.");
        }
        else
        {
            UpsertSite(_currentPolicy.Browser.BlockedSites, site);
            RemoveSite(_currentPolicy.Browser.AllowedSites, site);
            RemoveMatchingWebLauncher(site);
            await SaveCurrentPolicyAsync("Blocked website saved.");
        }

        WebsiteBox.Clear();
    }

    private async Task RemoveWebsiteRuleAsync(List<string>? sites, string site, string successMessage)
    {
        if (_currentPolicy is null || sites is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing website rules.", NoticeKind.Info);
            return;
        }

        RemoveSite(sites, site);
        await SaveCurrentPolicyAsync(successMessage);
    }

    private AppRule? BuildAppRuleFromFields()
    {
        var path = string.IsNullOrWhiteSpace(AppPathBox.Text) ? null : AppPathBox.Text.Trim();
        var processName = AppProcessNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName) && !string.IsNullOrWhiteSpace(path))
        {
            processName = System.IO.Path.GetFileName(path);
        }

        if (string.IsNullOrWhiteSpace(processName) && string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var displayName = AppDisplayNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = !string.IsNullOrWhiteSpace(path)
                ? System.IO.Path.GetFileNameWithoutExtension(path)
                : System.IO.Path.GetFileNameWithoutExtension(processName);
        }

        return new AppRule
        {
            DisplayName = displayName,
            ProcessName = processName,
            Path = path
        };
    }

    private async Task RemoveAppRuleAsync(List<AppRule>? rules, AppRule rule, string successMessage)
    {
        if (_currentPolicy is null || rules is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing app rules.", NoticeKind.Info);
            return;
        }

        RemoveMatchingRule(rules, rule);
        RemoveMatchingLauncher(rule);
        await SaveCurrentPolicyAsync(successMessage);
    }

    private async Task<bool> SaveCurrentPolicyAsync(string successMessage)
    {
        if (_currentPolicy is null)
        {
            return false;
        }

        if (await RunAdminActionAsync("/api/policy", HttpMethod.Put, JsonSerializer.Serialize(_currentPolicy, JsonOptions)))
        {
            ClearAppFields();
            ShowNotice("Saved", successMessage, NoticeKind.Success);
            return true;
        }

        return false;
    }

    private void BindAppRules()
    {
        AllowedAppsGrid.ItemsSource = _currentPolicy?.AllowedApps.OrderBy(app => app.DisplayName).ToList() ?? [];
        BlockedAppsGrid.ItemsSource = _currentPolicy?.BlockedApps.OrderBy(app => app.DisplayName).ToList() ?? [];
    }

    private void BindWebsiteRules()
    {
        AllowedSitesList.ItemsSource = _currentPolicy?.Browser.AllowedSites.OrderBy(site => site).ToList() ?? [];
        BlockedSitesList.ItemsSource = _currentPolicy?.Browser.BlockedSites.OrderBy(site => site).ToList() ?? [];
    }

    private void BindDedicatedKiosk()
    {
        var kiosk = _currentPolicy?.DedicatedKiosk ?? new DedicatedKioskPolicy();
        DedicatedKioskEnabledCheckBox.IsChecked = kiosk.Enabled;
        DedicatedKioskTypeBox.SelectedIndex = string.Equals(kiosk.Type, KioskLauncherTypes.App, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        DedicatedKioskNameBox.Text = kiosk.DisplayName;
        DedicatedKioskUrlBox.Text = kiosk.Url ?? "";
        DedicatedKioskProcessBox.Text = kiosk.ProcessName;
        DedicatedKioskPathBox.Text = kiosk.Path ?? "";
        DedicatedKioskArgumentsBox.Text = kiosk.Arguments ?? "";
    }

    private void BindUpdateSettings(KioskPolicy? policy)
    {
        var updates = policy?.Updates ?? new UpdatePolicy();
        UpdateEnabledCheckBox.IsChecked = updates.Enabled;
        UpdateManifestUrlBox.Text = string.IsNullOrWhiteSpace(updates.ManifestUrl)
            ? new UpdatePolicy().ManifestUrl
            : updates.ManifestUrl;
        UpdateChannelBox.Text = string.IsNullOrWhiteSpace(updates.Channel) ? "stable" : updates.Channel;
        var lines = new[]
        {
            updates.LastCheckMessage,
            updates.LastDownloadMessage,
            string.IsNullOrWhiteSpace(updates.LastDownloadedPath) ? null : $"Ready installer: {updates.LastDownloadedPath}"
        };
        UpdateStatusText.Text = string.Join(Environment.NewLine, lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private void ShowUpdateNoticeIfNeeded(KioskPolicy? policy)
    {
        var updates = policy?.Updates;
        if (_updateNoticeShown || updates is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(updates.LastAvailableVersion)
            && !string.Equals(updates.LastAvailableVersion, updates.LastDownloadedVersion, StringComparison.OrdinalIgnoreCase))
        {
            _updateNoticeShown = true;
            ShowNotice("Stable update available", $"Version {updates.LastAvailableVersion} is available. Use Download Update when ready.", NoticeKind.Info);
        }
    }

    private async Task CheckForStartupUpdateAsync(KioskPolicy? policy)
    {
        if (_startupUpdateChecked || policy?.Updates.Enabled != true || string.IsNullOrWhiteSpace(PinBox.Password))
        {
            return;
        }

        _startupUpdateChecked = true;
        try
        {
            var result = await SendUpdateCommandAsync("/api/updates/check", "Startup update check failed");
            if (result?.Available == true)
            {
                UpdateStatusText.Text = result.Message;
                ShowNotice("Stable update available", result.Message, NoticeKind.Info);
            }
        }
        catch
        {
            // Startup checks should never block local management.
        }
    }

    private string GetDedicatedKioskType()
    {
        if (DedicatedKioskTypeBox.SelectedItem is System.Windows.Controls.ComboBoxItem item
            && item.Tag is string tag
            && string.Equals(tag, KioskLauncherTypes.App, StringComparison.OrdinalIgnoreCase))
        {
            return KioskLauncherTypes.App;
        }

        return KioskLauncherTypes.Web;
    }

    private void ClearAppFields()
    {
        AppDisplayNameBox.Clear();
        AppProcessNameBox.Clear();
        AppPathBox.Clear();
    }

    private void UpsertLauncher(AppRule rule)
    {
        if (_currentPolicy is null)
        {
            return;
        }

        RemoveMatchingLauncher(rule);
        _currentPolicy.Launchers.Add(new KioskLauncher
        {
            Id = CreateLauncherId(rule.DisplayName),
            DisplayName = rule.DisplayName,
            Type = KioskLauncherTypes.App,
            WorkspaceMode = KioskWorkspaceModes.Lab,
            ProcessName = rule.ProcessName,
            Path = rule.Path,
            Arguments = rule.Arguments
        });
    }

    private void RemoveMatchingLauncher(AppRule rule)
    {
        _currentPolicy?.Launchers.RemoveAll(launcher =>
            MatchesRule(launcher.ProcessName, launcher.Path, rule.ProcessName, rule.Path));
    }

    private void UpsertWebLauncher(string site)
    {
        if (_currentPolicy is null)
        {
            return;
        }

        RemoveMatchingWebLauncher(site);
        var url = ToWorkspaceUrl(site);
        _currentPolicy.Launchers.Add(new KioskLauncher
        {
            Id = CreateLauncherId(site),
            DisplayName = site,
            Type = KioskLauncherTypes.Web,
            WorkspaceMode = KioskWorkspaceModes.Exam,
            Url = url,
            AllowedSites = [site]
        });
    }

    private void RemoveMatchingWebLauncher(string site)
    {
        _currentPolicy?.Launchers.RemoveAll(launcher =>
            string.Equals(launcher.Type, KioskLauncherTypes.Web, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(NormalizeSite(launcher.Url ?? ""), NormalizeSite(site), StringComparison.OrdinalIgnoreCase)
                || launcher.AllowedSites.Any(allowed => string.Equals(NormalizeSite(allowed), NormalizeSite(site), StringComparison.OrdinalIgnoreCase))));
    }

    private static void UpsertRule(List<AppRule> rules, AppRule rule)
    {
        RemoveMatchingRule(rules, rule);
        rules.Add(rule);
    }

    private static void RemoveMatchingRule(List<AppRule> rules, AppRule rule)
    {
        rules.RemoveAll(existing => MatchesRule(existing.ProcessName, existing.Path, rule.ProcessName, rule.Path));
    }

    private static bool MatchesRule(string? processName, string? path, string? otherProcessName, string? otherPath)
    {
        if (!string.IsNullOrWhiteSpace(processName)
            && !string.IsNullOrWhiteSpace(otherProcessName)
            && string.Equals(System.IO.Path.GetFileName(processName), System.IO.Path.GetFileName(otherProcessName), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(path)
            && !string.IsNullOrWhiteSpace(otherPath)
            && string.Equals(System.IO.Path.GetFullPath(path), System.IO.Path.GetFullPath(otherPath), StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateLauncherId(string displayName)
    {
        var chars = displayName
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static string NormalizeSite(string value)
    {
        var site = value.Trim();
        if (string.IsNullOrWhiteSpace(site))
        {
            return "";
        }

        if (Uri.TryCreate(site, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            site = uri.Host + uri.AbsolutePath.TrimEnd('/');
        }

        site = site
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .TrimEnd('/');

        return site.ToLowerInvariant();
    }

    private static string ToWorkspaceUrl(string site)
    {
        var normalized = NormalizeSite(site);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "";
        }

        return normalized.Contains("://", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"https://{normalized}/";
    }

    private static void UpsertSite(List<string> sites, string site)
    {
        RemoveSite(sites, site);
        sites.Add(site);
    }

    private static void RemoveSite(List<string> sites, string site)
    {
        var normalized = NormalizeSite(site);
        sites.RemoveAll(existing => string.Equals(NormalizeSite(existing), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> RunAdminActionAsync(string url, HttpMethod method, string body)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PinBox.Password))
            {
                ShowNotice("Admin PIN required", "Enter the admin PIN before using admin actions.", NoticeKind.Info);
                return false;
            }

            var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);

            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var message = await ReadApiErrorAsync(response);
                ShowNotice("Admin action blocked", message, NoticeKind.Warning);
                return false;
            }

            await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            ShowNotice("Request failed", ex.Message, NoticeKind.Error);
            return false;
        }
    }

    private async Task<T?> GetAdminJsonAsync<T>(string url)
    {
        if (string.IsNullOrWhiteSpace(PinBox.Password))
        {
            throw new HttpRequestException("Admin PIN required.", null, System.Net.HttpStatusCode.Unauthorized);
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
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

    private void ShowNotice(string title, string message, NoticeKind kind)
    {
        NoticeTitle.Text = title;
        NoticeText.Text = message;
        NoticeAccent.Background = kind switch
        {
            NoticeKind.Success => new SolidColorBrush(Color.FromRgb(31, 138, 112)),
            NoticeKind.Warning => new SolidColorBrush(Color.FromRgb(191, 120, 38)),
            NoticeKind.Error => new SolidColorBrush(Color.FromRgb(159, 45, 45)),
            _ => new SolidColorBrush(Color.FromRgb(23, 107, 135))
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

    private sealed class UpdateOperationResponse
    {
        public bool Available { get; set; }
        public bool Downloaded { get; set; }
        public string Message { get; set; } = "";
    }

}
