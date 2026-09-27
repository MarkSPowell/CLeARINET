; CLeARINET Windows installer (Inno Setup).
;
; Built by .github/workflows/release-windows.yml, which passes MyAppVersion,
; MyFileVersion, PublishDir, LegacyHostDir and ExtensionsDir on the command line via /D;
; the #ifndef fallbacks below exist purely so this script still compiles
; (with placeholder values) if someone runs ISCC.exe against it directly
; while testing, without having to remember all four /D switches every
; time.
;
; MyAppVersion and MyFileVersion are deliberately two different strings,
; not one reused in two places: MyAppVersion can be anything (this
; project's own tags look like "0.1.0-preview.1"), but VersionInfoVersion
; below (the compiled setup.exe's actual Win32 FileVersion resource) only
; accepts a strict numeric "X.X.X.X" -- a prerelease suffix there fails
; the whole compile outright ("Value of [Setup] section directive
; VersionInfoVersion is invalid"), which is exactly what happened before
; this comment was added. release-windows.yml's own "Determine version"
; step derives MyFileVersion from MyAppVersion by dropping everything from
; the first "-" onward and padding to four components.
;
; The one requirement this file exists to satisfy: a single setup.exe that
; can install either per-machine (Program Files, needs an admin elevation
; prompt) or per-user (the current user's own AppData\Local\Programs
; folder, no admin prompt at all), with the person choosing which at
; install time. PrivilegesRequired=lowest + PrivilegesRequiredOverridesAllowed=dialog
; is what does that: Setup shows an "Install mode" page (the same one
; Visual Studio Code's own installer uses) offering "Install for me only"
; (no elevation) or "Install for all users" (elevates, admin prompt), and
; every {auto*} constant below (AutoProgramFiles, AutoPrograms,
; AutoDesktop, ...) automatically resolves to the per-user or per-machine
; path depending on which one the person picked. Nothing in this script
; hardcodes Program Files or AppData\Local\Programs directly -- that's the
; whole point of the {auto*} family.
;
; CLeARINET's own runtime state (FiddlerScriptPreferenceStore's
; %LocalAppData%\CLeARINET\, ExtensionHost.DefaultExtensionsFolder's
; Documents\CLeARINET\Extensions, and SaveSaz's Desktop exports) all
; already live under the current user's own profile regardless of where
; the app itself gets installed, so neither install mode needs any special
; handling beyond where the app's own files land.

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

#define MyAppName "CLeARINET"
#define MyAppPublisher "CLeARINET"
#define MyAppExeName "Clearinet.DesktopUi.exe"
#define MyAppURL "https://github.com/MarkSPowell/CLeARINET"

[Setup]
; A fixed, random GUID -- Inno Setup uses this (not the app name) to
; recognize "this is the same product" across versions, so a later
; installer with a higher AppVersion correctly upgrades in place instead
; of installing side by side. Generated once for this project; never
; regenerate it for a version bump, only if CLeARINET were ever forked
; into a genuinely different product.
AppId={{A95DF57C-70EA-4373-91CF-38B1A0CA0879}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; VersionInfoVersion is the strict-numeric one (see this file's own header
; comment on why it can't just be MyAppVersion); VersionInfoTextVersion is
; Inno Setup's documented escape hatch for showing the real, full version
; string (with its "-preview.1"-style suffix intact) in the compiled
; setup.exe's own Properties > Details "File version"/"Product version"
; fields, in place of the numeric-only value VersionInfoVersion carries.
VersionInfoVersion={#MyFileVersion}
VersionInfoTextVersion={#MyAppVersion}

DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; See this file's own header comment for what these two do together.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=Output
OutputBaseFilename=CLeARINET-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; The app ships no standalone .ico today (its window/taskbar icon is an
; embedded avares:// resource, clearinet.webp, read at runtime -- not a
; file on disk this script can point at, and not a format Inno Setup's
; SetupIconFile accepts). Falls back to Inno Setup's own default wizard
; icon and the published .exe's own icon resource for the Start Menu/
; uninstaller entries below; a real CLeARINET-branded .ico is a follow-up,
; not something this pass adds.
UninstallDisplayIcon={app}\{#MyAppExeName}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
; Off by default, deliberately -- mirrors the main app's own posture for
; this feature (MainWindowViewModel.AutoLaunchLegacyHost also defaults to
; false; see LegacyExtensionHostLauncher's own remarks: "not just 'safe if
; it isn't running,' but 'won't even try to make it exist unless asked'").
; A Task, not a [Components]/[Types] split: this installer only ever
; produces one product either way, and a Task is the simplest mechanism
; that already exists in this file (see "desktopicon" above) for "only
; copy these files if the person checked this box."
Name: "legacyhost"; Description: "Legacy Fiddler Classic extension host (runs real, unmodified compiled Fiddler Classic extensions against CLeARINET's own traffic -- optional, and unrelated to normal use)"; GroupDescription: "Optional components:"; Flags: unchecked
#ifdef ExtensionsDir
; Optional extensions, installed into {app}\Extensions, which the app scans
; after the user's own Documents\CLeARINET\Extensions (a copy there wins).
; Each is a separate work under its own licence, installed alongside it in
; {app}\Extensions\licenses (see installer/build-extensions.ps1). The
; Privacy Scanner is off by default: P3P is obsolete, so it's mostly of
; interest as an example extension.
Name: "ext_netlog"; Description: "NetLog importer (File > Import via Extension: Chromium NetLog JSON captures)"; GroupDescription: "Optional extensions:"
Name: "ext_csp"; Description: "CSP Rule Collector (builds a Content-Security-Policy for the sites you browse)"; GroupDescription: "Optional extensions:"
Name: "ext_privacy"; Description: "Privacy Scanner (colours responses that set cookies; checks P3P headers)"; GroupDescription: "Optional extensions:"; Flags: unchecked
#endif

[Files]
; Everything dotnet publish produced -- the single-file app exe, its
; native-library self-extract payload, and the loose content files
; (Documentation\User Guide.md; see Clearinet.DesktopUi.csproj's own
; None/Link entry) that PublishSingleFile deliberately doesn't fold into
; the exe itself. recursesubdirs/createallsubdirs so that Documentation
; folder comes along intact.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Only copied if the "legacyhost" Task above is checked. DestDir matches
; LegacyExtensionHostLauncher.ExpectedExecutablePath's own fixed path
; convention exactly (<main app's own exe folder>\LegacyHost\...) -- see
; that class's own remarks and the .NET Extension Compatibility Design
; doc's "legacy host launch/stop lifecycle" section for why this is a
; convention, not a setting. {#LegacyHostDir} is release-windows.yml's own
; `dotnet build ... --output publish\legacyhost` step's output -- a plain
; framework-dependent net48 build (this is a net48 app; .NET Framework 4.8
; ships with Windows 10/11 already), flattened to include its own
; ProjectReferences (Clearinet.CompatShim.dll,
; Clearinet.LegacyExtensionHost.Bridge.dll) alongside the host .exe itself.
Source: "{#LegacyHostDir}\*"; DestDir: "{app}\LegacyHost"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: legacyhost

#ifdef ExtensionsDir
Source: "{#ExtensionsDir}\CLeARINETNetLog.dll"; DestDir: "{app}\Extensions"; Flags: ignoreversion; Tasks: ext_netlog
Source: "{#ExtensionsDir}\licenses\CLeARINETNetLog-LICENSE.txt"; DestDir: "{app}\Extensions\licenses"; Flags: ignoreversion; Tasks: ext_netlog
Source: "{#ExtensionsDir}\CLeARINETCSP.dll"; DestDir: "{app}\Extensions"; Flags: ignoreversion; Tasks: ext_csp
Source: "{#ExtensionsDir}\licenses\CLeARINETCSP-LICENSE.txt"; DestDir: "{app}\Extensions\licenses"; Flags: ignoreversion; Tasks: ext_csp
Source: "{#ExtensionsDir}\PrivacyScanner.dll"; DestDir: "{app}\Extensions"; Flags: ignoreversion; Tasks: ext_privacy
Source: "{#ExtensionsDir}\licenses\PrivacyScanner-LICENSE.txt"; DestDir: "{app}\Extensions\licenses"; Flags: ignoreversion; Tasks: ext_privacy
Source: "{#ExtensionsDir}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}\Extensions"; Flags: ignoreversion; Tasks: ext_netlog or ext_csp or ext_privacy

[InstallDelete]
; Re-running setup with an extension unticked removes it.
Type: files; Name: "{app}\Extensions\CLeARINETNetLog.dll"; Tasks: not ext_netlog
Type: files; Name: "{app}\Extensions\licenses\CLeARINETNetLog-LICENSE.txt"; Tasks: not ext_netlog
Type: files; Name: "{app}\Extensions\CLeARINETCSP.dll"; Tasks: not ext_csp
Type: files; Name: "{app}\Extensions\licenses\CLeARINETCSP-LICENSE.txt"; Tasks: not ext_csp
Type: files; Name: "{app}\Extensions\PrivacyScanner.dll"; Tasks: not ext_privacy
Type: files; Name: "{app}\Extensions\licenses\PrivacyScanner-LICENSE.txt"; Tasks: not ext_privacy
Type: files; Name: "{app}\Extensions\THIRD-PARTY-NOTICES.txt"; Tasks: not (ext_netlog or ext_csp or ext_privacy)
#endif

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Belt-and-suspenders alongside Inno Setup's own automatic per-file
; uninstall list: catches anything the app itself might drop into its own
; install folder at runtime (it doesn't today -- see this file's own
; header comment on why runtime state all lives elsewhere -- but this
; costs nothing to have in place if that ever changes) so an uninstall
; doesn't leave an orphaned, empty {app} directory behind.
Type: filesandordirs; Name: "{app}"
