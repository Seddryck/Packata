# Check whether GitVersion is installed
$gitVersion = Get-Command gitversion -ErrorAction SilentlyContinue

if ($gitVersion) {
    Write-Host "GitVersion is already installed."
    Write-Host "Path: $($gitVersion.Source)"
    exit 0
}

Write-Host "GitVersion is not installed. Installing..."

# Check whether dotnet is available
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue

if (-not $dotnet) {
    Write-Error ".NET SDK is not installed or not available in PATH. Install the .NET SDK first."
    exit 1
}

# Install GitVersion as a .NET global tool
$env:DOTNET_NOLOGO = "1"
dotnet tool install --global GitVersion.Tool

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to install GitVersion.Tool."
    exit 1
}

# .NET global tools path
$dotnetToolsPath = Join-Path $env:USERPROFILE ".dotnet\tools"

# Add to User PATH if missing
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")

if ($userPath -notlike "*$dotnetToolsPath*") {
    Write-Host "Adding .NET tools folder to User PATH..."

    $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
        $dotnetToolsPath
    }
    else {
        "$userPath;$dotnetToolsPath"
    }

    [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
}
else {
    Write-Host ".NET tools folder is already in User PATH."
}

# Update PATH for current session
$env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
            [Environment]::GetEnvironmentVariable("Path", "User")

$dotnetToolsPath = Join-Path $env:USERPROFILE ".dotnet\tools"
$gitVersionExe = Join-Path $dotnetToolsPath "dotnet-gitversion.exe"
$shimPath = Join-Path $dotnetToolsPath "gitversion.cmd"

if (-not (Test-Path $gitVersionExe)) {
    Write-Error "dotnet-gitversion.exe was not found. Install GitVersion.Tool first."
    exit 1
}

@"
@echo off
"%USERPROFILE%\.dotnet\tools\dotnet-gitversion.exe" %*
"@ | Set-Content -Path $shimPath -Encoding ASCII

Write-Host "Alias created: gitversion -> dotnet-gitversion"
Write-Host "You can now run: gitversion /version"

# Validate installation
$gitVersion = Get-Command gitversion -ErrorAction SilentlyContinue

if ($gitVersion) {
    Write-Host "GitVersion installed successfully."
    Write-Host "Path: $($gitVersion.Source)"
    gitversion /version
}
else {
    Write-Warning "GitVersion was installed, but it is not available in the current session."
    Write-Warning "Open a new terminal and run: gitversion /version"
}
