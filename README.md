# OTM Kiosk

OTM Kiosk is a local-first native Windows lockdown and kiosk management app. The MVP in this repo includes:

- `OTM.Service`: Windows service runtime for process enforcement, downloads quarantine/delete, logs, policy persistence, and local manager API.
- `OTM.ControlPanel`: native WPF admin UI. No Electron.
- `OTM.RecoveryTool`: offline local recovery/reset utility.
- Local web manager: `http://localhost:47821`, hosted by the service.

## MVP Behavior

- Policy is stored locally at `%ProgramData%\OTM Kiosk\policy.json`.
- Logs are stored locally at `%ProgramData%\OTM Kiosk\events.jsonl`.
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
.\scripts\build-installer.ps1 -Version 0.1.0
```

The installer will be created in:

```txt
artifacts\installer\OTM-Kiosk-Setup-0.1.0.exe
```

By default the script publishes self-contained `win-x64` binaries, so the test VPS does not need the .NET runtime preinstalled. Use `-FrameworkDependent` only if you want a smaller installer and you know the target machine has the .NET 8 Desktop Runtime installed.

You can also build the installer in GitHub Actions. Push the repo to GitHub, open **Actions > Build Installer > Run workflow**, then download the `OTM-Kiosk-Installer` artifact.

## Run Service Runtime During Development

```powershell
dotnet run --project .\src\OTM.Service\OTM.Service.csproj -- --console
```

Then open:

```txt
http://localhost:47821
```

## Install As Windows Service

Run PowerShell as Administrator:

```powershell
.\scripts\install-service.ps1
```

The installer publishes the projects, copies the service to `%ProgramFiles%\OTM Kiosk`, creates `OTMKioskService`, and starts it.

For normal testing on another machine, prefer the EXE installer from `scripts\build-installer.ps1`.

## VPS Test Flow

Use a Windows VPS with a desktop experience, not Windows Server Core. Take a snapshot before installing because kiosk enforcement can intentionally block tools.

1. Copy `artifacts\installer\OTM-Kiosk-Setup-0.1.0.exe` to the VPS.
2. Run it as Administrator.
3. Open **OTM Kiosk Control Panel** from the Start menu.
4. First-run PIN is `123456`.
5. Change the PIN before enabling a strict profile.
6. Open `http://localhost:47821` to test the local web manager.
7. Apply the Flight Simulator preset only after adding the remote-access app you use to the allowed list.

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

## Flight Simulator Preset

The Flight Simulator preset enables enforcement, turns on strict application whitelisting, allows Microsoft Flight Simulator related processes, blocks common system tools, and blocks installer/download extensions.

It can be applied from either the native control panel or the local web manager.

Example profile JSON files are available in `profiles\`.

## Security Notes

This is an MVP foundation, not a complete enterprise hardening product yet. The service currently focuses on process/download enforcement and local management durability. Production hardening should add signed binaries, tamper protection, Windows policy integration, audited installer packaging, stricter recovery ceremonies, Edge/Chrome policy synchronization from `policy.json`, USB device enforcement, and integration tests on clean Windows images.
