<#
.SYNOPSIS
    Fetches real Fiddler Classic extensions at pinned commits and applies
    CLeARINET's port to each, into tests/ExtensionPorts/.work/ (git-ignored).

.DESCRIPTION
    Upstream source is never committed to this repo. Each extension is
    downloaded as GitHub's zip archive of one pinned commit, so git doesn't
    need to be installed.

    The port of each extension is deliberately tiny and mechanical, so its
    size stays an honest measure of tenet 1 ("port with minimal effort"):

      1. Delete every `using Fiddler;` line. Nothing else in the upstream
         source is touched.
      2. The port project adds one file, PortGlobalUsings.cs, which imports
         Clearinet.CompatShim and aliases `Fiddler` to it, so qualified
         names like `Fiddler.Parser.ParseRequest(...)` compile unchanged.
      3. A new SDK-style project file targeting net10.0.

    An extension with a WinForms UI can't build that way on either
    platform. It gets a CLeARINET-only fork instead, with its own project
    file, which this script fetches and copies unchanged (see
    tests/ExtensionPorts/README.md).

    One script for every platform: it runs in Windows PowerShell 5.1 (built
    into Windows) and in PowerShell 7 (`pwsh`, which CI uses on both its
    Windows and macOS runners).

.EXAMPLE
    # Windows, from the repo root:
    powershell -ExecutionPolicy Bypass -File tests\ExtensionPorts\port.ps1

.EXAMPLE
    # macOS (or Windows with PowerShell 7), from the repo root:
    pwsh tests/ExtensionPorts/port.ps1

.PARAMETER CspSource
    A local clone of the CSP Rule Collector's CLeARINET fork
    (MarkSPowell/CSP-CLeARINET-Extension). Used instead of downloading, so
    changes can be tried before they're pushed.

.EXAMPLE
    # Use a local clone of the CSP Rule Collector fork:
    powershell -ExecutionPolicy Bypass -File tests\ExtensionPorts\port.ps1 -CspSource ..\CSP-CLeARINET-Extension
#>
[CmdletBinding()]
param(
    [string] $CspSource
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

$work = Join-Path $PSScriptRoot '.work'
New-Item -ItemType Directory -Force -Path $work | Out-Null

# Windows PowerShell 5.1 can default to TLS 1.0/1.1, which GitHub refuses.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

# The whole port, as one pattern: a `using Fiddler;` line (any indentation or
# trailing whitespace, CRLF or LF). Nothing else is ever changed.
$usingFiddlerLine = '(?m)^[ \t]*using[ \t]+Fiddler[ \t]*;[ \t]*(\r?\n|\z)'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Downloads one pinned commit as GitHub's zip archive (reused if already
# downloaded) and returns the folder it extracted to.
function Get-PinnedSource {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $Commit
    )

    $zip = Join-Path $work "$Name-$Commit.zip"
    $extracted = Join-Path $work "$Name-upstream"

    # A commit's archive never changes, so a previous download is reused.
    if (-not (Test-Path -LiteralPath $zip)) {
        $url = "https://github.com/$Repository/archive/$Commit.zip"
        Write-Host "Downloading $url"
        $previousProgress = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'   # the progress bar makes 5.1 downloads very slow
        try {
            Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        }
        finally {
            $ProgressPreference = $previousProgress
        }
    }

    if (Test-Path -LiteralPath $extracted) { Remove-Item -LiteralPath $extracted -Recurse -Force }
    Expand-Archive -LiteralPath $zip -DestinationPath $extracted

    # GitHub's archive holds a single top-level folder, <repo>-<commit>.
    $archiveRoot = @(Get-ChildItem -LiteralPath $extracted -Directory)
    if ($archiveRoot.Count -ne 1) { throw "Unexpected layout in $zip." }
    return $archiveRoot[0].FullName
}

