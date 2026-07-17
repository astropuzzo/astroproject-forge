#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif
#ifndef MyChannel
  #define MyChannel "Beta"
#endif
#ifndef MyNumericVersion
  #define MyNumericVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist-dotnet"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\distribution"
#endif

#if MyChannel == "Stable"
  #define ChannelSuffix ""
  #define ChannelAppId "{{C415EAC5-5B2C-4DB1-B349-1A70BB894F38}"
#else
  #define ChannelSuffix " Beta"
  #define ChannelAppId "{{BD5A66C8-8858-48EE-A36E-659D809D5549}"
#endif

[Setup]
AppId={#ChannelAppId}
AppName=AstroProject Forge{#ChannelSuffix}
AppVersion={#MyAppVersion}
AppVerName=AstroProject Forge{#ChannelSuffix} {#MyAppVersion}
AppPublisher=AstroProject Forge
AppPublisherURL=https://github.com/astropuzzo/astroproject-forge
DefaultDirName={localappdata}\Programs\AstroProject Forge{#ChannelSuffix}
DefaultGroupName=AstroProject Forge{#ChannelSuffix}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
SetupArchitecture=x64
OutputDir={#OutputDir}
OutputBaseFilename=AstroProjectForge-{#MyChannel}-{#MyAppVersion}-win-x64-setup
SetupIconFile=..\assets\astroforge.ico
UninstallDisplayIcon={app}\AstroForge.App.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
VersionInfoVersion={#MyNumericVersion}
VersionInfoProductName=AstroProject Forge
VersionInfoDescription=AstroProject Forge per PixInsight WBPP
ChangesAssociations=yes
DisableProgramGroupPage=yes

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; GroupDescription: "Collegamenti aggiuntivi:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\AstroForge.App.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\RELEASE-NOTES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\release-manifest.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\sbom-dotnet.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\AstroProject Forge{#ChannelSuffix}"; Filename: "{app}\AstroForge.App.exe"
Name: "{autodesktop}\AstroProject Forge{#ChannelSuffix}"; Filename: "{app}\AstroForge.App.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\.astroforge"; ValueType: string; ValueData: "AstroProjectForge.Project"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\AstroProjectForge.Project"; ValueType: string; ValueData: "Progetto AstroProject Forge"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\AstroProjectForge.Project\DefaultIcon"; ValueType: string; ValueData: "{app}\AstroForge.App.exe,0"
Root: HKCU; Subkey: "Software\Classes\AstroProjectForge.Project\shell\open\command"; ValueType: string; ValueData: """{app}\AstroForge.App.exe"" ""%1"""

[Code]
function InitializeUninstall(): Boolean;
var
  Choice: Integer;
begin
  Result := True;
  Choice := MsgBox(
    'Vuoi conservare impostazioni, cache e diagnostica locale?' + #13#10 + #13#10 +
    'I progetti .astroforge e le immagini FITS/XISF non vengono mai eliminati dal programma di disinstallazione.',
    mbConfirmation, MB_YESNO);
  if Choice = IDNO then
  begin
    if MsgBox('Confermi la rimozione dei soli dati locali in %LOCALAPPDATA%\AstroProjectForge?', mbConfirmation, MB_YESNO) = IDYES then
      DelTree(ExpandConstant('{localappdata}\AstroProjectForge'), True, True, True);
  end;
end;
