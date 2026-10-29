[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][string] $NuGetDirectory,
    [Parameter(Mandatory)][string] $SdkVersion,
    [Parameter(Mandatory)][string] $WorkingDirectory,
    [string] $Framework = 'net10.0',
    [switch] $ExpectUnsupportedSdk
)

$ErrorActionPreference = 'Stop'
$feed = (Resolve-Path -LiteralPath $NuGetDirectory).Path
if (Test-Path -LiteralPath $WorkingDirectory) { throw "Test directory already exists: '$WorkingDirectory'." }
$work = (New-Item -ItemType Directory -Path $WorkingDirectory -Force).FullName
@{ sdk = @{ version = $SdkVersion; rollForward = 'disable'; allowPrerelease = $false } } |
    ConvertTo-Json | Set-Content (Join-Path $work 'global.json')
# A private feed and cache prevent a published package from hiding a broken candidate.
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
"<configuration><packageSources><clear/><add key=`"candidate`" value=`"$escapedFeed`"/></packageSources></configuration>" |
    Set-Content (Join-Path $work 'NuGet.Config')
$previousPackages = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = Join-Path $work 'packages'
    Push-Location $work
    try {
        $selectedSdk = & dotnet --version
        if ($LASTEXITCODE -ne 0 -or $selectedSdk.Trim() -ne $SdkVersion) {
            throw "Expected SDK '$SdkVersion', selected '$selectedSdk'."
        }
        $toolDirectory = Join-Path $work 'tool'
        $output = @(& dotnet tool install Packata-cli --version $Version --tool-path $toolDirectory `
            --framework $Framework --configfile (Join-Path $work 'NuGet.Config') --no-cache 2>&1)
        $installExitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        if ($ExpectUnsupportedSdk) {
            if ($installExitCode -eq 0) { throw "SDK '$SdkVersion' unexpectedly accepted the RID-aware tool; update the compatibility policy and tests." }
            if (($output -join "`n") -notmatch 'DotnetToolSettings.xml|NU1202|not supported|not compatible') {
                throw "SDK '$SdkVersion' failed for an unexpected reason, rather than tool format compatibility."
            }
            Write-Host "Confirmed SDK '$SdkVersion' cannot install the RID-aware pointer package."
            # GitHub's pwsh wrapper otherwise propagates the expected native failure.
            $global:LASTEXITCODE = 0
            return
        }
        if ($installExitCode -ne 0) { throw "Tool installation failed on SDK '$SdkVersion'." }
        $executable = Join-Path $toolDirectory $(if ($IsWindows) { 'packata.exe' } else { 'packata' })
        $help = @(& $executable --help 2>&1)
        if ($LASTEXITCODE -ne 0 -or ($help -join "`n") -notmatch 'USAGE') { throw 'Installed CLI help smoke test failed.' }
        $reportedVersion = @(& $executable --version 2>&1)
        if ($LASTEXITCODE -ne 0 -or ($reportedVersion -join "`n") -notmatch [regex]::Escape($Version)) {
            throw "Installed CLI reported '$reportedVersion' instead of '$Version'."
        }
        Write-Host "Installed CLI $Version passed help/version checks with SDK $SdkVersion ($Framework)."
    }
    finally { Pop-Location }
}
finally { $env:NUGET_PACKAGES = $previousPackages }
