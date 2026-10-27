#Requires -Version 7.0
[CmdletBinding(DefaultParameterSetName = 'Release')]
param(
    [Parameter(Mandatory, ParameterSetName = 'PullRequest')]
    [ValidateRange(1, [int]::MaxValue)]
    [int] $PullRequest,

    [Parameter(ParameterSetName = 'Release')]
    [ValidateNotNullOrEmpty()]
    [string] $Release = 'latest',

    [ValidatePattern('^[^/]+/[^/]+$')]
    [string] $Repository = 'Seddryck/Packata',

    [ValidateNotNullOrEmpty()]
    [string] $ArchivePattern = 'packata-language-support-*-win32-x64.vsix',

    [ValidateNotNullOrEmpty()]
    [string] $ArtifactName = 'vscode-win32-x64-*',

    [ValidateNotNullOrEmpty()]
    [string] $Workflow = 'ci-release.yml',

    [ValidateSet('Insiders', 'Stable')]
    [string] $Channel = 'Insiders',

    [ValidateNotNullOrEmpty()]
    [string] $CodeCommand,

    [switch] $Force
)
$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('CodeCommand')) {
    $CodeCommand = if ($Channel -eq 'Insiders') { 'code-insiders' } else { 'code' }
}
function Assert-Command {
    param([Parameter(Mandatory)][string] $Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Invoke-GhJson {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI failed: gh $($Arguments -join ' ')"
    }

    return $output | ConvertFrom-Json
}

function Get-ReleaseForCommit {
    param(
        [Parameter(Mandatory)][string] $CommitSha,
        [Parameter(Mandatory)][string] $Repo
    )

    $page = 1
    do {
        $releases = @(Invoke-GhJson @('api', "/repos/$Repo/releases?per_page=100&page=$page"))
        foreach ($candidate in $releases) {
            if ($candidate.draft) {
                continue
            }

            $encodedTag = [uri]::EscapeDataString([string] $candidate.tag_name)
            $tagCommit = Invoke-GhJson @('api', "/repos/$Repo/commits/$encodedTag")
            if ($tagCommit.sha -eq $CommitSha) {
                return [string] $candidate.tag_name
            }
        }

        $page++
    } while ($releases.Count -eq 100)

    throw "No published release tag points to commit $CommitSha."
}

function Save-ReleaseArchive {
    param(
        [Parameter(Mandatory)][string] $Tag,
        [Parameter(Mandatory)][string] $Repo,
        [Parameter(Mandatory)][string] $Destination
    )

    & gh release download $Tag --repo $Repo --pattern $archivePattern --dir $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "Could not download archive matching $ArchivePattern from release '$Tag'."
    }
}

function Save-PullRequestArchive {
    param(
        [Parameter(Mandatory)] $PullRequestData,
        [Parameter(Mandatory)][string] $Repo,
        [Parameter(Mandatory)][string] $Destination
    )

    $headSha = [string] $PullRequestData.head.sha
    $runs = Invoke-GhJson @('api', "/repos/$Repo/actions/workflows/$([uri]::EscapeDataString($Workflow))/runs?event=pull_request&head_sha=$headSha&status=success&per_page=100")
    $run = @($runs.workflow_runs | Where-Object { $_.conclusion -eq 'success' } | Sort-Object created_at -Descending)[0]
    if (-not $run) {
        throw "No successful CI workflow run exists for open PR #$($PullRequestData.number) at $headSha."
    }

    $artifacts = Invoke-GhJson @('api', "/repos/$Repo/actions/runs/$($run.id)/artifacts?per_page=100")
    $matchingArtifacts = @($artifacts.artifacts | Where-Object {
        -not $_.expired -and $_.name -clike $ArtifactName
    } | Sort-Object created_at -Descending)
    if ($matchingArtifacts.Count -eq 0) {
        throw "Workflow run $($run.id) has no unexpired artifact named $ArtifactName."
    }

    if ($matchingArtifacts.Count -ne 1) {
        throw "Workflow run $($run.id) has multiple unexpired artifacts matching $ArtifactName; specify a narrower pattern."
    }
    $artifact = $matchingArtifacts[0]

    Write-Host "Downloading artifact '$($artifact.name)' from workflow run $($run.id)."
    & gh run download $run.id --repo $Repo --name $artifact.name --dir $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "Could not download artifact '$($artifact.name)' from workflow run $($run.id)."
    }
}

Assert-Command 'gh'
Assert-Command $CodeCommand
$downloadDirectory = Join-Path ([IO.Path]::GetTempPath()) "packata/install-vscode/$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
try {
    if ($PSCmdlet.ParameterSetName -eq 'PullRequest') {
        $pr = Invoke-GhJson @('api', "/repos/$Repository/pulls/$PullRequest")
        if ($pr.state -eq 'open') {
            Save-PullRequestArchive -PullRequestData $pr -Repo $Repository -Destination $downloadDirectory
        }
        elseif ($pr.merged_at) {
            $tag = Get-ReleaseForCommit -CommitSha ([string] $pr.merge_commit_sha) -Repo $Repository
            Write-Host "PR #$PullRequest was merged as $($pr.merge_commit_sha); downloading release '$tag'."
            Save-ReleaseArchive -Tag $tag -Repo $Repository -Destination $downloadDirectory
        }
        else {
            throw "PR #$PullRequest is closed without being merged, so it has no corresponding release."
        }
    }
    else {
        if ($Release -eq 'latest') {
            $latest = Invoke-GhJson @('api', "/repos/$Repository/releases/latest")
            $tag = [string] $latest.tag_name
        }
        else {
            $tag = $Release
        }

        Write-Host "Downloading Packata VS Code extension from release '$tag'."
        Save-ReleaseArchive -Tag $tag -Repo $Repository -Destination $downloadDirectory
    }

    $packages = @(Get-ChildItem -LiteralPath $downloadDirectory -Filter $ArchivePattern -File -Recurse)
    if ($packages.Count -ne 1) {
        throw "Expected exactly one VSIX matching $ArchivePattern, but found $($packages.Count)."
    }
    $package = $packages[0]
    # Read metadata without extracting or running anything from the package.
    $zip = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entry = $zip.GetEntry('extension/package.json')
        if (-not $entry) { throw 'VSIX has no extension/package.json.' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        $extensionId = "$($manifest.publisher).$($manifest.name)"
        if ($extensionId -cne 'seddryck.packata-language-support' -or -not $manifest.version) {
            throw "Unexpected VSIX identity: $extensionId."
        }
        $expected = "$extensionId@$($manifest.version)"
    }
    finally { $zip.Dispose() }
    $arguments = @('--install-extension', $package.FullName)
    if ($Force) { $arguments += '--force' }
    & $CodeCommand @arguments
    if ($LASTEXITCODE -ne 0) { throw "VS Code command '$CodeCommand' failed to install the VSIX." }
    $installed = @(& $CodeCommand --list-extensions --show-versions)
    if ($LASTEXITCODE -ne 0 -or $expected -notin $installed) {
        throw "VS Code command '$CodeCommand' did not report $expected as installed."
    }
    Write-Host "Installed $expected using '$CodeCommand' ($Channel). Reload the editor window to activate the update."
}
finally {
    $resolvedDownload = [IO.Path]::GetFullPath($downloadDirectory)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedDownload.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Download cleanup path is outside temporary directory.'
    }
    Remove-Item -LiteralPath $resolvedDownload -Recurse -Force -ErrorAction SilentlyContinue
}
