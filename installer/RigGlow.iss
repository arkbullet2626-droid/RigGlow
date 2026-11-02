; RigGlow installer (Inno Setup 6). Built by build.bat or the GitHub release workflow:
;   ISCC.exe /DAppVersion=1.0.0 installer\RigGlow.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "RigGlow"
#define AppPublisher "Opifex Studios"
#define AppURL "https://opifexstudios.ca"
#define RepoURL "https://github.com/arkbullet2626-droid/RigGlow"
#define AppExe "RigGlow.exe"

[Setup]
AppId={{A91D155B-58E4-4A66-BC65-E67791D35F8D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#RepoURL}/issues
AppUpdatesURL={#RepoURL}/releases
AppCopyright=© 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer
DefaultDirName={autopf}\{#AppPublisher}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
InfoBeforeFile=before-install.txt
InfoAfterFile=after-install.txt
OutputDir=..\release
OutputBaseFilename=RigGlow-{#AppVersion}-Setup
SetupIconFile=..\src\RigGlow\Assets\RigGlow.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#AppName} when I sign in to Windows (recommended, so fan curves keep working)"; GroupDescription: "Startup:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Start with Windows through a scheduled task, so it can run with admin rights without a prompt.
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""RigGlow"" /SC ONLOGON /RL HIGHEST /DELAY 0000:15 /TR ""\""{app}\{#AppExe}\"" --minimized"""; Flags: runhidden; Tasks: startup
Filename: "{app}\{#AppExe}"; Description: "Open {#AppName} now"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
procedure StopRigGlow(Exe: String);
var
  Code: Integer;
begin
  { Ask a running copy to exit cleanly, so the fans are handed back to the motherboard. }
  if FileExists(Exe) then
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRigGlow(ExpandConstant('{app}\{#AppExe}'));
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopRigGlow(ExpandConstant('{app}\{#AppExe}'));
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /F /TN "RigGlow"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  end;
end;
