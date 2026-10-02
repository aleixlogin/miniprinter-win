; MiniPrinter installer (Inno Setup 6).
; Build: scripts\build-installer.ps1 -Version X.Y.Z   (passes /DAppVersion=X.Y.Z)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "MiniPrinter"
#define ServiceName "MiniPrinter"
#define QueueName "X5h Thermal Printer"
#define IppPort "8631"
#define TrayExe "MiniPrinter.Tray.exe"

[Setup]
AppId={{8F6C2B1E-3D47-4A0B-9E61-5C2D7A4B9F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=aleixlogin
AppPublisherURL=https://github.com/aleixlogin/miniprinter-win
AppSupportURL=https://github.com/aleixlogin/miniprinter-win/issues
AppUpdatesURL=https://github.com/aleixlogin/miniprinter-win/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\installer
OutputBaseFilename=MiniPrinter-Setup-{#AppVersion}
SetupIconFile=..\assets\miniprinter.ico
UninstallDisplayIcon={app}\{#TrayExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2/ultra
SolidCompression=yes
; The tray app is closed by [Code] (CloseTray) before copying files. No AppMutex: Inno checks it at
; startup, before [Code] runs, and would ask the user to close the app even in /VERYSILENT mode.
CloseApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
spanish.OpenApp=Abrir MiniPrinter
english.OpenApp=Open MiniPrinter
spanish.Uninstall=Desinstalar MiniPrinter
english.Uninstall=Uninstall MiniPrinter
spanish.DownloadingRuntime=Descargando %1…
english.DownloadingRuntime=Downloading %1…
spanish.RuntimeFailed=No se pudo instalar %1. Descárgalo de https://dotnet.microsoft.com/download/dotnet/8.0 e inténtalo de nuevo.
english.RuntimeFailed=Could not install %1. Download it from https://dotnet.microsoft.com/download/dotnet/8.0 and try again.
spanish.KeepConfig=¿Conservar la configuración de MiniPrinter (impresora elegida, ajustes, tokens)?%n%nElige «Sí» si vas a volver a instalarlo.
english.KeepConfig=Keep the MiniPrinter configuration (selected printer, settings, tokens)?%n%nChoose "Yes" if you will reinstall it.
spanish.ServiceNotListening=El servicio MiniPrinter no ha abierto el puerto IPP {#IppPort}. Revisa el Visor de eventos (Aplicación, origen MiniPrinter).
english.ServiceNotListening=The MiniPrinter service did not open IPP port {#IppPort}. Check the Event Viewer (Application, source MiniPrinter).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Start from an empty program folder: leftovers from older layouts (e.g. the self-contained build of
; the PowerShell scripts, whose hostfxr.dll makes the new framework-dependent executables look for
; .NET inside this folder) must not survive. Settings live in %ProgramData%, not here.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#TrayExe}"; Parameters: "--open"; IconFilename: "{app}\{#TrayExe}"
Name: "{group}\{cm:Uninstall}"; Filename: "{uninstallexe}"; IconFilename: "{app}\{#TrayExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#TrayExe}"; Parameters: "--open"; Tasks: desktopicon
Name: "{usersendto}\{#AppName}"; Filename: "{app}\{#TrayExe}"; Parameters: "--print"; Comment: "Imprimir en la impresora térmica"

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#TrayExe}"""; Flags: uninsdeletevalue

[Run]
; Interactive install: optional checkbox on the last page. Silent install (auto-update): always reopen.
; runasoriginaluser starts the tray without the installer's elevation.
Filename: "{app}\{#TrayExe}"; Parameters: "--open"; Description: "{cm:OpenApp}"; Flags: nowait postinstall runasoriginaluser skipifsilent
Filename: "{app}\{#TrayExe}"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
const
  DotnetKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\';

var
  ResultCodeDummy: Integer;

function Run(const FileName, Params: String): Integer;
var
  Code: Integer;
begin
  if not Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Result := Code;
end;

function PowerShell(const Command: String): Integer;
begin
  Result := Run(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + Command + '"');
end;

function Sc(const Params: String): Integer;
begin
  Result := Run(ExpandConstant('{sys}\sc.exe'), Params);
end;

{ True when a .NET 8.x shared framework (e.g. Microsoft.WindowsDesktop.App) is installed for x64. }
function HasRuntime(const Framework: String): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(HKLM64, DotnetKey + Framework, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '8.' then
        Result := True;
end;

function InstallRuntime(const Framework, DisplayName, Url, FileName: String): String;
var
  Code: Integer;
begin
  Result := '';
  if HasRuntime(Framework) then
    Exit;
  WizardForm.StatusLabel.Caption := FmtMessage(CustomMessage('DownloadingRuntime'), [DisplayName]);
  try
    DownloadTemporaryFile(Url, FileName, '', nil);
    Code := Run(ExpandConstant('{tmp}\') + FileName, '/install /quiet /norestart');
    { 0 = ok, 3010 = ok but reboot needed, 1638 = a newer version is already installed }
    if (Code <> 0) and (Code <> 3010) and (Code <> 1638) then
      Result := FmtMessage(CustomMessage('RuntimeFailed'), [DisplayName]);
  except
    Result := FmtMessage(CustomMessage('RuntimeFailed'), [DisplayName]);
  end;
end;

{ Asks the running tray to exit (MiniPrinter.Tray.exe --exit), waits, then forces it if needed. }
procedure CloseTray(const TrayPath: String);
begin
  if FileExists(TrayPath) then
  begin
    Exec(TrayPath, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCodeDummy);
    PowerShell('Wait-Process -Name MiniPrinter.Tray -Timeout 8 -ErrorAction SilentlyContinue');
  end;
  Run(ExpandConstant('{sys}\taskkill.exe'), '/IM {#TrayExe} /F');
end;

{ Runs before files are copied: prerequisites, then stop what holds the files. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := InstallRuntime('Microsoft.AspNetCore.App', 'ASP.NET Core Runtime 8',
    'https://aka.ms/dotnet/8.0/aspnetcore-runtime-win-x64.exe', 'aspnetcore-runtime-8.exe');
  if Result = '' then
    Result := InstallRuntime('Microsoft.WindowsDesktop.App', '.NET Desktop Runtime 8',
      'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe', 'windowsdesktop-runtime-8.exe');
  if Result <> '' then
    Exit;

  CloseTray(ExpandConstant('{app}\{#TrayExe}'));
  Sc('stop {#ServiceName}');
  { Give the service process time to exit and release its files. }
  PowerShell('$s = Get-Service {#ServiceName} -ErrorAction SilentlyContinue; if ($s) { $s.WaitForStatus(''Stopped'', ''00:00:30'') }');
end;

procedure ConfigureSystem();
var
  DataDir, Binary: String;
begin
  DataDir := ExpandConstant('{commonappdata}\MiniPrinter');
  ForceDirectories(DataDir);
  { SYSTEM and Administrators: full control. Users: read (the tray reads the control token). }
  Run(ExpandConstant('{sys}\icacls.exe'),
    '"' + DataDir + '" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX');

  Binary := '"\"' + ExpandConstant('{app}') + '\MiniPrinter.Service.exe\""';
  if Sc('query {#ServiceName}') = 0 then
    Sc('config {#ServiceName} binPath= ' + Binary + ' start= auto')
  else
    Sc('create {#ServiceName} binPath= ' + Binary + ' start= auto DisplayName= "MiniPrinter"');
  Sc('description {#ServiceName} "Exposes Bluetooth thermal printers (X5h-E07A) as a standard IPP printer."');
  Sc('failure {#ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/30000');
  Sc('start {#ServiceName}');

  { Wait for the IPP endpoint, then create the print queue once. }
  if PowerShell('$deadline = (Get-Date).AddSeconds(30); ' +
      'do { Start-Sleep -Milliseconds 500; $ok = Test-NetConnection 127.0.0.1 -Port {#IppPort} -InformationLevel Quiet -WarningAction SilentlyContinue } ' +
      'until ($ok -or (Get-Date) -gt $deadline); if (-not $ok) { exit 1 }') <> 0 then
  begin
    if not WizardSilent then
      MsgBox(CustomMessage('ServiceNotListening'), mbError, MB_OK);
    Exit;
  end;
  PowerShell('if (-not (Get-Printer -Name ''{#QueueName}'' -ErrorAction SilentlyContinue)) { ' +
    'Add-Printer -Name ''{#QueueName}'' -IppURL ''http://127.0.0.1:{#IppPort}/ipp/print'' }');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    ConfigureSystem();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  KeepConfig: Boolean;
begin
  if CurUninstallStep = usUninstall then
  begin
    CloseTray(ExpandConstant('{app}\{#TrayExe}'));
    PowerShell('$p = Get-Printer -Name ''{#QueueName}'' -ErrorAction SilentlyContinue; ' +
      'if ($p) { Get-PrintJob -PrinterName ''{#QueueName}'' -ErrorAction SilentlyContinue | Remove-PrintJob -ErrorAction SilentlyContinue; ' +
      'Remove-Printer -Name ''{#QueueName}''; if ($p.PortName) { Remove-PrinterPort -Name $p.PortName -ErrorAction SilentlyContinue } }');
    Sc('stop {#ServiceName}');
    PowerShell('$s = Get-Service {#ServiceName} -ErrorAction SilentlyContinue; if ($s) { $s.WaitForStatus(''Stopped'', ''00:00:30'') }');
    Sc('delete {#ServiceName}');
    Run(ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall delete rule name="MiniPrinter IPP"');
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { Silent uninstalls keep the configuration. }
    KeepConfig := UninstallSilent or
      (MsgBox(CustomMessage('KeepConfig'), mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES);
    if not KeepConfig then
      DelTree(ExpandConstant('{commonappdata}\MiniPrinter'), True, True, True);
  end;
end;
