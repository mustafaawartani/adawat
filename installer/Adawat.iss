; Inno Setup script for Adawat. Built by .github/workflows/build.yml:
;   ISCC.exe /DAppVersion=0.1.0 installer\Adawat.iss
; Expects the published app in ..\publish

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{4B64AF9E-7E4B-4587-A719-C9AF07173802}
AppName=Adawat
AppVersion={#AppVersion}
AppVerName=Adawat {#AppVersion}
AppPublisher=Adawat
AppPublisherURL=https://github.com/mustafaawartani/adawat
AppSupportURL=https://github.com/mustafaawartani/adawat/issues
AppUpdatesURL=https://github.com/mustafaawartani/adawat/releases
; Per-user install: no administrator rights needed.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Adawat
DisableProgramGroupPage=yes
DisableDirPage=yes
DisableReadyPage=yes
OutputDir=Output
OutputBaseFilename=Adawat-Setup-{#AppVersion}
SetupIconFile=..\src\Adawat\Assets\adawat.ico
UninstallDisplayIcon={app}\Adawat.exe
UninstallDisplayName=Adawat
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Adawat"; Filename: "{app}\Adawat.exe"
Name: "{autodesktop}\Adawat"; Filename: "{app}\Adawat.exe"; Tasks: desktopicon

[Registry]
; Created by the app itself; listed here so uninstalling removes them.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Adawat"; Flags: dontcreatekey uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\Adawat.ExtractText"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\image\shell\Adawat.ExtractTranslate"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webp\shell\Adawat.ExtractText"; Flags: dontcreatekey uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.webp\shell\Adawat.ExtractTranslate"; Flags: dontcreatekey uninsdeletekey

[Run]
Filename: "{app}\Adawat.exe"; Description: "Start Adawat now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Adawat.exe /F"; Flags: runhidden; RunOnceId: "StopAdawat"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""Adawat"" /F"; Flags: runhidden; RunOnceId: "RemoveTask"

[Code]
// Close a running Adawat (it lives in the tray) before files are replaced.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Adawat.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
  Result := '';
end;
