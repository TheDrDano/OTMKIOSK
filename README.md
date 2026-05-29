# SimpleKioskOS

SimpleKioskOS is a local-first native Windows workspace launcher and shared-computer management app. The MVP in this repo includes:

- `OTM.Service`: Windows service runtime for process enforcement, downloads quarantine/delete, SQLite policy/log persistence, and the local API used by the native apps.
- `OTM.ControlPanel`: native WPF admin UI. No Electron.
- `OTM.KioskShell`: fullscreen user-facing kiosk screen with approved application launchers and admin access.
- `OTM.Manager`: separate native Remote Manager for connecting to multiple SimpleKioskOS stations.
- `OTM.RecoveryTool`: offline local recovery/reset utility.

## MVP Behavior

- Policy and logs are stored locally under the existing `%ProgramData%\OTM Kiosk` data folder.
- Existing `%ProgramData%\OTM Kiosk\policy.json` files are migrated into SQLite on first run.
- First-run admin PIN is `123456`; change it immediately from the control panel.
- A first-run recovery key is written to the local data folder.
- The service can run as a real Windows service and starts automatically after reboot once installed.
- Remote/cloud management is intentionally not required.

## Build

```powershell
dotnet build .\OTMKiosk.sln
```

## Build EXE Installer

Install prerequisites on a build machine:

- .NET 8 SDK
- Inno Setup 6

Then run:

```powershell
.\scripts\build-installer.ps1 -Version 8.1.2
```

The installer will be created in:

```txt
artifacts\installer\OTM-Kiosk-Setup.exe
```

By default the script publishes self-contained `win-x64` binaries, so the test VPS does not need the .NET runtime preinstalled. The build also downloads Microsoft's Evergreen WebView2 bootstrapper and packages it into the SimpleKioskOS installer so embedded exam/web mode can install its browser runtime dependency on clean machines. Use `-FrameworkDependent` only if you want a smaller installer and you know the target machine has the .NET 8 Desktop Runtime installed.

You can also build the installer in GitHub Actions. Push the repo to GitHub, open **Actions > Build Installer > Run workflow**, then download the `OTM-Kiosk-Installer` artifact. Pushes to `main` or `master` also build test artifacts automatically.

## GitHub Releases and Updates

For public testing, keep the station app and Remote Manager in this repo as separate projects and separate installers. Do not use a branch as the product boundary; branches are better for development lines. The release workflow builds both installers from the same tag.

To publish a release from GitHub:

```powershell
git tag v8.1.2
git push origin v8.1.2
```

Use one lowercase release tag per version, like `v8.1.2`. The workflow normalizes manual inputs and `V...` tags back to lowercase `v...`, but old duplicate GitHub releases should be deleted from the GitHub **Releases** page once so the list stays clean.

The **Release Installers** workflow creates a GitHub Release with:

- `OTM-Kiosk-Setup.exe`
- `SimpleKioskOS-Remote-Manager-Setup.exe`
- `update-manifest.json`

The stable latest download URLs are:

```txt
https://github.com/TheDrDano/OTMKIOSK/releases/latest/download/OTM-Kiosk-Setup.exe
https://github.com/TheDrDano/OTMKIOSK/releases/latest/download/SimpleKioskOS-Remote-Manager-Setup.exe
```

After the repo is public, set the station update manifest URL in the native Control Panel to:

```txt
https://github.com/TheDrDano/OTMKIOSK/releases/latest/download/update-manifest.json
```

The current app checks and reports updates. V8.1.2 can also download the stable station installer from the manifest to `%ProgramData%\OTM Kiosk\Updates\OTM-Kiosk-Setup.exe`, verify its SHA256 hash, and mark it ready for manual install. It does not silently run installers yet; that should wait until signing, hash verification, and a recovery-safe install flow are finished.

The generated `update-manifest.json` includes the stable station installer URL/hash plus Remote Manager installer metadata:

