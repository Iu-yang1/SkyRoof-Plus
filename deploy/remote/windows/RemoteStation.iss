; Station package installer. State and version payloads are not owned by Inno's uninstall log.
#ifndef PackageZip
  #error PackageZip must point to the tested Remote ZIP
#endif
#ifndef OutputPath
  #define OutputPath "Output"
#endif
[Setup]
AppId=SkyRoofRemoteStationAlpha
AppName=SkyRoof Remote Station
AppVersion=1.7
DefaultDirName={autopf}\SkyRoofRemote
DefaultGroupName=SkyRoof Remote
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputPath}
OutputBaseFilename=SkyRoof-Remote-Station-Setup
Compression=lzma2
SolidCompression=yes
DisableProgramGroupPage=yes
UninstallDisplayName=SkyRoof Remote Station (alpha)
[Files]
Source: "{#PackageZip}"; DestDir: "{tmp}"; DestName: "station.zip"; Flags: deleteafterinstall
Source: "*.ps1"; DestDir: "{app}\deployment"; Flags: ignoreversion
Source: "supervisor.icom9700.json.example"; DestDir: "{app}\deployment"; Flags: ignoreversion
[Code]
function PwshPath(): String;
begin
  Result := ExpandConstant('{pf}\PowerShell\7\pwsh.exe');
end;

function InitializeSetup(): Boolean;
begin
  Result := FileExists(PwshPath());
  if not Result then
    MsgBox('Install PowerShell 7 x64 before installing SkyRoof Remote Station.', mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\SkyRoofRemote-alpha') then
    Result := 'The alpha station already exists. Use the version switch script to upgrade it.';
  if FileExists(ExpandConstant('{app}\data\alpha\supervisor.json')) then
    Result := 'Existing alpha station data found. Restore or upgrade it using the deployment scripts.';
end;

function RunScript(const Script, Arguments: String): Boolean;
var
  Code: Integer;
begin
  Code := -1;
  Result := Exec(PwshPath(), '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ExpandConstant('{app}\deployment\') + Script + '" ' + Arguments,
    ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code);
  Result := Result and (Code = 0);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Args: String;
begin
  if CurStep <> ssPostInstall then exit;
  Args := '-Archive "' + ExpandConstant('{tmp}\station.zip') +
    '" -InstallRoot "' + ExpandConstant('{app}') +
    '" -StationId alpha -Template "' + ExpandConstant('{app}\deployment\supervisor.icom9700.json.example') +
    '" -RegisterOnly';
  if not RunScript('Install-RemoteStation.ps1', Args) then
    RaiseException('Station registration failed. Existing station configuration and data have not been overwritten.');
  if not SaveStringToFile(ExpandConstant('{app}\station-registration.txt'), 'SkyRoofRemote-alpha', False) then
    RaiseException('Could not record installer station ownership. Use the deployment scripts to manage the registered station.');
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not FileExists(ExpandConstant('{app}\station-registration.txt')) then exit;
  Result := FileExists(PwshPath()) and RunScript('Remove-RemoteService.ps1',
    '-ServiceName SkyRoofRemote-alpha -ExpectedConfiguration "' + ExpandConstant('{app}\data\alpha\supervisor.json') + '"');
  if Result then DeleteFile(ExpandConstant('{app}\station-registration.txt'));
  if not Result then MsgBox('Station service removal failed. Resolve it before uninstalling.', mbError, MB_OK);
end;
