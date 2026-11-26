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

function RunScript(const Script, Arguments: String): Boolean;
var
  Code: Integer;
begin
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
end;

function InitializeUninstall(): Boolean;
begin
  Result := FileExists(PwshPath()) and RunScript('Remove-RemoteService.ps1', '-ServiceName SkyRoofRemote-alpha');
  if not Result then MsgBox('Station service removal failed. Resolve it before uninstalling.', mbError, MB_OK);
end;
