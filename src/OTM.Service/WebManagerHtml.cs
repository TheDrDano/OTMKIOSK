namespace Otm.Kiosk.Service;

public static class WebManagerHtml
{
    public const string Page = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>OTM Kiosk Local Manager</title>
  <style>
    :root { color-scheme: light; font-family: Segoe UI, Arial, sans-serif; background: #f4f6f8; color: #17202a; }
    body { margin: 0; }
    header { background: #17202a; color: white; padding: 18px 24px; display: flex; align-items: center; justify-content: space-between; }
    main { padding: 24px; display: grid; gap: 16px; max-width: 1180px; margin: 0 auto; }
    section { background: white; border: 1px solid #d8dee6; border-radius: 8px; padding: 16px; }
    .row { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
    button { border: 1px solid #1d5f8d; background: #1d76b8; color: white; border-radius: 6px; padding: 9px 12px; font-weight: 600; cursor: pointer; }
    button.secondary { background: white; color: #1d5f8d; }
    input { border: 1px solid #b6c2cf; border-radius: 6px; padding: 9px; min-width: 220px; }
    textarea { width: 100%; min-height: 360px; font-family: Consolas, monospace; border: 1px solid #b6c2cf; border-radius: 6px; padding: 10px; box-sizing: border-box; }
    table { border-collapse: collapse; width: 100%; }
    td, th { border-bottom: 1px solid #e3e8ee; padding: 8px; text-align: left; vertical-align: top; }
    .status { font-weight: 700; }
    .muted { color: #64748b; font-size: 13px; }
  </style>
</head>
<body>
  <header>
    <strong>OTM Kiosk Local Manager</strong>
    <span class="muted">localhost:47821</span>
  </header>
  <main>
    <section>
      <div class="row">
        <label>Admin PIN <input id="pin" type="password" autocomplete="current-password" value=""></label>
        <button onclick="refresh()">Refresh</button>
        <button onclick="unlock()">Temporary Unlock</button>
        <button onclick="lock()">Lock Now</button>
        <button class="secondary" onclick="applyFlight()">Flight Simulator Preset</button>
      </div>
      <p class="status" id="status">Loading...</p>
    </section>
    <section>
      <h2>Policy JSON</h2>
      <textarea id="policy"></textarea>
      <div class="row" style="margin-top:10px">
        <button onclick="savePolicy()">Save Policy</button>
      </div>
    </section>
    <section>
      <h2>Recent Logs</h2>
      <table>
        <thead><tr><th>Time</th><th>Level</th><th>Event</th><th>Message</th></tr></thead>
        <tbody id="logs"></tbody>
      </table>
    </section>
  </main>
  <script>
    const pin = () => document.getElementById('pin').value;
    const headers = () => ({ 'Content-Type': 'application/json', 'X-OTM-Admin-PIN': pin() });
    async function getJson(url) { const r = await fetch(url); if (!r.ok) throw new Error(await r.text()); return r.json(); }
    async function authed(url, options = {}) { const r = await fetch(url, { ...options, headers: headers() }); if (!r.ok) throw new Error(await r.text()); return r.json(); }
    async function refresh() {
      const [status, policy, logs] = await Promise.all([getJson('/api/status'), authed('/api/policy'), authed('/api/logs?count=100')]);
      document.getElementById('status').textContent = `${status.policyName}: enforcement ${status.enforcementEnabled ? 'ON' : 'OFF'}${status.temporaryUnlockActive ? `, unlocked until ${status.temporaryUnlockUntil}` : ''}`;
      document.getElementById('policy').value = JSON.stringify(policy, null, 2);
      document.getElementById('logs').innerHTML = logs.map(l => `<tr><td>${l.timestamp}</td><td>${l.level}</td><td>${l.eventType}</td><td>${l.message}</td></tr>`).join('');
    }
    async function savePolicy() { await authed('/api/policy', { method: 'PUT', body: document.getElementById('policy').value }); await refresh(); }
    async function unlock() { await authed('/api/unlock', { method: 'POST', body: JSON.stringify({ minutes: 15 }) }); await refresh(); }
    async function lock() { await authed('/api/lock', { method: 'POST', body: '{}' }); await refresh(); }
    async function applyFlight() { await authed('/api/presets/flight-simulator', { method: 'POST', body: '{}' }); await refresh(); }
    refresh().catch(err => document.getElementById('status').textContent = err.message);
  </script>
</body>
</html>
""";
}
