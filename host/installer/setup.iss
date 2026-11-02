#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
[Setup]
AppId={{D477418B-ED93-4F2F-AD2D-9104CF587CEA}
AppName=Cemu Phone GamePad
AppVersion={#AppVersion}
AppPublisher=TheStonedGamer
AppPublisherURL=https://github.com/TheStonedGamer/cemu-phone-gamepad
AppSupportURL=https://github.com/TheStonedGamer/cemu-phone-gamepad/issues
AppUpdatesURL=https://github.com/TheStonedGamer/cemu-phone-gamepad/releases/latest
DefaultDirName={localappdata}\Programs\Cemu Phone GamePad
DefaultGroupName=Cemu Phone GamePad
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
AppMutex=Local\CemuPhoneGamePad.Host
OutputDir=..\..\dist
OutputBaseFilename=Cemu-Phone-GamePad-Windows-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Cemu-Phone-GamePad.exe
LicenseFile=..\..\LICENSE
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: startup; Description: "Open the host when I sign in to Windows"; GroupDescription: "Startup:"; Flags: unchecked
[Files]
Source: "..\..\dist\windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Cemu Phone GamePad"; Filename: "{app}\Cemu-Phone-GamePad.exe"
Name: "{autodesktop}\Cemu Phone GamePad"; Filename: "{app}\Cemu-Phone-GamePad.exe"; Tasks: desktopicon
Name: "{userstartup}\Cemu Phone GamePad"; Filename: "{app}\Cemu-Phone-GamePad.exe"; Tasks: startup
[Run]
Filename: "{app}\Cemu-Phone-GamePad.exe"; Description: "Open Cemu Phone GamePad"; Flags: nowait postinstall skipifsilent
