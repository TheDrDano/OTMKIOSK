#define MyAppName "SimpleKioskOS Remote Manager"
#define MyAppVersion GetEnv("OTM_KIOSK_VERSION")
#if MyAppVersion == ""
  #define MyAppVersion "7.2.0"
#endif
#define MyAppPublisher "SimpleKioskOS"
#define ManagerExeName "OTM.Manager.exe"

[Setup]
AppId={{8A75836D-56C9-4F00-97FB-458F32E2D6AC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SimpleKioskOS Remote Manager
DefaultGroupName=SimpleKioskOS Remote Manager
DisableProgramGroupPage=yes
OutputDir=..\artifacts\manager-installer
OutputBaseFilename=SimpleKioskOS-Remote-Manager-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\Manager\{#ManagerExeName}
SetupIconFile=..\branding\simplekioskos.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\artifacts\manager-stage\manager\*"; DestDir: "{app}\Manager"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\branding\*.ico"; DestDir: "{app}\Branding"; Flags: ignoreversion
Source: "..\branding\*.png"; DestDir: "{app}\Branding"; Flags: ignoreversion

[Icons]
Name: "{group}\SimpleKioskOS Remote Manager"; Filename: "{app}\Manager\{#ManagerExeName}"; IconFilename: "{app}\Branding\simplekioskos.ico"
Name: "{group}\Uninstall SimpleKioskOS Remote Manager"; Filename: "{uninstallexe}"; IconFilename: "{app}\Branding\simplekioskos.ico"
Name: "{autodesktop}\SimpleKioskOS Remote Manager"; Filename: "{app}\Manager\{#ManagerExeName}"; IconFilename: "{app}\Branding\simplekioskos.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\Manager\{#ManagerExeName}"; Description: "Open SimpleKioskOS Remote Manager"; Flags: nowait postinstall skipifsilent
