; Setup für MusicWorker (Inno Setup 6) – wird beim Release automatisch gebaut.
; Aufruf: ISCC.exe /DAppVersion=1.1.0 /DSourceDir=<publish-Ordner> MusicWorker.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
; Feste Kennung – damit erkennt Windows Updates und Deinstallation über alle Versionen hinweg.
AppId={{92368C8D-F17A-4594-82F4-E49A59D2ACFA}
AppName=MusicWorker
AppVersion={#AppVersion}
AppVerName=MusicWorker {#AppVersion}
AppPublisher=MusicWorker
AppPublisherURL=https://github.com/PSanne/MusicWorker
AppSupportURL=https://github.com/PSanne/MusicWorker
AppUpdatesURL=https://github.com/PSanne/MusicWorker/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\MusicWorker
DefaultGroupName=MusicWorker
DisableProgramGroupPage=yes
; Ohne Administratorrechte für den eigenen Benutzer installierbar; auf Wunsch für alle Benutzer.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=MusicWorker-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\MusicWorker.exe
UninstallDisplayName=MusicWorker
ChangesAssociations=yes
CloseApplications=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "projectassoc"; Description: "MusicWorker-Projekte (.mwproj) per Doppelklick öffnen"; GroupDescription: "Dateizuordnung:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\MusicWorker"; Filename: "{app}\MusicWorker.exe"
Name: "{autodesktop}\MusicWorker"; Filename: "{app}\MusicWorker.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.mwproj"; ValueType: string; ValueName: ""; ValueData: "MusicWorker.Projekt"; Flags: uninsdeletevalue; Tasks: projectassoc
Root: HKA; Subkey: "Software\Classes\MusicWorker.Projekt"; ValueType: string; ValueName: ""; ValueData: "MusicWorker-Projekt"; Flags: uninsdeletekey; Tasks: projectassoc
Root: HKA; Subkey: "Software\Classes\MusicWorker.Projekt\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\MusicWorker.exe,0"; Tasks: projectassoc
Root: HKA; Subkey: "Software\Classes\MusicWorker.Projekt\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\MusicWorker.exe"" ""%1"""; Tasks: projectassoc

[Run]
Filename: "{app}\MusicWorker.exe"; Description: "{cm:LaunchProgram,MusicWorker}"; Flags: nowait postinstall skipifsilent
