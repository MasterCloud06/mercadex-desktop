#define MyAppName "Mercadex"
#define MyAppVersion "1.6.9"
#define MyAppPublisher "Mercadex"
#define MyAppExeName "Mercadex.Desktop.App.exe"

[Setup]
SetupIconFile=Assets\mercadex.ico
AppId={{F751D3B7-63C9-45D2-B0CB-7A994BBE8A10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}

DefaultDirName={autopf}\Mercadex
DefaultGroupName=Mercadex
DisableProgramGroupPage=yes

OutputDir=installer
OutputBaseFilename=Mercadex-Setup-{#MyAppVersion}-win-x64

Compression=lzma2
SolidCompression=yes
WizardStyle=modern

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

PrivilegesRequired=admin
CloseApplications=yes
RestartApplications=no

UninstallDisplayName=Mercadex
UninstallDisplayIcon={app}\{#MyAppExeName}

VersionInfoVersion=1.6.0.0
VersionInfoCompany=Mercadex
VersionInfoDescription=Sistema de gestión y recaudación Mercadex
VersionInfoProductName=Mercadex
VersionInfoProductVersion=1.6.0

SetupLogging=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Mercadex"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Mercadex"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir Mercadex"; Flags: nowait postinstall skipifsilent