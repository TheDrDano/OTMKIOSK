namespace Otm.Kiosk.Service;

public static class WebManagerHtml
{
    public const string Page = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>SimpleKioskOS Local Manager</title>
  <style>
    :root { color-scheme: light; font-family: Segoe UI, Arial, sans-serif; background: #e8edf2; color: #17202a; }
    * { box-sizing: border-box; }
    body { margin: 0; }
    header { background: #ffffff; color: #17202a; padding: 14px 24px; display: flex; align-items: center; justify-content: space-between; gap: 18px; border-bottom: 1px solid #cbd5e1; }
    header img { display: block; width: min(268px, 48vw); height: 58px; object-fit: contain; object-position: left center; }
    header strong { display: block; font-size: 22px; letter-spacing: .2px; }
    header span { color: #526171; font-size: 13px; }
    .brand { display: flex; align-items: center; gap: 14px; min-width: 0; }
    .brand-text { display: grid; gap: 3px; }
    main { padding: 22px; display: grid; grid-template-columns: 350px minmax(0, 1fr); gap: 16px; max-width: 1280px; margin: 0 auto; }
    section { background: white; border: 1px solid #d8dee6; border-radius: 8px; padding: 16px; }
    h2 { font-size: 16px; margin: 0 0 12px; }
    h3 { font-size: 13px; margin: 0 0 8px; color: #405466; text-transform: uppercase; letter-spacing: .04em; }
    label { display: grid; gap: 6px; font-size: 13px; font-weight: 600; color: #344756; }
    button { border: 1px solid #d9550d; background: #ff6b1a; color: white; border-radius: 7px; padding: 10px 13px; font-weight: 750; cursor: pointer; min-height: 40px; }
    button:hover { filter: brightness(.96); }
    button.secondary { background: white; color: #1d2935; border-color: #c7d0da; }
    button.danger { background: #b42318; border-color: #8f1d14; }
    input { border: 1px solid #b6c2cf; border-radius: 6px; padding: 9px; width: 100%; }
    textarea { width: 100%; min-height: 420px; font-family: Consolas, monospace; border: 1px solid #cad5de; border-radius: 6px; padding: 12px; background: #fbfcfd; resize: vertical; }
    table { border-collapse: collapse; width: 100%; font-size: 13px; }
    td, th { border-bottom: 1px solid #e3e8ee; padding: 8px; text-align: left; vertical-align: top; }
    th { color: #405466; font-size: 12px; text-transform: uppercase; }
    .stack { display: grid; gap: 12px; }
    .actions { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .status { background: #f8fafc; border: 1px solid #d7e1ea; border-radius: 7px; padding: 12px; line-height: 1.45; }
    .status-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
    .metric { background: #ffffff; border: 1px solid #e3e8ee; border-radius: 7px; padding: 12px; }
    .metric span { display: block; color: #64748b; font-size: 12px; margin-bottom: 4px; }
    .metric strong { display: block; font-size: 18px; }
    .notice { display: none; border-radius: 7px; padding: 11px 12px; background: #fff7ed; border: 1px solid #fed7aa; color: #9a3412; font-weight: 650; }
    .muted { color: #64748b; font-size: 13px; }
    .wide { grid-column: 2; }
    details summary { cursor: pointer; font-weight: 750; color: #1d2935; margin-bottom: 12px; }
    @media (max-width: 900px) { main { grid-template-columns: 1fr; } .wide { grid-column: auto; } }
  </style>
</head>
<body>
  <header>
    <div class="brand">
      <img src="/assets/simplekioskos_side.png" alt="SimpleKioskOS">
      <div class="brand-text">
        <strong>SimpleKioskOS</strong>
        <span>Local lockdown manager</span>
      </div>
    </div>
    <span>localhost:47821</span>
  </header>
  <main>
    <section class="stack">
      <h2>Access</h2>
      <label>Admin PIN <input id="pin" type="password" autocomplete="current-password" value=""></label>
      <button onclick="refresh()">Connect / Refresh</button>
      <p class="muted">First-run PIN is 123456. Change it before strict lockdown testing.</p>
      <div id="notice" class="notice"></div>
    </section>
    <section class="stack">
      <h2>Simple Controls</h2>
      <div class="status" id="status">Loading...</div>
      <div class="actions">
        <button onclick="unlock()">Unlock 15m</button>
        <button class="danger" onclick="lock()">Lock Now</button>
        <button class="secondary" onclick="applyTemplate('exam-mode')">Exam Template</button>
        <button class="secondary" onclick="applyTemplate('lab-lockdown')">Lab Template</button>
      </div>
      <p class="muted">Use templates for quick testing. The advanced policy editor is below for detailed allow/block rules.</p>
    </section>
    <section class="wide">
      <details open>
        <summary>Advanced Policy JSON</summary>
        <textarea id="policy"></textarea>
        <div style="margin-top:10px"><button onclick="savePolicy()">Save Advanced Policy</button></div>
      </details>
    </section>
    <section class="wide">
      <h2>Activity Logs</h2>
      <table>
        <thead><tr><th>Time</th><th>Level</th><th>Event</th><th>Message</th></tr></thead>
        <tbody id="logs"></tbody>
      </table>
    </section>
  </main>
  <script>
    const pin = () => document.getElementById('pin').value;
    const headers = () => ({ 'Content-Type': 'application/json', 'X-OTM-Admin-PIN': pin() });
    const html = value => String(value ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch]));
    function notice(message, good = false) {
      const n = document.getElementById('notice');
      n.textContent = message;
      n.style.display = 'block';
      n.style.background = good ? '#ecfdf3' : '#fff7ed';
      n.style.borderColor = good ? '#bbf7d0' : '#fed7aa';
      n.style.color = good ? '#166534' : '#9a3412';
    }
    async function apiError(response) {
      const text = await response.text();
      try { return JSON.parse(text).error || text; } catch { return text || `${response.status} ${response.statusText}`; }
    }
    async function getJson(url) { const r = await fetch(url); if (!r.ok) throw new Error(await apiError(r)); return r.json(); }
    async function authed(url, options = {}) { const r = await fetch(url, { ...options, headers: headers() }); if (!r.ok) throw new Error(await apiError(r)); return r.json(); }
    const pick = (obj, camel, pascal) => obj[camel] ?? obj[pascal];
    async function refresh() {
      const status = await getJson('/api/status');
      const policyName = pick(status, 'policyName', 'PolicyName') ?? 'Local Policy';
      const enforcement = pick(status, 'enforcementEnabled', 'EnforcementEnabled');
      const unlocked = pick(status, 'temporaryUnlockActive', 'TemporaryUnlockActive');
      const unlockUntil = pick(status, 'temporaryUnlockUntil', 'TemporaryUnlockUntil');
      document.getElementById('status').innerHTML = `<div class="status-grid">
        <div class="metric"><span>Profile</span><strong>${html(policyName)}</strong></div>
        <div class="metric"><span>Enforcement</span><strong>${enforcement ? 'ON' : 'OFF'}</strong></div>
        <div class="metric"><span>Unlock</span><strong>${unlocked ? 'Active' : 'Locked'}</strong></div>
        <div class="metric"><span>Until</span><strong>${unlocked ? html(unlockUntil) : 'N/A'}</strong></div>
      </div>`;
      if (!pin()) {
        document.getElementById('policy').value = 'Enter the admin PIN above, then click Refresh.\n\nFirst-run PIN: 123456';
        document.getElementById('logs').innerHTML = '<tr><td colspan="4">Admin PIN required to view logs.</td></tr>';
        notice('Enter the admin PIN to manage policy and logs.');
        return;
      }
      const [policy, logs] = await Promise.all([authed('/api/policy'), authed('/api/logs?count=100')]);
      document.getElementById('policy').value = JSON.stringify(policy, null, 2);
      document.getElementById('logs').innerHTML = logs.map(l => `<tr><td>${html(pick(l, 'timestamp', 'Timestamp'))}</td><td>${html(pick(l, 'level', 'Level'))}</td><td>${html(pick(l, 'eventType', 'EventType'))}</td><td>${html(pick(l, 'message', 'Message'))}</td></tr>`).join('');
      notice('Connected to local SimpleKioskOS service.', true);
    }
    async function savePolicy() { await authed('/api/policy', { method: 'PUT', body: document.getElementById('policy').value }); notice('Policy saved.', true); await refresh(); }
    async function unlock() { await authed('/api/unlock', { method: 'POST', body: JSON.stringify({ minutes: 15 }) }); notice('Device unlocked for 15 minutes.', true); await refresh(); }
    async function lock() { await authed('/api/lock', { method: 'POST', body: '{}' }); notice('Device locked.', true); await refresh(); }
    async function applyTemplate(name) { await authed(`/api/templates/${name}`, { method: 'POST', body: '{}' }); notice(`${name === 'exam-mode' ? 'Exam' : 'Lab'} template applied.`, true); await refresh(); }
    refresh().catch(err => { document.getElementById('status').textContent = err.message; notice(err.message); });
  </script>
</body>
</html>
""";
}