```json
{
  "product": "SimpleKioskOS",
  "version": "8.1.2",
  "channel": "stable",
  "releaseTag": "v8.1.2",
  "installerUrl": "https://github.com/TheDrDano/OTMKIOSK/releases/latest/download/OTM-Kiosk-Setup.exe",
  "sha256": "generated during release",
  "managerInstallerUrl": "https://github.com/TheDrDano/OTMKIOSK/releases/latest/download/SimpleKioskOS-Remote-Manager-Setup.exe",
  "managerSha256": "generated during release",
  "autoInstallEnabled": false
}
```

## EXE Code Signing

Windows publisher identity for an `.exe` uses **Authenticode code signing**, not an SSL/TLS certificate. Signing requires `signtool.exe`, which is installed with the Windows SDK, and a code-signing certificate issued for software signing.

For local signing with a certificate installed in the machine certificate store:

```powershell
.\scripts\build-installer.ps1 -Version 8.1.2 -Sign -CertificateThumbprint "YOUR_CERT_THUMBPRINT"
```

Use `-CertificateStore LocalMachine` if the certificate is installed in the local machine store instead of the current user store.

For local signing with a PFX:

```powershell
.\scripts\build-installer.ps1 -Version 8.1.2 -Sign -PfxPath "C:\secure\otm-signing.pfx" -PfxPassword "PFX_PASSWORD"
```

The build signs published EXE/DLL files before packaging and signs the final setup EXE after Inno Setup finishes. If the final setup EXE is not Authenticode-signed by a trusted certificate, Windows will show **Unknown publisher**.

To verify signatures:

```powershell
.\scripts\verify-signatures.ps1 -Path .\artifacts\installer -Recurse
```

For GitHub Actions signing, add repository secrets:

- `OTM_SIGN_PFX_BASE64`: base64 of the PFX file
- `OTM_SIGN_PFX_PASSWORD`: PFX password

