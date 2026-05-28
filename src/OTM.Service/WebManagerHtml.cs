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
    :root { color-scheme: light; font-family: Segoe UI, Arial, sans-serif; background: #eef2f6; color: #17202a; }
    * { box-sizing: border-box; }
    body { margin: 0; }
    header { background: #18242e; color: white; padding: 18px 24px; display: flex; align-items: center; justify-content: space-between; gap: 18px; }
    header strong { display: block; font-size: 22px; letter-spacing: .2px; }
    header span { color: #afc1cf; font-size: 13px; }
    main { padding: 22px; display: grid; grid-template-columns: 330px minmax(0, 1fr); gap: 16px; max-width: 1280px; margin: 0 auto; }
    section { background: white; border: 1px solid #d8dee6; border-radius: 8px; padding: 16px; }
    h2 { font-size: 16px; margin: 0 0 12px; }
    label { display: grid; gap: 6px; font-size: 13px; font-weight: 600; color: #344756; }
    button { border: 1px solid #145b73; background: #176b87; color: white; border-radius: 6px; padding: 9px 12px; font-weight: 700; cursor: pointer; min-height: 36px; }
    button.secondary { background: white; color: #176b87; border-color: #b8c7d2; }
    button.danger { background: #9f2d2d; border-color: #812424; }
    input { border: 1px solid #b6c2cf; border-radius: 6px; padding: 9px; width: 100%; }
    textarea { width: 100%; min-height: 500px; font-family: Consolas, monospace; border: 1px solid #cad5de; border-radius: 6px; padding: 12px; background: #fbfcfd; resize: vertical; }
    table { border-collapse: collapse; width: 100%; font-size: 13px; }
    td, th { border-bottom: 1px solid #e3e8ee; padding: 8px; text-align: left; vertical-align: top; }
    th { color: #405466; font-size: 12px; text-transform: uppercase; }
    .stack { display: grid; gap: 12px; }
    .actions { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .status { background: #f3f7fa; border: 1px solid #d7e1ea; border-radius: 6px; padding: 12px; font-weight: 700; line-height: 1.45; }
    .muted { color: #64748b; font-size: 13px; }
    .wide { grid-column: 2; }
    @media (max-width: 900px) { main { grid-template-columns: 1fr; } .wide { grid-column: auto; } }
  </style>
</head>
<body>
  <header>
    <div>
      <strong>OTM Kiosk</strong>
      <span>Local lockdown manager</span>
    </div>
    <span>localhost:47821</span>
  </header>
  <main>
    <section class="stack">
      <h2>Access</h2>
      <label>Admin PIN <input id="pin" type="password" autocomplete="current-password" value=""></label>
      <button onclick="refresh()">Refresh</button>
      <p class="muted">First-run PIN is 123456. Change it from the native control panel after setup.</p>
    </section>
    <section class="stack">
      <h2>Status</h2>
      <p class="status" id="status">Loading...</p>
      <div class="actions">
        <button onclick="unlock()">Unlock 15m</button>
        <button class="danger" onclick="lock()">Lock Now</button>
        <button class="secondary" onclick="applyTemplate('exam-mode')">Exam Template</button>
        <button class="secondary" onclick="applyTemplate('lab-lockdown')">Lab Template</button>
      </div>
    </section>
    <section class="wide">
      <h2>Policy JSON</h2>
      <textarea id="policy"></textarea>
      <div style="margin-top:10px"><button onclick="savePolicy()">Save Policy</button></div>
    </section>
    <section class="wide">
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
    const pick = (obj, camel, pascal) => obj[camel] ?? obj[pascal];
    async function refresh() {
      const status = await getJson('/api/status');
      const policyName = pick(status, 'policyName', 'PolicyName') ?? 'Local Policy';
      const enforcement = pick(status, 'enforcementEnabled', 'EnforcementEnabled');
      const unlocked = pick(status, 'temporaryUnlockActive', 'TemporaryUnlockActive');
      const unlockUntil = pick(status, 'temporaryUnlockUntil', 'TemporaryUnlockUntil');
      document.getElementById('status').textContent = `${policyName}: enforcement ${enforcement ? 'ON' : 'OFF'}${unlocked ? `, unlocked until ${unlockUntil}` : ''}`;
      if (!pin()) {
        document.getElementById('policy').value = 'Enter the admin PIN above, then click Refresh.\n\nFirst-run PIN: 123456';
        document.getElementById('logs').innerHTML = '<tr><td colspan="4">Admin PIN required to view logs.</td></tr>';
        return;
      }
      const [policy, logs] = await Promise.all([authed('/api/policy'), authed('/api/logs?count=100')]);
      document.getElementById('policy').value = JSON.stringify(policy, null, 2);
      document.getElementById('logs').innerHTML = logs.map(l => `<tr><td>${pick(l, 'timestamp', 'Timestamp')}</td><td>${pick(l, 'level', 'Level')}</td><td>${pick(l, 'eventType', 'EventType')}</td><td>${pick(l, 'message', 'Message')}</td></tr>`).join('');
    }
    async function savePolicy() { await authed('/api/policy', { method: 'PUT', body: document.getElementById('policy').value }); await refresh(); }
    async function unlock() { await authed('/api/unlock', { method: 'POST', body: JSON.stringify({ minutes: 15 }) }); await refresh(); }
    async function lock() { await authed('/api/lock', { method: 'POST', body: '{}' }); await refresh(); }
    async function applyTemplate(name) { await authed(`/api/templates/${name}`, { method: 'POST', body: '{}' }); await refresh(); }
    refresh().catch(err => document.getElementById('status').textContent = err.message);
  </script>
</body>
</html>
""";
}
