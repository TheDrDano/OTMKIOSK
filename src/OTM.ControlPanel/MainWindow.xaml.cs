using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
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
            if (string.IsNullOrWhiteSpace(PinBox.Password))
            {
                ShowNotice("Admin PIN required", "Enter the admin PIN before using admin actions.", NoticeKind.Info);
                return;
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
                return;
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowNotice("Request failed", ex.Message, NoticeKind.Error);
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
}
