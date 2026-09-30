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

; The Avalonia app (dotnet/AstroForge.CrossPlatform) is the only UI on every platform.
; Releases up to 1.8.2 shipped the WPF executable AstroForge.App.exe instead.
#define MyAppExeName "AstroProjectForge.exe"
#define LegacyAppExeName "AstroForge.App.exe"

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
UninstallDisplayIcon={app}\{#MyAppExeName}
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

[CustomMessages]
italian.UpdateProgressTitle=Aggiornamento di AstroProject Forge
italian.UpdateProgressDescription=Installazione in corso. L'app si riavvierà automaticamente.
italian.UpdateProgressPhase=Aggiornamento dei file…
english.UpdateProgressTitle=Updating AstroProject Forge
english.UpdateProgressDescription=Installation in progress. The app will restart automatically.
english.UpdateProgressPhase=Updating files…

[Tasks]
Name: "desktopicon"; Description: "Crea un collegamento sul desktop"; GroupDescription: "Collegamenti aggiuntivi:"; Flags: unchecked

[Files]
; Install the complete verified publish output. Avalonia keeps its native rendering
; libraries (SkiaSharp, HarfBuzz, ANGLE) beside the single-file executable.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[InstallDelete]
; Upgrades from the WPF releases (<= 1.8.2): remove the retired executable and the native
; WPF runtime DLLs it kept beside it. The Start menu and desktop shortcuts keep their names,
; so the [Icons] entries below overwrite them to point at the new executable.
Type: files; Name: "{app}\{#LegacyAppExeName}"
Type: files; Name: "{app}\D3DCompiler_47_cor3.dll"
Type: files; Name: "{app}\PenImc_cor3.dll"
Type: files; Name: "{app}\PresentationNative_cor3.dll"
Type: files; Name: "{app}\vcruntime140_cor3.dll"
Type: files; Name: "{app}\wpfgfx_cor3.dll"

[Icons]
Name: "{group}\AstroProject Forge{#ChannelSuffix}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\AstroProject Forge{#ChannelSuffix}"; Filename: "{app}\{#MyAppExeName}"; Check: ShouldCreateDesktopIcon

[Run]
; Manual installs keep the familiar optional launch checkbox.
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia AstroProject Forge{#ChannelSuffix}"; Flags: nowait postinstall skipifsilent
; In-app updates relaunch the exact installed executable, including custom paths.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--updated"; Flags: nowait; Check: IsAutomaticUpdate

[Registry]
Root: HKA; Subkey: "Software\Classes\.astroforge"; ValueType: string; ValueData: "AstroProjectForge.Project"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\AstroProjectForge.Project"; ValueType: string; ValueData: "Progetto AstroProject Forge"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\AstroProjectForge.Project\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\AstroProjectForge.Project\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""

[Code]
var
  LegacyInstallDirectories: TArrayOfString;
  LegacyUninstallRegistryKeys: TArrayOfString;
  UpdateProgressForm: TSetupForm;
  UpdateProgressBar: TNewProgressBar;
  UpdateProgressLabel: TNewStaticText;
  UpdatePercentLabel: TNewStaticText;

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
    (FileExists(AddBackslash(NormalizedValue) + '{#LegacyAppExeName}') or
     FileExists(AddBackslash(NormalizedValue) + '{#MyAppExeName}'));
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

function IsAutomaticUpdate(): Boolean; forward;

function NeedsCompatibilityProgress(): Boolean;
begin
  Result :=
    IsAutomaticUpdate() and
    (CompareText(ExpandConstant('{param:APFVISIBLE|0}'), '1') <> 0);
end;

procedure InitializeWizard();
begin
  if not NeedsCompatibilityProgress() then
    Exit;

  UpdateProgressForm := CreateCustomForm(ScaleX(480), ScaleY(150), False, False);
  UpdateProgressForm.Caption := ExpandConstant('{cm:UpdateProgressTitle}');
  UpdateProgressForm.Position := poScreenCenter;

  UpdateProgressLabel := TNewStaticText.Create(UpdateProgressForm);
  UpdateProgressLabel.Parent := UpdateProgressForm;
  UpdateProgressLabel.Left := ScaleX(24);
  UpdateProgressLabel.Top := ScaleY(22);
  UpdateProgressLabel.Width := ScaleX(432);
  UpdateProgressLabel.Height := ScaleY(42);
  UpdateProgressLabel.AutoSize := False;
  UpdateProgressLabel.WordWrap := True;
  UpdateProgressLabel.Caption := ExpandConstant('{cm:UpdateProgressDescription}');

  UpdateProgressBar := TNewProgressBar.Create(UpdateProgressForm);
  UpdateProgressBar.Parent := UpdateProgressForm;
  UpdateProgressBar.Left := ScaleX(24);
  UpdateProgressBar.Top := ScaleY(82);
  UpdateProgressBar.Width := ScaleX(432);
  UpdateProgressBar.Height := ScaleY(18);
  UpdateProgressBar.Min := 0;
  UpdateProgressBar.Max := 100;

  UpdatePercentLabel := TNewStaticText.Create(UpdateProgressForm);
  UpdatePercentLabel.Parent := UpdateProgressForm;
  UpdatePercentLabel.Left := ScaleX(24);
  UpdatePercentLabel.Top := ScaleY(111);
  UpdatePercentLabel.Width := ScaleX(432);
  UpdatePercentLabel.Alignment := taCenter;
  UpdatePercentLabel.Caption := ExpandConstant('{cm:UpdateProgressPhase}');
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

// Inno Setup 7 dropped the DisableFsRedir argument of RegisterExtraCloseApplicationsResource.
procedure RegisterCloseResource(const Path: String);
begin
#if Ver >= 0x07000000
  RegisterExtraCloseApplicationsResource(Path);
#else
  RegisterExtraCloseApplicationsResource(False, Path);
#endif
end;

procedure RegisterExtraCloseApplicationsResources();
var
  Index: Integer;
begin
  // A WPF-era AstroForge.App.exe in the target folder is deleted, not replaced, so it
  // must be registered explicitly for Restart Manager to close it first.
  RegisterCloseResource(ExpandConstant('{app}\{#LegacyAppExeName}'));
  for Index := 0 to GetArrayLength(LegacyInstallDirectories) - 1 do
  begin
    RegisterCloseResource(AddBackslash(LegacyInstallDirectories[Index]) + '{#LegacyAppExeName}');
    RegisterCloseResource(AddBackslash(LegacyInstallDirectories[Index]) + '{#MyAppExeName}');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Index: Integer;
begin
  if (UpdateProgressForm <> nil) and (CurStep = ssInstall) then
  begin
    UpdateProgressForm.Show();
    UpdateProgressForm.Update();
  end;

  if CurStep = ssPostInstall then
  begin
    if UpdateProgressForm <> nil then
      UpdateProgressForm.Hide();

    for Index := 0 to GetArrayLength(LegacyInstallDirectories) - 1 do
      DelTree(LegacyInstallDirectories[Index], True, True, True);

    for Index := 0 to GetArrayLength(LegacyUninstallRegistryKeys) - 1 do
      RegDeleteKeyIncludingSubkeys(HKU, LegacyUninstallRegistryKeys[Index]);
  end;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  Percent: Integer;
begin
  if (UpdateProgressForm = nil) or (MaxProgress <= 0) then
    Exit;

  Percent := (CurProgress * 100) div MaxProgress;
  UpdateProgressBar.Position := Percent;
  UpdatePercentLabel.Caption :=
    ExpandConstant('{cm:UpdateProgressPhase}') + ' ' + IntToStr(Percent) + '%';
  UpdateProgressForm.Update();
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
