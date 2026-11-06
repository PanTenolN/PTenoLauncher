function New-VerificationCandidate([string]$Path, [string]$Origin, [string]$Use) {
    if (!(Test-Path -LiteralPath $Path)) { return }
    $bytes = $null
    try { $bytes = (Read-VerificationTree $Path).Bytes }
    catch { $Use += " Measurement refused: $($_.Exception.Message)" }
    [pscustomobject]@{ Path = $Path; Bytes = $bytes; Origin = $Origin; Needed = $Use; Action = 'PreviewOnly' }
}

function Get-ProjectBuildCandidates([string]$Source, [string]$Origin) {
    foreach ($area in 'src','tests','scripts') {
        $base = Join-Path $Source $area
        if (!(Test-Path -LiteralPath $base -PathType Container)) { continue }
        Assert-VerificationPath $base
        foreach ($project in (Get-ChildItem -LiteralPath $base -Directory)) {
            Assert-VerificationPath $project.FullName
            if (!(Get-ChildItem -LiteralPath $project.FullName -Filter '*.csproj' -File)) { continue }
            New-VerificationCandidate (Join-Path $project.FullName 'obj') "$Origin MSBuild intermediates" 'Rebuildable. Preserve any logs and TRX separately.'
            $use = if ($project.Name -in 'PTenoLauncher.Desktop','PTenoLauncher.UpdateHelper') {
                'Keep. Existing launcher/helper commands may use these binaries.'
            } else {
                'Rebuildable. Older scripts and historical reproduction commands may still use these binaries.'
            }
            New-VerificationCandidate (Join-Path $project.FullName 'bin') "$Origin MSBuild binaries" $use
        }
    }
}

function Write-VerificationInventory {
    Assert-VerificationPath $evidenceBase
    [IO.Directory]::CreateDirectory($evidenceBase) | Out-Null
    $candidates = @(Get-ProjectBuildCandidates $projectRoot 'Workspace project')
    $local = Join-Path $projectRoot '.local'
    Assert-VerificationPath $local
    if (Test-Path -LiteralPath $local) {
        foreach ($directory in (Get-ChildItem -LiteralPath $local -Directory -Force)) {
            if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
            if ($directory.Name -in 'nuget','cli','appdata','localappdata','user','test-runs' -or
                $directory.Name -like 'manual-*') { continue }
            foreach ($leaf in 'bin','obj','build-a','build-b','build-bad') {
                $origin = if (Test-Path -LiteralPath (Join-Path $directory.FullName '.pteno-owned') -PathType Leaf) {
                    'Previously marked local build operation'
                } elseif ($directory.Name -like 'updater-e2e-*') {
                    'Historical updater E2E naming pattern. Ownership requires separate evidence.'
                } else { 'Historical local build layout. Owner unverified.' }
                $use = if ($directory.Name -eq 'content-cart-names-20261106' -and $leaf -eq 'bin') {
                    'Keep. SESSION_CHECKPOINT.md and the previous manual command distinguish this build from the current retained package.'
                } else { 'Review older run evidence before any maintenance. Current verification does not use this copy.' }
                $candidates += @(New-VerificationCandidate (Join-Path $directory.FullName $leaf) $origin $use)
            }
            if (Test-Path -LiteralPath (Join-Path $directory.FullName 'PTenoLauncher.exe') -PathType Leaf) {
                # Measure only application files. Neighboring portable data can be a real profile.
                $parts = @(Get-ChildItem -LiteralPath $directory.FullName -Force |
                    Where-Object { $_.Name -in 'runtimes','docs','updater' -or
                        (!$_.PSIsContainer -and $_.Name -match '\.(dll|exe|pdb|deps\.json|runtimeconfig\.json)$') })
                $bytes = $null
                $use = 'Keep until the startup or historical reproduction role is confirmed. Adjacent profile data is protected.'
                try {
                    $bytes = ($parts | ForEach-Object { (Read-VerificationTree $_.FullName).Bytes } | Measure-Object -Sum).Sum
                } catch { $use += " Measurement refused: $($_.Exception.Message)" }
                $candidates += [pscustomobject]@{ Path = $directory.FullName; Bytes = $bytes; Origin = 'Historical extracted launcher files only'; Needed = $use; Action = 'PreviewOnly' }
            }
            if ($directory.Name -eq 'github-source') {
                foreach ($checkout in (Get-ChildItem -LiteralPath $directory.FullName -Directory)) {
                    if (!($checkout.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                        $candidates += @(Get-ProjectBuildCandidates $checkout.FullName 'Historical local checkout')
                    }
                }
            }
        }
    }
    if (Test-Path -LiteralPath $operationsBase) {
        Assert-VerificationPath $operationsBase
        foreach ($operation in (Get-ChildItem -LiteralPath $operationsBase -Directory -Filter 'verification-*')) {
            $candidates += @(New-VerificationCandidate $operation.FullName 'Verification operation naming pattern' 'Recovery requires matching markers and an exited owner.')
        }
    }
    $report = Join-Path $evidenceBase 'storage-preview.json'
    Assert-VerificationPath $report
    $protected = @('.tools/dotnet','.local/nuget','.local/manual-*','AppData launcher profiles','release packages',
        'manual-testing/HexCasting','artifacts/profile-mods-navigation-20261106/launcher')
    Write-VerificationJson $report @{ GeneratedUtc = [DateTime]::UtcNow; Candidates = $candidates; Protected = $protected }
    $candidates | Format-Table Path, Bytes, Origin, Needed -Wrap
    Write-Host "Read-only storage preview: $report"
}
