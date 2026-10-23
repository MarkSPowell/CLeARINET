; VioLens Windows installer (Inno Setup).
;
; Built by .github/workflows/release-windows.yml, which passes MyAppVersion,
; MyFileVersion, PublishDir, LegacyHostDir and ExtensionsDir with /D. The
; fallbacks below let ISCC.exe compile it on its own for testing.
;
; MyFileVersion is the numeric form of MyAppVersion ("0.1.0-preview.1"
; becomes "0.1.0.0"), because VersionInfoVersion rejects a prerelease
; suffix.
;
; One setup.exe installs either for all users (Program Files, with an admin
; prompt) or just for the current user (AppData\Local\Programs, no prompt),
; chosen at install time: PrivilegesRequired=lowest with
; PrivilegesRequiredOverridesAllowed=dialog, and {auto*} paths throughout.
; The app keeps its own data in the user's profile either way.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef MyFileVersion
  #define MyFileVersion "0.0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif
#ifndef LegacyHostDir
  #define LegacyHostDir "..\publish\legacyhost"
#endif
; ExtensionsDir (optional): installer/build-extensions.ps1's output. When
; it's passed, the installer offers the optional extensions below; when it
; isn't (a quick local ISCC run), they're left out.

#define MyAppName "VioLens"
#define MyAppPublisher "VioLens"
#define MyAppExeName "Violens.DesktopUi.exe"
#define MyAppURL "https://github.com/MarkSPowell/VioLens"

