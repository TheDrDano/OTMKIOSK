using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;

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
    private async void Restart_Click(object sender, RoutedEventArgs e) => await PostSelectedAsync("/api/system/restart", "{}");
    private async void Shutdown_Click(object sender, RoutedEventArgs e) => await PostSelectedAsync("/api/system/shutdown", "{}");

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

            station.Touch();
            SaveStations();
            UpdateSelectedStatus(station);
            Log($"Refreshed {station.Name}: {station.LastStatus.Replace(Environment.NewLine, " | ")}");
        }
        catch (Exception ex)
        {
            station.LastStatus = $"Offline or unreachable: {ex.Message}";
            station.Touch();
            UpdateSelectedStatus(station);
            Log($"{station.Name}: {station.LastStatus}");
        }
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

            Log($"{station.Name}: command sent to {path}");
            await RefreshStationAsync(station);
        }
        catch (Exception ex)
        {
            Log($"{station.Name}: command failed: {ex.Message}");
        }
    }

    private static HttpClient CreateClient(ManagedStation station)
    {
        return new HttpClient
        {
            BaseAddress = new Uri(station.Url),
            Timeout = TimeSpan.FromSeconds(8)
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

        return url.TrimEnd('/');
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
