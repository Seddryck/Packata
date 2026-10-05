[CmdletBinding()]
param(
    [string] $Version = $env:GitVersion_SemVer,
    [string] $OutputDirectory = './.publish'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw 'A release version is required.'
}

$baseUrl = "https://datapackage.org/profiles/2.0/"
$schemas = @(
    @{ Class = "Packata.Core.TableDialect";  Value = "tabledialect.json"; Error = "Failed to generate TableDialect schema" },
    @{ Class = "Packata.Core.Schema";        Value = "tableschema.json";  Error = "Failed to generate Table schema" },
    @{ Class = "Packata.Core.Resource";      Value = "dataresource.json"; Error = "Failed to generate Resource schema" },
    @{ Class = "Packata.Core.DataPackage";   Value = "datapackage.json";  Error = "Failed to generate DataPackage schema" }
)

$assemblyPath = ".\src\Packata.Core\bin\Release\net8.0\Packata.Core.dll"

$dir = ".\.schemas"    
    if (Test-Path $dir) {
        Remove-Item -Recurse -Force $dir
    }
    New-Item -Path $dir -ItemType Directory | Out-Null

foreach ($schema in $schemas) {
    Write-Host "Generating schema for $($schema.Class) based on $($baseUrl + $schema.Value)"
    schemathief delta -a $assemblyPath -c $schema.Class -b ($baseUrl + $schema.Value) -x "paths|profile" -o (Join-Path $dir $schema.Value)

    if ($LASTEXITCODE -ne 0) {
        throw $schema.Error
    }
}

$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    Remove-Item -LiteralPath $outputPath -Recurse -Force
}
New-Item -Path $outputPath -ItemType Directory | Out-Null

$archivePath = Join-Path $outputPath "schemas-$Version.zip"
$generatedSchemas = @(Get-ChildItem -LiteralPath $dir -File)
if ($generatedSchemas.Count -eq 0) {
    Write-Host 'No delta schemas were generated; no schema archive is required.'
    return
}

Compress-Archive -Path $generatedSchemas.FullName -DestinationPath $archivePath -CompressionLevel Optimal
