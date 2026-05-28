using System.Net;
using System.Text;
using System.Text.Json;
using Otm.Kiosk.Shared.Models;
using Otm.Kiosk.Shared.Security;
using Otm.Kiosk.Shared.Storage;

namespace Otm.Kiosk.Service;

public sealed class LocalManagementServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly KioskRuntime _runtime;
    private readonly JsonLogStore _logs;
    private readonly HttpListener _listener = new();
    private bool _closed;

    public LocalManagementServer(KioskRuntime runtime, JsonLogStore logs)
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
            _listener.Start();
            _runtime.Log("Info", "LocalManagerStarted", "Local manager listening at http://localhost:47821.");
        }
        catch (Exception ex)
        {
            _runtime.Log("Error", "LocalManagerFailed", ex.Message);
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
                await WriteHtmlAsync(response, WebManagerHtml.Page);
                return;
            }

            if (path.Equals("/api/status", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(response, _runtime.GetState());
                return;
            }

            if (path.Equals("/api/presets/flight-simulator", StringComparison.OrdinalIgnoreCase))
            {
                if (request.HttpMethod == "GET")
                {
                    await WriteJsonAsync(response, PolicyPresets.FlightSimulator());
                    return;
                }

                if (request.HttpMethod == "POST")
                {
                    if (!IsAuthorized(request))
                    {
                        await Unauthorized(response);
                        return;
                    }

                    var preset = PolicyPresets.FlightSimulator();
                    preset.Admin = _runtime.GetPolicy().Admin;
                    _runtime.SavePolicy(preset, "Flight Simulator preset applied.");
                    await WriteJsonAsync(response, _runtime.GetPolicy());
                    return;
                }
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

                _runtime.SavePolicy(policy, "Policy updated from local manager.");
                await WriteJsonAsync(response, _runtime.GetPolicy());
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
            _runtime.Log("Error", "LocalManagerRequestFailed", ex.Message);
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

    private static async Task WriteHtmlAsync(HttpListenerResponse response, string html)
    {
        response.ContentType = "text/html; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(html);
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
}
