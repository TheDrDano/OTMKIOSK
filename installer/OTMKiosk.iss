#define MyAppName "SimpleKioskOS"
#define MyAppVersion GetEnv("OTM_KIOSK_VERSION")
#if MyAppVersion == ""
  #define MyAppVersion "4.0.0"
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
OutputBaseFilename=OTM-Kiosk-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\KioskShell\{#ShellExeName}
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startupshell"; Description: "Start fullscreen kiosk shell when Windows signs in"; GroupDescription: "Kiosk shell:"; Flags: unchecked

[Files]
Source: "..\artifacts\stage\service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\stage\control-panel\*"; DestDir: "{app}\ControlPanel"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\stage\kiosk-shell\*"; DestDir: "{app}\KioskShell"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\stage\recovery\*"; DestDir: "{app}\Recovery"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\stage\dependencies\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "..\branding\*.png"; DestDir: "{app}\Branding"; Flags: ignoreversion
Source: "..\scripts\uninstall-testing.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\uninstall-production.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\enable-kiosk-shell-startup.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\disable-kiosk-shell-startup.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\verify-signatures.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\trust-test-signing-cert.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SimpleKioskOS"; Filename: "{app}\KioskShell\{#ShellExeName}"
Name: "{group}\SimpleKioskOS Control Panel"; Filename: "{app}\ControlPanel\{#MyAppExeName}"
Name: "{group}\SimpleKioskOS Local Manager"; Filename: "http://localhost:47821"
Name: "{group}\SimpleKioskOS Recovery Tool"; Filename: "{app}\Recovery\OTM.RecoveryTool.exe"
Name: "{group}\Uninstall SimpleKioskOS"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SimpleKioskOS"; Filename: "{app}\KioskShell\{#ShellExeName}"; Tasks: desktopicon
Name: "{commonstartup}\SimpleKioskOS"; Filename: "{app}\KioskShell\{#ShellExeName}"; Tasks: startupshell

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime for exam/web kiosk mode..."; Check: not IsWebView2RuntimePresent; Flags: waituntilterminated
Filename: "{cmd}"; Parameters: "/c sc.exe create OTMKioskService binPath= ""{app}\Service\{#ServiceExeName}"" start= auto DisplayName= ""OTM Kiosk Service"""; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c sc.exe description OTMKioskService ""Local-first Windows lockdown and kiosk enforcement service."""; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c sc.exe failure OTMKioskService reset= 86400 actions= restart/5000/restart/10000/restart/30000"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c sc.exe start OTMKioskService"; Flags: runhidden waituntilterminated
Filename: "{app}\KioskShell\{#ShellExeName}"; Description: "Open SimpleKioskOS fullscreen shell"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c sc.exe stop OTMKioskService"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c sc.exe delete OTMKioskService"; Flags: runhidden waituntilterminated

[Code]
function IsWebView2RuntimePresent(): Boolean;
begin
  Result :=
    DirExists(ExpandConstant('{pf}\Microsoft\EdgeWebView\Application')) or
    DirExists(ExpandConstant('{pf32}\Microsoft\EdgeWebView\Application')) or
    FileExists(ExpandConstant('{sys}\MicrosoftEdgeWebView\msedgewebview2.exe'));
end;

function InitializeSetup(): Boolean;
begin
  if not IsWebView2RuntimePresent() then
  begin
    MsgBox('SimpleKioskOS exam/web mode uses Microsoft Edge WebView2 Runtime. The installer will run the Microsoft Evergreen WebView2 installer if it is not already present.', mbInformation, MB_OK);
  end;
  Result := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/c sc.exe stop OTMKioskService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{cmd}'), '/c sc.exe delete OTMKioskService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
  Result := '';
end;
