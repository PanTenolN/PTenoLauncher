param([switch]$Execute)

# Explicit project maintenance only. No user data or system caches are targets.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6)
$reportRoot = Join-Path $projectRoot ('artifacts\maintenance\' + $runId)
New-Item -ItemType Directory -Path $reportRoot -Force | Out-Null
$activeProcessPaths = @(Get-Process -ErrorAction SilentlyContinue | ForEach-Object { try { if ($_.Path) { $_.Path } } catch { } })
$initialFreeBytes = ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($projectRoot))).AvailableFreeSpace

function Assert-ProjectPath([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (-not $absolute.StartsWith($projectRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Target is outside the project: $absolute"
    }
    $ancestor = $absolute
    while ($ancestor.Length -ge $projectRoot.Length) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Link/reparse point refused: $ancestor"
            }
        }
        if ($ancestor -eq $projectRoot) { break }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $absolute
}

function Read-SafeTree([string]$Path) {
    $absolute = Assert-ProjectPath $Path
    $pending = [Collections.Generic.Stack[IO.FileSystemInfo]]::new()
    $files = [Collections.Generic.List[IO.FileInfo]]::new()
    $pending.Push((Get-Item -LiteralPath $absolute -Force))
    [long]$bytes = 0
    while ($pending.Count -gt 0) {
        $item = $pending.Pop()
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Link/reparse point refused: $($item.FullName)"
        }
        if ($item -is [IO.DirectoryInfo]) {
            foreach ($child in $item.EnumerateFileSystemInfos()) { $pending.Push($child) }
        } else { $files.Add($item); $bytes += $item.Length }
    }
    return [pscustomobject]@{ Files = $files; Bytes = $bytes }
}

function Assert-NotRunning([string]$Path) {
    foreach ($executable in $activeProcessPaths) {
        if ($executable -and ($executable.Equals($Path, [StringComparison]::OrdinalIgnoreCase) -or
            $executable.StartsWith($Path.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase))) {
            throw "Running process uses cleanup target; no process will be stopped: $Path"
        }
    }
}