Create the base64 value locally with:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\secure\otm-signing.pfx")) | Set-Clipboard
```

Then run the **Build Installer** workflow. If signing secrets are present, the workflow signs the installer. If signing secrets are missing, the workflow builds an unsigned test installer by default. Set `allow_unsigned=false` when you want CI to fail instead of producing an unsigned artifact.

Signing reduces Defender/SmartScreen friction and changes the publisher from unknown to your verified identity. It does not guarantee that SmartScreen warnings disappear immediately for a brand-new app; reputation still builds over time.

For lab-only testing without buying a certificate yet, create a self-signed test code-signing cert:

```powershell
.\scripts\create-test-signing-cert.ps1 -Password "test-password"
.\scripts\build-installer.ps1 -Version 8.1.2 -Sign -PfxPath ".\artifacts\signing\simplekioskos-test.pfx" -PfxPassword "test-password"
```

On the test machine, trust that test cert before installing:

```powershell
.\scripts\trust-test-signing-cert.ps1 -PfxPath ".\simplekioskos-test.pfx" -Password "test-password"
```

For UAC/elevated installer prompts on a test VPS, run PowerShell as Administrator and trust it at the machine level:

```powershell
.\scripts\trust-test-signing-cert.ps1 -PfxPath ".\simplekioskos-test.pfx" -Password "test-password" -LocalMachine
```

Self-signed certs are for machines you control only. Public production installs need an OV/EV **code-signing** certificate from a trusted certificate authority.

If Windows still shows **Unknown publisher**, check the build log step named **Show installer publisher**. If it says `<unsigned>`, you downloaded an unsigned artifact or signing secrets were missing. If it shows your certificate subject but Windows still says unknown, the target PC does not trust that certificate chain. If Windows shows a SmartScreen “unrecognized app” warning while the publisher is correct, that is reputation, not a missing signature.

## Run Service Runtime During Development

```powershell
dotnet run --project .\src\OTM.Service\OTM.Service.csproj -- --console
```

To test the fullscreen kiosk shell after the service is running:

```powershell
dotnet run --project .\src\OTM.KioskShell\OTM.KioskShell.csproj
```

The kiosk shell is the user-facing locked workspace. It runs fullscreen, uses the SimpleKioskOS shield/monitor logo, shows a left rail of approved launchers, renders exam websites inside embedded WebView2 with no browser controls, and includes an in-shell **Open apps** taskbar for lab mode so approved apps can be brought back above the shell. Admin controls stay behind the smaller bottom-right **Admin** button. The shell hides the Windows taskbar while locked and installs a low-level keyboard hook to suppress common escape shortcuts such as Alt+F4, Alt+Tab, Ctrl+Esc, Ctrl+Shift+Esc, and Windows-key combinations. Ctrl+Alt+Del cannot be blocked by a normal Windows app and should be handled with Windows policy in production.

The Control Panel also includes a **Kiosk** tab for a dedicated single-purpose station. When enabled, managed mode auto-opens one approved website fullscreen inside WebView2 or starts one approved application as the primary kiosk app. This is intended for true web kiosks, museum displays, exam sites, and single-app shared stations.

Exam mode requires Microsoft Edge WebView2 Runtime on the target PC. The installer packages the Microsoft Evergreen WebView2 bootstrapper, checks Microsoft's WebView2 EdgeUpdate registry keys, and runs the bootstrapper silently if WebView2 is not registered. The bootstrapper needs internet access on the target PC. If WebView2 does not register after the bootstrapper runs, the installer shows a clear warning before opening the control panel.

Launcher policy supports:

- `type`: `web` or `app`
- `workspaceMode`: `exam`, `lab`, or `appOwner`
- `url` and `allowedSites` for embedded exam sites
- `path`, `processName`, and `arguments` for native apps
- `allowMultiMonitorOwnership` for approved apps that should own all displays until exit

The installer creates the desktop shortcut for the fullscreen SimpleKioskOS shell and opens the Control Panel after install. Launch the fullscreen shell only when you are ready to test kiosk mode. Installed test machines can also enable the fullscreen shell at sign-in:

```powershell
.\scripts\enable-kiosk-shell-startup.ps1
```

Disable it with:

```powershell
.\scripts\disable-kiosk-shell-startup.ps1
```

Branding assets live in `branding\`. The kiosk shell, secondary lock displays, native control panel, and installer payload use the SimpleKioskOS icon, side wordmark, and bottom wordmark.

## Install As Windows Service

Run PowerShell as Administrator:

```powershell
.\scripts\install-service.ps1
```

The installer publishes the projects, copies the app to `%ProgramFiles%\SimpleKioskOS`, creates `OTMKioskService`, starts it, and opens the fullscreen shell.

For normal testing on another machine, prefer the EXE installer from `scripts\build-installer.ps1`.

## Uninstall

For normal production-style uninstall, use Windows **Apps & features** or run:

```powershell
.\scripts\uninstall-production.ps1
```

This removes the app and service but keeps local SQLite data.

For test machines where you want a clean reinstall:

```powershell
.\scripts\uninstall-testing.ps1 -RemoveData
```

Omit `-RemoveData` if you want to keep the local database and recovery files.

## VPS Test Flow

Use a Windows VPS with a desktop experience, not Windows Server Core. Take a snapshot before installing because kiosk enforcement can intentionally block tools.

1. Copy `artifacts\installer\OTM-Kiosk-Setup.exe` to the VPS.
2. Run it as Administrator.
3. The **SimpleKioskOS Control Panel** opens after install. Start the fullscreen shell from the Start menu only after confirming the admin PIN works.
4. First-run PIN is `123456`.
5. Change the PIN before enabling a strict profile.
6. Add the remote-access app you use to the allowed list before enabling strict managed mode.

Avoid enabling strict whitelist on a remote VPS until your RDP/remote support tools are allowed, or you can lock yourself out of the session.

## Recovery

Run from an elevated terminal:

```powershell
dotnet run --project .\src\OTM.RecoveryTool\OTM.RecoveryTool.csproj
```

To disable enforcement and reset the admin PIN:

```powershell
dotnet run --project .\src\OTM.RecoveryTool\OTM.RecoveryTool.csproj -- --reset-pin
```

## Browser Policies

The service stores browser allow/block intent in the local policy. The MVP also includes a helper script for Edge/Chrome policy registry settings:

```powershell
.\scripts\apply-browser-policies.ps1
```

Use `-WhitelistOnly -AllowedSites @("https://example.edu/*")` for whitelist mode.

Uninstall now clears the Edge/Chrome URL and download policy values created by SimpleKioskOS. If an older test build or manual folder deletion left browser restrictions behind, open **PowerShell as Administrator** and run:

```powershell
.\scripts\clear-browser-policies.ps1
```

or:

```powershell
.\scripts\apply-browser-policies.ps1 -Clear
```

Installed builds also include **Start > SimpleKioskOS > Clear Browser Restrictions**, which launches the cleanup script elevated.

If the old build was deleted and the cleanup script is not available, open **PowerShell as Administrator** and paste this standalone cleanup:

```powershell
$roots=@("HKLM:\SOFTWARE\Policies\Microsoft\Edge","HKLM:\SOFTWARE\Policies\Google\Chrome","HKCU:\SOFTWARE\Policies\Microsoft\Edge","HKCU:\SOFTWARE\Policies\Google\Chrome"); $lists=@("URLBlocklist","URLAllowlist","URLBlacklist","URLWhitelist"); $values=@("DownloadRestrictions","SafeBrowsingAllowlistDomains"); foreach($root in $roots){ if(Test-Path $root){ foreach($value in $values){ Remove-ItemProperty -Path $root -Name $value -ErrorAction SilentlyContinue }; foreach($list in $lists){ $path=Join-Path $root $list; if(Test-Path $path){ Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue } } } }
```

Close every Edge/Chrome window after clearing policies. If the blocked page is still cached, open `edge://policy` or `chrome://policy` and choose **Reload policies**, or restart Windows.

