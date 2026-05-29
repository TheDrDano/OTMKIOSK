using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;
using Otm.Kiosk.Shared.Models;

namespace Otm.Kiosk.Manager;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SimpleKioskOS",
        "manager-stations.json");

    private readonly ObservableCollection<ManagedStation> _stations = [];
    private KioskPolicy? _currentPolicy;

    public MainWindow()
    {
        InitializeComponent();
        StationsList.ItemsSource = _stations;
        LoadStations();
    }

    private ManagedStation? SelectedStation => StationsList.SelectedItem as ManagedStation;

    private void SaveStation_Click(object sender, RoutedEventArgs e)
    {
        var url = NormalizeUrl(StationUrlBox.Text);
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var stationUri))
        {
            Log("Enter a valid station API URL.");
            return;
        }

        var name = string.IsNullOrWhiteSpace(StationNameBox.Text)
            ? stationUri.Host
            : StationNameBox.Text.Trim();

        var existing = _stations.FirstOrDefault(station => string.Equals(station.Url, url, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new ManagedStation { Name = name, Url = url };
            _stations.Add(existing);
        }
        else
        {
            existing.Name = name;
            existing.Url = url;
            existing.Touch();
        }

        StationsList.SelectedItem = existing;
        SaveStations();
        Log($"Saved station: {existing.Name} ({existing.Url})");
    }

    private void RemoveStation_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStation is null)
        {
            return;
        }

        Log($"Removed station: {SelectedStation.Name}");
        _stations.Remove(SelectedStation);
        SaveStations();
    }

    private async void RefreshSelected_Click(object sender, RoutedEventArgs e) => await RefreshStationAsync(SelectedStation);
    private async void RefreshAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var station in _stations.ToList())
        {
            await RefreshStationAsync(station);
        }
    }

    private async void Lock_Click(object sender, RoutedEventArgs e) => await PostSelectedAsync("/api/lock", "{}");
    private async void Unlock_Click(object sender, RoutedEventArgs e) => await PostSelectedAsync("/api/unlock", JsonSerializer.Serialize(new { minutes = 15 }));
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await PostSelectedAsync("/api/updates/check", "{}");
    private async void DownloadStationUpdate_Click(object sender, RoutedEventArgs e) => await PostSelectedAsync("/api/updates/download", "{}");
    private async void Restart_Click(object sender, RoutedEventArgs e) => await PostSelectedAsync("/api/system/restart", "{}");
    private async void Shutdown_Click(object sender, RoutedEventArgs e) => await PostSelectedAsync("/api/system/shutdown", "{}");
    private async void LoadRules_Click(object sender, RoutedEventArgs e) => await LoadPolicyAsync();
    private async void LoadMonitoring_Click(object sender, RoutedEventArgs e) => await LoadMonitoringAsync();
    private async void SaveMonitoring_Click(object sender, RoutedEventArgs e) => await SaveMonitoringAsync();
    private async void AllowApp_Click(object sender, RoutedEventArgs e) => await AddAppRuleAsync(allow: true);
    private async void BlockApp_Click(object sender, RoutedEventArgs e) => await AddAppRuleAsync(allow: false);
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

    private async void AllowWebsite_Click(object sender, RoutedEventArgs e) => await AddWebsiteRuleAsync(allow: true);
    private async void BlockWebsite_Click(object sender, RoutedEventArgs e) => await AddWebsiteRuleAsync(allow: false);
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

    private async void SaveWebsiteMode_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsurePolicyLoadedAsync())
        {
            return;
        }

        _currentPolicy!.Browser.Enabled = true;
        _currentPolicy.Browser.WhitelistOnly = WhitelistOnlyCheckBox.IsChecked == true;
        await SavePolicyAsync("Website mode saved.");
    }

    private void StationsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SelectedStation is null)
        {
            SelectedStationText.Text = "No station selected";
            SelectedStatusText.Text = "Add or select a station to manage it.";
            return;
        }

        StationNameBox.Text = SelectedStation.Name;
        StationUrlBox.Text = SelectedStation.Url;
        SelectedStationText.Text = SelectedStation.Name;
        SelectedStatusText.Text = SelectedStation.LastStatus;
        _currentPolicy = null;
        BindPolicyRules();
    }

    private async Task RefreshStationAsync(ManagedStation? station)
    {
        if (station is null)
        {
            Log("Select a station first.");
            return;
        }

        try
        {
            using var client = CreateClient(station);
            var status = await client.GetFromJsonAsync<RuntimeStateDto>("/api/status", JsonOptions);
            var device = await client.GetFromJsonAsync<DeviceDto>("/api/device", JsonOptions);
            station.LastStatus = status is null
                ? "Status unavailable."
                : $"{status.PolicyName}: managed mode {(status.EnforcementEnabled ? "ON" : "OFF")}";
            if (device is not null)
            {
                station.Name = string.IsNullOrWhiteSpace(device.ConfiguredName) ? station.Name : device.ConfiguredName;
                station.LastStatus += $"{Environment.NewLine}Device: {device.DeviceName}";
            }

            if (!string.IsNullOrWhiteSpace(PinBox.Password))
            {
                var remote = await GetRemoteStatusAsync(client);
                if (remote is not null)
                {
                    if (!string.IsNullOrWhiteSpace(remote.LastAvailableVersion))
                    {
                        station.LastStatus += $"{Environment.NewLine}Update: {remote.LastAvailableVersion} available";
                    }

                    if (!string.IsNullOrWhiteSpace(remote.LastDownloadedVersion))
                    {
                        station.LastStatus += $"{Environment.NewLine}Downloaded: {remote.LastDownloadedVersion}";
                    }

                    if (!string.IsNullOrWhiteSpace(remote.LastDownloadMessage))
                    {
                        station.LastStatus += $"{Environment.NewLine}{remote.LastDownloadMessage}";
                    }
                }
            }

            station.Touch();
            SaveStations();
            UpdateSelectedStatus(station);
            Log($"Refreshed {station.Name}: {station.LastStatus.Replace(Environment.NewLine, " | ")}");
        }
        catch (Exception ex)
        {
            station.LastStatus = $"Offline or unreachable: {DescribeConnectionFailure(station, ex)}";
            station.Touch();
            UpdateSelectedStatus(station);
            Log($"{station.Name}: {station.LastStatus}");
        }
    }

    private async Task<RemoteStatusDto?> GetRemoteStatusAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/remote/status");
        request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
        var response = await client.SendAsync(request);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<RemoteStatusDto>(JsonOptions)
            : null;
    }

    private async Task PostSelectedAsync(string path, string body)
    {
        var station = SelectedStation;
        if (station is null)
        {
            Log("Select a station first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(PinBox.Password))
        {
            Log("Enter the admin PIN for the selected station.");
            return;
        }

        try
        {
            using var client = CreateClient(station);
            var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log($"{station.Name}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return;
            }

            var message = await ReadApiMessageAsync(response);
            Log($"{station.Name}: {(string.IsNullOrWhiteSpace(message) ? $"command sent to {path}" : message)}");
            await RefreshStationAsync(station);
        }
        catch (Exception ex)
        {
            Log($"{station.Name}: command failed: {DescribeConnectionFailure(station, ex)}");
        }
    }

    private async Task<bool> LoadPolicyAsync()
    {
        var station = SelectedStation;
        if (station is null)
        {
            Log("Select a station first.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(PinBox.Password))
        {
            Log("Enter the admin PIN before loading or changing rules.");
            return false;
        }

        try
        {
            using var client = CreateClient(station);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/policy");
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log($"{station.Name}: could not load policy: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return false;
            }

            _currentPolicy = await response.Content.ReadFromJsonAsync<KioskPolicy>(JsonOptions);
            BindPolicyRules();
            Log($"{station.Name}: rules loaded.");
            return true;
        }
        catch (Exception ex)
        {
            Log($"{station.Name}: could not load rules: {DescribeConnectionFailure(station, ex)}");
            return false;
        }
    }

    private async Task<bool> EnsurePolicyLoadedAsync()
    {
        return _currentPolicy is not null || await LoadPolicyAsync();
    }

    private async Task<bool> SavePolicyAsync(string successMessage)
    {
        var station = SelectedStation;
        if (station is null || _currentPolicy is null)
        {
            Log("Select a station and load rules first.");
            return false;
        }

        try
        {
            using var client = CreateClient(station);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/policy")
            {
                Content = JsonContent.Create(_currentPolicy, options: JsonOptions)
            };
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log($"{station.Name}: could not save policy: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return false;
            }

            _currentPolicy = await response.Content.ReadFromJsonAsync<KioskPolicy>(JsonOptions) ?? _currentPolicy;
            BindPolicyRules();
            Log($"{station.Name}: {successMessage}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"{station.Name}: could not save rules: {DescribeConnectionFailure(station, ex)}");
            return false;
        }
    }

    private async Task AddAppRuleAsync(bool allow)
    {
        if (!await EnsurePolicyLoadedAsync())
        {
            return;
        }

        var rule = BuildAppRule();
        if (rule is null)
        {
            return;
        }

        if (allow)
        {
            UpsertRule(_currentPolicy!.AllowedApps, rule);
            RemoveMatchingRule(_currentPolicy.BlockedApps, rule);
            UpsertLauncher(rule);
            await SavePolicyAsync("Allowed app saved.");
        }
        else
        {
            UpsertRule(_currentPolicy!.BlockedApps, rule);
            RemoveMatchingRule(_currentPolicy.AllowedApps, rule);
            RemoveMatchingLauncher(rule);
            await SavePolicyAsync("Blocked app saved.");
        }
    }

    private async Task RemoveAppRuleAsync(List<AppRule>? rules, AppRule rule, string successMessage)
    {
        if (_currentPolicy is null || rules is null)
        {
            return;
        }

        RemoveMatchingRule(rules, rule);
        RemoveMatchingLauncher(rule);
        await SavePolicyAsync(successMessage);
    }

    private async Task AddWebsiteRuleAsync(bool allow)
    {
        if (!await EnsurePolicyLoadedAsync())
        {
            return;
        }

        var site = NormalizeSite(WebsiteBox.Text);
        if (string.IsNullOrWhiteSpace(site))
        {
            Log("Enter a website domain or URL.");
            return;
        }

        _currentPolicy!.Browser.Enabled = true;
        _currentPolicy.Browser.WhitelistOnly = WhitelistOnlyCheckBox.IsChecked == true;

        if (allow)
        {
            UpsertSite(_currentPolicy.Browser.AllowedSites, site);
            RemoveSite(_currentPolicy.Browser.BlockedSites, site);
            UpsertWebsiteLauncher(site);
            await SavePolicyAsync("Allowed website saved.");
        }
        else
        {
            UpsertSite(_currentPolicy.Browser.BlockedSites, site);
            RemoveSite(_currentPolicy.Browser.AllowedSites, site);
            RemoveWebsiteLauncher(site);
            await SavePolicyAsync("Blocked website saved.");
        }

        WebsiteBox.Clear();
    }

    private async Task RemoveWebsiteRuleAsync(List<string>? sites, string site, string successMessage)
    {
        if (_currentPolicy is null || sites is null)
        {
            return;
        }

        RemoveSite(sites, site);
        RemoveWebsiteLauncher(site);
        await SavePolicyAsync(successMessage);
    }

    private AppRule? BuildAppRule()
    {
        var displayName = AppDisplayNameBox.Text.Trim();
        var processName = AppProcessBox.Text.Trim();
        var path = AppPathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(processName) && !string.IsNullOrWhiteSpace(path))
        {
            processName = Path.GetFileName(path);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = !string.IsNullOrWhiteSpace(processName) ? processName : Path.GetFileNameWithoutExtension(path);
        }

        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(processName))
        {
            Log("Enter an app name plus a process name or EXE path.");
            return null;
        }

        return new AppRule
        {
            DisplayName = displayName,
            ProcessName = processName,
            Path = string.IsNullOrWhiteSpace(path) ? null : path
        };
    }

    private void BindPolicyRules()
    {
        AllowedAppsGrid.ItemsSource = _currentPolicy?.AllowedApps.OrderBy(app => app.DisplayName).ToList() ?? [];
        BlockedAppsGrid.ItemsSource = _currentPolicy?.BlockedApps.OrderBy(app => app.DisplayName).ToList() ?? [];
        AllowedSitesList.ItemsSource = _currentPolicy?.Browser.AllowedSites.OrderBy(site => site).ToList() ?? [];
        BlockedSitesList.ItemsSource = _currentPolicy?.Browser.BlockedSites.OrderBy(site => site).ToList() ?? [];
        WhitelistOnlyCheckBox.IsChecked = _currentPolicy?.Browser.WhitelistOnly == true;
    }

    private async Task<bool> LoadMonitoringAsync()
    {
        var station = SelectedStation;
        if (station is null)
        {
            Log("Select a station first.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(PinBox.Password))
        {
            Log("Enter the admin PIN before loading monitoring settings.");
            return false;
        }

        try
        {
            using var client = CreateClient(station);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/monitoring/config");
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log($"{station.Name}: could not load monitoring settings: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return false;
            }

            var monitoring = await response.Content.ReadFromJsonAsync<RemoteMonitoringStatusDto>(JsonOptions);
            BindMonitoring(monitoring);
            Log($"{station.Name}: monitoring settings loaded.");
            return true;
        }
        catch (Exception ex)
        {
            Log($"{station.Name}: could not load monitoring settings: {DescribeConnectionFailure(station, ex)}");
            return false;
        }
    }

    private async Task SaveMonitoringAsync()
    {
        var station = SelectedStation;
        if (station is null)
        {
            Log("Select a station first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(PinBox.Password))
        {
            Log("Enter the admin PIN before saving monitoring settings.");
            return;
        }

        var monitoring = BuildMonitoringPolicy();
        try
        {
            using var client = CreateClient(station);
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/monitoring/config")
            {
                Content = JsonContent.Create(monitoring, options: JsonOptions)
            };
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                Log($"{station.Name}: could not save monitoring settings: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return;
            }

            var saved = await response.Content.ReadFromJsonAsync<RemoteMonitoringStatusDto>(JsonOptions);
            BindMonitoring(saved);
            Log($"{station.Name}: monitoring settings saved.");
        }
        catch (Exception ex)
        {
            Log($"{station.Name}: could not save monitoring settings: {DescribeConnectionFailure(station, ex)}");
        }
    }

    private RemoteMonitoringPolicy BuildMonitoringPolicy()
    {
        var refreshSeconds = int.TryParse(MonitoringRefreshSecondsBox.Text, out var parsed)
            ? Math.Clamp(parsed, 1, 60)
            : 5;

        var transport = "secure-agent-planned";
        if (MonitoringTransportBox.SelectedItem is System.Windows.Controls.ComboBoxItem item
            && item.Content is string selectedTransport
            && !string.IsNullOrWhiteSpace(selectedTransport))
        {
            transport = selectedTransport;
        }

        return new RemoteMonitoringPolicy
        {
            Enabled = MonitoringEnabledCheckBox.IsChecked == true,
            AllowScreenView = MonitoringScreenViewCheckBox.IsChecked == true,
            RequireAdminApproval = MonitoringApprovalCheckBox.IsChecked != false,
            LanOnly = MonitoringLanOnlyCheckBox.IsChecked != false,
            ScreenRefreshSeconds = refreshSeconds,
            Transport = transport,
            Notes = "Configured from SimpleKioskOS Remote Manager. Live encrypted viewing requires the monitor agent."
        };
    }

    private void BindMonitoring(RemoteMonitoringStatusDto? monitoring)
    {
        monitoring ??= new RemoteMonitoringStatusDto
        {
            RequireAdminApproval = true,
            LanOnly = true,
            ScreenRefreshSeconds = 5,
            Transport = "secure-agent-planned",
            LiveViewState = "Monitoring settings have not been loaded."
        };

        MonitoringEnabledCheckBox.IsChecked = monitoring.Enabled;
        MonitoringScreenViewCheckBox.IsChecked = monitoring.AllowScreenView;
        MonitoringApprovalCheckBox.IsChecked = monitoring.RequireAdminApproval;
        MonitoringLanOnlyCheckBox.IsChecked = monitoring.LanOnly;
        MonitoringRefreshSecondsBox.Text = Math.Clamp(monitoring.ScreenRefreshSeconds, 1, 60).ToString();

        foreach (var item in MonitoringTransportBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (item.Content is string content && string.Equals(content, monitoring.Transport, StringComparison.OrdinalIgnoreCase))
            {
                MonitoringTransportBox.SelectedItem = item;
                break;
            }
        }

        MonitoringStatusBox.Text =
            $"Enabled: {monitoring.Enabled}{Environment.NewLine}" +
            $"Screen view: {monitoring.AllowScreenView}{Environment.NewLine}" +
            $"Transport: {monitoring.Transport}{Environment.NewLine}" +
            $"Live view: {monitoring.LiveViewState}{Environment.NewLine}" +
            $"Security: {monitoring.Security}{Environment.NewLine}" +
            $"Notes: {monitoring.Notes}";
    }

    private static HttpClient CreateClient(ManagedStation station)
    {
        return new HttpClient
        {
            BaseAddress = new Uri(station.Url),
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    private static string NormalizeUrl(string value)
    {
        var url = value.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            return "";
        }

        if (!url.Contains("://", StringComparison.Ordinal))
        {
            url = "http://" + url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri);
            if (builder.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort)
            {
                builder.Port = 47821;
            }

            return builder.Uri.ToString().TrimEnd('/');
        }

        return url.TrimEnd('/');
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
            Id = $"app-{CreateStableId(rule.DisplayName)}",
            DisplayName = rule.DisplayName,
            Type = KioskLauncherTypes.App,
            WorkspaceMode = KioskWorkspaceModes.Lab,
            ProcessName = rule.ProcessName,
            Path = rule.Path,
            Arguments = rule.Arguments,
            Required = rule.Required
        });
    }

    private void RemoveMatchingLauncher(AppRule rule)
    {
        _currentPolicy?.Launchers.RemoveAll(launcher =>
            string.Equals(launcher.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(rule.Path)
                && string.Equals(launcher.Path, rule.Path, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpsertWebsiteLauncher(string site)
    {
        if (_currentPolicy is null)
        {
            return;
        }

        RemoveWebsiteLauncher(site);
        var url = ToWebsiteUrl(site);
        _currentPolicy.Launchers.Add(new KioskLauncher
        {
            Id = $"web-{CreateStableId(site)}",
            DisplayName = site,
            Type = KioskLauncherTypes.Web,
            WorkspaceMode = KioskWorkspaceModes.Exam,
            Url = url,
            AllowedSites = [site]
        });
    }

    private void RemoveWebsiteLauncher(string site)
    {
        var normalized = NormalizeSite(site);
        _currentPolicy?.Launchers.RemoveAll(launcher =>
            string.Equals(NormalizeSite(launcher.Url ?? ""), normalized, StringComparison.OrdinalIgnoreCase)
            || launcher.AllowedSites.Any(allowed => string.Equals(NormalizeSite(allowed), normalized, StringComparison.OrdinalIgnoreCase)));
    }

    private static void UpsertRule(List<AppRule> rules, AppRule rule)
    {
        RemoveMatchingRule(rules, rule);
        rules.Add(rule);
    }

    private static void RemoveMatchingRule(List<AppRule> rules, AppRule rule)
    {
        rules.RemoveAll(existing =>
            string.Equals(existing.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(rule.Path)
                && string.Equals(existing.Path, rule.Path, StringComparison.OrdinalIgnoreCase)));
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

    private static string NormalizeSite(string value)
    {
        var site = value.Trim();
        if (string.IsNullOrWhiteSpace(site))
        {
            return "";
        }

        if (Uri.TryCreate(site, UriKind.Absolute, out var uri))
        {
            site = uri.Host + uri.AbsolutePath;
        }

        site = site.Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Trim('/')
            .ToLowerInvariant();

        return site;
    }

    private static string ToWebsiteUrl(string site)
    {
        return site.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || site.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? site
            : $"https://{site}";
    }

    private static string CreateStableId(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        }

        return builder.ToString().Trim('-');
    }

    private static string DescribeConnectionFailure(ManagedStation station, Exception exception)
    {
        if (exception is TaskCanceledException)
        {
            return $"Timed out connecting to {station.Url}. Check that the station is powered on, SimpleKioskOS 8.1.1 or newer is installed, OTMKioskService is running, and Windows Firewall allows TCP 47821 on the station network profile.";
        }

        if (exception is HttpRequestException httpException)
        {
            if (httpException.StatusCode == HttpStatusCode.NotFound)
            {
                return "Connected to the station, but the SimpleKioskOS API endpoint was not found. Update the station build and try again.";
            }

            return $"Network error connecting to {station.Url}: {httpException.Message}. From this PC, try opening {station.Url}/api/status in a browser.";
        }

        return exception.Message;
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
            // Fall back to raw response text below.
        }

        return body.Trim();
    }

    private void UpdateSelectedStatus(ManagedStation station)
    {
        if (SelectedStation == station)
        {
            SelectedStationText.Text = station.Name;
            SelectedStatusText.Text = station.LastStatus;
        }
    }

    private void LoadStations()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                _stations.Add(new ManagedStation { Name = "This PC", Url = "http://localhost:47821" });
                return;
            }

            var stations = JsonSerializer.Deserialize<List<ManagedStation>>(File.ReadAllText(StorePath), JsonOptions) ?? [];
            foreach (var station in stations)
            {
                _stations.Add(station);
            }
        }
        catch (Exception ex)
        {
            Log($"Could not load stations: {ex.Message}");
        }
    }

    private void SaveStations()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        File.WriteAllText(StorePath, JsonSerializer.Serialize(_stations, JsonOptions));
    }

    private void Log(string message)
    {
        ActivityBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        ActivityBox.ScrollToEnd();
    }

    private sealed class RuntimeStateDto
    {
        public string PolicyName { get; set; } = "";
        public bool EnforcementEnabled { get; set; }
    }

    private sealed class DeviceDto
    {
        public string DeviceName { get; set; } = "";
        public string ConfiguredName { get; set; } = "";
    }

    private sealed class RemoteStatusDto
    {
        public string LastAvailableVersion { get; set; } = "";
        public string LastDownloadedVersion { get; set; } = "";
        public string LastDownloadMessage { get; set; } = "";
    }

    private sealed class RemoteMonitoringStatusDto
    {
        public bool Enabled { get; set; }
        public bool AllowScreenView { get; set; }
        public bool RequireAdminApproval { get; set; } = true;
        public bool LanOnly { get; set; } = true;
        public int ScreenRefreshSeconds { get; set; } = 5;
        public string Transport { get; set; } = "secure-agent-planned";
        public string Notes { get; set; } = "";
        public bool LiveViewAvailable { get; set; }
        public string LiveViewState { get; set; } = "";
        public string Security { get; set; } = "";
    }
}

public sealed class ManagedStation : INotifyPropertyChanged
{
    private string _name = "";
    private string _url = "";
    private string _lastStatus = "Not checked yet.";

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            Touch();
        }
    }

    public string Url
    {
        get => _url;
        set
        {
            _url = value;
            Touch();
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        set
        {
            _lastStatus = value;
            Touch();
        }
    }

    public string Summary => $"{Name}  -  {Url}";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Touch()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Url)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastStatus)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
    }
}