function Read-ProtectedHashes {
    $result = @{}
    foreach ($directory in @('src','tests','scripts','docs','installer','.github','.codex')) {
        $base = Join-Path $projectRoot $directory
        if (-not (Test-Path -LiteralPath $base)) { continue }
        foreach ($file in (Read-SafeTree $base).Files) {
            $relative = [IO.Path]::GetRelativePath($projectRoot, $file.FullName)
            if ($relative -match '(^|\\)(bin|obj)(\\|$)') { continue }
            $result[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    foreach ($file in (Get-ChildItem -LiteralPath $projectRoot -File -Force)) {
        $result[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    foreach ($relative in @('src\PTenoLauncher.Desktop\bin\Release\net10.0','src\PTenoLauncher.UpdateHelper\bin\Release\net10.0')) {
        $directory = Join-Path $projectRoot $relative
        if (Test-Path -LiteralPath $directory) {
            foreach ($file in (Read-SafeTree $directory).Files) {
                $result[[IO.Path]::GetRelativePath($projectRoot, $file.FullName)] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }
        }
    }
    return $result
}

$targets = [Collections.Generic.List[object]]::new()
$seenTargets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-Target([string]$Relative, [string]$Kind) {
    $path = Assert-ProjectPath (Join-Path $projectRoot $Relative)
    if (-not (Test-Path -LiteralPath $path)) { return }
    if (-not $seenTargets.Add($path)) { return }
    $tree = Read-SafeTree $path
    # Synthetic updater E2E data is created by the harness and explicitly owned by it.
    # Every other target must be free of profile databases and protected secrets.
    if ($Kind -ne 'updater-e2e' -and $tree.Files.Where({ $_.Extension -eq '.dpapi' -or $_.Name -like 'settings.db*' }).Count -gt 0) {
        throw "Profile data found in candidate; refused: $Relative"
    }
    Assert-NotRunning $path
    $targets.Add([pscustomobject]@{ Relative = $Relative; Path = $path; Kind = $Kind; Bytes = $tree.Bytes; Files = $tree.Files.Count })
}

foreach ($directory in (Get-ChildItem -LiteralPath (Join-Path $projectRoot '.local') -Directory -Force)) {
    if ($directory.Name -match '^updater-e2e-[0-9]+-[0-9a-f]{6}$') {
        if (-not (Test-Path -LiteralPath (Join-Path $directory.FullName 'source-a\Directory.Build.props'))) {
            throw "Updater test ownership not established: $($directory.FullName)"
        }
        Add-Target ('.local\' + $directory.Name) 'updater-e2e'
    }
}
Add-Target '.local\checkpoints' 'old-source-snapshots'
Add-Target '.local\localappdata\NuGet\v3-cache' 'http-cache'
Add-Target '.tools\innosetup-6.7.3.exe' 'downloaded-tool-installer'

# These are old extracted application copies. Their portable-data directories stay untouched.
$binaryCopies = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot '.local') -Directory -Force |
    Where-Object { $_.Name -match '^(portable-|auth-fix-portable-check-)' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'PTenoLauncher.exe')) })
foreach ($relative in @('artifacts\win-x64','artifacts\0.2.0\win-x64','artifacts\0.2.0\auth-fix-win-x64','artifacts\0.2.0\online-ai-win-x64','artifacts\0.2.0\quilt-mods-win-x64')) {
    $path = Join-Path $projectRoot $relative
    if (Test-Path -LiteralPath $path) { $binaryCopies += Get-Item -LiteralPath $path }
}
foreach ($directory in $binaryCopies) {
    $copyPaths = [Collections.Generic.List[string]]::new()
    [long]$copyBytes = 0
    [long]$copyFiles = 0
    foreach ($item in (Get-ChildItem -LiteralPath $directory.FullName -Force)) {
        if ($item.PSIsContainer -and $item.Name -notin @('runtimes','docs')) { continue }
        if (-not $item.PSIsContainer -and $item.Extension -notin @('.dll','.exe','.pdb','.xml','.md') -and $item.Name -notmatch '\.(deps|runtimeconfig)\.json$') { continue }
        $tree = Read-SafeTree $item.FullName
        if ($tree.Files.Where({ $_.Extension -eq '.dpapi' -or $_.Name -like 'settings.db*' }).Count -gt 0) { throw 'Unexpected user data in binary copy.' }
        $copyBytes += $tree.Bytes
        $copyFiles += $tree.Files.Count
        $copyPaths.Add($item.FullName)
    }
    if ($copyPaths.Count -gt 0) {
        Assert-NotRunning $directory.FullName
        $targets.Add([pscustomobject]@{ Relative = [IO.Path]::GetRelativePath($projectRoot, $directory.FullName); Path = $directory.FullName; Kind = 'obsolete-application-copy'; Bytes = $copyBytes; Files = $copyFiles; RemovalPaths = $copyPaths.ToArray() })
    }
}
foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $projectRoot 'artifacts') -File -Force)) {
    if ($file.Name -match '^PTenoLauncher-(Portable|Setup|Source)-.+\.(zip|exe)$') {
        Add-Target ([IO.Path]::GetRelativePath($projectRoot, $file.FullName)) 'old-distribution-package'
    }
}
foreach ($base in @('src','tests','scripts')) {
    foreach ($directory in (Get-ChildItem -LiteralPath (Join-Path $projectRoot $base) -Directory -Force)) {
        Add-Target ([IO.Path]::GetRelativePath($projectRoot, (Join-Path $directory.FullName 'obj'))) 'build-intermediates'
        if ($base -ne 'src') { Add-Target ([IO.Path]::GetRelativePath($projectRoot, (Join-Path $directory.FullName 'bin'))) 'rebuildable-tool-test-binaries' }
    }
}

$plan = [pscustomobject]@{ Root = $projectRoot; Execute = $Execute.IsPresent; CreatedUtc = [DateTime]::UtcNow; Targets = $targets; Bytes = ($targets | Measure-Object -Property Bytes -Sum).Sum }
$plan | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportRoot 'plan.json') -Encoding utf8
Write-Output ("Plan: {0} targets; {1:N3} GiB; report: {2}" -f $targets.Count, ($plan.Bytes / 1GB), $reportRoot)
if (-not $Execute) { return }

