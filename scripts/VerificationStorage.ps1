function Assert-VerificationPath([string]$Path) {
    if ($Path -cne [IO.Path]::GetFullPath($Path) -or
        !$Path.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside verification workspace: $Path"
    }
    for ($ancestor = $Path; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        try { $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop }
        catch [Management.Automation.ItemNotFoundException] { continue }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Link or reparse point: $ancestor" }
    }
}

function Read-VerificationTree([string]$Path) {
    Assert-VerificationPath $Path
    $pending = [Collections.Generic.Stack[IO.FileSystemInfo]]::new()
    $pending.Push((Get-Item -LiteralPath $Path -Force))
    $files = [Collections.Generic.List[IO.FileInfo]]::new()
    [long]$bytes = 0
    while ($pending.Count) {
        $item = $pending.Pop()
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Link or reparse point: $($item.FullName)" }
        if ($item -is [IO.DirectoryInfo]) {
            foreach ($entry in $item.EnumerateFileSystemInfos()) { $pending.Push($entry) }
        } else {
            $files.Add($item)
            $bytes += $item.Length
        }
    }
    [pscustomobject]@{ Bytes = $bytes; Files = $files }
}

function Read-VerificationMarker([string]$Path, [string]$EvidencePath) {
    $inside = Join-Path $Path 'owned-test-root.json'
    $outside = Join-Path $EvidencePath 'owned-test-root.json'
    foreach ($file in $inside, $outside) {
        Assert-VerificationPath $file
        if ((Get-Item -LiteralPath $file -Force).Length -gt 4096) { throw "Oversized ownership marker: $file" }
    }
    $raw = [IO.File]::ReadAllText($inside)
    if ($raw -cne [IO.File]::ReadAllText($outside)) { throw "Ownership markers differ: $Path" }
    $owner = $raw | ConvertFrom-Json
    if ($owner.Id -cne [IO.Path]::GetFileName($Path) -or $owner.Root -cne $Path) {
        throw "Invalid ownership marker: $inside"
    }
    $owner
}

function Assert-VerificationProcessExited($Identity, [switch]$AllowSelf) {
    if ($Identity.Pid -isnot [long] -or $Identity.Pid -le 0 -or $Identity.Pid -gt [int]::MaxValue -or
        $Identity.StartTicks -isnot [long] -or $Identity.StartTicks -le 0) { throw 'Invalid recorded process identity' }
    try { $process = [Diagnostics.Process]::GetProcessById([int]$Identity.Pid) }
    catch [ArgumentException] { return }
    try {
        if (!$process.HasExited -and $process.StartTime.ToUniversalTime().Ticks -eq $Identity.StartTicks -and
            (!$AllowSelf -or $process.Id -ne $PID)) {
            throw "Recorded process is still running (PID $($process.Id))"
        }
    } finally { $process.Dispose() }
}

function Assert-VerificationChildrenExited([string]$Path) {
    if (Test-Path -LiteralPath (Join-Path $Path 'process-starting')) {
        throw "A child launch was interrupted before its process identity was recorded: $Path"
    }
    $file = Join-Path $Path 'owned-processes.json'
    if (Test-Path -LiteralPath $file) {
        Assert-VerificationPath $file
        if ((Get-Item -LiteralPath $file).Length -gt 32768) { throw "Oversized process manifest: $file" }
        foreach ($identity in @(Get-Content -LiteralPath $file -Raw | ConvertFrom-Json)) {
            Assert-VerificationProcessExited $identity
        }
    }
}

function Remove-VerificationOperation([string]$Id, [switch]$AllowSelf) {
    if ([IO.Path]::GetFileName($Id) -cne $Id) { throw "Invalid verification directory name: $Id" }
    $path = Join-Path $operationsBase $Id
    $evidencePath = Join-Path $evidenceBase $Id
    $report = [ordered]@{ Path = $path; Bytes = $null; RemovedBytes = 0; RemainingBytes = $null; Status = 'Retained'; Reason = $null }
    try {
        $tree = Read-VerificationTree $path
        $report.Bytes = $tree.Bytes
        if ($Id -cnotmatch '^verification-[0-9a-f]{32}$') { throw "Invalid verification ID: $Id" }
        $owner = Read-VerificationMarker $path $evidencePath
        Assert-VerificationProcessExited $owner -AllowSelf:$AllowSelf
        Assert-VerificationChildrenExited $path
        foreach ($entry in (Get-ChildItem -LiteralPath $path -Force)) {
            if ($entry.Name -notin 'bin','obj','fixtures','tmp','owned-test-root.json','owned-processes.json') {
                throw "Unexpected operation entry: $($entry.FullName)"
            }
        }
        $fixtures = Join-Path $path 'fixtures'
        if (Test-Path -LiteralPath $fixtures) {
            foreach ($fixture in (Get-ChildItem -LiteralPath $fixtures -Force)) {
                if (!$fixture.PSIsContainer -or $fixture.Name -cnotmatch ('^[a-z0-9-]+-' + $Id + '-[0-9a-f]{32}$')) {
                    throw "Unexpected fixture: $($fixture.FullName)"
                }
                $fixtureOwner = Read-VerificationMarker $fixture.FullName (Join-Path $evidencePath "fixtures/$($fixture.Name)")
                Assert-VerificationProcessExited $fixtureOwner
                Assert-VerificationChildrenExited $fixture.FullName
            }
        }
        foreach ($file in $tree.Files) {
            $handle = [IO.File]::Open($file.FullName, 'Open', 'Read', 'None')
            $handle.Dispose()
        }
        foreach ($entry in (Get-ChildItem -LiteralPath $path -Force)) {
            if ($entry.Name -eq 'owned-test-root.json') { continue }
            if ($entry.PSIsContainer) { Remove-Item -LiteralPath $entry.FullName -Recurse -Force }
            else { Remove-Item -LiteralPath $entry.FullName -Force }
        }
        [IO.File]::Delete((Join-Path $path 'owned-test-root.json'))
        try { [IO.Directory]::Delete($path, $false) }
        catch {
            if ([IO.Directory]::Exists($path)) {
                [IO.File]::WriteAllText((Join-Path $path 'owned-test-root.json'), [IO.File]::ReadAllText((Join-Path $evidencePath 'owned-test-root.json')))
            }
            throw
        }
        $report.RemovedBytes = $tree.Bytes
        $report.RemainingBytes = 0
        $report.Status = 'Removed'
    } catch {
        $report.Reason = $_.Exception.Message
        try {
            $report.RemainingBytes = (Read-VerificationTree $path).Bytes
            if ($null -ne $report.Bytes) { $report.RemovedBytes = [Math]::Max(0, $report.Bytes - $report.RemainingBytes) }
        } catch { }
    }
    [pscustomobject]$report
}

function Write-VerificationJson([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 8))
}
