; Inno Setup script - builds dist\UniversalPOS-Setup.exe
; Build locally:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\UniversalPOS.iss
; (run "dotnet publish UniversalPOS\UniversalPOS.vbproj -c Release -o publish" first)

#define AppName "Universal POS"
#define AppVersion "1.0.0"
#define AppExe "UniversalPOS.exe"

[Setup]
AppId={{6C1E5E3A-8D2B-4F2B-9A57-2B1F3D6A9C11}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\UniversalPOS
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=UniversalPOS-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

; Note: shop data lives in %LOCALAPPDATA%\UniversalPOS and is NOT removed on uninstall.
