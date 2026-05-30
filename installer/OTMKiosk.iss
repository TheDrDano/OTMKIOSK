#define MyAppName "SimpleKioskOS"
#define MyAppVersion GetEnv("OTM_KIOSK_VERSION")
#if MyAppVersion == ""
  #define MyAppVersion "9.1.1"
#endif
#define MyAppPublisher "SimpleKioskOS"
#define MyAppExeName "OTM.ControlPanel.exe"
#define ShellExeName "OTM.KioskShell.exe"
#define ServiceExeName "OTM.Service.exe"

[Setup]
AppId={{8A75836D-56C9-4F00-97FB-458F32E2D6AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SimpleKioskOS
DefaultGroupName=SimpleKioskOS
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=OTM-Kiosk-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\KioskShell\{#ShellExeName}
SetupIconFile=..\branding\simplekioskos.ico
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startupshell"; Description: "Start fullscreen kiosk shell when Windows signs in"; GroupDescription: "Kiosk shell:"
Name: "startupupdates"; Description: "Check GitHub and download verified updates when SimpleKioskOS starts"; GroupDescription: "Updates:"; Flags: unchecked

[Files]
Source: "..\artifacts\stage\service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\stage\control-panel\*"; DestDir: "{app}\ControlPanel"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\stage\kiosk-shell\*"; DestDir: "{app}\KioskShell"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\stage\recovery\*"; DestDir: "{app}\Recovery"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\stage\dependencies\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "..\branding\*.png"; DestDir: "{app}\Branding"; Flags: ignoreversion
Source: "..\branding\*.ico"; DestDir: "{app}\Branding"; Flags: ignoreversion
Source: "..\scripts\uninstall-testing.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\start-testing-uninstall-admin.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\uninstall-production.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\enable-kiosk-shell-startup.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\disable-kiosk-shell-startup.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\clear-browser-policies.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\start-clear-browser-policies-admin.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\verify-signatures.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\trust-test-signing-cert.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{commonappdata}\OTM Kiosk"
Name: "{commonappdata}\OTM Kiosk\WebView2"; Permissions: users-modify

[Icons]
Name: "{group}\SimpleKioskOS"; Filename: "{app}\KioskShell\{#ShellExeName}"; IconFilename: "{app}\Branding\simplekioskos.ico"
Name: "{group}\SimpleKioskOS Control Panel"; Filename: "{app}\ControlPanel\{#MyAppExeName}"; IconFilename: "{app}\Branding\simplekioskos.ico"
Name: "{group}\SimpleKioskOS Recovery Tool"; Filename: "{app}\Recovery\OTM.RecoveryTool.exe"; IconFilename: "{app}\Branding\simplekioskos.ico"
Name: "{group}\Emergency Testing Uninstall"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\Scripts\start-testing-uninstall-admin.ps1"""; IconFilename: "{app}\Branding\simplekioskos.ico"
Name: "{group}\Clear Browser Restrictions"; Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\Scripts\start-clear-browser-policies-admin.ps1"""; IconFilename: "{app}\Branding\simplekioskos.ico"
Name: "{group}\Uninstall SimpleKioskOS"; Filename: "{uninstallexe}"; IconFilename: "{app}\Branding\simplekioskos.ico"
Name: "{autodesktop}\SimpleKioskOS"; Filename: "{app}\KioskShell\{#ShellExeName}"; IconFilename: "{app}\Branding\simplekioskos.ico"; Tasks: desktopicon
Name: "{commonstartup}\SimpleKioskOS"; Filename: "{app}\KioskShell\{#ShellExeName}"; IconFilename: "{app}\Branding\simplekioskos.ico"; Tasks: startupshell

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SimpleKioskOS"; ValueData: """{app}\KioskShell\{#ShellExeName}"""; Tasks: startupshell; Flags: uninsdeletevalue

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime for exam/web kiosk mode..."; Check: not IsWebView2RuntimePresent; AfterInstall: VerifyWebView2Installed; Flags: waituntilterminated runhidden
Filename: "{sys}\sc.exe"; Parameters: "create OTMKioskService binPath= ""{app}\Service\{#ServiceExeName}"" start= auto DisplayName= ""SimpleKioskOS Service"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "description OTMKioskService ""Local-first fullscreen workspace and kiosk enforcement service."""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure OTMKioskService reset= 86400 actions= restart/5000/restart/10000/restart/30000"; Flags: runhidden waituntilterminated
Filename: "{app}\Service\{#ServiceExeName}"; Parameters: "--configure-startup-updates true"; Check: ShouldEnableStartupUpdates; Flags: runhidden waituntilterminated
Filename: "{app}\Service\{#ServiceExeName}"; Parameters: "--configure-startup-updates false"; Check: ShouldDisableStartupUpdates; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start OTMKioskService"; Flags: runhidden waituntilterminated
Filename: "{app}\ControlPanel\{#MyAppExeName}"; Description: "Open SimpleKioskOS Control Panel"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\Scripts\clear-browser-policies.ps1"" -Quiet"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure OTMKioskService reset= 0 actions= """""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "stop OTMKioskService"; Flags: runhidden waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/IM OTM.KioskShell.exe /F /T"; Flags: runhidden waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/IM OTM.Service.exe /F /T"; Flags: runhidden waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/IM OTM.ControlPanel.exe /F /T"; Flags: runhidden waituntilterminated
Filename: "{sys}\taskkill.exe"; Parameters: "/IM OTM.RecoveryTool.exe /F /T"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "delete OTMKioskService"; Flags: runhidden waituntilterminated

[Code]
function HasWebView2RuntimeVersion(RootKey: Integer; Subkey: String): Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(RootKey, Subkey, 'pv', Version) and
    (Version <> '') and
    (Version <> '0.0.0.0');
end;

function IsWebView2RuntimePresent(): Boolean;
begin
  Result :=
    HasWebView2RuntimeVersion(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
    HasWebView2RuntimeVersion(HKLM, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}') or
    HasWebView2RuntimeVersion(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}');
end;

function ShouldEnableStartupUpdates(): Boolean;
begin
  Result := WizardIsTaskSelected('startupupdates');
end;

function ShouldDisableStartupUpdates(): Boolean;
begin
  Result := not WizardIsTaskSelected('startupupdates');
end;

procedure VerifyWebView2Installed();
var
  Attempt: Integer;
begin
  for Attempt := 1 to 30 do
  begin
    if IsWebView2RuntimePresent() then
    begin
      Exit;
    end;

    Sleep(2000);
  end;

  MsgBox(
    'Microsoft Edge WebView2 Runtime did not finish installing or did not register correctly. ' +
    'Embedded exam/web kiosk mode will not work until WebView2 Runtime is installed. ' +
    'Make sure this PC has internet access during install, or install Microsoft Edge WebView2 Runtime manually.',
    mbError,
    MB_OK);
end;

function InitializeSetup(): Boolean;
begin
  if not IsWebView2RuntimePresent() then
  begin
    MsgBox('SimpleKioskOS exam/web mode uses Microsoft Edge WebView2 Runtime. The installer will run the Microsoft Evergreen WebView2 installer because WebView2 is not registered on this PC.', mbInformation, MB_OK);
  end;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'failure OTMKioskService reset= 0 actions= ""', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop OTMKioskService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM OTM.KioskShell.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM OTM.Service.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM OTM.ControlPanel.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM OTM.RecoveryTool.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete OTMKioskService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
  Result := '';
end;