function Invoke-ExtensionPort {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $SourceFolder,
        [string] $Repository,
        [string] $Commit,
        [string] $LocalSource
    )

    if ($LocalSource) {
        $root = (Resolve-Path -LiteralPath $LocalSource).Path
        $label = "local clone $root"
    }
    else {
        $root = Get-PinnedSource -Name $Name -Repository $Repository -Commit $Commit
        $label = "$Repository @ $($Commit.Substring(0, 7))"
    }

    $source = (Resolve-Path -LiteralPath (Join-Path $root $SourceFolder)).Path.TrimEnd('\', '/')
    $ported = Join-Path $work $Name
    if (Test-Path -LiteralPath $ported) { Remove-Item -LiteralPath $ported -Recurse -Force }

    $files = 0
    $removed = 0
    foreach ($file in Get-ChildItem -LiteralPath $source -Filter '*.cs' -Recurse -File) {
        $relative = $file.FullName.Substring($source.Length).TrimStart('\', '/')
        # A local clone may hold build output; never port that.
        if ($relative -match '(^|[\\/])(bin|obj|\.git)[\\/]') { continue }

        $target = Join-Path $ported $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null

        $text = [System.IO.File]::ReadAllText($file.FullName)
        $removed += [regex]::Matches($text, $usingFiddlerLine).Count
        [System.IO.File]::WriteAllText($target, [regex]::Replace($text, $usingFiddlerLine, ''), $utf8NoBom)
        $files++
    }

    Write-Host "Ported $Name from ${label}: $files source file(s), $removed line(s) removed, no other changes."
}

Invoke-ExtensionPort `
    -Name 'FiddlerImportNetlog' `
    -Repository 'ericlaw1979/FiddlerImportNetlog' `
    -Commit '1a927f4fe1519d681b112c5b02c6a1cc20142fb9' `
    -SourceFolder 'FiddlerImportNetlog'

# Eric Lawrence's Privacy Scanner (cookies and P3P), published as a sample in
# Telerik's Fiddler docs (Apache-2.0). It's one C# code block in a Markdown
# page, fetched at a pinned commit. Besides the usual `using Fiddler;` lines,
# its port drops `using System.Windows.Forms;` and the `System.Windows.Forms.`
# prefix on MenuItem: CLeARINET's compatibility layer has a MenuItem of its
# own, which the app draws as an Avalonia menu.
function Invoke-DocsSamplePort {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $Commit,
        [Parameter(Mandatory)] [string] $MarkdownPath,
        [Parameter(Mandatory)] [string] $OutputFile
    )

    $markdown = Join-Path $work "$Name-$Commit.md"
    if (-not (Test-Path -LiteralPath $markdown)) {
        $url = "https://raw.githubusercontent.com/$Repository/$Commit/$MarkdownPath"
        Write-Host "Downloading $url"
        Invoke-WebRequest -Uri $url -OutFile $markdown -UseBasicParsing
    }

    # The docs repo's licence, shipped with the extension (see
    # installer/build-extensions.ps1).
    $license = Join-Path $work "$Name-$Commit-LICENSE"
    if (-not (Test-Path -LiteralPath $license)) {
        $url = "https://raw.githubusercontent.com/$Repository/$Commit/LICENSE"
        Write-Host "Downloading $url"
        Invoke-WebRequest -Uri $url -OutFile $license -UseBasicParsing
    }

    $text = [System.IO.File]::ReadAllText($markdown)
    $match = [regex]::Match($text, '(?s)```c#\r?\n(?<code>.*?)\r?\n```')
    if (-not $match.Success) { throw "No C# code block in $MarkdownPath." }
    $code = $match.Groups['code'].Value

    $removed = [regex]::Matches($code, $usingFiddlerLine).Count
    $code = [regex]::Replace($code, $usingFiddlerLine, '')

    $winForms = '(?m)^[ \t]*using[ \t]+System\.Windows\.Forms[ \t]*;[ \t]*(\r?\n|\z)'
    $removed += [regex]::Matches($code, $winForms).Count
    $code = [regex]::Replace($code, $winForms, '')

    $qualifiers = 'System\.Windows\.Forms\.(?=(MenuItem|MainMenu)\b)'
    $renamed = [regex]::Matches($code, $qualifiers).Count
    $code = [regex]::Replace($code, $qualifiers, '')

    $ported = Join-Path $work $Name
    if (Test-Path -LiteralPath $ported) { Remove-Item -LiteralPath $ported -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $ported | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $ported $OutputFile), $code, $utf8NoBom)
    Copy-Item -LiteralPath $license -Destination (Join-Path $ported 'LICENSE')

    Write-Host "Ported $Name from $Repository @ $($Commit.Substring(0, 7)): $removed using line(s) removed, $renamed System.Windows.Forms. prefix(es) dropped, no other changes."
}

Invoke-DocsSamplePort `
    -Name 'PrivacyScanner' `
    -Repository 'telerik/fiddler-docs' `
    -Commit '51c6ebebdbb79726b286caa3fc28bb2641a2f0c7' `
    -MarkdownPath 'extend-fiddler/cookieextension.md' `
    -OutputFile 'TagCookies.cs'

# The CSP Rule Collector is a CLeARINET-only fork with its own project file
# (CLeARINETCSP.csproj) and no Fiddler code left in it, so it's copied as it
# is: nothing to port. The test project builds it from .work/CLeARINETCSP/.
# To try unpushed changes, pass -CspSource <local clone> instead.
$cspRepository = 'MarkSPowell/CSP-CLeARINET-Extension'
$cspCommit = '4d4cb9ab242d509ee2a4cdbc14ffa1fc9b4c9d38'

function Copy-ExtensionSource {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $From,
        [Parameter(Mandatory)] [string] $Label
    )

    $from = (Resolve-Path -LiteralPath $From).Path.TrimEnd('\', '/')
    $target = Join-Path $work $Name
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }

    $files = 0
    foreach ($file in Get-ChildItem -LiteralPath $from -Recurse -File -Force) {
        $relative = $file.FullName.Substring($from.Length).TrimStart('\', '/')
        # A local clone has git data and may have build output; neither is source.
        if ($relative -match '(^|[\\/])(bin|obj|\.git|\.vs)([\\/]|$)') { continue }

        $destination = Join-Path $target $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $files++
    }

    Write-Host "Copied $Name from ${Label}: $files file(s), unchanged."
}

if ($CspSource) {
    Copy-ExtensionSource -Name 'CLeARINETCSP' -From $CspSource -Label "local clone $CspSource"
}
elseif ($cspCommit) {
    $root = Get-PinnedSource -Name 'CLeARINETCSP' -Repository $cspRepository -Commit $cspCommit
    Copy-ExtensionSource -Name 'CLeARINETCSP' -From $root -Label "$cspRepository @ $($cspCommit.Substring(0, 7))"
}
else {
    $stale = Join-Path $work 'CLeARINETCSP'
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Recurse -Force }
    Write-Host 'Skipped CLeARINETCSP: no commit pinned yet. Pass -CspSource <local clone> to use one.'
}
