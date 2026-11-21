; Installer of Home Assistant Agent (Inno Setup 6).
;
; Packs the folder build\Publish.ps1 produces. Compile with:
;   ISCC.exe /DAppVersion=1.2.0 /DSourceDir=..\artifacts\app installer\HomeAssistantAgent.iss
;
; Layout:
;   {app}                               Program Files\HomeAssistant Agent — binaries
;                                       and the plugins installed from a catalog
;                                       (Plugins\), admin-only
;   {commonappdata}\HomeAssistantAgent  settings, plugin state, logs — shared by the
;                                       desktop app and the service, writable by users
;
; The same installer updates an existing installation: it closes the running
; agent the orderly way (offline announced to Home Assistant), stops the
; service, replaces the files and brings both back. The updater in the app
; runs it with /VERYSILENT /SUPPRESSMSGBOXES /NORESTART.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\artifacts\app"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define AppName "Home Assistant Agent"
#define AppExe "HomeAssistant.Agent.exe"
#define ServiceExe "HomeAssistant.Agent.Service.exe"
#define DataFolder "HomeAssistantAgent"
; The value the app writes for "Start with Windows" (AutoStart.RunValueName).
#define RunValueName "HomeAssistantAgent"
#define RepositoryUrl "https://github.com/Navatusein/Home-Assistant-Agent"
; Inbound firewall rule of the desktop app (see AllowInbound).
#define FirewallRule "Home Assistant Agent"

[Setup]
; Never change: Windows recognises installed versions by it.
AppId={{2B5FAF12-B0EC-4D92-B366-C70540780D22}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Navatusein
AppPublisherURL={#RepositoryUrl}
AppSupportURL={#RepositoryUrl}/issues
AppUpdatesURL={#RepositoryUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\HomeAssistant Agent
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004: the media session API the Windows plugin uses.
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=HomeAssistantAgent-Setup-{#AppVersion}
SetupIconFile=..\src\HomeAssistant.Agent\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The agent is closed by the [Code] below, which lets it go offline cleanly;
; the Restart Manager would simply terminate it.
CloseApplications=no
RestartApplications=no

[Tasks]
Name: "service"; Description: "Install the Windows service (power buttons and session state also before sign-in)"; Flags: checkedonce

[Dirs]
; Shared by the desktop app (the user) and the service (SYSTEM).
Name: "{commonappdata}\{#DataFolder}"; Permissions: users-modify
Name: "{commonappdata}\{#DataFolder}\Logs"; Permissions: users-modify

[Files]
; No plugins ship with the agent; the ones installed from a catalog sit in
; {app}\Plugins and are left alone by an update.
Source: "{#SourceDir}\*"; Excludes: "Plugins\*,portable"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
; The name and icon Windows notifications show for the agent. The app writes
; it at startup (for the user it runs as); only removed here.
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\Navatusein.HomeAssistantAgent"; Flags: uninsdeletekey dontcreatekey

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
; Started as the user who ran the installer, not as the elevated installer,
; so the agent lives in the user's session (tray, audio, clipboard).
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
; Same session as the user's agent, so the signal reaches it even from the
; elevated uninstaller.
Filename: "{app}\{#AppExe}"; Parameters: "--shutdown"; Flags: waituntilterminated; RunOnceId: "StopAgent"
Filename: "{app}\{#ServiceExe}"; Parameters: "uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "RemoveService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FirewallRule}"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallRule"

[Code]
var
  ServiceWasInstalled: Boolean;

// 0 running, 1 stopped, 2 not installed (ServiceInstaller "status"); -1 when
// there is no service executable yet (a first install).
function ServiceStatus(): Integer;
var
  ResultCode: Integer;
begin
  Result := -1;
  if FileExists(ExpandConstant('{app}\{#ServiceExe}')) then
    if Exec(ExpandConstant('{app}\{#ServiceExe}'), 'status', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Result := ResultCode;
end;

procedure RunService(const Verb: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{app}\{#ServiceExe}'), Verb, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Before any file is replaced: the agent and the service hold them open.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  Status: Integer;
begin
  Result := '';

  // The agent runs in the user's session; asking it there makes it exit the
  // way the tray's Exit does (offline announced to Home Assistant).
  if FileExists(ExpandConstant('{app}\{#AppExe}')) then
    ExecAsOriginalUser(ExpandConstant('{app}\{#AppExe}'), '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Status := ServiceStatus();
  ServiceWasInstalled := (Status = 0) or (Status = 1);

  if ServiceWasInstalled then
    RunService('stop');
end;

procedure Netsh(const Parameters: String);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\netsh.exe'), Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Lets the network reach the desktop app: a plugin that is reached from the
// network (Music Assistant connects to the Sendspin player, over TCP, and
// finds it over mDNS, UDP 5353) would otherwise wait for Windows to ask the
// user, who cannot allow it without being an administrator. Every protocol of
// this one program, on private and domain networks only — a public network is
// left to that prompt. Rewritten on every install, so it follows {app}.
procedure AllowInbound();
begin
  Netsh('advfirewall firewall delete rule name="{#FirewallRule}"');
  Netsh(ExpandConstant('advfirewall firewall add rule name="{#FirewallRule}" dir=in action=allow ' +
    'program="{app}\{#AppExe}" profile=private,domain enable=yes'));
end;

// After the files are in place: (re)register the service when it was there
// before or is asked for now. "install" also points an existing service at
// this folder — an update, or a move from a development build.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    AllowInbound();

    if ServiceWasInstalled or WizardIsTaskSelected('service') then
      RunService('install');
  end;
end;

// The data in ProgramData stays (settings, pairing tokens): a reinstall picks
// it up again. What goes is what points at the removed binaries.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#RunValueName}');
end;
