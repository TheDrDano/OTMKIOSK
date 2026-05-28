#define MyAppName "OTM Kiosk"
#define MyAppVersion GetEnv("OTM_KIOSK_VERSION")
#if MyAppVersion == ""
  #define MyAppVersion "0.1.0"
#endif
#define MyAppPublisher "OTM"
#define MyAppExeName "OTM.ControlPanel.exe"
#define ServiceExeName "OTM.Service.exe"

[Setup]
AppId={{8A75836D-56C9-4F00-97FB-458F32E2D6AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\OTM Kiosk
DefaultGroupName=OTM Kiosk
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=OTM-Kiosk-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\ControlPanel\{#MyAppExeName}
CloseApplications=force
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\artifacts\stage\service\*"; DestDir: "{app}\Service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\stage\control-panel\*"; DestDir: "{app}\ControlPanel"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\artifacts\stage\recovery\*"; DestDir: "{app}\Recovery"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\uninstall-testing.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\scripts\uninstall-production.ps1"; DestDir: "{app}\Scripts"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\OTM Kiosk Control Panel"; Filename: "{app}\ControlPanel\{#MyAppExeName}"
Name: "{group}\OTM Kiosk Local Manager"; Filename: "http://localhost:47821"
Name: "{group}\OTM Kiosk Recovery Tool"; Filename: "{app}\Recovery\OTM.RecoveryTool.exe"
Name: "{group}\Uninstall OTM Kiosk"; Filename: "{uninstallexe}"
Name: "{autodesktop}\OTM Kiosk"; Filename: "{app}\ControlPanel\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{cmd}"; Parameters: "/c sc.exe create OTMKioskService binPath= ""{app}\Service\{#ServiceExeName}"" start= auto DisplayName= ""OTM Kiosk Service"""; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c sc.exe description OTMKioskService ""Local-first Windows lockdown and kiosk enforcement service."""; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c sc.exe failure OTMKioskService reset= 86400 actions= restart/5000/restart/10000/restart/30000"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c sc.exe start OTMKioskService"; Flags: runhidden waituntilterminated
Filename: "{app}\ControlPanel\{#MyAppExeName}"; Description: "Open OTM Kiosk Control Panel"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c sc.exe stop OTMKioskService"; Flags: runhidden waituntilterminated
Filename: "{cmd}"; Parameters: "/c sc.exe delete OTMKioskService"; Flags: runhidden waituntilterminated

[Code]
function InitializeSetup(): Boolean;
begin
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
