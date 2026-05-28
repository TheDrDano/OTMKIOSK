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
    .form-grid { display: grid; grid-template-columns: 1fr 1fr 1.4fr; gap: 10px; align-items: end; }
    .rule-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; }
    .rule-actions { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 10px; }
    .status { background: #f8fafc; border: 1px solid #d7e1ea; border-radius: 7px; padding: 12px; line-height: 1.45; }
    .status-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
    .metric { background: #ffffff; border: 1px solid #e3e8ee; border-radius: 7px; padding: 12px; }
    .metric span { display: block; color: #64748b; font-size: 12px; margin-bottom: 4px; }
    .metric strong { display: block; font-size: 18px; }
    .notice { display: none; border-radius: 7px; padding: 11px 12px; background: #fff7ed; border: 1px solid #fed7aa; color: #9a3412; font-weight: 650; }
    .safe-banner { display: none; border-radius: 7px; padding: 11px 12px; background: #fff7ed; border: 1px solid #fdba74; color: #9a3412; font-weight: 750; }
    .muted { color: #64748b; font-size: 13px; }
    .wide { grid-column: 2; }
    details summary { cursor: pointer; font-weight: 750; color: #1d2935; margin-bottom: 12px; }
    @media (max-width: 900px) { main { grid-template-columns: 1fr; } .wide { grid-column: auto; } .form-grid, .rule-grid { grid-template-columns: 1fr; } }
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
      <div id="safeBanner" class="safe-banner">Safe test mode: enforcement is off. This PC is not actively blocking apps or websites.</div>
      <div class="actions">
        <button onclick="unlock()">Unlock 15m</button>
        <button class="danger" onclick="lock()">Lock Now</button>
        <button class="secondary" onclick="applyTemplate('exam-mode')">Exam Template</button>
        <button class="secondary" onclick="applyTemplate('lab-lockdown')">Lab Template</button>
      </div>
      <p class="muted">Use templates for quick testing. The advanced policy editor is below for detailed allow/block rules.</p>
    </section>
    <section class="stack">
      <h2>Remote Manager</h2>
      <div class="status" id="remoteStatus">Loading device identity...</div>
      <label style="display:flex;align-items:center;gap:8px"><input id="remoteEnabled" type="checkbox" style="width:auto"> Enable remote manager</label>
      <label>Server URL <input id="remoteServerUrl" placeholder="https://manager.simplekioskos.com"></label>
      <label>Organization ID <input id="remoteOrganizationId" placeholder="school-or-lab-id"></label>
      <label>Device name <input id="remoteDeviceAlias" placeholder="Lab PC 01"></label>
      <label style="display:flex;align-items:center;gap:8px"><input id="remotePolicyPush" type="checkbox" style="width:auto"> Allow remote policy changes</label>
      <label style="display:flex;align-items:center;gap:8px"><input id="remoteUnlock" type="checkbox" style="width:auto"> Allow remote unlock</label>
      <label style="display:flex;align-items:center;gap:8px"><input id="remoteUpdate" type="checkbox" style="width:auto"> Allow remote update approval</label>
      <h3>Updates</h3>
      <label style="display:flex;align-items:center;gap:8px"><input id="updatesEnabled" type="checkbox" style="width:auto"> Check for updates</label>
      <label>Manifest URL <input id="updateManifestUrl" placeholder="https://example.com/simplekioskos/updates.json"></label>
      <label>Channel <input id="updateChannel" placeholder="stable"></label>
      <button class="secondary" onclick="saveRemoteSettings()">Save Remote Settings</button>
      <button class="secondary" onclick="checkUpdates()">Check Updates</button>
      <button class="secondary" onclick="generatePairingCode()">Generate Pairing Code</button>
      <input id="pairingCode" readonly placeholder="Pairing code">
      <div class="muted" id="updateStatus"></div>
      <p class="muted">LAN and cloud management are not opened yet. This only prepares the local pairing foundation.</p>
    </section>
    <section class="wide">
      <h2>Simple App Rules</h2>
      <div class="form-grid">
        <label>Name <input id="appName" placeholder="Chrome"></label>
        <label>Process <input id="appProcess" placeholder="chrome.exe"></label>
        <label>EXE path <input id="appPath" placeholder="C:\Program Files\...\app.exe"></label>
      </div>
      <div class="rule-actions">
        <label style="display:flex;align-items:center;gap:8px"><input id="enforcementEnabled" type="checkbox" style="width:auto"> Turn blocking on</label>
        <label style="display:flex;align-items:center;gap:8px"><input id="strictWhitelist" type="checkbox" style="width:auto"> Only allowed apps can run</label>
        <button class="secondary" onclick="saveProtectionMode()">Save Protection Mode</button>
      </div>
      <label style="display:flex;align-items:center;gap:8px;margin-top:10px"><input id="appLauncher" type="checkbox" checked style="width:auto"> Show allowed app in kiosk launcher</label>
      <div class="rule-actions">
        <button onclick="addAppRule('allow')">Allow App</button>
        <button class="danger" onclick="addAppRule('block')">Block App</button>
      </div>
      <div class="rule-grid" style="margin-top:14px">
        <div>
          <h3>Allowed Apps</h3>
          <table><thead><tr><th>Name</th><th>Process</th><th></th></tr></thead><tbody id="allowedApps"></tbody></table>
        </div>
        <div>
          <h3>Blocked Apps</h3>
          <table><thead><tr><th>Name</th><th>Process</th><th></th></tr></thead><tbody id="blockedApps"></tbody></table>
        </div>
      </div>
    </section>
    <section class="wide">
      <h2>Simple Website Rules</h2>
      <div class="form-grid" style="grid-template-columns: minmax(0, 1fr) auto auto">
        <label>Domain or URL <input id="siteValue" placeholder="testing.example.edu or youtube.com"></label>
        <button onclick="addSiteRule('allow')">Allow Site</button>
        <button class="danger" onclick="addSiteRule('block')">Block Site</button>
      </div>
      <div class="rule-actions">
        <label style="display:flex;align-items:center;gap:8px"><input id="browserEnabled" type="checkbox" style="width:auto"> Website rules on</label>
        <label style="display:flex;align-items:center;gap:8px"><input id="whitelistOnly" type="checkbox" style="width:auto"> Only allowed websites can open</label>
        <label style="display:flex;align-items:center;gap:8px"><input id="browserBlockDownloads" type="checkbox" style="width:auto"> Block browser downloads</label>
        <button class="secondary" onclick="saveWebsiteMode()">Save Website Mode</button>
        <button class="secondary" onclick="applyBrowserPolicy()">Apply Edge/Chrome Policy</button>
      </div>
      <div class="rule-grid" style="margin-top:14px">
        <div>
          <h3>Allowed Websites</h3>
          <table><thead><tr><th>Site</th><th></th></tr></thead><tbody id="allowedSites"></tbody></table>
        </div>
        <div>
          <h3>Blocked Websites</h3>
          <table><thead><tr><th>Site</th><th></th></tr></thead><tbody id="blockedSites"></tbody></table>
        </div>
      </div>
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
    let currentPolicy = null;
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
      document.getElementById('safeBanner').style.display = enforcement ? 'none' : 'block';
      document.getElementById('status').innerHTML = `<div class="status-grid">
        <div class="metric"><span>Profile</span><strong>${html(policyName)}</strong></div>
        <div class="metric"><span>Enforcement</span><strong>${enforcement ? 'ON' : 'OFF'}</strong></div>
        <div class="metric"><span>Unlock</span><strong>${unlocked ? 'Active' : 'Locked'}</strong></div>
        <div class="metric"><span>Until</span><strong>${unlocked ? html(unlockUntil) : 'N/A'}</strong></div>
      </div>`;
      refreshDeviceStatus().catch(err => { document.getElementById('remoteStatus').textContent = err.message; });
      if (!pin()) {
        document.getElementById('policy').value = 'Enter the admin PIN above, then click Refresh.\n\nFirst-run PIN: 123456';
        document.getElementById('logs').innerHTML = '<tr><td colspan="4">Admin PIN required to view logs.</td></tr>';
        notice('Enter the admin PIN to manage policy and logs.');
        return;
      }
      const [policy, logs] = await Promise.all([authed('/api/policy'), authed('/api/logs?count=100')]);
      currentPolicy = policy;
      document.getElementById('policy').value = JSON.stringify(policy, null, 2);
      const enforcementPolicy = currentPolicy.enforcement ?? currentPolicy.Enforcement ?? {};
      document.getElementById('enforcementEnabled').checked = !!(enforcementPolicy.enabled ?? enforcementPolicy.Enabled);
      document.getElementById('strictWhitelist').checked = !!(enforcementPolicy.strictApplicationWhitelist ?? enforcementPolicy.StrictApplicationWhitelist);
      const browserPolicy = browser();
      document.getElementById('browserEnabled').checked = !!(browserPolicy.enabled ?? browserPolicy.Enabled);
      document.getElementById('whitelistOnly').checked = !!(browserPolicy.whitelistOnly ?? browserPolicy.WhitelistOnly);
      document.getElementById('browserBlockDownloads').checked = !!(browserPolicy.blockDownloads ?? browserPolicy.BlockDownloads);
      bindRemoteSettings();
      renderAppRules();
      renderWebsiteRules();
      document.getElementById('logs').innerHTML = logs.map(l => `<tr><td>${html(pick(l, 'timestamp', 'Timestamp'))}</td><td>${html(pick(l, 'level', 'Level'))}</td><td>${html(pick(l, 'eventType', 'EventType'))}</td><td>${html(pick(l, 'message', 'Message'))}</td></tr>`).join('');
      notice('Connected to local SimpleKioskOS service.', true);
    }
    async function refreshDeviceStatus() {
      const device = await getJson('/api/device');
      document.getElementById('remoteStatus').innerHTML = `<div><strong>${html(pick(device, 'configuredName', 'ConfiguredName') || pick(device, 'deviceName', 'DeviceName'))}</strong></div>
        <div class="muted">Device ID: ${html(pick(device, 'deviceId', 'DeviceId'))}</div>
        <div class="muted">LAN access: ${pick(device, 'lanApiEnabled', 'LanApiEnabled') ? 'enabled' : 'local-only for now'}</div>
        <div class="muted">Pairing: ${pick(device, 'pairingEnabled', 'PairingEnabled') ? 'active' : 'not active'}</div>`;
    }
    const allowedApps = () => currentPolicy.allowedApps ?? currentPolicy.AllowedApps ?? (currentPolicy.allowedApps = []);
    const blockedApps = () => currentPolicy.blockedApps ?? currentPolicy.BlockedApps ?? (currentPolicy.blockedApps = []);
    const launchers = () => currentPolicy.launchers ?? currentPolicy.Launchers ?? (currentPolicy.launchers = []);
    const browser = () => currentPolicy.browser ?? currentPolicy.Browser ?? (currentPolicy.browser = { enabled: true, whitelistOnly: false, blockDownloads: true, allowedSites: [], blockedSites: [] });
    const remote = () => currentPolicy.remote ?? currentPolicy.Remote ?? (currentPolicy.remote = { enabled: false, serverUrl: '', organizationId: '', deviceAlias: '', allowRemotePolicyPush: false, allowRemoteUnlock: false, allowRemoteUpdate: false });
    const updates = () => currentPolicy.updates ?? currentPolicy.Updates ?? (currentPolicy.updates = { enabled: false, channel: 'stable', manifestUrl: '', autoDownload: false, autoInstall: false, checkIntervalHours: 24 });
    const allowedSites = () => browser().allowedSites ?? browser().AllowedSites ?? (browser().allowedSites = []);
    const blockedSites = () => browser().blockedSites ?? browser().BlockedSites ?? (browser().blockedSites = []);
    const appValue = (app, name) => app[name] ?? app[name[0].toUpperCase() + name.slice(1)] ?? '';
    const sameRule = (a, b) => {
      const ap = appValue(a, 'processName').toLowerCase();
      const bp = appValue(b, 'processName').toLowerCase();
      const ax = appValue(a, 'path').toLowerCase();
      const bx = appValue(b, 'path').toLowerCase();
      return (!!ap && !!bp && ap === bp) || (!!ax && !!bx && ax === bx);
    };
    const launcherId = name => String(name || 'app').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
    function buildRule() {
      const path = document.getElementById('appPath').value.trim();
      let processName = document.getElementById('appProcess').value.trim();
      if (!processName && path) processName = path.split(/[\\/]/).pop();
      if (!processName && !path) throw new Error('Enter a process name or EXE path.');
      const displayName = document.getElementById('appName').value.trim() || (processName || path.split(/[\\/]/).pop()).replace(/\.exe$/i, '');
      return { displayName, processName, path: path || null, required: false, arguments: null };
    }
    function removeMatches(list, rule) {
      for (let i = list.length - 1; i >= 0; i--) if (sameRule(list[i], rule)) list.splice(i, 1);
    }
    function renderAppRules() {
      if (!currentPolicy) return;
      document.getElementById('allowedApps').innerHTML = allowedApps().map((app, i) => `<tr><td>${html(appValue(app, 'displayName'))}</td><td>${html(appValue(app, 'processName'))}</td><td><button class="secondary" onclick="removeAppRule('allow', ${i})">Remove</button></td></tr>`).join('') || '<tr><td colspan="3">No allowed apps yet.</td></tr>';
      document.getElementById('blockedApps').innerHTML = blockedApps().map((app, i) => `<tr><td>${html(appValue(app, 'displayName'))}</td><td>${html(appValue(app, 'processName'))}</td><td><button class="secondary" onclick="removeAppRule('block', ${i})">Remove</button></td></tr>`).join('') || '<tr><td colspan="3">No blocked apps yet.</td></tr>';
    }
    function normalizeSite(value) {
      let site = String(value || '').trim();
      try {
        const parsed = new URL(site);
        site = `${parsed.hostname}${parsed.pathname.replace(/\/$/, '')}`;
      } catch {}
      return site.replace(/^https?:\/\//i, '').replace(/\/$/, '').toLowerCase();
    }
    function removeSiteMatches(list, site) {
      const normalized = normalizeSite(site);
      for (let i = list.length - 1; i >= 0; i--) if (normalizeSite(list[i]) === normalized) list.splice(i, 1);
    }
    function renderWebsiteRules() {
      if (!currentPolicy) return;
      document.getElementById('allowedSites').innerHTML = allowedSites().map((site, i) => `<tr><td>${html(site)}</td><td><button class="secondary" onclick="removeSiteRule('allow', ${i})">Remove</button></td></tr>`).join('') || '<tr><td colspan="2">No allowed websites yet.</td></tr>';
      document.getElementById('blockedSites').innerHTML = blockedSites().map((site, i) => `<tr><td>${html(site)}</td><td><button class="secondary" onclick="removeSiteRule('block', ${i})">Remove</button></td></tr>`).join('') || '<tr><td colspan="2">No blocked websites yet.</td></tr>';
    }
    function bindRemoteSettings() {
      const r = remote();
      const u = updates();
      document.getElementById('remoteEnabled').checked = !!(r.enabled ?? r.Enabled);
      document.getElementById('remoteServerUrl').value = r.serverUrl ?? r.ServerUrl ?? '';
      document.getElementById('remoteOrganizationId').value = r.organizationId ?? r.OrganizationId ?? '';
      document.getElementById('remoteDeviceAlias').value = r.deviceAlias ?? r.DeviceAlias ?? '';
      document.getElementById('remotePolicyPush').checked = !!(r.allowRemotePolicyPush ?? r.AllowRemotePolicyPush);
      document.getElementById('remoteUnlock').checked = !!(r.allowRemoteUnlock ?? r.AllowRemoteUnlock);
      document.getElementById('remoteUpdate').checked = !!(r.allowRemoteUpdate ?? r.AllowRemoteUpdate);
      document.getElementById('updatesEnabled').checked = !!(u.enabled ?? u.Enabled);
      document.getElementById('updateManifestUrl').value = u.manifestUrl ?? u.ManifestUrl ?? '';
      document.getElementById('updateChannel').value = u.channel ?? u.Channel ?? 'stable';
      document.getElementById('updateStatus').textContent = u.lastCheckMessage ?? u.LastCheckMessage ?? '';
    }
    async function savePolicyObject(message) {
      await authed('/api/policy', { method: 'PUT', body: JSON.stringify(currentPolicy, null, 2) });
      notice(message, true);
      await refresh();
    }
    async function saveProtectionMode() {
      if (!currentPolicy) { notice('Enter the admin PIN and refresh first.'); return; }
      const enforcementPolicy = currentPolicy.enforcement ?? currentPolicy.Enforcement ?? (currentPolicy.enforcement = {});
      if ('Enforcement' in currentPolicy) {
        enforcementPolicy.Enabled = document.getElementById('enforcementEnabled').checked;
        enforcementPolicy.StrictApplicationWhitelist = document.getElementById('strictWhitelist').checked;
      } else {
        enforcementPolicy.enabled = document.getElementById('enforcementEnabled').checked;
        enforcementPolicy.strictApplicationWhitelist = document.getElementById('strictWhitelist').checked;
      }
      await savePolicyObject('Protection mode saved.');
    }
    async function saveWebsiteMode() {
      if (!currentPolicy) { notice('Enter the admin PIN and refresh first.'); return; }
      const browserPolicy = browser();
      if ('Browser' in currentPolicy) {
        browserPolicy.Enabled = document.getElementById('browserEnabled').checked;
        browserPolicy.WhitelistOnly = document.getElementById('whitelistOnly').checked;
        browserPolicy.BlockDownloads = document.getElementById('browserBlockDownloads').checked;
      } else {
        browserPolicy.enabled = document.getElementById('browserEnabled').checked;
        browserPolicy.whitelistOnly = document.getElementById('whitelistOnly').checked;
        browserPolicy.blockDownloads = document.getElementById('browserBlockDownloads').checked;
      }
      await savePolicyObject('Website mode saved.');
    }
    async function saveRemoteSettings() {
      if (!currentPolicy) { notice('Enter the admin PIN and refresh first.'); return; }
      const r = remote();
      const u = updates();
      if ('Remote' in currentPolicy) {
        r.Enabled = document.getElementById('remoteEnabled').checked;
        r.ServerUrl = document.getElementById('remoteServerUrl').value.trim();
        r.OrganizationId = document.getElementById('remoteOrganizationId').value.trim();
        r.DeviceAlias = document.getElementById('remoteDeviceAlias').value.trim();
        r.AllowRemotePolicyPush = document.getElementById('remotePolicyPush').checked;
        r.AllowRemoteUnlock = document.getElementById('remoteUnlock').checked;
        r.AllowRemoteUpdate = document.getElementById('remoteUpdate').checked;
        u.Enabled = document.getElementById('updatesEnabled').checked;
        u.ManifestUrl = document.getElementById('updateManifestUrl').value.trim();
        u.Channel = document.getElementById('updateChannel').value.trim() || 'stable';
      } else {
        r.enabled = document.getElementById('remoteEnabled').checked;
        r.serverUrl = document.getElementById('remoteServerUrl').value.trim();
        r.organizationId = document.getElementById('remoteOrganizationId').value.trim();
        r.deviceAlias = document.getElementById('remoteDeviceAlias').value.trim();
        r.allowRemotePolicyPush = document.getElementById('remotePolicyPush').checked;
        r.allowRemoteUnlock = document.getElementById('remoteUnlock').checked;
        r.allowRemoteUpdate = document.getElementById('remoteUpdate').checked;
        u.enabled = document.getElementById('updatesEnabled').checked;
        u.manifestUrl = document.getElementById('updateManifestUrl').value.trim();
        u.channel = document.getElementById('updateChannel').value.trim() || 'stable';
      }
      await savePolicyObject('Remote settings saved.');
    }
    async function addAppRule(mode) {
      try {
        if (!currentPolicy) throw new Error('Enter the admin PIN and refresh first.');
        const rule = buildRule();
        if (mode === 'allow') {
          removeMatches(allowedApps(), rule); removeMatches(blockedApps(), rule); allowedApps().push(rule);
          if (document.getElementById('appLauncher').checked) {
            const ls = launchers(); removeMatches(ls, rule);
            ls.push({ id: launcherId(rule.displayName), displayName: rule.displayName, type: 'app', workspaceMode: 'lab', processName: rule.processName, path: rule.path, arguments: null, required: false, allowMultiMonitorOwnership: false, allowedSites: [] });
          }
          await savePolicyObject('Allowed app saved.');
        } else {
          removeMatches(blockedApps(), rule); removeMatches(allowedApps(), rule); removeMatches(launchers(), rule); blockedApps().push(rule);
          await savePolicyObject('Blocked app saved.');
        }
        document.getElementById('appName').value = ''; document.getElementById('appProcess').value = ''; document.getElementById('appPath').value = '';
      } catch (err) { notice(err.message); }
    }
    async function removeAppRule(mode, index) {
      if (!currentPolicy) return;
      const list = mode === 'allow' ? allowedApps() : blockedApps();
      const [rule] = list.splice(index, 1);
      if (rule) removeMatches(launchers(), rule);
      await savePolicyObject('App rule removed.');
    }
    async function addSiteRule(mode) {
      try {
        if (!currentPolicy) throw new Error('Enter the admin PIN and refresh first.');
        const site = normalizeSite(document.getElementById('siteValue').value);
        if (!site) throw new Error('Enter a domain or URL.');
        if (mode === 'allow') {
          removeSiteMatches(allowedSites(), site); removeSiteMatches(blockedSites(), site); allowedSites().push(site);
          await savePolicyObject('Allowed website saved.');
        } else {
          removeSiteMatches(blockedSites(), site); removeSiteMatches(allowedSites(), site); blockedSites().push(site);
          await savePolicyObject('Blocked website saved.');
        }
        document.getElementById('siteValue').value = '';
      } catch (err) { notice(err.message); }
    }
    async function removeSiteRule(mode, index) {
      if (!currentPolicy) return;
      const list = mode === 'allow' ? allowedSites() : blockedSites();
      list.splice(index, 1);
      await savePolicyObject('Website rule removed.');
    }
    async function generatePairingCode() {
      try {
        const pairing = await authed('/api/device/pairing-code', { method: 'POST', body: '{}' });
        document.getElementById('pairingCode').value = pick(pairing, 'code', 'Code') ?? '';
        notice('Pairing code generated. LAN access is still local-only until remote manager is enabled.', true);
        await refreshDeviceStatus();
      } catch (err) { notice(err.message); }
    }
    async function checkUpdates() {
      try {
        const result = await authed('/api/updates/check', { method: 'POST', body: '{}' });
        document.getElementById('updateStatus').textContent = pick(result, 'message', 'Message') ?? 'Update check completed.';
        notice(document.getElementById('updateStatus').textContent, !!pick(result, 'available', 'Available'));
        await refresh();
      } catch (err) { notice(err.message); }
    }
    async function applyBrowserPolicy() {
      try {
        await saveWebsiteMode();
        await authed('/api/browser/apply-policy', { method: 'POST', body: '{}' });
        notice('Edge/Chrome policy applied. Restart browsers for changes to apply.', true);
      } catch (err) { notice(err.message); }
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
