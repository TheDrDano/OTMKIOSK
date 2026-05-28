using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;
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

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void Unlock_Click(object sender, RoutedEventArgs e) => await RunAdminActionAsync("/api/unlock", HttpMethod.Post, "{\"minutes\":15}");
    private async void Lock_Click(object sender, RoutedEventArgs e) => await RunAdminActionAsync("/api/lock", HttpMethod.Post, "{}");
    private async void FlightPreset_Click(object sender, RoutedEventArgs e) => await RunAdminActionAsync("/api/presets/flight-simulator", HttpMethod.Post, "{}");

    private async void SavePolicy_Click(object sender, RoutedEventArgs e)
    {
        await RunAdminActionAsync("/api/policy", HttpMethod.Put, PolicyTextBox.Text);
    }

    private async void ChangePin_Click(object sender, RoutedEventArgs e)
    {
        var newPin = NewPinBox.Password;
        if (newPin.Length < 6)
        {
            MessageBox.Show(this, "PIN must be at least 6 characters.", "OTM Kiosk", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            var policy = await GetAdminJsonAsync<KioskPolicy>("/api/policy");
            var logs = await GetAdminJsonAsync<List<LogEntry>>("/api/logs?count=300") ?? [];

            PolicyTextBox.Text = JsonSerializer.Serialize(policy, JsonOptions);
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

    private async Task RunAdminActionAsync(string url, HttpMethod method, string body)
    {
        try
        {
            var request = new HttpRequestMessage(method, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);

            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var message = await response.Content.ReadAsStringAsync();
                MessageBox.Show(this, message, "OTM Kiosk", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "OTM Kiosk", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<T?> GetAdminJsonAsync<T>(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-OTM-Admin-PIN", PinBox.Password);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }
}
