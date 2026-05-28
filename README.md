# OTM Kiosk

OTM Kiosk is a local-first native Windows lockdown and kiosk management app. The MVP in this repo includes:

- `OTM.Service`: Windows service runtime for process enforcement, downloads quarantine/delete, SQLite policy/log persistence, and local manager API.
- `OTM.ControlPanel`: native WPF admin UI. No Electron.
- `OTM.KioskShell`: fullscreen user-facing kiosk screen with approved application launchers and admin access.
- `OTM.RecoveryTool`: offline local recovery/reset utility.
- Local web manager: `http://localhost:47821`, hosted by the service.

## MVP Behavior

- Policy and logs are stored locally at `%ProgramData%\OTM Kiosk\otm-kiosk.db`.
- Existing `%ProgramData%\OTM Kiosk\policy.json` files are migrated into SQLite on first run.
- First-run admin PIN is `123456`; change it immediately from the control panel.
- A first-run recovery key is written to `%ProgramData%\OTM Kiosk\first-run-recovery-key.txt`.
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
.\scripts\build-installer.ps1 -Version 3.2.0
```

The installer will be created in:

```txt
artifacts\installer\OTM-Kiosk-Setup-3.2.0.exe
```

By default the script publishes self-contained `win-x64` binaries, so the test VPS does not need the .NET runtime preinstalled. Use `-FrameworkDependent` only if you want a smaller installer and you know the target machine has the .NET 8 Desktop Runtime installed.

You can also build the installer in GitHub Actions. Push the repo to GitHub, open **Actions > Build Installer > Run workflow**, then download the `OTM-Kiosk-Installer` artifact.

## Code Signing

Signing requires `signtool.exe`, which is installed with the Windows SDK.

For local signing with a certificate installed in the machine certificate store:

```powershell
.\scripts\build-installer.ps1 -Version 3.2.0 -Sign -CertificateThumbprint "YOUR_CERT_THUMBPRINT"
```

Use `-CertificateStore LocalMachine` if the certificate is installed in the local machine store instead of the current user store.

For local signing with a PFX:

```powershell
.\scripts\build-installer.ps1 -Version 3.2.0 -Sign -PfxPath "C:\secure\otm-signing.pfx" -PfxPassword "PFX_PASSWORD"
```

The build signs published EXE/DLL files before packaging and signs the final setup EXE after Inno Setup finishes.

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

Then run the **Build Installer** workflow. If the secrets are missing, the workflow still builds an unsigned installer.

Signing reduces Defender/SmartScreen friction and changes the publisher from unknown to your verified identity. It does not guarantee that SmartScreen warnings disappear immediately for a brand-new app; reputation still builds over time.

## Run Service Runtime During Development

```powershell
dotnet run --project .\src\OTM.Service\OTM.Service.csproj -- --console
```

Then open:

```txt
http://localhost:47821
```

To test the fullscreen kiosk shell after the service is running:

```powershell
dotnet run --project .\src\OTM.KioskShell\OTM.KioskShell.csproj
```

The kiosk shell is the user-facing locked workspace. It runs fullscreen, uses the OTM shield/monitor logo, shows a left rail of approved launchers, renders exam websites inside embedded WebView2 with no browser controls, and keeps admin controls behind the **Admin** button. Common escape shortcuts such as Alt+F4, Alt+Tab, Ctrl+Esc, and Windows keys are suppressed while the shell has focus. Ctrl+Alt+Del cannot be blocked by a normal Windows app and should be handled with Windows policy in production.

Exam mode requires Microsoft Edge WebView2 Runtime on the target PC. The installer shows a warning if WebView2 is not detected.

Launcher policy supports:

- `type`: `web` or `app`
- `workspaceMode`: `exam`, `lab`, or `appOwner`
- `url` and `allowedSites` for embedded exam sites
- `path`, `processName`, and `arguments` for native apps
- `allowMultiMonitorOwnership` for approved apps that should own all displays until exit

Installed test machines can enable the fullscreen shell at sign-in:

```powershell
.\scripts\enable-kiosk-shell-startup.ps1
```

Disable it with:

```powershell
.\scripts\disable-kiosk-shell-startup.ps1
```

Branding assets live in `branding\`. The kiosk shell currently uses the SimpleKioskOS icon, side wordmark, and bottom wordmark.

## Install As Windows Service

Run PowerShell as Administrator:

```powershell
.\scripts\install-service.ps1
```

The installer publishes the projects, copies the service to `%ProgramFiles%\OTM Kiosk`, creates `OTMKioskService`, and starts it.

For normal testing on another machine, prefer the EXE installer from `scripts\build-installer.ps1`.

## Uninstall

For normal production-style uninstall, use Windows **Apps & features** or run:

```powershell
.\scripts\uninstall-production.ps1
```

This removes the app and service but keeps local SQLite data in `%ProgramData%\OTM Kiosk`.

For test machines where you want a clean reinstall:

```powershell
.\scripts\uninstall-testing.ps1 -RemoveData
```

Omit `-RemoveData` if you want to keep the local database and recovery files.

## VPS Test Flow

Use a Windows VPS with a desktop experience, not Windows Server Core. Take a snapshot before installing because kiosk enforcement can intentionally block tools.

1. Copy `artifacts\installer\OTM-Kiosk-Setup-3.2.0.exe` to the VPS.
2. Run it as Administrator.
3. Open **OTM Kiosk Control Panel** from the Start menu.
4. First-run PIN is `123456`.
5. Change the PIN before enabling a strict profile.
6. Open `http://localhost:47821` to test the local web manager.
7. Apply the Exam or Lab template only after adding the remote-access app you use to the allowed list.

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

## Templates

The Exam Mode and Lab Lockdown templates enable enforcement, turn on strict application whitelisting, block common system tools, and block installer/download extensions.

Templates can be applied from either the native control panel or the local web manager.

Example profile JSON files are available in `profiles\`.

## Security Notes

This is an MVP foundation, not a complete enterprise hardening product yet. The service currently focuses on process/download enforcement and local management durability. Production hardening should add signed binaries, tamper protection, Windows policy integration, audited installer packaging, stricter recovery ceremonies, Edge/Chrome policy synchronization from SQLite policy state, USB device enforcement, and integration tests on clean Windows images.
