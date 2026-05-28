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
.\scripts\build-installer.ps1 -Version 5.0.0
```

The installer will be created in:

```txt
artifacts\installer\OTM-Kiosk-Setup-5.0.0.exe
```

By default the script publishes self-contained `win-x64` binaries, so the test VPS does not need the .NET runtime preinstalled. The build also downloads Microsoft's Evergreen WebView2 bootstrapper and packages it into the SimpleKioskOS installer so embedded exam/web mode can install its browser runtime dependency on clean machines. Use `-FrameworkDependent` only if you want a smaller installer and you know the target machine has the .NET 8 Desktop Runtime installed.

You can also build the installer in GitHub Actions. Push the repo to GitHub, open **Actions > Build Installer > Run workflow**, then download the `OTM-Kiosk-Installer` artifact.

## EXE Code Signing

Windows publisher identity for an `.exe` uses **Authenticode code signing**, not an SSL/TLS certificate. Signing requires `signtool.exe`, which is installed with the Windows SDK, and a code-signing certificate issued for software signing.

For local signing with a certificate installed in the machine certificate store:

```powershell
.\scripts\build-installer.ps1 -Version 5.0.0 -Sign -CertificateThumbprint "YOUR_CERT_THUMBPRINT"
```

Use `-CertificateStore LocalMachine` if the certificate is installed in the local machine store instead of the current user store.

For local signing with a PFX:

```powershell
.\scripts\build-installer.ps1 -Version 5.0.0 -Sign -PfxPath "C:\secure\otm-signing.pfx" -PfxPassword "PFX_PASSWORD"
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
.\scripts\build-installer.ps1 -Version 5.0.0 -Sign -PfxPath ".\artifacts\signing\simplekioskos-test.pfx" -PfxPassword "test-password"
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

Then open:

```txt
http://localhost:47821
```

To test the fullscreen kiosk shell after the service is running:

```powershell
dotnet run --project .\src\OTM.KioskShell\OTM.KioskShell.csproj
```

The kiosk shell is the user-facing locked workspace. It runs fullscreen, uses the SimpleKioskOS shield/monitor logo, shows a left rail of approved launchers, renders exam websites inside embedded WebView2 with no browser controls, and includes an in-shell **Open apps** taskbar for lab mode so approved apps can be brought back above the shell. Admin controls stay behind the bottom-right **Admin** button. `Ctrl+Shift+A` also toggles the admin drawer for testing. For lockout recovery during MVP testing, `Ctrl+Shift+Alt+U` disables enforcement through the local recovery endpoint and `Ctrl+Shift+Alt+End` exits the shell. Common escape shortcuts such as Alt+F4, Alt+Tab, Ctrl+Esc, and Windows keys are suppressed while the shell has focus. Ctrl+Alt+Del cannot be blocked by a normal Windows app and should be handled with Windows policy in production.

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

Branding assets live in `branding\`. The kiosk shell, secondary lock displays, native control panel, local web manager, and installer payload use the SimpleKioskOS icon, side wordmark, and bottom wordmark.

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

This removes the app and service but keeps local SQLite data in `%ProgramData%\OTM Kiosk`.

For test machines where you want a clean reinstall:

```powershell
.\scripts\uninstall-testing.ps1 -RemoveData
```

Omit `-RemoveData` if you want to keep the local database and recovery files.

## VPS Test Flow

Use a Windows VPS with a desktop experience, not Windows Server Core. Take a snapshot before installing because kiosk enforcement can intentionally block tools.

1. Copy `artifacts\installer\OTM-Kiosk-Setup-5.0.0.exe` to the VPS.
2. Run it as Administrator.
3. The **SimpleKioskOS Control Panel** opens after install. Start the fullscreen shell from the Start menu only after confirming the admin PIN works.
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

## Simple App Rules

The native control panel and local web manager both include **Simple App Rules** so admins can allow or block apps without editing policy JSON. Enter a display name, process name such as `chrome.exe`, or browse/type an EXE path, then choose **Allow App** or **Block App**. Allowed apps can also be added to the fullscreen kiosk launcher automatically.

## Security Notes

This is an MVP foundation, not a complete enterprise hardening product yet. The service currently focuses on process/download enforcement and local management durability. Production hardening should add signed binaries, tamper protection, Windows policy integration, audited installer packaging, stricter recovery ceremonies, Edge/Chrome policy synchronization from SQLite policy state, USB device enforcement, and integration tests on clean Windows images.
