using System.Net.Http;
using System.Net.Http.Json;
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
    private async void ExamTemplate_Click(object sender, RoutedEventArgs e) => await RunAdminActionAsync("/api/templates/exam-mode", HttpMethod.Post, "{}");
    private async void LabTemplate_Click(object sender, RoutedEventArgs e) => await RunAdminActionAsync("/api/templates/lab-lockdown", HttpMethod.Post, "{}");

    private async void SaveProtectionMode_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing protection mode.", NoticeKind.Info);
            return;
        }

        _currentPolicy.Enforcement.Enabled = EnableEnforcementCheckBox.IsChecked == true;
        _currentPolicy.Enforcement.StrictApplicationWhitelist = StrictWhitelistCheckBox.IsChecked == true;
        await SaveCurrentPolicyAsync("Protection mode saved.");
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
                : $"{state.PolicyName}: enforcement {(state.EnforcementEnabled ? "ON" : "OFF")}"
                    + (state.TemporaryUnlockActive ? $"{Environment.NewLine}Unlocked until {state.TemporaryUnlockUntil:yyyy-MM-dd HH:mm:ss zzz}" : "");
            SafeTestBanner.Visibility = state?.EnforcementEnabled == false ? Visibility.Visible : Visibility.Collapsed;
            await RefreshRemoteStatusAsync();

            var policy = await GetAdminJsonAsync<KioskPolicy>("/api/policy");
            var logs = await GetAdminJsonAsync<List<LogEntry>>("/api/logs?count=300") ?? [];

            _currentPolicy = policy;
            EnableEnforcementCheckBox.IsChecked = policy?.Enforcement.Enabled == true;
            StrictWhitelistCheckBox.IsChecked = policy?.Enforcement.StrictApplicationWhitelist == true;
            BrowserEnabledCheckBox.IsChecked = policy?.Browser.Enabled == true;
            WhitelistOnlyCheckBox.IsChecked = policy?.Browser.WhitelistOnly == true;
            BrowserBlockDownloadsCheckBox.IsChecked = policy?.Browser.BlockDownloads == true;
            BindRemoteSettings(policy);
            PolicyTextBox.Text = JsonSerializer.Serialize(policy, JsonOptions);
            BindAppRules();
            BindWebsiteRules();
            LogsGrid.ItemsSource = logs.OrderByDescending(log => log.Timestamp).ToList();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            StatusText.Text += $"{Environment.NewLine}Enter the admin PIN and refresh to view or change policy.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not reach OTM Kiosk Service at localhost:47821.{Environment.NewLine}{ex.Message}";
        }
    }

    private async Task RefreshRemoteStatusAsync()
    {
        try
        {
            var device = await _client.GetFromJsonAsync<RemoteDeviceStatus>("/api/device", JsonOptions);
            RemoteStatusText.Text = device is null
                ? "Remote foundation unavailable."
                : $"{(string.IsNullOrWhiteSpace(device.ConfiguredName) ? device.DeviceName : device.ConfiguredName)}{Environment.NewLine}Device ID: {device.DeviceId}{Environment.NewLine}LAN access: {(device.LanApiEnabled ? "enabled" : "local-only for now")}";
        }
        catch
        {
            RemoteStatusText.Text = "Remote foundation unavailable until the service is running.";
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

    private async void GeneratePairingCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PinBox.Password))
            {
                ShowNotice("Admin PIN required", "Enter the admin PIN before generating a pairing code.", NoticeKind.Info);
                return;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/device/pairing-code")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                ShowNotice("Pairing blocked", await ReadApiErrorAsync(response), NoticeKind.Warning);
                return;
            }

            var pairing = await response.Content.ReadFromJsonAsync<PairingCodeResponse>(JsonOptions);
            PairingCodeBox.Text = pairing?.Code ?? "";
            ShowNotice("Pairing code created", $"Code expires at {pairing?.ExpiresAt:yyyy-MM-dd HH:mm:ss zzz}. LAN access is still local-only until remote manager is enabled.", NoticeKind.Success);
            await RefreshRemoteStatusAsync();
        }
        catch (Exception ex)
        {
            ShowNotice("Pairing failed", ex.Message, NoticeKind.Error);
        }
    }

    private async void SaveRemoteSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPolicy is null)
        {
            ShowNotice("Refresh required", "Enter the admin PIN and refresh before changing remote settings.", NoticeKind.Info);
            return;
        }

        _currentPolicy.Remote.Enabled = RemoteEnabledCheckBox.IsChecked == true;
        _currentPolicy.Remote.ServerUrl = RemoteServerUrlBox.Text.Trim();
        _currentPolicy.Remote.OrganizationId = RemoteOrganizationBox.Text.Trim();
        _currentPolicy.Remote.DeviceAlias = RemoteDeviceAliasBox.Text.Trim();
        _currentPolicy.Remote.AllowRemotePolicyPush = AllowRemotePolicyPushCheckBox.IsChecked == true;
        _currentPolicy.Remote.AllowRemoteUnlock = AllowRemoteUnlockCheckBox.IsChecked == true;
        _currentPolicy.Remote.AllowRemoteUpdate = AllowRemoteUpdateCheckBox.IsChecked == true;
        _currentPolicy.Updates.Enabled = UpdateEnabledCheckBox.IsChecked == true;
        _currentPolicy.Updates.ManifestUrl = UpdateManifestUrlBox.Text.Trim();
        _currentPolicy.Updates.Channel = string.IsNullOrWhiteSpace(UpdateChannelBox.Text) ? "stable" : UpdateChannelBox.Text.Trim();

        await SaveCurrentPolicyAsync("Remote and update settings saved.");
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(PinBox.Password))
            {
                ShowNotice("Admin PIN required", "Enter the admin PIN before checking for updates.", NoticeKind.Info);
                return;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/updates/check")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                ShowNotice("Update check failed", await ReadApiErrorAsync(response), NoticeKind.Warning);
                return;
            }

            var result = await response.Content.ReadFromJsonAsync<UpdateCheckResponse>(JsonOptions);
            UpdateStatusText.Text = result?.Message ?? "Update check completed.";
            ShowNotice("Update check completed", UpdateStatusText.Text, result?.Available == true ? NoticeKind.Success : NoticeKind.Info);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowNotice("Update check failed", ex.Message, NoticeKind.Error);
        }
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
            await SaveCurrentPolicyAsync("Allowed website saved.");
        }
        else
        {
            UpsertSite(_currentPolicy.Browser.BlockedSites, site);
            RemoveSite(_currentPolicy.Browser.AllowedSites, site);
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

    private async Task SaveCurrentPolicyAsync(string successMessage)
    {
        if (_currentPolicy is null)
        {
            return;
        }

        if (await RunAdminActionAsync("/api/policy", HttpMethod.Put, JsonSerializer.Serialize(_currentPolicy, JsonOptions)))
        {
            ClearAppFields();
            ShowNotice("Saved", successMessage, NoticeKind.Success);
        }
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

    private void BindRemoteSettings(KioskPolicy? policy)
    {
        RemoteEnabledCheckBox.IsChecked = policy?.Remote.Enabled == true;
        RemoteServerUrlBox.Text = policy?.Remote.ServerUrl ?? "";
        RemoteOrganizationBox.Text = policy?.Remote.OrganizationId ?? "";
        RemoteDeviceAliasBox.Text = policy?.Remote.DeviceAlias ?? "";
        AllowRemotePolicyPushCheckBox.IsChecked = policy?.Remote.AllowRemotePolicyPush == true;
        AllowRemoteUnlockCheckBox.IsChecked = policy?.Remote.AllowRemoteUnlock == true;
        AllowRemoteUpdateCheckBox.IsChecked = policy?.Remote.AllowRemoteUpdate == true;
        UpdateEnabledCheckBox.IsChecked = policy?.Updates.Enabled == true;
        UpdateManifestUrlBox.Text = policy?.Updates.ManifestUrl ?? "";
        UpdateChannelBox.Text = string.IsNullOrWhiteSpace(policy?.Updates.Channel) ? "stable" : policy.Updates.Channel;
        UpdateStatusText.Text = policy?.Updates.LastCheckMessage ?? "";
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

    private sealed class RemoteDeviceStatus
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string ConfiguredName { get; set; } = "";
        public bool PairingEnabled { get; set; }
        public bool LanApiEnabled { get; set; }
        public string LocalManagerUrl { get; set; } = "";
    }

    private sealed class PairingCodeResponse
    {
        public string Code { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed class UpdateCheckResponse
    {
        public bool Available { get; set; }
        public string Message { get; set; } = "";
    }
}
