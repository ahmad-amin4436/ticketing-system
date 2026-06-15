; Inno Setup script for Indian Ticketing (Trial Version)
; -----------------------------------------------------
; Build the app first with:
;   dotnet publish -c Release -r win-x64 --self-contained true
; Then compile this script with Inno Setup (ISCC.exe indian-ticketing.iss)
; The output installer is written to the .\Output folder.

#define MyAppName "Indian Ticketing"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Indian Ticketing"
#define MyAppExeName "indian-ticketing.exe"

; Path to the published self-contained build (relative to this .iss file).
#define PublishDir "..\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

[Setup]
; A unique AppId keeps upgrades/uninstalls tied to the same product.
AppId={{8F3A1C42-7B9E-4D5A-9E21-6C0F2A4B8D71}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion} (Trial)
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=indian-ticketing-setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The self-contained .NET runtime is 64-bit only.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayName={#MyAppName} (Trial)

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Package the entire self-contained publish output (exe + .NET runtime + WebView2 + dependencies).
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Messages]
; Surface the trial nature in the welcome page.
WelcomeLabel2=This will install [name/ver] on your computer.%n%nThis is a TRIAL VERSION and will stop working after 17 June 2026.%n%nIt is recommended that you close all other applications before continuing.
