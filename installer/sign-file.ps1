# Signs one file with Azure Artifact Signing. Inno Setup runs this (see the
# SignTool lines in CLeARINET.iss) for the installer and the uninstaller it
# builds; .github/workflows/release-windows.yml sets it up.
#
# Needs: the ArtifactSigning PowerShell module installed, an Azure sign-in
# (the workflow's azure/login step), and these environment variables:
#   ARTIFACT_SIGNING_ENDPOINT, ARTIFACT_SIGNING_ACCOUNT, ARTIFACT_SIGNING_PROFILE
param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)

$ErrorActionPreference = 'Stop'

foreach ($name in 'ARTIFACT_SIGNING_ENDPOINT', 'ARTIFACT_SIGNING_ACCOUNT', 'ARTIFACT_SIGNING_PROFILE') {
    if (-not [Environment]::GetEnvironmentVariable($name)) {
        throw "$name isn't set, so $Path can't be signed."
    }
}

Import-Module ArtifactSigning

Invoke-ArtifactSigning `
    -Endpoint $env:ARTIFACT_SIGNING_ENDPOINT `
    -CodeSigningAccountName $env:ARTIFACT_SIGNING_ACCOUNT `
    -CertificateProfileName $env:ARTIFACT_SIGNING_PROFILE `
    -Files $Path `
    -FileDigest SHA256 `
    -TimestampRfc3161 'http://timestamp.acs.microsoft.com' `
    -TimestampDigest SHA256 `
    -Description 'CLeARINET' `
    -DescriptionUrl 'https://github.com/MarkSPowell/CLeARINET' `
    -ExcludeInteractiveBrowserCredential

# Fail loudly if nothing was signed, so Inno Setup stops the build rather
# than shipping an unsigned file.
$status = (Get-AuthenticodeSignature -FilePath $Path).Status
if ($status -eq 'NotSigned') {
    throw "$Path is still not signed."
}

Write-Host "Signed $Path ($status)."
