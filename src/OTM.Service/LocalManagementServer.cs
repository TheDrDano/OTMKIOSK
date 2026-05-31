using System.Net;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Otm.Kiosk.Shared.Models;
using Otm.Kiosk.Shared.Security;
using Otm.Kiosk.Shared.Storage;

namespace Otm.Kiosk.Service;

public sealed class LocalManagementServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly KioskRuntime _runtime;
    private readonly SqliteKioskStore _logs;
    private readonly HttpListener _listener = new();
    private bool _closed;
    private bool _startupUpdateStarted;

    public LocalManagementServer(KioskRuntime runtime, SqliteKioskStore logs)
    {
        _runtime = runtime;
        _logs = logs;
        _listener.Prefixes.Add("http://localhost:47821/");
        _listener.Prefixes.Add("http://127.0.0.1:47821/");
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            StartListenerWithFallback();
            ApplyBrowserPolicySafely(_runtime.GetPolicy());
            _runtime.Log("Info", "LocalApiStarted", "Local API listening at http://localhost:47821.");
            _ = Task.Run(() => RunStartupUpdateAsync(cancellationToken), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _runtime.Log("Error", "LocalApiFailed", ex.Message);
            return;
        }

        while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleAsync(context), cancellationToken);
            }
            catch when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
        }
    }

    private void StartListenerWithFallback()
    {
        _listener.Start();
    }

    public void Stop()
    {
        if (_closed)
        {
            return;
        }

        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        _listener.Close();
        _closed = true;
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            AddCors(response);

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (path.Length == 0)
            {
                response.StatusCode = 404;
                await WriteJsonAsync(response, new { error = "Browser-based local UI has been removed. Use the native SimpleKioskOS Control Panel." });
                return;
            }

            if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(response, _runtime.GetState());
                return;
            }

            if (path.Equals("/api/device", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(response, GetDeviceStatus());
                return;
            }

            if (path.Equals("/api/kiosk/launchers", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(response, GetKioskLaunchers());
                return;
            }

            if (path.Equals("/api/kiosk/launch", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                var launchRequest = await ReadJsonAsync<KioskLaunchRequest>(request);
                var launcher = FindKioskLauncher(launchRequest?.Id, launchRequest?.DisplayName);
                if (launcher is null)
                {
                    await BadRequest(response, "Launcher is not approved by the current policy.");
                    return;
                }

                _runtime.Log("Info", "KioskLaunchRequested", $"Kiosk launcher selected: {launcher.DisplayName}", launcher.ProcessName, launcher.Path ?? launcher.Url);
                await WriteJsonAsync(response, launcher);
                return;
            }

            if (path.Equals("/api/kiosk/violations", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "GET")
            {
                var since = DateTimeOffset.TryParse(request.QueryString["since"], out var parsed)
                    ? parsed
                    : DateTimeOffset.UtcNow.AddMinutes(-5);
                await WriteJsonAsync(response, GetKioskViolations(since));
                return;
            }

            if (path.Equals("/api/kiosk/shell-heartbeat", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                _runtime.MarkShellHeartbeat();
                await WriteJsonAsync(response, new { ok = true, shellReady = true });
                return;
            }

            if (path.Equals("/api/kiosk/violation", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                var violation = await ReadJsonAsync<KioskViolationRequest>(request);
                if (violation is null || string.IsNullOrWhiteSpace(violation.Message))
                {
                    await BadRequest(response, "Violation message required.");
                    return;
                }

                _runtime.Log("Warning", violation.EventType ?? "KioskViolation", violation.Message, path: violation.Path);
                await WriteJsonAsync(response, new { ok = true });
                return;
            }

            if (path.Equals("/api/recovery/disable-enforcement", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                _runtime.EmergencyDisableEnforcement("Local recovery disabled enforcement from kiosk shell.");
                await WriteJsonAsync(response, _runtime.GetState());
                return;
            }

            if (!IsAuthorized(request))
            {
                await Unauthorized(response);
                return;
            }

            if (path.Equals("/api/policy", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "GET")
            {
                await WriteJsonAsync(response, _runtime.GetPolicy());
                return;
            }

            if (path.Equals("/api/logs", StringComparison.OrdinalIgnoreCase))
            {
                var count = int.TryParse(request.QueryString["count"], out var parsed) ? parsed : 200;
                await WriteJsonAsync(response, _logs.ReadLatest(Math.Clamp(count, 1, 1000)));
                return;
            }

            if (path.Equals("/api/policy", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "PUT")
            {
                var policy = await ReadJsonAsync<KioskPolicy>(request);
                if (policy is null)
                {
                    await BadRequest(response, "Invalid policy JSON.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(policy.Admin.PasswordHash))
                {
                    policy.Admin = _runtime.GetPolicy().Admin;
                }

                policy.Updates ??= new UpdatePolicy();
                _runtime.SavePolicy(policy, "Policy updated from native control panel.");
                ApplyBrowserPolicySafely(policy);
                await WriteJsonAsync(response, _runtime.GetPolicy());
                return;
            }

            if (path.Equals("/api/monitoring/config", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "GET")
            {
                await WriteJsonAsync(response, GetMonitoringStatus());
                return;
            }

            if (path.Equals("/api/monitoring/config", StringComparison.OrdinalIgnoreCase)
                && (request.HttpMethod == "PUT" || request.HttpMethod == "POST"))
            {
                var monitoring = await ReadJsonAsync<RemoteMonitoringPolicy>(request);
                if (monitoring is null)
                {
                    await BadRequest(response, "Invalid monitoring settings JSON.");
                    return;
                }

                var policy = _runtime.GetPolicy();
                policy.Monitoring = NormalizeMonitoringPolicy(monitoring);
                _runtime.SavePolicy(policy, "Remote monitoring settings updated.");
                await WriteJsonAsync(response, GetMonitoringStatus());
                return;
            }

            if (path.Equals("/api/updates/check", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                await WriteJsonAsync(response, await CheckForUpdatesAsync());
                return;
            }

            if (path.Equals("/api/updates/download", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                await WriteJsonAsync(response, await DownloadUpdateAsync());
                return;
            }

            if (path.Equals("/api/browser/apply-policy", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                ApplyBrowserPolicySafely(_runtime.GetPolicy());
                await WriteJsonAsync(response, new { ok = true, message = "Edge/Chrome browser policies applied. Restart browsers for changes to apply." });
                return;
            }

            if (path.Equals("/api/unlock", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                var requestBody = await ReadJsonAsync<UnlockRequest>(request) ?? new UnlockRequest();
                var minutes = Math.Clamp(requestBody.Minutes ?? _runtime.GetPolicy().Enforcement.TemporaryUnlockMinutes, 1, 480);
                _runtime.TemporaryUnlock(TimeSpan.FromMinutes(minutes));
                await WriteJsonAsync(response, _runtime.GetState());
                return;
            }

            if (path.Equals("/api/lock", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                _runtime.Relock();
                await WriteJsonAsync(response, _runtime.GetState());
                return;
            }

            if (path.Equals("/api/system/shutdown", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                StartSystemCommand("/s /t 0");
                _runtime.Log("Warning", "SystemShutdownRequested", "Shutdown requested from SimpleKioskOS admin controls.");
                await WriteJsonAsync(response, new { ok = true });
                return;
            }

            if (path.Equals("/api/system/restart", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                StartSystemCommand("/r /t 0");
                _runtime.Log("Warning", "SystemRestartRequested", "Restart requested from SimpleKioskOS admin controls.");
                await WriteJsonAsync(response, new { ok = true });
                return;
            }

            if (path.Equals("/api/admin/password", StringComparison.OrdinalIgnoreCase) && request.HttpMethod == "POST")
            {
                var requestBody = await ReadJsonAsync<PasswordChangeRequest>(request);
                if (requestBody is null || string.IsNullOrWhiteSpace(requestBody.NewPassword) || requestBody.NewPassword.Length < 6)
                {
                    await BadRequest(response, "New password must be at least 6 characters.");
                    return;
                }

                var policy = _runtime.GetPolicy();
                policy.Admin.PasswordHash = PasswordHasher.Hash(requestBody.NewPassword);
                policy.Admin.RequirePasswordChange = false;
                _runtime.SavePolicy(policy, "Admin password changed.");
                await WriteJsonAsync(response, new { ok = true });
                return;
            }

            response.StatusCode = 404;
            await WriteJsonAsync(response, new { error = "Not found" });
        }
        catch (Exception ex)
        {
            _runtime.Log("Error", "LocalApiRequestFailed", ex.Message);
            if (context.Response.OutputStream.CanWrite)
            {
                context.Response.StatusCode = 500;
                await WriteJsonAsync(context.Response, new { error = ex.Message });
            }
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var secret = request.Headers["X-OTM-Admin-PIN"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = request.Headers["X-OTM-Recovery-Key"];
        }

        var admin = _runtime.GetPolicy().Admin;
        var authorized = PasswordHasher.Verify(secret ?? "", admin.PasswordHash)
            || PasswordHasher.Verify(secret ?? "", admin.RecoveryKeyHash);

        if (!authorized)
        {
            _runtime.Log("Warning", "UnauthorizedLocalRequest", $"Unauthorized local request: {request.HttpMethod} {request.Url?.AbsolutePath}");
        }

        return authorized;
    }

    private object GetDeviceStatus()
    {
        var policy = _runtime.GetPolicy();
        var identity = LoadOrCreateDeviceIdentity();

        return new
        {
            identity.DeviceId,
            identity.DeviceName,
            configuredName = GetConfiguredDeviceName(identity, policy),
            pairingEnabled = false,
            lanApiEnabled = false,
            localApiUrl = "http://localhost:47821",
            remoteFoundation = "disabled"
        };
    }

    private object GetMonitoringStatus()
    {
        var policy = _runtime.GetPolicy();
        policy.Monitoring ??= new RemoteMonitoringPolicy();
        var monitoring = NormalizeMonitoringPolicy(policy.Monitoring);
        return new
        {
            monitoring.Enabled,
            monitoring.AllowScreenView,
            monitoring.RequireAdminApproval,
            monitoring.LanOnly,
            monitoring.ScreenRefreshSeconds,
            monitoring.Transport,
            monitoring.Notes,
            liveViewAvailable = false,
            liveViewState = monitoring.Enabled
                ? "Monitoring is configured. Live encrypted screen viewing requires the upcoming user-session monitor agent."
                : "Monitoring is disabled on this station.",
            security = "Do not expose the station API or future VNC transport directly to the public internet. Use LAN or VPN."
        };
    }

    private static RemoteMonitoringPolicy NormalizeMonitoringPolicy(RemoteMonitoringPolicy monitoring)
    {
        monitoring.ScreenRefreshSeconds = Math.Clamp(monitoring.ScreenRefreshSeconds, 1, 60);
        if (string.IsNullOrWhiteSpace(monitoring.Transport))
        {
            monitoring.Transport = "secure-agent-planned";
        }

        if (string.IsNullOrWhiteSpace(monitoring.Notes))
        {
            monitoring.Notes = "Disabled by default. Use only over trusted LAN/VPN until the encrypted monitor agent is installed.";
        }

        return monitoring;
    }

    private async Task RunStartupUpdateAsync(CancellationToken cancellationToken)
    {
        if (_startupUpdateStarted)
        {
            return;
        }

        _startupUpdateStarted = true;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
        }
        catch
        {
            return;
        }

        var policy = _runtime.GetPolicy();
        policy.Updates ??= new UpdatePolicy();
        if (!policy.Updates.Enabled || !policy.Updates.CheckOnStartup)
        {
            return;
        }

        var hold = policy.Updates.HoldEnforcementDuringStartupUpdate;
        if (hold)
        {
            _runtime.BeginMaintenanceHold(TimeSpan.FromMinutes(30), "Startup GitHub update check/download is running.");
        }

        try
        {
            if (policy.Updates.AutoDownload)
            {
                await DownloadUpdateAsync();
            }
            else
            {
                await CheckForUpdatesAsync();
            }
        }
        catch (Exception ex)
        {
            _runtime.Log("Error", "StartupUpdateFailed", ex.Message);
        }
        finally
        {
            if (hold)
            {
                _runtime.EndMaintenanceHold("Startup GitHub update check/download finished.");
            }
        }
    }

    private async Task<object> CheckForUpdatesAsync()
    {
        var policy = _runtime.GetPolicy();
        policy.Updates ??= new UpdatePolicy();
        policy.Updates.LastCheckedAt = DateTimeOffset.UtcNow;

        if (!policy.Updates.Enabled)
        {
            policy.Updates.LastCheckMessage = "Update checks are disabled.";
            _runtime.SavePolicy(policy, "Update check skipped because updates are disabled.");
            return BuildUpdateCheckResponse(policy, available: false);
        }

        try
        {
            var manifest = await FetchUpdateManifestAsync(policy);
            var manifestError = ValidateStationUpdateManifest(manifest, requireInstaller: false);
            if (!string.IsNullOrWhiteSpace(manifestError))
            {
                policy.Updates.LastCheckMessage = manifestError;
                _runtime.SavePolicy(policy, "Update check failed because manifest is invalid.");
                return BuildUpdateCheckResponse(policy, available: false);
            }

            var currentVersion = GetCurrentVersion();
            var available = IsNewerVersion(manifest.Version, currentVersion);
            policy.Updates.LastAvailableVersion = available ? manifest.Version : "";
            policy.Updates.LastInstallerUrl = manifest.InstallerUrl;
            policy.Updates.LastInstallerSha256 = manifest.Sha256;
            policy.Updates.LastReleaseNotes = manifest.ReleaseNotes;
            policy.Updates.LastCheckMessage = available
                ? $"Stable version {manifest.Version} is available. Download it from the Updates panel when ready."
                : $"No update available. Current version is {currentVersion}.";
            _runtime.SavePolicy(policy, "Update check completed.");

            return BuildUpdateCheckResponse(policy, available, manifest);
        }
        catch (Exception ex)
        {
            policy.Updates.LastCheckMessage = $"Update check failed: {ex.Message}";
            _runtime.SavePolicy(policy, "Update check failed.");
            return BuildUpdateCheckResponse(policy, available: false);
        }
    }

    private async Task<object> DownloadUpdateAsync()
    {
        var policy = _runtime.GetPolicy();
        policy.Updates ??= new UpdatePolicy();
        policy.Updates.LastCheckedAt = DateTimeOffset.UtcNow;

        if (!policy.Updates.Enabled)
        {
            policy.Updates.LastDownloadMessage = "Update downloads are disabled.";
            _runtime.SavePolicy(policy, "Update download skipped because updates are disabled.");
            return BuildUpdateDownloadResponse(policy, downloaded: false);
        }

        try
        {
            var manifest = await FetchUpdateManifestAsync(policy);
            var manifestError = ValidateStationUpdateManifest(manifest, requireInstaller: true);
            if (!string.IsNullOrWhiteSpace(manifestError))
            {
                policy.Updates.LastDownloadMessage = manifestError;
                _runtime.SavePolicy(policy, "Update download failed because manifest is invalid.");
                return BuildUpdateDownloadResponse(policy, downloaded: false);
            }

            var currentVersion = GetCurrentVersion();
            var available = IsNewerVersion(manifest.Version, currentVersion);
            policy.Updates.LastAvailableVersion = available ? manifest.Version : "";
            policy.Updates.LastInstallerUrl = manifest.InstallerUrl;
            policy.Updates.LastInstallerSha256 = manifest.Sha256;
            policy.Updates.LastReleaseNotes = manifest.ReleaseNotes;
            if (!available)
            {
                policy.Updates.LastDownloadMessage = $"No download needed. Current version is {currentVersion}.";
                _runtime.SavePolicy(policy, "Update download skipped because the station is current.");
                return BuildUpdateDownloadResponse(policy, downloaded: false);
            }

            Directory.CreateDirectory(KioskPaths.UpdatesDirectory);
            var tempPath = KioskPaths.StationInstallerUpdatePath + ".download";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
            {
                await using var source = await client.GetStreamAsync(manifest.InstallerUrl);
                await using var destination = File.Create(tempPath);
                await source.CopyToAsync(destination);
            }

            var hash = await ComputeSha256Async(tempPath);
            if (!string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tempPath);
                policy.Updates.LastDownloadMessage = "Downloaded installer failed SHA256 verification and was deleted.";
                _runtime.SavePolicy(policy, "Update download failed SHA256 verification.");
                return BuildUpdateDownloadResponse(policy, downloaded: false);
            }

            if (File.Exists(KioskPaths.StationInstallerUpdatePath))
            {
                File.Delete(KioskPaths.StationInstallerUpdatePath);
            }

            File.Move(tempPath, KioskPaths.StationInstallerUpdatePath);
            policy.Updates.LastDownloadedAt = DateTimeOffset.UtcNow;
            policy.Updates.LastDownloadedVersion = manifest.Version;
            policy.Updates.LastDownloadedPath = KioskPaths.StationInstallerUpdatePath;
            policy.Updates.LastDownloadMessage = $"Version {manifest.Version} downloaded and verified. Install manually when ready.";
            _runtime.SavePolicy(policy, "Update downloaded and verified.");
            _runtime.Log("Info", "UpdateDownloaded", policy.Updates.LastDownloadMessage, path: KioskPaths.StationInstallerUpdatePath);
            return BuildUpdateDownloadResponse(policy, downloaded: true);
        }
        catch (Exception ex)
        {
            policy.Updates.LastDownloadMessage = $"Update download failed: {ex.Message}";
            _runtime.SavePolicy(policy, "Update download failed.");
            return BuildUpdateDownloadResponse(policy, downloaded: false);
        }
    }

    private static object BuildUpdateCheckResponse(KioskPolicy policy, bool available, UpdateManifest? manifest = null)
    {
        return new
        {
            available,
            currentVersion = GetCurrentVersion(),
            version = manifest?.Version ?? policy.Updates.LastAvailableVersion,
            channel = manifest?.Channel ?? policy.Updates.Channel,
            installerUrl = manifest?.InstallerUrl ?? policy.Updates.LastInstallerUrl,
            sha256 = manifest?.Sha256 ?? policy.Updates.LastInstallerSha256,
            releaseNotes = manifest?.ReleaseNotes ?? policy.Updates.LastReleaseNotes,
            message = policy.Updates.LastCheckMessage,
            downloadedVersion = policy.Updates.LastDownloadedVersion,
            downloadedPath = policy.Updates.LastDownloadedPath,
            downloadMessage = policy.Updates.LastDownloadMessage,
            autoInstallEnabled = false
        };
    }

    private static object BuildUpdateDownloadResponse(KioskPolicy policy, bool downloaded)
    {
        return new
        {
            downloaded,
            currentVersion = GetCurrentVersion(),
            version = policy.Updates.LastAvailableVersion,
            installerUrl = policy.Updates.LastInstallerUrl,
            sha256 = policy.Updates.LastInstallerSha256,
            releaseNotes = policy.Updates.LastReleaseNotes,
            downloadedVersion = policy.Updates.LastDownloadedVersion,
            downloadedAt = policy.Updates.LastDownloadedAt,
            downloadedPath = policy.Updates.LastDownloadedPath,
            message = policy.Updates.LastDownloadMessage,
            autoInstallEnabled = false
        };
    }

    private static async Task<UpdateManifest> FetchUpdateManifestAsync(KioskPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.Updates.ManifestUrl))
        {
            policy.Updates.ManifestUrl = new UpdatePolicy().ManifestUrl;
        }

        if (!Uri.TryCreate(policy.Updates.ManifestUrl, UriKind.Absolute, out var manifestUri)
            || (manifestUri.Scheme != Uri.UriSchemeHttps && manifestUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Enter a valid HTTP or HTTPS update manifest URL.");
        }

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var json = await client.GetStringAsync(manifestUri);
        return JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions)
            ?? new UpdateManifest();
    }

    private static string ValidateStationUpdateManifest(UpdateManifest manifest, bool requireInstaller)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version))
        {
            return "Update manifest did not include a version.";
        }

        if (!string.IsNullOrWhiteSpace(manifest.Channel)
            && !string.Equals(manifest.Channel, "stable", StringComparison.OrdinalIgnoreCase))
        {
            return $"Update manifest channel '{manifest.Channel}' is not stable.";
        }

        if (requireInstaller)
        {
            if (string.IsNullOrWhiteSpace(manifest.InstallerUrl))
            {
                return "Update manifest did not include installerUrl for OTM-Kiosk-Setup.exe.";
            }

            if (string.IsNullOrWhiteSpace(manifest.Sha256))
            {
                return "Update manifest did not include sha256 for OTM-Kiosk-Setup.exe.";
            }
        }

        return "";
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsNewerVersion(string candidate, string current)
    {
        if (Version.TryParse(candidate, out var candidateVersion) && Version.TryParse(current, out var currentVersion))
        {
            return candidateVersion > currentVersion;
        }

        return !string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase);
    }

    private static void StartSystemCommand(string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private void ApplyBrowserPolicySafely(KioskPolicy policy)
    {
        try
        {
            ApplyBrowserPolicy(policy);
            _runtime.Log("Info", "BrowserPolicyApplied", "Edge/Chrome browser policies applied from local policy.");
        }
        catch (Exception ex)
        {
            _runtime.Log("Error", "BrowserPolicyApplyFailed", ex.Message);
        }
    }

    private static void ApplyBrowserPolicy(KioskPolicy policy)
    {
        foreach (var rootPath in new[] { @"SOFTWARE\Policies\Microsoft\Edge", @"SOFTWARE\Policies\Google\Chrome" })
        {
            using var root = Registry.LocalMachine.CreateSubKey(rootPath, writable: true)
                ?? throw new InvalidOperationException($"Could not open HKLM\\{rootPath}.");

            if (!policy.Browser.Enabled)
            {
                root.SetValue("DownloadRestrictions", 0, RegistryValueKind.DWord);
                SetPolicyList(rootPath, "URLBlocklist", []);
                SetPolicyList(rootPath, "URLAllowlist", []);
                continue;
            }

            root.SetValue("DownloadRestrictions", policy.Browser.BlockDownloads ? 3 : 0, RegistryValueKind.DWord);

            if (policy.Browser.WhitelistOnly)
            {
                SetPolicyList(rootPath, "URLBlocklist", ["*"]);
                SetPolicyList(rootPath, "URLAllowlist", NormalizeBrowserPolicySites(policy.Browser.AllowedSites));
            }
            else
            {
                SetPolicyList(rootPath, "URLBlocklist", NormalizeBrowserPolicySites(policy.Browser.BlockedSites));
                SetPolicyList(rootPath, "URLAllowlist", []);
            }
        }
    }

    private static string[] NormalizeBrowserPolicySites(IEnumerable<string> sites)
    {
        return sites
            .Where(static site => !string.IsNullOrWhiteSpace(site))
            .SelectMany(static site =>
            {
                var trimmed = site.Trim();
                if (trimmed.Contains("://", StringComparison.Ordinal) || trimmed.Contains("*", StringComparison.Ordinal))
                {
                    return new[] { trimmed };
                }

                var domain = trimmed.TrimStart('.').TrimEnd('/');
                return new[] { $"*://{domain}/*", $"*://*.{domain}/*" };
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void SetPolicyList(string rootPath, string name, IReadOnlyList<string> values)
    {
        using var root = Registry.LocalMachine.OpenSubKey(rootPath, writable: true)
            ?? Registry.LocalMachine.CreateSubKey(rootPath, writable: true)
            ?? throw new InvalidOperationException($"Could not open HKLM\\{rootPath}.");

        root.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        if (values.Count == 0)
        {
            return;
        }

        using var list = root.CreateSubKey(name, writable: true)
            ?? throw new InvalidOperationException($"Could not create HKLM\\{rootPath}\\{name}.");
        for (var index = 0; index < values.Count; index++)
        {
            list.SetValue((index + 1).ToString(), values[index], RegistryValueKind.String);
        }
    }

    private static string GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "9.4.0";
    }

    private static string GetConfiguredDeviceName(DeviceIdentity identity, KioskPolicy policy)
    {
        if (!string.IsNullOrWhiteSpace(policy.Remote.DeviceAlias))
        {
            return policy.Remote.DeviceAlias;
        }

        return string.IsNullOrWhiteSpace(identity.DeviceName) ? Environment.MachineName : identity.DeviceName;
    }

    private static DeviceIdentity LoadOrCreateDeviceIdentity()
    {
        try
        {
            if (File.Exists(KioskPaths.DeviceIdentityPath))
            {
                var json = File.ReadAllText(KioskPaths.DeviceIdentityPath);
                var existing = JsonSerializer.Deserialize<DeviceIdentity>(json, JsonOptions);
                if (existing is not null && !string.IsNullOrWhiteSpace(existing.DeviceId))
                {
                    return existing;
                }
            }
        }
        catch
        {
            // Recreate identity if the local cache is unreadable.
        }

        var identity = new DeviceIdentity
        {
            DeviceId = Guid.NewGuid().ToString("N"),
            DeviceName = Environment.MachineName,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Directory.CreateDirectory(KioskPaths.RootDirectory);
        File.WriteAllText(KioskPaths.DeviceIdentityPath, JsonSerializer.Serialize(identity, JsonOptions));
        return identity;
    }

    private IReadOnlyList<KioskLauncher> GetKioskLaunchers()
    {
        var policy = _runtime.GetPolicy();
        var dedicatedKioskLauncher = CreateDedicatedKioskLauncher(policy.DedicatedKiosk ?? new DedicatedKioskPolicy());
        if (!string.IsNullOrWhiteSpace(dedicatedKioskLauncher.DisplayName))
        {
            return [NormalizeLauncher(dedicatedKioskLauncher)];
        }

        if (policy.Launchers.Count > 0)
        {
            var launcher = policy.Launchers
                .Where(launcher => !string.IsNullOrWhiteSpace(launcher.DisplayName))
                .Select(NormalizeLauncher)
                .OrderByDescending(launcher => launcher.Required)
                .ThenBy(launcher => launcher.DisplayName)
                .FirstOrDefault();
            return launcher is null ? [] : [launcher];
        }

        var appLauncher = policy.RequiredApps
            .Concat(policy.AllowedApps)
            .Where(app => !string.IsNullOrWhiteSpace(app.DisplayName) && !string.IsNullOrWhiteSpace(app.ProcessName))
            .GroupBy(app => app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var app = group.First();
                return new KioskLauncher
                {
                    Id = CreateLauncherId(app.DisplayName),
                    DisplayName = app.DisplayName,
                    Type = KioskLauncherTypes.App,
                    WorkspaceMode = KioskWorkspaceModes.Lab,
                    ProcessName = app.ProcessName,
                    Path = app.Path,
                    Arguments = app.Arguments,
                    Required = app.Required
                };
            })
            .Where(launcher => !string.IsNullOrWhiteSpace(launcher.DisplayName))
            .OrderByDescending(app => app.Required)
            .ThenBy(app => app.DisplayName)
            .FirstOrDefault();
        return appLauncher is null ? [] : [appLauncher];
    }

    private static KioskLauncher CreateDedicatedKioskLauncher(DedicatedKioskPolicy dedicatedKiosk)
    {
        if (!dedicatedKiosk.Enabled)
        {
            return new KioskLauncher();
        }

        var isWeb = string.Equals(dedicatedKiosk.Type, KioskLauncherTypes.Web, StringComparison.OrdinalIgnoreCase);
        return new KioskLauncher
        {
            Id = "dedicated-kiosk",
            DisplayName = string.IsNullOrWhiteSpace(dedicatedKiosk.DisplayName) ? "Kiosk" : dedicatedKiosk.DisplayName,
            Type = isWeb ? KioskLauncherTypes.Web : KioskLauncherTypes.App,
            WorkspaceMode = KioskWorkspaceModes.DedicatedKiosk,
            Url = dedicatedKiosk.Url,
            ProcessName = isWeb ? "msedge.exe" : dedicatedKiosk.ProcessName,
            Path = dedicatedKiosk.Path,
            Arguments = dedicatedKiosk.Arguments,
            Required = true,
            AllowMultiMonitorOwnership = false,
            AllowedSites = isWeb && !string.IsNullOrWhiteSpace(dedicatedKiosk.Url) ? [dedicatedKiosk.Url] : []
        };
    }

    private KioskLauncher? FindKioskLauncher(string? id, string? displayName)
    {
        return GetKioskLaunchers().FirstOrDefault(launcher =>
            (!string.IsNullOrWhiteSpace(id) && string.Equals(launcher.Id, id, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(displayName) && string.Equals(launcher.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)));
    }

    private IReadOnlyList<LogEntry> GetKioskViolations(DateTimeOffset since)
    {
        var violationTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "BlockedProcess",
            "WhitelistViolation",
            "ShellStartupGuard",
            "DownloadDeleted",
            "DownloadQuarantined",
            "DownloadBlockFailed",
            "UnauthorizedLocalRequest",
            "BlockedWebsite",
            "KioskViolation"
        };

        return _logs.ReadLatest(500)
            .Where(log => log.Timestamp >= since && violationTypes.Contains(log.EventType))
            .OrderBy(log => log.Timestamp)
            .ToList();
    }

    private static KioskLauncher NormalizeLauncher(KioskLauncher launcher)
    {
        if (string.IsNullOrWhiteSpace(launcher.Id))
        {
            launcher.Id = CreateLauncherId(launcher.DisplayName);
        }

        if (string.IsNullOrWhiteSpace(launcher.Type))
        {
            launcher.Type = string.IsNullOrWhiteSpace(launcher.Url) ? KioskLauncherTypes.App : KioskLauncherTypes.Web;
        }

        if (string.IsNullOrWhiteSpace(launcher.WorkspaceMode))
        {
            launcher.WorkspaceMode = string.Equals(launcher.Type, KioskLauncherTypes.Web, StringComparison.OrdinalIgnoreCase)
                ? KioskWorkspaceModes.Exam
                : KioskWorkspaceModes.Lab;
        }

        return launcher;
    }

    private static string CreateLauncherId(string displayName)
    {
        var chars = displayName
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest request)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var json = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object value)
    {
        response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task Unauthorized(HttpListenerResponse response)
    {
        response.StatusCode = 401;
        await WriteJsonAsync(response, new { error = "Admin PIN or recovery key required." });
    }

    private static async Task BadRequest(HttpListenerResponse response, string message)
    {
        response.StatusCode = 400;
        await WriteJsonAsync(response, new { error = message });
    }

    private static void AddCors(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "http://localhost:47821";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, X-OTM-Admin-PIN, X-OTM-Recovery-Key";
        response.Headers["Access-Control-Allow-Methods"] = "GET, PUT, POST, OPTIONS";
    }

    private sealed class UnlockRequest
    {
        public int? Minutes { get; set; }
    }

    private sealed class PasswordChangeRequest
    {
        public string NewPassword { get; set; } = "";
    }

    private sealed class KioskLaunchRequest
    {
        public string? Id { get; set; }
        public string? DisplayName { get; set; }
    }

    private sealed class KioskViolationRequest
    {
        public string? EventType { get; set; }
        public string Message { get; set; } = "";
        public string? Path { get; set; }
    }

    private sealed class DeviceIdentity
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class UpdateManifest
    {
        public string Version { get; set; } = "";
        public string Channel { get; set; } = "";
        public string InstallerUrl { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
    }
}