[Setup]
; Identifies the product across versions, so a newer installer upgrades in
; place. Never change it.
AppId={{A95DF57C-70EA-4373-91CF-38B1A0CA0879}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; VersionInfoTextVersion shows the full version, suffix included, in the
; file's properties; VersionInfoVersion must be numeric.
VersionInfoVersion={#MyFileVersion}
VersionInfoTextVersion={#MyAppVersion}

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Not the folder a previous version was installed in: until 0.2 the app was
; called CLeARINET and lived in ...\CLeARINET. Upgrading installs into
; ...\VioLens instead and removes what CLeARINET installed (see [Code]).
UsePreviousAppDir=no

; See the header comment.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=Output
OutputBaseFilename=VioLens-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Code signing, release builds only: when the build passes /DSignToolName
; (and defines that tool with /S on the ISCC command line), Inno Setup signs
; the installer and the uninstaller it writes into it. The uninstaller can
; only be signed here, while Inno Setup builds it. See
; .github/workflows/release-windows.yml and installer/sign-file.ps1.
#ifdef SignToolName
SignTool={#SignToolName}
SignedUninstaller=yes
#endif

; The setup program uses the app's own icon. The uninstall entry and the
; shortcuts use the icon built into the installed .exe (the same file, set
; as ApplicationIcon in Violens.DesktopUi.csproj).
SetupIconFile=..\apps\Violens.DesktopUi\Assets\violens.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
; Off by default, like the app's own auto-launch setting.
Name: "legacyhost"; Description: "Legacy Fiddler Classic extension host (runs real, unmodified compiled Fiddler Classic extensions against VioLens's own traffic -- optional, and unrelated to normal use)"; GroupDescription: "Optional components:"; Flags: unchecked
#ifdef ExtensionsDir
; Optional extensions, installed into {app}\Extensions, which the app scans
; after the user's own Documents\VioLens\Extensions (a copy there wins).
; Each is a separate work under its own licence, installed alongside it in
; {app}\Extensions\licenses (see installer/build-extensions.ps1). The
; Privacy Scanner is off by default: P3P is obsolete, so it's mostly of
; interest as an example extension.
Name: "ext_netlog"; Description: "NetLog importer (File > Import via Extension: Chromium NetLog JSON captures)"; GroupDescription: "Optional extensions:"
Name: "ext_csp"; Description: "CSP Rule Collector (builds a Content-Security-Policy for the sites you browse)"; GroupDescription: "Optional extensions:"
Name: "ext_privacy"; Description: "Privacy Scanner (colours responses that set cookies; checks P3P headers)"; GroupDescription: "Optional extensions:"; Flags: unchecked
#endif

[Files]
; Everything dotnet publish produced, including the loose Documentation
; folder that PublishSingleFile leaves outside the exe.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Only with the "legacyhost" task. LegacyHost\ beside the app is where
; LegacyExtensionHostLauncher looks. A framework-dependent net48 build;
; .NET Framework 4.8 comes with Windows 10 and 11.
Source: "{#LegacyHostDir}\*"; DestDir: "{app}\LegacyHost"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: legacyhost

#ifdef ExtensionsDir
Source: "{#ExtensionsDir}\VioLensNetLog.dll"; DestDir: "{app}\Extensions"; Flags: ignoreversion; Tasks: ext_netlog
Source: "{#ExtensionsDir}\licenses\VioLensNetLog-LICENSE.txt"; DestDir: "{app}\Extensions\licenses"; Flags: ignoreversion; Tasks: ext_netlog
Source: "{#ExtensionsDir}\VioLensCSP.dll"; DestDir: "{app}\Extensions"; Flags: ignoreversion; Tasks: ext_csp
Source: "{#ExtensionsDir}\licenses\VioLensCSP-LICENSE.txt"; DestDir: "{app}\Extensions\licenses"; Flags: ignoreversion; Tasks: ext_csp
Source: "{#ExtensionsDir}\PrivacyScanner.dll"; DestDir: "{app}\Extensions"; Flags: ignoreversion; Tasks: ext_privacy
Source: "{#ExtensionsDir}\licenses\PrivacyScanner-LICENSE.txt"; DestDir: "{app}\Extensions\licenses"; Flags: ignoreversion; Tasks: ext_privacy
Source: "{#ExtensionsDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}\Extensions"; Flags: ignoreversion; Tasks: ext_netlog or ext_csp or ext_privacy
#endif

[InstallDelete]
#ifdef ExtensionsDir
; Re-running setup with an extension unticked removes it.
Type: files; Name: "{app}\Extensions\VioLensNetLog.dll"; Tasks: not ext_netlog
Type: files; Name: "{app}\Extensions\licenses\VioLensNetLog-LICENSE.txt"; Tasks: not ext_netlog
Type: files; Name: "{app}\Extensions\VioLensCSP.dll"; Tasks: not ext_csp
Type: files; Name: "{app}\Extensions\licenses\VioLensCSP-LICENSE.txt"; Tasks: not ext_csp
Type: files; Name: "{app}\Extensions\PrivacyScanner.dll"; Tasks: not ext_privacy
Type: files; Name: "{app}\Extensions\licenses\PrivacyScanner-LICENSE.txt"; Tasks: not ext_privacy
Type: files; Name: "{app}\Extensions\THIRD-PARTY-NOTICES.txt"; Tasks: not (ext_netlog or ext_csp or ext_privacy)
#endif

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Removes VioLens's root certificate from the user's certificate stores, so
; a root that can sign for any site isn't left trusted after VioLens is
; gone. Windows asks the user to confirm; a "No" leaves it in place (it
; can also be removed later with certmgr.msc). Runs before the files go.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--remove-root-certificate"; Flags: waituntilterminated; RunOnceId: "RemoveRootCertificate"

[UninstallDelete]
; Removes anything left in the install folder, so no empty folder remains.
Type: filesandordirs; Name: "{app}"

[Code]
// Until 0.2 the app was called CLeARINET. It has the same AppId, so Setup
// treats this as an upgrade, but the new version goes in its own folder
// (UsePreviousAppDir=no above). Once it's installed, what CLeARINET
// installed in its old folder -- including the old uninstaller, whose
// entry this install has replaced -- is removed.
//
// File by file, never the whole folder: another project with a similar
// name installs to "...\Clearinet", which Windows treats as the same
// folder, so it may hold someone else's files too. The folder itself goes
// only if nothing else is left in it. Only a folder named CLeARINET that
// holds the old app's exe is touched at all. Settings live elsewhere and
// are carried over by the app.
var
  FormerAppDir: String;

procedure RemoveFormerFiles(Dir: String);
var
  Ext: String;
begin
  DeleteFile(Dir + '\Clearinet.DesktopUi.exe');
  DeleteFile(Dir + '\Clearinet.DesktopUi.pdb');
  DeleteFile(Dir + '\unins000.exe');
  DeleteFile(Dir + '\unins000.dat');
  DeleteFile(Dir + '\unins000.msg');
  DeleteFile(Dir + '\Documentation\User Guide.md');
  RemoveDir(Dir + '\Documentation');
  DelTree(Dir + '\LegacyHost', True, True, True);

  Ext := Dir + '\Extensions';
  DeleteFile(Ext + '\CLeARINETNetLog.dll');
  DeleteFile(Ext + '\CLeARINETCSP.dll');
  DeleteFile(Ext + '\PrivacyScanner.dll');
  DeleteFile(Ext + '\THIRD-PARTY-NOTICES.txt');
  DeleteFile(Ext + '\licenses\CLeARINETNetLog-LICENSE.txt');
  DeleteFile(Ext + '\licenses\CLeARINETCSP-LICENSE.txt');
  DeleteFile(Ext + '\licenses\PrivacyScanner-LICENSE.txt');
  RemoveDir(Ext + '\licenses');
  RemoveDir(Ext);

  if not RemoveDir(Dir) then
    Log('Left the former CLeARINET folder in place, as it holds other files: ' + Dir);

  // CLeARINET's shortcuts. Only reached when this computer had CLeARINET
  // installed under this AppId (see CurStepChanged).
  DeleteFile(ExpandConstant('{autoprograms}\CLeARINET.lnk'));
  DeleteFile(ExpandConstant('{autodesktop}\CLeARINET.lnk'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Location: String;
begin
  if CurStep = ssInstall then
  begin
    // Read before this install replaces the uninstall entry.
    // ExpandConstant turns the AppId's escaped "{{" back into "{".
    if RegQueryStringValue(HKA,
      ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1'),
      'InstallLocation', Location) then
      FormerAppDir := RemoveBackslashUnlessRoot(Location);
  end
  else if CurStep = ssPostInstall then
  begin
    if (FormerAppDir <> '')
      and (CompareText(ExtractFileName(FormerAppDir), 'CLeARINET') = 0)
      and (CompareText(FormerAppDir, RemoveBackslashUnlessRoot(ExpandConstant('{app}'))) <> 0)
      and FileExists(AddBackslash(FormerAppDir) + 'Clearinet.DesktopUi.exe') then
      RemoveFormerFiles(FormerAppDir);
  end;
end;
