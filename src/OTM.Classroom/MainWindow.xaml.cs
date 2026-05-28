using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace Otm.Kiosk.Classroom;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDeviceAsync();
    }

    private async void Pairing_Click(object sender, RoutedEventArgs e)
    {
        var response = await AdminPostAsync("/api/device/pairing-code", "{}");
        if (response is null)
        {
            return;
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        if (json.TryGetProperty("code", out var code))
        {
            PairingCodeBox.Text = code.GetString() ?? "";
        }

        AppendActivity("Pairing code generated.");
        await RefreshDeviceAsync();
    }

    private async void Lock_Click(object sender, RoutedEventArgs e)
    {
        if (await AdminPostAsync("/api/lock", "{}") is not null)
        {
            AppendActivity("Station locked.");
            await RefreshDeviceAsync();
        }
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if (await AdminPostAsync("/api/unlock", "{\"minutes\":15}") is not null)
        {
            AppendActivity("Station unlocked for 15 minutes.");
            await RefreshDeviceAsync();
        }
    }

    private async Task RefreshDeviceAsync()
    {
        try
        {
            using var client = CreateClient();
            var device = await client.GetFromJsonAsync<JsonElement>("/api/device", JsonOptions);
            var status = await client.GetFromJsonAsync<JsonElement>("/api/status", JsonOptions);
            var name = GetString(device, "configuredName") ?? GetString(device, "deviceName") ?? "SimpleKioskOS station";
            DeviceNameText.Text = name;
            DeviceStatusText.Text =
                $"Device ID: {GetString(device, "deviceId") ?? "unknown"}{Environment.NewLine}" +
                $"Policy: {GetString(status, "policyName") ?? "unknown"}{Environment.NewLine}" +
                $"Enforcement: {(GetBoolean(status, "enforcementEnabled") ? "ON" : "OFF")}";
            AppendActivity("Station status refreshed.");
        }
        catch (Exception ex)
        {
            AppendActivity($"Connection failed: {ex.Message}");
        }
    }

    private async Task<HttpResponseMessage?> AdminPostAsync(string path, string body)
    {
        try
        {
            using var client = CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                AppendActivity($"Request failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                return null;
            }

            return response;
        }
        catch (Exception ex)
        {
            AppendActivity($"Request failed: {ex.Message}");
            return null;
        }
    }

    private HttpClient CreateClient()
    {
        var url = ManagerUrlBox.Text.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url))
        {
            url = "http://localhost:47821";
        }

        return new HttpClient { BaseAddress = new Uri(url) };
    }

    private void AppendActivity(string message)
    {
        ActivityBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        ActivityBox.ScrollToEnd();
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private static bool GetBoolean(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }
}
