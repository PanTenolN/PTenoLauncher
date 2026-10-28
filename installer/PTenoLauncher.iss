; Compile: ISCC.exe /DAppVersion=0.2.0 /DPublishDir=C:\absolute\publish PTenoLauncher.iss
#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\0.2.0\win-x64"
#endif
[Setup]
AppId={{CF2E0B60-B0F5-4E74-9481-E0CE4ABBE030}
AppName=PTenoLauncher
AppVersion={#AppVersion}
AppPublisher=PTenoLauncher
DefaultDirName={localappdata}\Programs\PTenoLauncher
DefaultGroupName=PTenoLauncher
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts
OutputBaseFilename=PTenoLauncher-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\PTenoLauncher.Desktop\Assets\launcher.ico
UninstallDisplayIcon={app}\PTenoLauncher.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=PTenoLauncher
VersionInfoDescription=PTenoLauncher installer
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "portable.flag,portable-data\*,*.dpapi,*.pdb"
[Registry]
Root: HKCU; Subkey: "Software\PTenoLauncher"; ValueType: string; ValueName: "UninstallDataRoot"; ValueData: "{code:GetDataRoot}"; Flags: uninsdeletevalue
[Icons]
Name: "{group}\PTenoLauncher"; Filename: "{app}\PTenoLauncher.exe"
Name: "{autodesktop}\PTenoLauncher"; Filename: "{app}\PTenoLauncher.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\PTenoLauncher.exe"; Description: "{cm:LaunchProgram,PTenoLauncher}"; Flags: nowait postinstall skipifsilent
; User data live outside {app} and are deliberately absent from [UninstallDelete].
; Signing is enabled only when an actual certificate/signing service is supplied.

[Code]
var UninstallDataRoot: String;
function GetDataRoot(Param: String): String;
begin
  Result := ExpandConstant('{param:INTEGRATIONDATAROOT|}');
  if Result = '' then Result := ExpandConstant('{localappdata}\PTenoLauncher');
end;
function InitializeUninstall(): Boolean;
begin
  RegQueryStringValue(HKCU, 'Software\PTenoLauncher', 'UninstallDataRoot', UninstallDataRoot);
  Result := True;
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataRoot, Params: String;
  Purge: Boolean;
  ExitCode: Integer;
begin
  if CurUninstallStep <> usUninstall then exit;
  DataRoot := UninstallDataRoot;
  if DataRoot = '' then exit;
  Purge := ExpandConstant('{param:PURGEUSERDATA|0}') = '1';
  if not UninstallSilent then begin
    Purge := MsgBox('Delete profiles, worlds, backups and other PTenoLauncher data?' + #13#10 + DataRoot + #13#10 + 'Choose No to preserve your data.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;

  end;
  if Purge then begin
    Params := '--purge-user-data --data-root ' + AddQuotes(DataRoot) + ' --confirm-data-deletion PTenoLauncher';
    if not Exec(ExpandConstant('{app}\PTenoLauncher.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      if not UninstallSilent then MsgBox('User data cleanup could not be completed. Close Minecraft and preserve remaining data for manual review.', mbError, MB_OK);
  end;
end;
