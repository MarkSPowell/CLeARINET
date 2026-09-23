; CLeARINET Windows installer (Inno Setup).
;
; Built by .github/workflows/release-windows.yml, which passes MyAppVersion
; and PublishDir on the command line via /D; the #ifndef fallbacks below
; exist purely so this script still compiles (with placeholder values) if
; someone runs ISCC.exe against it directly while testing, without having
; to remember both /D switches every time.
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
#ifndef PublishDir
  #define PublishDir "..\publish\win-x64"
#endif

#define MyAppName "CLeARINET"
#define MyAppPublisher "CLeARINET"
#define MyAppExeName "Clearinet.DesktopUi.exe"
#define MyAppURL "https://github.com/ericlaw1979/Clearinet"

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
VersionInfoVersion={#MyAppVersion}

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

[Files]
; Everything dotnet publish produced -- the single-file app exe, its
; native-library self-extract payload, and the loose content files
; (Documentation\User Guide.md; see Clearinet.DesktopUi.csproj's own
; None/Link entry) that PublishSingleFile deliberately doesn't fold into
; the exe itself. recursesubdirs/createallsubdirs so that Documentation
; folder comes along intact.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

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