$activeProcessPaths = @(Get-Process -ErrorAction SilentlyContinue | ForEach-Object { try { if ($_.Path) { $_.Path } } catch { } })
$before = Read-ProtectedHashes
$before | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $reportRoot 'protected-before.json') -Encoding utf8
$completed = [Collections.Generic.List[object]]::new()
foreach ($target in $targets) {
    $path = Assert-ProjectPath $target.Path
    Assert-NotRunning $path
    if ($target.Kind -eq 'obsolete-application-copy') {
        foreach ($removal in $target.RemovalPaths) {
            $null = Read-SafeTree (Assert-ProjectPath $removal)
        }
        Remove-Item -LiteralPath $target.RemovalPaths -Recurse -Force -ErrorAction Stop
        $completed.Add([pscustomobject]@{ Path = $target.Relative; Kind = $target.Kind; RemovedBytes = $target.Bytes; EvidenceFilesRetained = 0 })
        $completed | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $reportRoot 'completed.json') -Encoding utf8
        Write-Output ("Removed old binaries: {0}" -f $target.Relative)
        continue
    }
    $tree = Read-SafeTree $path
    $evidence = [Collections.Generic.List[object]]::new()
    if ($target.Kind -eq 'updater-e2e') {
        [long]$evidenceBytes = 0
        foreach ($file in $tree.Files) {
            $relative = [IO.Path]::GetRelativePath($path, $file.FullName)
            if ($relative -match '^(source-|build-)') { continue }
            if ($file.Extension -notin @('.json','.log','.txt','.trx','.png','.pem') -or $file.Name -match '\.(deps|runtimeconfig)\.json$') { continue }
            $evidenceBytes += $file.Length
            if ($file.Length -gt 16MB -or $evidenceBytes -gt 128MB) { throw 'Evidence exceeds bounds; refusing deletion.' }
            $destination = Join-Path $reportRoot ('evidence\' + (Split-Path $path -Leaf) + '\' + $relative)
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination
            $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) { throw 'Evidence copy verification failed.' }
            $evidence.Add([pscustomobject]@{ Relative = $relative; Stored = $destination; Hash = $hash })
        }
    }
    # Native PowerShell end to end; target paths and all descendants were checked above.
    if ((Get-Item -LiteralPath $path -Force).PSIsContainer) {
        Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop
    } else { Remove-Item -LiteralPath $path -Force -ErrorAction Stop }
    if (Test-Path -LiteralPath $path) { throw "Target still exists: $path" }
    if ($evidence.Count -gt 0) {
        foreach ($record in $evidence) {
            $destination = Join-Path $path $record.Relative
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $record.Stored -Destination $destination
            if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $record.Hash) { throw 'Restored evidence verification failed.' }
        }
        'Historical test evidence only. Generated binaries, packages and synthetic rollback copies were removed by owner-authorized cleanup. This is not a usable installation or a new test result.' |
            Set-Content -LiteralPath (Join-Path $path 'PAYLOADS_REMOVED.txt') -Encoding utf8
    }
    $completed.Add([pscustomobject]@{ Path = $target.Relative; Kind = $target.Kind; RemovedBytes = $tree.Bytes; EvidenceFilesRetained = $evidence.Count })
    $completed | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $reportRoot 'completed.json') -Encoding utf8
    if ($target.Kind -ne 'obsolete-application-copy') { Write-Output ("Removed: {0} ({1:N1} MiB)" -f $target.Relative, ($tree.Bytes / 1MB)) }
}

$after = Read-ProtectedHashes
if ($before.Count -ne $after.Count) { throw 'Protected file set changed.' }
foreach ($relative in $before.Keys) {
    if (-not $after.ContainsKey($relative) -or $after[$relative] -ne $before[$relative]) { throw "Protected file changed: $relative" }
}
$summary = [pscustomobject]@{ State = 'COMPLETED'; ProtectedFilesVerified = $after.Count; TargetsCompleted = $completed.Count; RemovedPayloadBytes = ($completed | Measure-Object -Property RemovedBytes -Sum).Sum; FreeBytesBefore = $initialFreeBytes; FreeBytesAfter = ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($projectRoot))).AvailableFreeSpace; Report = $reportRoot; Note = 'Evidence retained. No project build/test rerun or user data deletion. SDK, dependencies, current Desktop/helper binaries retained.' }
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $reportRoot 'result.json') -Encoding utf8
Write-Output ($summary | ConvertTo-Json -Compress)
