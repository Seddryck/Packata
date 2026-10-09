[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [string] $NuGetDirectory,

    [string] $ReleaseDirectory,

    [string[]] $ExpectedFrameworks = @('net8.0', 'net9.0', 'net10.0'),

    [string[]] $ExpectedRids = @(
        'win-x64',
        'win-arm64',
        'linux-x64',
        'linux-arm64',
        'linux-musl-x64',
        'linux-musl-arm64',
        'osx-x64',
        'osx-arm64'
    ),

    [switch] $SkipNuGetPackage,

    [switch] $SkipFrameworkDependentArchives,

    [switch] $SkipSelfContainedArchives
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-ZipEntries {
    param([Parameter(Mandatory)][string] $Path)

    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Path))
    try {
        return @($archive.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName.Replace('\', '/') })
    }
    finally {
        $archive.Dispose()
    }
}

function Get-TarEntries {
    param([Parameter(Mandatory)][string] $Path)

    $entries = @(& tar -tzf $Path)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list archive '$Path'."
    }
    return @($entries | ForEach-Object { $_ -replace '^\./', '' } | Where-Object { $_ -and $_ -ne './' })
}

function Get-ArchiveEntries {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Extension
    )

    if ($Extension -eq 'zip') {
        return @(Get-ZipEntries $Path)
    }
    return @(Get-TarEntries $Path)
}

function Assert-TarExecutable {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Executable
    )

    $details = @(& tar -tvzf $Path)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect permissions in archive '$Path'."
    }
    Assert-Condition ([bool]($details | Where-Object { $_ -match "^-rwx.*\s(?:\./)?$([regex]::Escape($Executable))$" })) "Archive '$Path' does not preserve executable permissions for '$Executable'."
}

