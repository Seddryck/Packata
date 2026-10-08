[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [string] $NuGetDirectory,

    [string] $ReleaseDirectory,

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

    [switch] $SkipReleaseArchives
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
    return @($entries | ForEach-Object { $_ -replace '^\./', '' } | Where-Object { $_ })
}

if (-not $SkipNuGetPackage) {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($NuGetDirectory)) 'A NuGet directory is required when package validation is enabled.'
    $packages = @(Get-ChildItem -LiteralPath $NuGetDirectory -Filter '*.nupkg' -File)
    $symbolPackages = @(Get-ChildItem -LiteralPath $NuGetDirectory -Filter '*.snupkg' -File)
    $packagePath = Join-Path $NuGetDirectory "Packata-cli.$Version.nupkg"

    Assert-Condition ($packages.Count -eq 1) "Expected one Packata CLI NuGet package, found $($packages.Count)."
    Assert-Condition ($symbolPackages.Count -eq 0) "Packata CLI must not publish symbol packages; found $($symbolPackages.Count)."
    Assert-Condition (Test-Path -LiteralPath $packagePath -PathType Leaf) "Missing package '$packagePath'."

    $packageArchive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $packagePath))
    try {
        $entries = @($packageArchive.Entries.FullName)
        Assert-Condition ($entries -contains 'Packata-cli.nuspec') 'The CLI package has no matching nuspec.'
        Assert-Condition ($entries -contains 'tools/net10.0/any/packata.dll') 'The CLI package does not target net10.0.'
        Assert-Condition ($entries -contains 'tools/net10.0/any/DotnetToolSettings.xml') 'The CLI package has no .NET tool settings.'
        Assert-Condition (-not ($entries | Where-Object { $_ -match '^tools/net(?:8|9)\.0/' })) 'The CLI package contains an unsupported target framework.'

        $settingsEntry = $packageArchive.GetEntry('tools/net10.0/any/DotnetToolSettings.xml')
        $reader = [System.IO.StreamReader]::new($settingsEntry.Open())
        try {
            [xml] $settings = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $command = $settings.DotNetCliTool.Commands.Command
        Assert-Condition ($command.Name -eq 'packata') "Expected tool command 'packata', found '$($command.Name)'."
        Assert-Condition ($command.EntryPoint -eq 'packata.dll') "Expected tool entry point 'packata.dll', found '$($command.EntryPoint)'."
    }
    finally {
        $packageArchive.Dispose()
    }
}

if (-not $SkipReleaseArchives) {
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($ReleaseDirectory)) 'A release directory is required when archive validation is enabled.'
    $releaseArchives = @(
        Get-ChildItem -LiteralPath $ReleaseDirectory -File |
            Where-Object { $_.Name -match '^Packata-cli-.+-(?:win|linux|linux-musl|osx)-(?:x64|arm64)\.(?:zip|tar\.gz)$' }
    )
    Assert-Condition ($releaseArchives.Count -eq $ExpectedRids.Count) "Expected $($ExpectedRids.Count) CLI release archives, found $($releaseArchives.Count)."

    foreach ($rid in $ExpectedRids) {
        $extension = if ($rid.StartsWith('win-')) { 'zip' } else { 'tar.gz' }
        $archivePath = Join-Path $ReleaseDirectory "Packata-cli-$Version-$rid.$extension"
        Assert-Condition (Test-Path -LiteralPath $archivePath -PathType Leaf) "Missing CLI archive '$archivePath'."

        $archiveEntries = if ($extension -eq 'zip') { Get-ZipEntries $archivePath } else { Get-TarEntries $archivePath }
        $executable = if ($rid.StartsWith('win-')) { 'packata.exe' } else { 'packata' }
        $expectedEntries = @($executable, 'LICENSE', 'README.md')

        Assert-Condition ($archiveEntries.Count -eq $expectedEntries.Count) "Archive '$archivePath' contains $($archiveEntries.Count) files; expected $($expectedEntries.Count)."
        foreach ($entry in $expectedEntries) {
            Assert-Condition ($archiveEntries -contains $entry) "Archive '$archivePath' is missing '$entry'."
        }

        foreach ($entry in $archiveEntries) {
            Assert-Condition ($entry -in $expectedEntries) "Archive '$archivePath' contains unexpected entry '$entry'."
        }

        if ($extension -eq 'tar.gz') {
            $details = @(& tar -tvzf $archivePath)
            if ($LASTEXITCODE -ne 0) {
                throw "Could not inspect permissions in archive '$archivePath'."
            }
            Assert-Condition ([bool]($details | Where-Object { $_ -match '^-rwx.*\s(?:\./)?packata$' })) "Archive '$archivePath' does not preserve executable permissions for 'packata'."
        }
    }
}

Write-Host "Validated the requested Packata-cli $Version artifacts."
