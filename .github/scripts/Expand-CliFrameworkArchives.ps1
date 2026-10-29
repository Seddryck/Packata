[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][string] $ArchiveDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path (Split-Path $PSScriptRoot)
[xml] $properties = Get-Content (Join-Path $repository 'Directory.Build.props') -Raw
$frameworks = ($properties.Project.PropertyGroup.TargetFrameworks | Where-Object { $_ }) -split ';'
$rids = ($properties.Project.PropertyGroup.CliToolRuntimeIdentifiers | Where-Object { $_ }) -split ';'

# Validate the complete immutable archive set before using any of it as package input.
& "$PSScriptRoot/Validate-CliArtifacts.ps1" -Version $Version -ReleaseDirectory $ArchiveDirectory `
    -ExpectedFrameworks $frameworks -ExpectedRids $rids -SkipNuGetPackage -SkipSelfContainedArchives

foreach ($framework in $frameworks) {
    foreach ($rid in $rids) {
        $extension = if ($rid.StartsWith('win-')) { 'zip' } else { 'tar.gz' }
        $archive = Join-Path $ArchiveDirectory "Packata-cli-$Version-$framework-$rid.$extension"
        $destination = Join-Path $OutputDirectory "$framework/$rid"
        if (Test-Path -LiteralPath $destination) { throw "Payload destination already exists: '$destination'." }
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        if ($extension -eq 'zip') {
            Expand-Archive -LiteralPath $archive -DestinationPath $destination
        }
        else {
            & tar -xzf $archive -C $destination
            if ($LASTEXITCODE -ne 0) { throw "Could not extract '$archive'." }
        }
        $runtimeConfig = Get-Content (Join-Path $destination 'packata.runtimeconfig.json') -Raw | ConvertFrom-Json
        if ($runtimeConfig.runtimeOptions.tfm -ne $framework) {
            throw "Archive '$archive' targets '$($runtimeConfig.runtimeOptions.tfm)' instead of '$framework'."
        }
    }
}
