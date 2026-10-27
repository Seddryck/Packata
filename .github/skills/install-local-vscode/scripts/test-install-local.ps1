# Offline VSIX integration tests. GitHub and editor commands are mocked; no editor is changed.
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "packata-vsix-test-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $testState = @{ Calls=@(); LastDownload=$null }
    function Assert-Test($Condition, $Message) {
        if (-not $Condition) { throw $Message }
    }
    function Assert-Fails($Action, $Pattern) {
        try { & $Action } catch {
            Assert-Test ($_.Exception.Message -like $Pattern) "Unexpected failure: $_"
            return
        }
        throw "Expected failure matching $Pattern."
    }
    function gh {
        $global:LASTEXITCODE = 0
        if ($args[0] -eq 'api') {
            $endpoint = [string]$args[1]
            Assert-Test ($endpoint.StartsWith("/repos/$($scenario.Repository)/", [StringComparison]::Ordinal)) "Repository casing changed: $endpoint"
            switch -Wildcard ($endpoint) {
                '*/pulls/*' {
                    if ($mode -eq 'merged') {
                        '{"number":642,"state":"closed","merged_at":"2026-01-01","merge_commit_sha":"merge"}'
                    } elseif ($mode -eq 'closed') {
                        '{"number":642,"state":"closed","merged_at":null}'
                    } else {
                        '{"number":642,"state":"open","head":{"sha":"head"}}'
                    }
                }
                '*/actions/workflows/*' {
                    Assert-Test ($endpoint.Contains("/$($scenario.Workflow)/runs?")) "Wrong workflow: $endpoint"
                    Assert-Test ($endpoint.Contains('head_sha=head&status=success')) 'Head/success filter missing.'
                    '{"workflow_runs":[{"id":42,"conclusion":"success","created_at":"2026-01-02"},{"id":41,"conclusion":"success","created_at":"2026-01-01"}]}'
                }
                '*/actions/runs/42/artifacts*' {
                    $artifacts = @(@{name=$scenario.ArtifactName.Replace('*','1.2.3');expired=($mode -eq 'expired')})
                    if ($mode -eq 'ambiguous') { $artifacts += @{name=$scenario.ArtifactName.Replace('*','1.2.4');expired=$false} }
                    @{artifacts=$artifacts} | ConvertTo-Json -Depth 5
                }
                '*/releases[?]*' { '[{"tag_name":"test","draft":false}]' }
                '*/commits/test' { '{"sha":"merge"}' }
                '*/releases/latest' { '{"tag_name":"test"}' }
                default { throw "Unexpected endpoint: $endpoint" }
            }
        } elseif ($args[0] -in @('release', 'run')) {
            Assert-Test ($args[[Array]::IndexOf($args,'--repo')+1] -ceq $scenario.Repository) 'Download repository casing changed.'
            if ($args[0] -eq 'release') {
                Assert-Test ($args[2] -ceq 'test') 'Wrong release tag.'
                Assert-Test ($args[[Array]::IndexOf($args,'--pattern')+1] -ceq $scenario.ArchivePattern) 'Wrong VSIX pattern.'
            } else {
                Assert-Test ($args[2] -eq 42) 'Did not select newest successful run.'
                Assert-Test ($args[[Array]::IndexOf($args,'--name')+1] -ceq $scenario.ArtifactName.Replace('*','1.2.3')) 'Wrong artifact selected.'
            }
            $download = $args[[Array]::IndexOf($args,'--dir')+1]
            $testState.LastDownload = $download
            Copy-Item -LiteralPath $package -Destination $download
        } else { throw "Unexpected GitHub command: $args" }
    }
    function Invoke-TestEditor($CommandName, $Arguments) {
        $global:LASTEXITCODE = 0
        $testState.Calls += ,(@($CommandName) + $Arguments)
        if ($Arguments[0] -eq '--install-extension') {
            Assert-Test (Test-Path -LiteralPath $Arguments[1]) 'Editor received a missing VSIX.'
            Assert-Test (($Arguments -contains '--force') -eq $expectForce) 'Force option was changed.'
            if ($mode -eq 'editor-failure') { $global:LASTEXITCODE = 1 }
        } elseif ($Arguments[0] -eq '--list-extensions') {
            Assert-Test ($Arguments -contains '--show-versions') 'Version verification missing.'
            if ($mode -ne 'verification-failure') { 'seddryck.packata-language-support@1.2.3' }
        } else { throw "Unexpected editor command: $Arguments" }
    }
    function code-insiders { Invoke-TestEditor 'code-insiders' $args }
    function code { Invoke-TestEditor 'code' $args }
    function custom-editor { Invoke-TestEditor 'custom-editor' $args }

    foreach ($scenario in @(
        @{Repository='Seddryck/Packata'; Workflow='ci-release.yml'; ArtifactName='vscode-win32-x64-*'; ArchivePattern='packata-language-support-*-win32-x64.vsix'},
        @{Repository='ExampleOwner/PackataFork'; Workflow='Custom.yml'; ArtifactName='VSIX-Custom-*'; ArchivePattern='custom-package-*.vsix'}
    )) {
        $source = Join-Path $testRoot ([guid]::NewGuid().ToString())
        New-Item -ItemType Directory -Path (Join-Path $source 'extension') -Force | Out-Null
        Set-Content (Join-Path $source 'extension/package.json') '{"publisher":"seddryck","name":"packata-language-support","version":"1.2.3"}'
        $package = Join-Path $source ($scenario.ArchivePattern.Replace('*','1.2.3'))
        $zip = Join-Path $source 'fixture.zip'
        Compress-Archive -Path (Join-Path $source 'extension') -DestinationPath $zip
        Move-Item -LiteralPath $zip -Destination $package

        $parameters = if ($scenario.Repository -ceq 'Seddryck/Packata') { @{} } else { $scenario }
        foreach ($mode in @('release','named','open','merged')) {
            $testState.Calls = @()
            $expectForce = ($mode -eq 'named')
            $sourceParameters = if ($mode -in @('open','merged')) { @{PullRequest=642} } elseif ($mode -eq 'named') { @{Release='test';Force=$true} } else { @{} }
            & "$PSScriptRoot/install-local.ps1" @parameters @sourceParameters 6>$null
            Assert-Test ($testState.Calls.Count -eq 2) 'Expected install followed by verification.'
            Assert-Test ($testState.Calls[0][0] -ceq 'code-insiders') 'Default installation did not target Insiders.'
            Assert-Test (-not (Test-Path -LiteralPath $testState.LastDownload)) 'Download directory was not cleaned.'
        }
        $mode = 'release'
        $expectForce = $false
        $testState.Calls = @()
        & "$PSScriptRoot/install-local.ps1" @parameters -Channel Stable 6>$null
        Assert-Test ($testState.Calls[0][0] -ceq 'code') 'Explicit Stable selection ignored.'
        $testState.Calls = @()
        & "$PSScriptRoot/install-local.ps1" @parameters -CodeCommand custom-editor 6>$null
        Assert-Test ($testState.Calls[0][0] -ceq 'custom-editor') 'Explicit editor command ignored.'
        foreach ($mode in @('closed','expired','ambiguous','editor-failure','verification-failure')) {
            $testState.Calls = @()
            $pattern = switch ($mode) {
                closed { '*closed without being merged*' }
                expired { '*no unexpired artifact*' }
                ambiguous { '*multiple unexpired artifacts*' }
                editor-failure { '*failed to install*' }
                verification-failure { '*did not report*' }
            }
            Assert-Fails { & "$PSScriptRoot/install-local.ps1" @parameters -PullRequest 642 6>$null } $pattern
            if ($mode -in @('closed','expired','ambiguous')) {
                Assert-Test ($testState.Calls.Count -eq 0) 'Editor invoked despite invalid source.'
            }
        }
        Set-Content (Join-Path $source 'extension/package.json') '{"publisher":"other","name":"extension","version":"1.2.3"}'
        Compress-Archive -Path (Join-Path $source 'extension') -DestinationPath $zip -Force
        Move-Item -LiteralPath $zip -Destination $package -Force
        $mode = 'release'
        $testState.Calls = @()
        Assert-Fails { & "$PSScriptRoot/install-local.ps1" @parameters 6>$null } '*Unexpected VSIX identity*'
        Assert-Test ($testState.Calls.Count -eq 0) 'Unexpected extension was installed.'
    }
    Write-Output 'Passed: VSIX releases/PRs, newest run, versioned artifacts, Insiders/Stable/custom command, force, verification, identity checks, failure handling and cleanup.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Test cleanup path is outside temporary directory.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