if (-not $SkipNuGetPackage) {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($NuGetDirectory)) 'A NuGet directory is required when package validation is enabled.'
    $packages = @(Get-ChildItem -LiteralPath $NuGetDirectory -Filter '*.nupkg' -File)
    $symbolPackages = @(Get-ChildItem -LiteralPath $NuGetDirectory -Filter '*.snupkg' -File)
    $pointerPackagePath = Join-Path $NuGetDirectory "Packata-cli.$Version.nupkg"

    Assert-Condition ($packages.Count -eq ($ExpectedRids.Count + 1)) "Expected one pointer package and $($ExpectedRids.Count) RID packages, found $($packages.Count) packages."
    Assert-Condition ($symbolPackages.Count -eq 0) "Packata CLI must not publish symbol packages; found $($symbolPackages.Count)."
    Assert-Condition (Test-Path -LiteralPath $pointerPackagePath -PathType Leaf) "Missing pointer package '$pointerPackagePath'."

    $pointerArchive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $pointerPackagePath))
    try {
        $entries = @($pointerArchive.Entries.FullName | ForEach-Object { $_.Replace('\', '/') })
        Assert-Condition ($entries -contains 'Packata-cli.nuspec') 'The CLI pointer package has no matching nuspec.'
        Assert-Condition ($entries -contains 'tools/any/any/DotnetToolSettings.xml') 'The CLI pointer package has no RID-aware .NET tool settings.'
        Assert-Condition (-not ($entries | Where-Object { $_ -match '/packata(?:\.dll|\.exe)?$' })) 'The CLI pointer package unexpectedly contains an implementation payload.'

        $settingsEntry = $pointerArchive.GetEntry('tools/any/any/DotnetToolSettings.xml')
        $reader = [System.IO.StreamReader]::new($settingsEntry.Open())
        try {
            [xml] $settings = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        Assert-Condition ($settings.DotNetCliTool.Version -eq '2') "Expected .NET tool settings version '2', found '$($settings.DotNetCliTool.Version)'."
        $command = $settings.DotNetCliTool.Commands.Command
        Assert-Condition ($command.Name -eq 'packata') "Expected tool command 'packata', found '$($command.Name)'."

        $ridPackages = @($settings.DotNetCliTool.RuntimeIdentifierPackages.RuntimeIdentifierPackage)
        Assert-Condition ($ridPackages.Count -eq $ExpectedRids.Count) "The pointer package references $($ridPackages.Count) RID packages; expected $($ExpectedRids.Count)."
        foreach ($rid in $ExpectedRids) {
            $ridPackage = @($ridPackages | Where-Object { $_.RuntimeIdentifier -eq $rid })
            Assert-Condition ($ridPackage.Count -eq 1) "The pointer package must reference RID '$rid' exactly once."
            Assert-Condition ($ridPackage[0].Id -eq "Packata-cli.$rid") "RID '$rid' points to '$($ridPackage[0].Id)' instead of 'Packata-cli.$rid'."
        }
    }
    finally {
        $pointerArchive.Dispose()
    }

    foreach ($rid in $ExpectedRids) {
        $packageId = "Packata-cli.$rid"
        $packagePath = Join-Path $NuGetDirectory "$packageId.$Version.nupkg"
        Assert-Condition (Test-Path -LiteralPath $packagePath -PathType Leaf) "Missing RID package '$packagePath'."

        $packageArchive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $packagePath))
        try {
            $entries = @($packageArchive.Entries.FullName | ForEach-Object { $_.Replace('\', '/') })
            Assert-Condition ($entries -contains "$packageId.nuspec") "RID package '$packageId' has no matching nuspec."

            $nuspecEntry = $packageArchive.GetEntry("$packageId.nuspec")
            $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
            try {
                [xml] $nuspec = $reader.ReadToEnd()
            }
            finally {
                $reader.Dispose()
            }
            $packageType = $nuspec.package.metadata.packageTypes.packageType.name
            Assert-Condition ($packageType -eq 'DotnetToolRidPackage') "RID package '$packageId' has package type '$packageType'."

            $executable = if ($rid.StartsWith('win-')) { 'packata.exe' } else { 'packata' }
            foreach ($framework in $ExpectedFrameworks) {
                $toolPath = "tools/$framework/$rid"
                Assert-Condition ($entries -contains "$toolPath/DotnetToolSettings.xml") "RID package '$packageId' has no settings for '$framework'."
                Assert-Condition ($entries -contains "$toolPath/$executable") "RID package '$packageId' has no '$framework' executable."
                Assert-Condition ($entries -contains "$toolPath/packata.dll") "RID package '$packageId' has no '$framework' implementation assembly."
            }
        }
        finally {
            $packageArchive.Dispose()
        }
    }
}

if (-not $SkipFrameworkDependentArchives -or -not $SkipSelfContainedArchives) {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($ReleaseDirectory)) 'A release directory is required when archive validation is enabled.'
}

if (-not $SkipFrameworkDependentArchives) {
    $frameworkArchives = @(
        Get-ChildItem -LiteralPath $ReleaseDirectory -File |
            Where-Object { $_.Name -match '^Packata-cli-.+-net(?:8|9|10)\.0-(?:win|linux|linux-musl|osx)-(?:x64|arm64)\.(?:zip|tar\.gz)$' }
    )
    $expectedCount = $ExpectedFrameworks.Count * $ExpectedRids.Count
    Assert-Condition ($frameworkArchives.Count -eq $expectedCount) "Expected $expectedCount framework-dependent CLI archives, found $($frameworkArchives.Count)."

    foreach ($framework in $ExpectedFrameworks) {
        foreach ($rid in $ExpectedRids) {
            $extension = if ($rid.StartsWith('win-')) { 'zip' } else { 'tar.gz' }
            $archivePath = Join-Path $ReleaseDirectory "Packata-cli-$Version-$framework-$rid.$extension"
            Assert-Condition (Test-Path -LiteralPath $archivePath -PathType Leaf) "Missing framework-dependent CLI archive '$archivePath'."

            $entries = @(Get-ArchiveEntries -Path $archivePath -Extension $extension)
            $executable = if ($rid.StartsWith('win-')) { 'packata.exe' } else { 'packata' }
            foreach ($entry in @($executable, 'packata.dll', 'packata.deps.json', 'packata.runtimeconfig.json', 'LICENSE', 'README.md')) {
                Assert-Condition ($entries -contains $entry) "Archive '$archivePath' is missing '$entry'."
            }
            Assert-Condition (-not ($entries -contains 'System.Private.CoreLib.dll')) "Framework-dependent archive '$archivePath' unexpectedly contains the .NET runtime."

            if ($extension -eq 'tar.gz') {
                Assert-TarExecutable -Path $archivePath -Executable $executable
            }
        }
    }
}

if (-not $SkipSelfContainedArchives) {
    $selfContainedArchives = @(
        Get-ChildItem -LiteralPath $ReleaseDirectory -File |
            Where-Object {
                $_.Name -match '^Packata-cli-.+-(?:win|linux|linux-musl|osx)-(?:x64|arm64)\.(?:zip|tar\.gz)$' -and
                $_.Name -notmatch '-net(?:8|9|10)\.0-'
            }
    )
    Assert-Condition ($selfContainedArchives.Count -eq $ExpectedRids.Count) "Expected $($ExpectedRids.Count) self-contained CLI archives, found $($selfContainedArchives.Count)."

    foreach ($rid in $ExpectedRids) {
        $extension = if ($rid.StartsWith('win-')) { 'zip' } else { 'tar.gz' }
        $archivePath = Join-Path $ReleaseDirectory "Packata-cli-$Version-$rid.$extension"
        Assert-Condition (Test-Path -LiteralPath $archivePath -PathType Leaf) "Missing self-contained CLI archive '$archivePath'."

        $entries = @(Get-ArchiveEntries -Path $archivePath -Extension $extension)
        $executable = if ($rid.StartsWith('win-')) { 'packata.exe' } else { 'packata' }
        $expectedEntries = @($executable, 'LICENSE', 'README.md')
        Assert-Condition ($entries.Count -eq $expectedEntries.Count) "Archive '$archivePath' contains $($entries.Count) files; expected $($expectedEntries.Count)."
        foreach ($entry in $expectedEntries) {
            Assert-Condition ($entries -contains $entry) "Archive '$archivePath' is missing '$entry'."
        }

        if ($extension -eq 'tar.gz') {
            Assert-TarExecutable -Path $archivePath -Executable $executable
        }
    }
}

Write-Host "Validated the requested Packata-cli $Version artifacts."
