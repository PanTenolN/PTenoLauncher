$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manual=Join-Path $repo 'manual-testing\HexCasting'
$inventory=Get-Content -LiteralPath (Join-Path $manual 'app-inventory.json') -Raw | ConvertFrom-Json
foreach($file in $inventory.files){$path=[IO.Path]::GetFullPath((Join-Path (Join-Path $manual 'app') $file.path));if(-not $path.StartsWith((Join-Path $manual 'app')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Inventory path rejected'};if((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Link rejected'};if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw 'Manual bundle hash mismatch'}}
$id='hex-native-verification-manual-'+(Get-Date -Format yyyyMMdd-HHmmss)+'-'+[Guid]::NewGuid().ToString('N')
$base=Join-Path $repo '.local\test-runs'
$root=[IO.Path]::GetFullPath((Join-Path $base $id))
$evidence=[IO.Path]::GetFullPath((Join-Path $repo ('artifacts\test-lifecycle\'+$id)))
foreach($p in @($base,$root,$evidence)){for($a=$p;$a -and $a.StartsWith($repo,[StringComparison]::OrdinalIgnoreCase);$a=[IO.Path]::GetDirectoryName($a)){if((Test-Path -LiteralPath $a) -and ((Get-Item -LiteralPath $a -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Linked test root rejected'}}}
if((Test-Path -LiteralPath $root) -or (Test-Path -LiteralPath $evidence)){throw 'Operation root must be fresh'}
New-Item -ItemType Directory -Path $root,$evidence -Force | Out-Null
$owner=[Diagnostics.Process]::GetCurrentProcess()
$marker=@{Id=$id;Root=$root;Limit=16MB;Pid=$PID;StartTicks=$owner.StartTime.ToUniversalTime().Ticks}|ConvertTo-Json -Compress
$marker | Set-Content -LiteralPath (Join-Path $root 'owned-test-root.json') -Encoding utf8
$marker | Set-Content -LiteralPath (Join-Path $evidence 'owned-test-root.json') -Encoding utf8
$process=$null;$failure=$null
try{
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $repo '.tools\dotnet\dotnet.exe'))
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$root
    foreach($arg in @((Join-Path $manual 'app\PTenoLauncher.dll'),'--data-root',$root,'--smoke-test','--open-translations','--capture-ui','--smoke-size','980','680')){$start.ArgumentList.Add($arg)}
    $process=[Diagnostics.Process]::Start($start)
    $identity=@{Pid=$process.Id;StartTicks=$process.StartTime.ToUniversalTime().Ticks;Executable=$start.FileName}
    @($identity) | ConvertTo-Json -AsArray | Set-Content -LiteralPath (Join-Path $root 'owned-processes.json') -Encoding utf8
    if(-not $process.WaitForExit(40000)){throw ('Owned test process is still running: '+$process.Id+'; no forced termination')}
    $smoke=Get-Content -LiteralPath (Join-Path $root 'gui-smoke.txt') -Raw
    if($process.ExitCode -ne 0 -or $smoke -notmatch 'initialized=True' -or $smoke -notmatch 'page=Settings' -or -not (Test-Path -LiteralPath (Join-Path $root 'window-preview.png'))){throw 'Retained manual application did not pass native startup'}
    @{state='PASSED';manualApp=$true;frameworkDependent=$true;exitCode=$process.ExitCode;logicalWidth=980;logicalHeight=680;physicalDpiVerified=$false;liveAi=$false;gameplay=$false;smoke=$smoke} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'manual-native-result.json') -Encoding utf8
}catch{$failure=$_; $_.ToString() | Set-Content -LiteralPath (Join-Path $root 'failure.log') -Encoding utf8}
finally{
    foreach($name in @('gui-smoke.txt','window-preview.png','manual-native-result.json','owned-processes.json','failure.log')){if(Test-Path -LiteralPath (Join-Path $root $name)){Copy-Item -LiteralPath (Join-Path $root $name) -Destination (Join-Path $evidence $name)}}
    $size=(Get-ChildItem -LiteralPath $root -File -Recurse | Measure-Object Length -Sum).Sum
    $reason=$null
    try{
        if($process -and -not $process.HasExited){throw 'Recorded child is still running'}
        if([IO.Directory]::GetParent($root).FullName -ne $base -or (Get-Content -LiteralPath (Join-Path $root 'owned-test-root.json') -Raw).Trim() -ne $marker.Trim()){throw 'Ownership mismatch'}
        foreach($entry in @(Get-Item -LiteralPath $root -Force)+@(Get-ChildItem -LiteralPath $root -Force -Recurse)){if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Link refused before cleanup'}}
        if($size -gt 16MB){$failure='Owned test payload budget exceeded'}
        Remove-Item -LiteralPath $root -Recurse -Force
        if(Test-Path -LiteralPath $root){throw 'Owned payload still exists'}
    }catch{$reason=$_.ToString();if(-not $failure){$failure=$_}}
    $remaining=if(Test-Path -LiteralPath $root){(Get-ChildItem -LiteralPath $root -File -Recurse | Measure-Object Length -Sum).Sum}else{0}
    @{Id=$id;Root=$root;CreatedBytes=$size;RemovedBytes=($size-$remaining);RetainedBytes=$remaining;State=$(if($reason){'RETAINED'}else{'REMOVED'});Reason=$reason}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $evidence ('cleanup-'+[Guid]::NewGuid().ToString('N')+'.json')) -Encoding utf8
    if($process){$process.Dispose()};$owner.Dispose()
}
Write-Output ('Manual native evidence: '+$evidence)
if($failure){throw $failure}
