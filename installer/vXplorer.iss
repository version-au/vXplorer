; vXplorer setup (Inno Setup 6), like vShell's. Packages dist\vXplorer.exe - build it first with build.cmd (which also runs this).
;
; Make the setup:  "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\vXplorer.iss
; It lands in dist\setup\vXplorer-setup.exe - the same name in every release, so a web page can link to the latest (the version is read from dist\vXplorer.exe).
;
; Installs for the current user only (no admin): %LOCALAPPDATA%\Programs\vXplorer. Settings and the log stay in %APPDATA%\vXplorer,
; so an existing setup carries straight over. Everything the options change is under HKCU and is undone by the uninstaller.

#define SourceExe "..\dist\vXplorer.exe"
#if !FileExists(SourceExe)
  #error dist\vXplorer.exe is missing - run build.cmd first
#endif
; "0.1.20.0" -> "0.1.20"
#define AppVer RemoveFileExt(GetVersionNumbersString(SourceExe))

[Setup]
; signed with the Certum certificate when build.cmd signs (/DSIGN /Svsuite=...sign.cmd): the setup and its uninstaller
#ifdef SIGN
SignTool=vsuite
#endif
AppId={{9C3E5B71-2A4D-4F8E-B6C1-5E7A9D2F0B84}
AppName=vXplorer
AppVersion={#AppVer}
AppVerName=vXplorer {#AppVer}
AppPublisher=Steven Van Teylingen
VersionInfoVersion={#AppVer}
DefaultDirName={autopf}\vXplorer
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; every running vXplorer holds this (Program.cs): setup and uninstall ask you to close it first
AppMutex=vXplorer.Running
SetupIconFile=..\vxplorer.ico
UninstallDisplayIcon={app}\vXplorer.exe
UninstallDisplayName=vXplorer
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
OutputDir=..\dist\setup
OutputBaseFilename=vXplorer-setup
; a newer setup over an older one: keeps the options picked last time
UsePreviousTasks=yes

[Messages]
SetupAppRunningError=vXplorer is open. Close its windows, then click OK to carry on.
UninstallAppRunningError=vXplorer is open. Close its windows, then click OK to carry on.

[Tasks]
Name: "replaceexplorer"; Description: "Open folders, drives and Win+E with vXplorer instead of File Explorer (the same as Options > Windows; the uninstaller puts File Explorer back)"; Flags: unchecked
Name: "openinmenu"; Description: "Add ""Open in vXplorer"" to the right-click menu of folders and drives"
Name: "desktopicon"; Description: "A shortcut on the desktop"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\vXplorer"; Filename: "{app}\vXplorer.exe"
Name: "{autodesktop}\vXplorer"; Filename: "{app}\vXplorer.exe"; Tasks: desktopicon

[Registry]
; "Open in vXplorer" on folders, drives and a folder's empty space (its own verb: "Open folders with vXplorer" uses "vxplorer")
Root: HKCU; Subkey: "Software\Classes\Directory\shell\vxplorer.open"; ValueType: string; ValueName: ""; ValueData: "Open in vXplorer"; Flags: uninsdeletekey; Tasks: openinmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\vxplorer.open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\vXplorer.exe"",0"; Tasks: openinmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\vxplorer.open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\vXplorer.exe"" ""%1"""; Tasks: openinmenu
Root: HKCU; Subkey: "Software\Classes\Drive\shell\vxplorer.open"; ValueType: string; ValueName: ""; ValueData: "Open in vXplorer"; Flags: uninsdeletekey; Tasks: openinmenu
Root: HKCU; Subkey: "Software\Classes\Drive\shell\vxplorer.open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\vXplorer.exe"",0"; Tasks: openinmenu
Root: HKCU; Subkey: "Software\Classes\Drive\shell\vxplorer.open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\vXplorer.exe"" ""%1"""; Tasks: openinmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\vxplorer.open"; ValueType: string; ValueName: ""; ValueData: "Open in vXplorer"; Flags: uninsdeletekey; Tasks: openinmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\vxplorer.open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\vXplorer.exe"",0"; Tasks: openinmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\vxplorer.open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\vXplorer.exe"" ""%V"""; Tasks: openinmenu

[Run]
; "Open folders with vXplorer": vXplorer writes (and remembers what it replaces) itself, so Options shows it on and can turn it off
Filename: "{app}\vXplorer.exe"; Parameters: "--default-file-manager on"; Flags: runhidden waituntilterminated; Tasks: replaceexplorer
Filename: "{app}\vXplorer.exe"; Description: "Start vXplorer now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; left by the updater next to the exe
Type: files; Name: "{app}\vXplorer.new.exe"
Type: files; Name: "{app}\vXplorer.old.exe"
Type: files; Name: "{app}\vXplorer.failed.exe"
Type: files; Name: "{app}\vxplorer-write-test.tmp"
Type: dirifempty; Name: "{app}"

[Code]
{ Is "Open folders with vXplorer" on, pointing at this installed copy? (Not one pointing at another vXplorer.exe, e.g. a build folder.) }
function ReplacingWithThisCopy(): Boolean;
var
  Cmd: String;
begin
  Result := RegQueryStringValue(HKEY_CURRENT_USER, 'Software\Classes\Directory\shell\vxplorer\command', '', Cmd) and
            (Pos(Lowercase(ExpandConstant('{app}\vXplorer.exe')), Lowercase(Cmd)) > 0);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    { vXplorer is closed by now (AppMutex). Put File Explorer back for folders, drives and Win+E - with whatever was the default
      before - if it's this copy that took them over. }
    if ReplacingWithThisCopy() and FileExists(ExpandConstant('{app}\vXplorer.exe')) then
      Exec(ExpandConstant('{app}\vXplorer.exe'), '--default-file-manager off', '', SW_HIDE, ewWaitUntilTerminated, Code);
  end;

  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() and DirExists(ExpandConstant('{userappdata}\vXplorer')) then
  begin
    if MsgBox('Also delete your vXplorer settings (tabs, Quick access, options) and log?' + #13#10 + #13#10 +
              ExpandConstant('{userappdata}\vXplorer') + #13#10 + #13#10 +
              'Keep them (No) if you might install vXplorer again - it picks up where you left off.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\vXplorer'), True, True, True);
  end;
end;