## Simple App Rules

The native control panel includes **Simple App Rules** so admins can allow or block apps without editing policy JSON. Enter a display name, process name such as `chrome.exe`, or browse/type an EXE path, then choose **Add App** or **Block App**. Allowed apps can also be added to the fullscreen kiosk launcher automatically.

The Websites tab works the same way. Add an allowed website and keep **Show allowed site in Launchpad** checked to create an embedded WebView2 workspace directly.

## Management

Management is native-app first. The station installer includes the SimpleKioskOS Control Panel for setup, app rules, website rules, profiles, logs, and admin PIN changes. The service hosts a local/LAN API on port `47821` for the Control Panel, kiosk shell, and Remote Manager. The installer opens this port in Windows Firewall so LAN/VPN management can reach the station. There is no browser-based local manager UI.

The separate **SimpleKioskOS Remote Manager** app can track multiple stations by URL, refresh their status, send PIN-protected lock, unlock, restart, and shutdown commands, manage allowed/blocked app and website rules remotely, configure the remote-monitoring foundation, and trigger stable station update checks/downloads. Station update downloads are stored on the station and must be installed manually.

Remote monitoring is disabled by default and admin-PIN protected. The app stores and exposes monitoring settings through `/api/monitoring/config`, including LAN/VPN-only mode, screen-view permission, local/admin approval, and the planned secure transport. Live encrypted screen viewing should be implemented as a separate user-session monitor agent, not inside the LocalSystem service, because Windows services cannot reliably capture the interactive desktop. Do not expose the station API or a future VNC/RFB port directly to the public internet; use LAN, VPN, or a managed TLS relay.

If a station times out from Remote Manager, first test this from the manager PC:

```txt
http://STATION-IP:47821/api/status
```

For older test installs, run this on the station in **PowerShell as Administrator** to reopen the LAN API firewall rule:

```powershell
netsh advfirewall firewall delete rule name="SimpleKioskOS Local API"
netsh advfirewall firewall add rule name="SimpleKioskOS Local API" dir=in action=allow protocol=TCP localport=47821 profile=any
```

Also verify `OTMKioskService` is running on the station.

Build the Remote Manager installer with:

```powershell
.\scripts\build-manager-installer.ps1 -Version 8.1.2
```

## Security Notes

This is an MVP foundation, not a complete enterprise hardening product yet. The service currently focuses on process/download enforcement and local management durability. Production hardening should add signed binaries, tamper protection, Windows policy integration, audited installer packaging, stricter recovery ceremonies, Edge/Chrome policy synchronization from SQLite policy state, USB device enforcement, signed update manifest validation, remote policy audit trails, and integration tests on clean Windows images.
