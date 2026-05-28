# Branding Assets

Place temporary test branding here until the app has final packaged assets.

Suggested files:

- `otm-logo.png`
- `otm-kiosk-logo.png`
- `blocked-background.jpg`
- `app-icon.ico`

Current main logo:

- `simplekioskos.png`
- `simplekioskos_side.png`
- `simplekioskos_bottom.png`
- `simplekioskos_app_icon.png`
- `simplekioskos.ico`

The fullscreen kiosk shell embeds these under `src\OTM.KioskShell\Assets\`. The installer also copies this folder to `{app}\Branding`. `simplekioskos.ico` is generated from `simplekioskos_app_icon.png` by `scripts\create-simplekioskos-icon.ps1` and is used for the setup EXE, app EXEs, and Windows shortcuts. If the app icon PNG is missing, the script falls back to `simplekioskos.png`.
