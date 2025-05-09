$ErrorActionPreference = 'Stop'

$schemas = @(
    @{ Class = "Packata.Core.TableDialect";  Url = "https://datapackage.org/profiles/2.0/tabledialect.json"; Output = ".\.schemas\tabledialect.json"; Error = "Failed to generate TableDialect schema" },
    @{ Class = "Packata.Core.Schema";        Url = "https://datapackage.org/profiles/2.0/tableschema.json";   Output = ".\.schemas\tableschema.json";   Error = "Failed to generate Table schema" },
    @{ Class = "Packata.Core.Resource";      Url = "https://datapackage.org/profiles/2.0/dataresource.json";  Output = ".\.schemas\dataresource.json";  Error = "Failed to generate Resource schema" },
    @{ Class = "Packata.Core.DataPackage";   Url = "https://datapackage.org/profiles/2.0/datapackage.json";   Output = ".\.schemas\datapackage.json";   Error = "Failed to generate DataPackage schema" }
)

$assemblyPath = ".\src\Packata.Core\bin\Release\net8.0\Packata.Core.dll"

$dir = ".\.schemas"    
    if (Test-Path $dir) {
        Remove-Item -Recurse -Force $dir
    }
    New-Item -Path $dir -ItemType Directory | Out-Null

foreach ($schema in $schemas) {
    try {
        Write-Host "Generating schema for $($schema.Class)..."
        schemathief delta -a $assemblyPath -c $schema.Class -b $schema.Url -x "paths|profile" -o $schema.Output
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
