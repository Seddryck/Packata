$ErrorActionPreference = 'Stop'

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
    try {
        Write-Host "Generating schema for $($schema.Class) based on $($baseUrl + $schema.Value)"
        schemathief delta -a $assemblyPath -c $schema.Class -b ($baseUrl + $schema.Value) -x "paths|profile" -o (Join-Path $dir $schema.Value)
    } catch {
        Write-Error $schema.Error
        exit 1
    }
}

$dir = ".\.publish"    
if (Test-Path $dir) {
    Remove-Item -Recurse -Force $dir
}
New-Item -Path $dir -ItemType Directory | Out-Null
7z a .\.publish\schemas-$env:GitVersion_SemVer.zip .\.schemas\*.* | Out-Null
