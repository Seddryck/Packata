[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][string] $Directory
)
$ErrorActionPreference = 'Stop'
$targets = @(
    @{ Name = 'win32-x64'; Executable = 'Packata.LanguageServer.exe'; Runtime = 'coreclr.dll' },
    @{ Name = 'linux-x64'; Executable = 'Packata.LanguageServer'; Runtime = 'libcoreclr.so' },
    @{ Name = 'darwin-arm64'; Executable = 'Packata.LanguageServer'; Runtime = 'libcoreclr.dylib' }
)
$packages = @(Get-ChildItem -LiteralPath $Directory -Filter '*.vsix' -File)
if ($packages.Count -ne $targets.Count) { throw "Expected $($targets.Count) VSIX files, found $($packages.Count)." }
foreach ($target in $targets) {
    $path = Join-Path $Directory "packata-language-support-$Version-$($target.Name).vsix"
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path))
    try {
        foreach ($name in @('extension/package.json', 'extension/dist/extension.js',
            "extension/server/$($target.Executable)", "extension/server/$($target.Runtime)",
            'extension/server/Packata.LanguageServer.dll', 'extension/server/Packata.LanguageServer.runtimeconfig.json')) {
            if (-not $zip.GetEntry($name)) { throw "Missing '$name' in '$path'." }
        }
        $reader = [IO.StreamReader]::new($zip.GetEntry('extension/package.json').Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        if ($manifest.version -ne $Version) { throw "Unexpected extension version in '$path'." }
        $reader = [IO.StreamReader]::new($zip.GetEntry('extension.vsixmanifest').Open())
        try { $vsixManifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($vsixManifest -notmatch ('TargetPlatform="' + [regex]::Escape($target.Name) + '"')) {
            throw "Unexpected VSIX platform in '$path'."
        }
        if (@($zip.Entries | Where-Object { $_.FullName -match '^extension/node_modules/|/test/|\.pdb$' }).Count -ne 0) {
            throw "Development-only files found in '$path'."
        }
    }
    finally { $zip.Dispose() }
}
Write-Output "Validated $($packages.Count) self-contained VSIX packages for $Version."
