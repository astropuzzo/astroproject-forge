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
AppPublisher=Gianmarco Spagnoli
AppPublisherURL=https://github.com/astropuzzo/astroproject-forge
DefaultDirName={autopf}\AstroProject Forge{#ChannelSuffix}
DefaultGroupName=AstroProject Forge{#ChannelSuffix}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupArchitecture=x64
UsePreviousAppDir=no
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
; Install the complete verified publish output. WPF keeps a small set of native
; runtime DLLs beside the single-file executable on some Windows/.NET builds.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{group}\AstroProject Forge{#ChannelSuffix}"; Filename: "{app}\AstroForge.App.exe"
Name: "{autodesktop}\AstroProject Forge{#ChannelSuffix}"; Filename: "{app}\AstroForge.App.exe"; Check: ShouldCreateDesktopIcon

[Run]
; Manual installs keep the familiar optional launch checkbox.
Filename: "{app}\AstroForge.App.exe"; Description: "Avvia AstroProject Forge{#ChannelSuffix}"; Flags: nowait postinstall skipifsilent
; In-app updates relaunch the exact installed executable, including custom paths.
Filename: "{app}\AstroForge.App.exe"; Parameters: "--updated"; Flags: nowait; Check: IsAutomaticUpdate

[Registry]
Root: HKA; Subkey: "Software\Classes\.astroforge"; ValueType: string; ValueData: "AstroProjectForge.Project"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\AstroProjectForge.Project"; ValueType: string; ValueData: "Progetto AstroProject Forge"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\AstroProjectForge.Project\DefaultIcon"; ValueType: string; ValueData: "{app}\AstroForge.App.exe,0"
Root: HKA; Subkey: "Software\Classes\AstroProjectForge.Project\shell\open\command"; ValueType: string; ValueData: """{app}\AstroForge.App.exe"" ""%1"""

[Code]
var
  LegacyInstallDirectories: TArrayOfString;
  LegacyUninstallRegistryKeys: TArrayOfString;

function LegacyUninstallId(): String;
begin
#if MyChannel == "Stable"
  Result := '{C415EAC5-5B2C-4DB1-B349-1A70BB894F38}_is1';
#else
  Result := '{BD5A66C8-8858-48EE-A36E-659D809D5549}_is1';
#endif
end;

function IsExactLegacyInstallPath(Value: String): Boolean;
var
  NormalizedValue: String;
  RequiredSuffix: String;
begin
  NormalizedValue := RemoveBackslashUnlessRoot(ExpandFileName(Value));
  RequiredSuffix := '\AppData\Local\Programs\AstroProject Forge{#ChannelSuffix}';
  Result :=
    (Length(NormalizedValue) > Length(RequiredSuffix)) and
    (CompareText(
      Copy(NormalizedValue, Length(NormalizedValue) - Length(RequiredSuffix) + 1, Length(RequiredSuffix)),
      RequiredSuffix) = 0) and
    FileExists(AddBackslash(NormalizedValue) + 'AstroForge.App.exe');
end;

procedure DiscoverLegacyInstalls();
var
  UserSids: TArrayOfString;
  Index: Integer;
  Count: Integer;
  RegistryKey: String;
  InstallDirectory: String;
begin
  if not RegGetSubkeyNames(HKU, '', UserSids) then
    Exit;

  for Index := 0 to GetArrayLength(UserSids) - 1 do
  begin
    RegistryKey :=
      UserSids[Index] +
      '\Software\Microsoft\Windows\CurrentVersion\Uninstall\' +
      LegacyUninstallId();
    if
      RegQueryStringValue(HKU, RegistryKey, 'InstallLocation', InstallDirectory) and
      IsExactLegacyInstallPath(InstallDirectory)
    then
    begin
      Count := GetArrayLength(LegacyInstallDirectories);
      SetArrayLength(LegacyInstallDirectories, Count + 1);
      SetArrayLength(LegacyUninstallRegistryKeys, Count + 1);
      LegacyInstallDirectories[Count] := RemoveBackslashUnlessRoot(ExpandFileName(InstallDirectory));
      LegacyUninstallRegistryKeys[Count] := RegistryKey;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  DiscoverLegacyInstalls();
  Result := True;
end;

function ShouldCreateDesktopIcon(): Boolean;
begin
  Result :=
    WizardIsTaskSelected('desktopicon') or
    FileExists(ExpandConstant('{autodesktop}\AstroProject Forge{#ChannelSuffix}.lnk'));
end;

function IsAutomaticUpdate(): Boolean;
begin
  Result := CompareText(ExpandConstant('{param:APFUPDATE|0}'), '1') = 0;
end;

procedure RegisterExtraCloseApplicationsResources();
var
  Index: Integer;
begin
  for Index := 0 to GetArrayLength(LegacyInstallDirectories) - 1 do
    RegisterExtraCloseApplicationsResource(
      AddBackslash(LegacyInstallDirectories[Index]) + 'AstroForge.App.exe');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Index: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    for Index := 0 to GetArrayLength(LegacyInstallDirectories) - 1 do
      DelTree(LegacyInstallDirectories[Index], True, True, True);

    for Index := 0 to GetArrayLength(LegacyUninstallRegistryKeys) - 1 do
      RegDeleteKeyIncludingSubkeys(HKU, LegacyUninstallRegistryKeys[Index]);
  end;
end;

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
