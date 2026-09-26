<#
.SYNOPSIS
    Builds the optional extensions the installers offer, and collects them
    with their licences into one folder.

.DESCRIPTION
    Runs tests/ExtensionPorts/port.ps1 (which fetches each extension's source
    at its pinned commit), builds each extension in Release, and writes:

      <OutputDir>/
        CLeARINETNetLog.dll        NetLog importer (BSD-3-Clause)
        CLeARINETCSP.dll           CSP Rule Collector (MIT)
        PrivacyScanner.dll         Privacy Scanner sample (Apache-2.0)
        licenses/                  each extension's licence text
        THIRD-PARTY-NOTICES.txt    where each one came from and what changed

    Only the extension's own .dll ships: CLeARINET supplies the
    compatibility layer and Avalonia at run time.

    Used by .github/workflows/release-windows.yml (the installer's optional
    extensions) and release-macos.yml (the .dmg's Optional Extensions
    folder). Runs in Windows PowerShell 5.1 and PowerShell 7.

.PARAMETER OutputDir
    Where to put the result. Emptied first.

.EXAMPLE
    pwsh installer/build-extensions.ps1 -OutputDir publish/extensions
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutputDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

$repo = Split-Path -Parent $PSScriptRoot
$ports = Join-Path $repo 'tests/ExtensionPorts'
$work = Join-Path $ports '.work'

& (Join-Path $ports 'port.ps1')

# One entry per shipped extension. Commits must match port.ps1's pins.
$extensions = @(
    [pscustomobject]@{
        Name        = 'NetLog importer'
        Project     = Join-Path $ports 'FiddlerImportNetlog/FiddlerImportNetlog.Port.csproj'
        Dll         = Join-Path $work 'bin/FiddlerImportNetlog/CLeARINETNetLog.dll'
        License     = { Join-Path (@(Get-ChildItem -LiteralPath (Join-Path $work 'FiddlerImportNetlog-upstream') -Directory)[0].FullName) 'LICENSE' }
        LicenseName = 'CLeARINETNetLog-LICENSE.txt'
        Notice      = @'
NetLog importer (CLeARINETNetLog.dll)
  Source:  https://github.com/ericlaw1979/FiddlerImportNetlog
  Commit:  1a927f4fe1519d681b112c5b02c6a1cc20142fb9
  Author:  Eric Lawrence
  Licence: BSD 3-Clause (licenses/CLeARINETNetLog-LICENSE.txt)
  Changes: its `using Fiddler;` lines were removed, and it is built for .NET 10
           against CLeARINET's compatibility layer, as CLeARINETNetLog.dll
           (upstream builds FiddlerImportNetlog.dll).
'@
    },
    [pscustomobject]@{
        Name        = 'CSP Rule Collector'
        Project     = Join-Path $work 'CLeARINETCSP/CLeARINETCSP.csproj'
        Dll         = Join-Path $work 'CLeARINETCSP/bin/Release/net10.0/CLeARINETCSP.dll'
        License     = { Join-Path $work 'CLeARINETCSP/LICENSE' }
        LicenseName = 'CLeARINETCSP-LICENSE.txt'
        Notice      = @'
CSP Rule Collector (CLeARINETCSP.dll)
  Source:  https://github.com/MarkSPowell/CSP-CLeARINET-Extension
           (a CLeARINET fork of https://github.com/ericlaw1979/CSP-Fiddler-Extension)
  Commit:  4d4cb9ab242d509ee2a4cdbc14ffa1fc9b4c9d38
  Author:  David Risney; CLeARINET changes by the CLeARINET project
  Licence: MIT (licenses/CLeARINETCSP-LICENSE.txt)
  Changes: see the fork's history and README.
'@
    },
    [pscustomobject]@{
        Name        = 'Privacy Scanner'
        Project     = Join-Path $ports 'PrivacyScanner/PrivacyScanner.Port.csproj'
        Dll         = Join-Path $work 'bin/PrivacyScanner/PrivacyScanner.dll'
        License     = { Join-Path $work 'PrivacyScanner/LICENSE' }
        LicenseName = 'PrivacyScanner-LICENSE.txt'
        Notice      = @'
Privacy Scanner (PrivacyScanner.dll)
  Source:  the cookie/P3P sample in https://github.com/telerik/fiddler-docs
           (extend-fiddler/cookieextension.md)
  Commit:  51c6ebebdbb79726b286caa3fc28bb2641a2f0c7
  Author:  Eric Lawrence
  Licence: Apache License 2.0 (licenses/PrivacyScanner-LICENSE.txt)
  Changes: the C# code block was extracted from the page; its `using Fiddler;`
           and `using System.Windows.Forms;` lines were removed, the
           `System.Windows.Forms.` prefix was dropped from MenuItem, and it is
           built for .NET 10 against CLeARINET's compatibility layer.
'@
    }
)

if (Test-Path -LiteralPath $OutputDir) { Remove-Item -LiteralPath $OutputDir -Recurse -Force }
$licenses = Join-Path $OutputDir 'licenses'
New-Item -ItemType Directory -Force -Path $licenses | Out-Null

$notices = @(
    'Optional extensions included with CLeARINET',
    '============================================',
    '',
    'These extensions are separate works by their authors, included under their',
    'own licences. Each is optional; CLeARINET works without them.',
    ''
)

foreach ($extension in $extensions) {
    if (-not (Test-Path -LiteralPath $extension.Project)) {
        throw "$($extension.Name): project not found at $($extension.Project). Did port.ps1 fetch it?"
    }

    Write-Host "Building $($extension.Name)"
    & dotnet build $extension.Project --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "$($extension.Name): build failed." }

    if (-not (Test-Path -LiteralPath $extension.Dll)) { throw "$($extension.Name): expected $($extension.Dll)." }
    $license = & $extension.License
    if (-not (Test-Path -LiteralPath $license)) { throw "$($extension.Name): licence not found at $license." }

    Copy-Item -LiteralPath $extension.Dll -Destination $OutputDir
    Copy-Item -LiteralPath $license -Destination (Join-Path $licenses $extension.LicenseName)
    $notices += $extension.Notice.Replace("`r`n", "`n").Split("`n")
    $notices += ''
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $OutputDir 'THIRD-PARTY-NOTICES.txt'), ($notices -join [Environment]::NewLine), $utf8NoBom)

Write-Host "Optional extensions ready in $OutputDir"
Get-ChildItem -LiteralPath $OutputDir -Recurse -File | ForEach-Object { Write-Host "  $($_.FullName.Substring((Resolve-Path -LiteralPath $OutputDir).Path.Length + 1))" }
