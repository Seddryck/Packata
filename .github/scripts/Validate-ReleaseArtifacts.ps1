[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [string] $NuGetDirectory,

    [Parameter(Mandatory)]
    [string] $ReleaseDirectory
)

$ErrorActionPreference = 'Stop'

$packageIds = @(
    'Packata.Core',
    'Packata.DataPackage',
    'Packata.ResourceReaders',
    'Packata.Provisioners',
    'Packata.Storages',
    'Packata.OpenDataContract'
)
$frameworks = @('net8.0', 'net9.0', 'net10.0')

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

$packages = @(Get-ChildItem -LiteralPath $NuGetDirectory -Filter '*.nupkg' -File)
$symbolPackages = @(Get-ChildItem -LiteralPath $NuGetDirectory -Filter '*.snupkg' -File)

Assert-Condition ($packages.Count -eq $packageIds.Count) "Expected $($packageIds.Count) NuGet packages, found $($packages.Count)."
Assert-Condition ($symbolPackages.Count -eq $packageIds.Count) "Expected $($packageIds.Count) symbol packages, found $($symbolPackages.Count)."

foreach ($packageId in $packageIds) {
    $packagePath = Join-Path $NuGetDirectory "$packageId.$Version.nupkg"
    $symbolPath = Join-Path $NuGetDirectory "$packageId.$Version.snupkg"

    Assert-Condition (Test-Path -LiteralPath $packagePath -PathType Leaf) "Missing package '$packagePath'."
    Assert-Condition (Test-Path -LiteralPath $symbolPath -PathType Leaf) "Missing symbol package '$symbolPath'."

    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $packagePath))
    try {
        $entries = @($archive.Entries.FullName)
        Assert-Condition ($entries -contains "$packageId.nuspec") "Package '$packageId' has no matching nuspec."

        foreach ($framework in $frameworks) {
            Assert-Condition ($entries -contains "lib/$framework/$packageId.dll") "Package '$packageId' has no assembly for '$framework'."
        }
    }
    finally {
        $archive.Dispose()
    }
}

$schemaArchivePath = Join-Path $ReleaseDirectory "schemas-$Version.zip"
if (Test-Path -LiteralPath $schemaArchivePath -PathType Leaf) {
    $expectedSchemas = @('tabledialect.json', 'tableschema.json', 'dataresource.json', 'datapackage.json')
    $schemaArchive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $schemaArchivePath))
    try {
        $schemaEntries = @($schemaArchive.Entries.FullName)
        Assert-Condition ($schemaEntries.Count -gt 0) 'The schema archive is empty.'
        foreach ($schema in $schemaEntries) {
            Assert-Condition ($schema -in $expectedSchemas) "Schema archive contains unexpected entry '$schema'."
        }
    }
    finally {
        $schemaArchive.Dispose()
    }

    Write-Host "Validated $($packages.Count) packages, $($symbolPackages.Count) symbol packages, and the schema archive for version $Version."
}
else {
    Write-Host "Validated $($packages.Count) packages and $($symbolPackages.Count) symbol packages for version $Version; no delta schemas were generated."
}
