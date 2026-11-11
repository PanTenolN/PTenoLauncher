#requires -Version 7.4
param(
    [ValidateSet('build','ai','imports','offline-ui','focused','all','native','instance-native','inventory')]
    [string]$Mode = 'build',
    [string[]]$FocusedClass,
    [switch]$Smoke,
    [switch]$KeepManual,
    [switch]$Package,
    [switch]$VerifyJeiNames,
    [switch]$VerifyLocalServers,
    [switch]$VerifyVanillaCommands,
    [switch]$VerifyForgeServer,
    [switch]$VerifyForgeClient,
    [switch]$VerifyServerForm,
    [switch]$VerifyStartup,
    [switch]$VerifySetup,
    [switch]$VerifyForgeInExe,
    [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ReplaceManualSha256,
    [switch]$CachedPackages,
    [ValidateRange(1,7200)][int]$PhaseTimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$operationsBase = Join-Path $projectRoot '.local/test-runs'
$evidenceBase = Join-Path $projectRoot 'artifacts/verification'
. "$PSScriptRoot/VerificationStorage.ps1"
if ($Mode -eq 'inventory') {
    . "$PSScriptRoot/VerificationInventory.ps1"
    Write-VerificationInventory
    return
}
if ($FocusedClass -and $Mode -notin 'all','focused') { throw 'FocusedClass requires the all or focused mode.' }
if ($Mode -eq 'focused' -and !$FocusedClass) { throw 'The focused mode requires FocusedClass.' }
if ($Package -and $Mode -ne 'all') { throw 'Packaging requires a full verification run.' }
if ($VerifyJeiNames -and $Mode -ne 'all') { throw 'Live Modrinth verification requires the all mode.' }
if (($VerifyLocalServers -or $VerifyVanillaCommands -or $VerifyForgeServer) -and $Mode -ne 'all') { throw 'Live server verification requires the all mode.' }
if ($VerifyForgeClient -and $Mode -ne 'all') { throw 'Live Forge client verification requires the all mode.' }
if ($VerifyServerForm -and !$Package) { throw 'Native server form verification requires -Package.' }
if (($VerifySetup -or $VerifyForgeInExe) -and !$Package) { throw 'Setup and native Forge verification require -Package.' }
if ($ReplaceManualSha256 -and (!$KeepManual -or $Mode -ne 'all')) {
    throw 'Replacing the retained launcher requires the full run and -KeepManual.'
}

function Get-SourceFingerprint {
    $inputs = @(
        Get-ChildItem -LiteralPath "$projectRoot/src","$projectRoot/tests","$projectRoot/scripts","$projectRoot/installer" -Recurse -File |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
        Get-ChildItem -LiteralPath $projectRoot -File |
            Where-Object { $_.Extension -in '.sln','.props','.targets','.json','.Config' }
    )
    $lines = foreach ($file in ($inputs | Sort-Object FullName)) {
        '{0} {1}' -f (Get-FileHash -LiteralPath $file.FullName).Hash,[IO.Path]::GetRelativePath($projectRoot, $file.FullName)
    }
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($lines -join [char]10))))
}

function Invoke-VerificationPhase([string]$Name, [string[]]$Arguments, [string]$Executable = $dotnet) {
    Write-Host "Verification phase: $Name"
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.WorkingDirectory = $projectRoot
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($arg in $Arguments) { $info.ArgumentList.Add($arg) }
    $launchPending = Join-Path $operation 'process-starting'
    # Avoid a user-space log buffer that would disappear if the runner is killed.
    $out = [IO.FileStream]::new((Join-Path $evidence "$Name.log"), 'CreateNew', 'Write', 'Read', 1)
    try {
        $err = [IO.FileStream]::new((Join-Path $evidence "$Name.stderr.log"), 'CreateNew', 'Write', 'Read', 1)
        try {
            [IO.File]::WriteAllText($launchPending, $Name)
            $child = $null
            try {
                $child = [Diagnostics.Process]::Start($info)
                $identities.Add(@{ Pid = $child.Id; StartTicks = $child.StartTime.ToUniversalTime().Ticks; Executable = $Executable })
                Write-VerificationJson (Join-Path $operation 'owned-processes.json') $identities.ToArray()
                [IO.File]::Delete($launchPending)
                $stdout = $child.StandardOutput.BaseStream.CopyToAsync($out)
                $stderr = $child.StandardError.BaseStream.CopyToAsync($err)
                $deadline = [Diagnostics.Stopwatch]::StartNew()
                while (!$child.WaitForExit(200)) {
                    if ($deadline.Elapsed.TotalSeconds -ge $PhaseTimeoutSeconds) {
                        throw [OperationCanceledException]::new("Phase $Name exceeded $PhaseTimeoutSeconds seconds")
                    }
                    Start-Sleep -Milliseconds 10
                }
                if (![Threading.Tasks.Task]::WaitAll(@($stdout, $stderr), 5000)) {
                    throw "Phase $Name exited with an open output pipe"
                }
                if ($child.ExitCode -ne 0) { throw "Phase $Name failed with exit code $($child.ExitCode). See $evidence." }
            } finally {
                if ($null -ne $child) {
                    try {
                        if (!$child.HasExited) {
                            $child.Kill($true)
                            if (!$child.WaitForExit(10000)) { throw "Owned process did not exit: $($child.Id)" }
                        }
                    } finally { $child.Dispose() }
                }
            }
        } finally { $err.Dispose() }
    } finally { $out.Dispose() }
}

function Invoke-Tests([string]$Name, [string[]]$Classes) {
    $runnerArgs = @($testBinary,'-noLogo','-noColor','-maxThreads','2','-longRunning','30','-trx',(Join-Path $evidence "$Name.trx"))
    foreach ($class in $Classes) { $runnerArgs += @('-class', $class) }
    Invoke-VerificationPhase $Name $runnerArgs
    if ((Get-FileHash -LiteralPath $testBinary).Hash -ne $scope.TestBinarySha256) { throw 'The tested binary changed during verification' }
}

$packageGateFailures = [Collections.Generic.List[string]]::new()
function Test-PreviewPackage {
    $packageArgs = @('-NoProfile','-File',(Join-Path $projectRoot 'scripts/build.ps1'),'-Publish','-Installer',
        '-VerificationOperation',$operation,'-ReleaseSequence','1','-BuildId',$id)
    if ($CachedPackages) { $packageArgs += '-CachedPackages' }
    Invoke-VerificationPhase 'package' $packageArgs (Join-Path $PSHOME 'pwsh.exe')
    $packageDirectory = Join-Path $operation 'tmp/package'
    $publish = Join-Path $packageDirectory 'win-x64'
    $exe = Join-Path $publish 'PTenoLauncher.exe'
    $packageDll = Join-Path $publish 'PTenoLauncher.dll'
    $python = (Get-Command python -ErrorAction Stop).Source
    Invoke-VerificationPhase 'portable-smoke' @('scripts/verify-packages.py','--smoke','--artifacts',$packageDirectory,'--smoke-root',$temp) $python
    $report = Get-Content -LiteralPath (Join-Path $packageDirectory 'final-artifact-verification.json') -Raw | ConvertFrom-Json
    $scope.ManualAppBinarySha256 = (Get-FileHash -LiteralPath $packageDll).Hash
    $scope.ManualExeSha256 = (Get-FileHash -LiteralPath $exe).Hash
    if ($report.portableSmoke.exeSha256 -ne $scope.ManualExeSha256 -or $report.portableSmoke.appSha256 -ne $scope.ManualAppBinarySha256) {
        throw 'Portable startup used different launcher binaries.'
    }
    $installedData = Join-Path $temp 'installed-smoke-data'
    Invoke-VerificationPhase 'exe-smoke' @('--data-root',$installedData,'--smoke-test','--skip-update-check') $exe
    $marker = Get-Content -LiteralPath (Join-Path $installedData 'gui-smoke.txt') -Raw
    if ($marker -notmatch 'Window opened' -or $marker -notmatch 'initialized=True') { throw 'Self-contained launcher did not initialize.' }
    Set-Content -LiteralPath (Join-Path $evidence 'exe-startup.txt') -Value $marker
    if ($VerifySetup) {
        $setups = @(Get-ChildItem -LiteralPath $packageDirectory -File -Filter 'PTenoLauncher-Setup-*-win-x64.exe')
        if ($setups.Count -ne 1) { throw 'Expected one Setup package.' }
        $setupRoot = Join-Path $temp 'setup-smoke'
        try {
            Invoke-VerificationPhase 'setup-smoke' @('scripts/test-installer.py','--setup',$setups[0].FullName,
                '--smoke-root',$setupRoot,'--app-sha256',$scope.ManualAppBinarySha256) $python
        } catch {
            if ($_.Exception -is [OperationCanceledException]) { throw }
            $packageGateFailures.Add($_.Exception.Message)
        } finally {
            foreach ($file in @('setup-result.json','setup.log','setup.stdout.log','installed-gui.stdout.log','uninstall.log','uninstall.stdout.log')) {
                $path = Join-Path $setupRoot $file
                if (Test-Path -LiteralPath $path -PathType Leaf) { Copy-Item -LiteralPath $path -Destination $evidence }
            }
        }
    }
    if ($Smoke) {
        foreach ($width in 980,1360) {
            $profileData = Join-Path $temp "profiles-$width"
            Invoke-VerificationPhase "profiles-$width" @('--data-root',$profileData,'--smoke-test','--skip-update-check',
                '--smoke-page','Profiles','--smoke-size',"$width",'720','--capture-ui') $exe
            $profileMarker = Get-Content -LiteralPath (Join-Path $profileData 'gui-smoke.txt') -Raw
            if ($profileMarker -notmatch 'page=Profiles' -or $profileMarker -notmatch 'initialized=True') {
                throw "Profiles did not initialize at width $width."
            }
            Copy-Item -LiteralPath (Join-Path $profileData 'window-preview.png') -Destination (Join-Path $evidence "profiles-$width.png")
        }
    }
    if ($VerifyServerForm) {
        foreach ($width in 980,1360) {
            foreach ($form in 'scratch','profile') {
                $phase = "server-form-$width-$form"
                $formData = Join-Path $temp $phase
                Invoke-VerificationPhase "$phase-fixture" @($smokeBinary,$formData,'--server-form-fixture')
                Invoke-VerificationPhase $phase @('--data-root',$formData,'--smoke-test','--skip-update-check',
                    '--smoke-server-form',$form,'--smoke-size',"$width",'720','--capture-ui') $exe
                $formMarker = Get-Content -LiteralPath (Join-Path $formData 'gui-smoke.txt') -Raw
                if ($formMarker -notmatch 'page=Servers' -or $formMarker -notmatch "serverForm=$form; core=Forge; minecraft=1.21.1; build=52.1.0; create=True") {
                    throw "Servers form did not initialize at $width in $form mode."
                }
                Set-Content -LiteralPath (Join-Path $evidence "$phase.txt") -Value $formMarker
                Copy-Item -LiteralPath (Join-Path $formData 'window-preview.png') -Destination (Join-Path $evidence "$phase.png")
            }
        }
    }
    if ($VerifyForgeInExe) {
        $forgeData = Join-Path $temp 'forge-exe'
        try {
            $checkpoints = [ordered]@{
                create = 'PASS: Forge stopped and saved its world.'
                reopen = 'PASS: Forge card, settings and world persisted after reopening the EXE.'
            }
            foreach ($phase in $checkpoints.Keys) {
                Invoke-VerificationPhase "forge-exe-$phase" @('--data-root',$forgeData,'--smoke-test',
                    '--skip-update-check','--smoke-forge-lifecycle',$phase) $exe
                $output = Get-Content -LiteralPath (Join-Path $evidence "forge-exe-$phase.log") -Raw
                if (!$output -or !$output.Contains($checkpoints[$phase], [StringComparison]::Ordinal)) {
                    throw "Forge EXE phase $phase did not reach its lifecycle checkpoint. See $evidence/forge-exe-$phase.stderr.log."
                }
            }
        } catch {
            if ($_.Exception -is [OperationCanceledException]) { throw }
            $packageGateFailures.Add($_.Exception.Message)
        }
    }
    Copy-Item -LiteralPath (Join-Path $packageDirectory 'SHA256SUMS.txt'),(Join-Path $packageDirectory 'final-artifact-verification.json') -Destination $evidence
    Write-VerificationJson (Join-Path $evidence 'scope.json') $scope
}

function Keep-ManualBuild {
    $manual = Join-Path $evidenceBase 'manual'
    $previous = Join-Path $evidenceBase 'manual.previous'
    $buildOutput = Join-Path $operation 'bin/PTenoLauncher.Desktop/release'
    $app = if ($Package) { Join-Path $operation 'tmp/package/win-x64' } else { $buildOutput }
    Assert-VerificationPath $manual
    Assert-VerificationPath $previous
    $newTree = Read-VerificationTree $app
    if (Test-Path -LiteralPath $previous) {
        $previousTree = Read-VerificationTree $previous
        if (!$ReplaceManualSha256 -or (Test-Path -LiteralPath $manual) -or
            (Get-FileHash -LiteralPath (Join-Path $previous 'PTenoLauncher.dll')).Hash -ne $ReplaceManualSha256) {
            throw "Retained interrupted manual replacement at $previous ($($previousTree.Bytes) bytes). Resolve this copy before replacing the launcher."
        }
        Move-Item -LiteralPath $previous -Destination $manual
    }
    if (Test-Path -LiteralPath $manual) {
        $oldTree = Read-VerificationTree $manual
        $retained = Join-Path $manual 'PTenoLauncher.dll'
        $manifest = Join-Path $manual 'verification-build.json'
        $old = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
        $oldHash = (Get-FileHash -LiteralPath $retained).Hash
        if ($oldHash -eq ($scope.ManualAppBinarySha256 ?? $scope.AppBinarySha256) -and $old.SourceFingerprint -eq $scope.SourceFingerprint) { return $manual }
        if (!$ReplaceManualSha256 -or $oldHash -ne $ReplaceManualSha256 -or $old.Run -cnotmatch '^verification-[0-9a-f]{32}$') {
            throw "A different manual build is retained at $manual ($($oldTree.Bytes) bytes). Its verified SHA-256 is required for replacement."
        }
        $oldEvidence = Join-Path $evidenceBase $old.Run
        Assert-VerificationPath $oldEvidence
        Assert-VerificationPath (Join-Path $oldEvidence 'scope.json')
        Assert-VerificationPath (Join-Path $oldEvidence 'result.json')
        $oldScope = Get-Content -LiteralPath (Join-Path $oldEvidence 'scope.json') -Raw | ConvertFrom-Json
        $oldResult = Get-Content -LiteralPath (Join-Path $oldEvidence 'result.json') -Raw | ConvertFrom-Json
        if (($oldScope.ManualAppBinarySha256 ?? $oldScope.AppBinarySha256) -ne $oldHash -or $oldScope.SourceFingerprint -ne $old.SourceFingerprint -or
            $oldResult.State -ne 'Passed' -or $oldResult.ManualBuild -cne $manual) {
            throw "Manual launcher at $manual ($($oldTree.Bytes) bytes) does not match successful verification evidence."
        }
        $distribution = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($file in $newTree.Files) { $null = $distribution.Add([IO.Path]::GetRelativePath($app, $file.FullName)) }
        if ($Package) {
            foreach ($file in (Read-VerificationTree $buildOutput).Files) {
                $null = $distribution.Add([IO.Path]::GetRelativePath($buildOutput, $file.FullName))
            }
        }
        $null = $distribution.Add('verification-build.json')
        foreach ($file in $oldTree.Files) {
            $relative = [IO.Path]::GetRelativePath($manual, $file.FullName)
            if (!$distribution.Contains($relative) -and !($relative -ceq '.pteno-running.lock' -and $file.Length -eq 0)) {
                throw "Manual launcher at $manual ($($oldTree.Bytes) bytes) contains an unexpected file: $($file.FullName)"
            }
            $probe = [IO.File]::Open($file.FullName, 'Open', 'Read', 'None')
            $probe.Dispose()
        }
        foreach ($dir in (Get-ChildItem -LiteralPath $manual -Recurse -Directory -Force)) {
            $relative = [IO.Path]::GetRelativePath($manual, $dir.FullName)
            if (!(Test-Path -LiteralPath (Join-Path $app $relative) -PathType Container) -and
                !(Test-Path -LiteralPath (Join-Path $buildOutput $relative) -PathType Container)) {
                throw "Manual launcher at $manual ($($oldTree.Bytes) bytes) contains an unexpected directory: $($dir.FullName)"
            }
        }
        Write-VerificationJson (Join-Path $app 'verification-build.json') $scope
        $superseded = Join-Path $operation 'tmp/superseded-manual'
        Assert-VerificationPath $superseded
        if (Test-Path -LiteralPath $superseded) { throw "Manual replacement staging is occupied: $superseded" }
        Move-Item -LiteralPath $manual -Destination $previous
        try { Move-Item -LiteralPath $app -Destination $manual }
        catch {
            Move-Item -LiteralPath $previous -Destination $manual
            throw
        }
        Move-Item -LiteralPath $previous -Destination $superseded
        Write-Host "Replaced the prior verified launcher ($($oldTree.Bytes) bytes)."
        return $manual
    }
    Write-VerificationJson (Join-Path $app 'verification-build.json') $scope
    Move-Item -LiteralPath $app -Destination $manual
    return $manual
}

Assert-VerificationPath $operationsBase
Assert-VerificationPath $evidenceBase
[IO.Directory]::CreateDirectory($operationsBase) | Out-Null
[IO.Directory]::CreateDirectory($evidenceBase) | Out-Null
$lockPath = Join-Path $operationsBase 'verification.lock'
Assert-VerificationPath $lockPath
try { $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
catch { throw "Verification lock unavailable at $lockPath. $($_.Exception.Message)" }

try {
    $recoveries = @(foreach ($prior in (Get-ChildItem -LiteralPath $operationsBase -Directory -Force -Filter 'verification-*')) {
        $recovery = Remove-VerificationOperation $prior.Name
        if ($recovery.Status -eq 'Removed') { Write-Host "Recovered $($recovery.Path) ($($recovery.RemovedBytes) bytes)" }
        else { Write-Warning "Retained $($recovery.Path) ($($recovery.RemainingBytes ?? 'unknown') bytes): $($recovery.Reason)" }
        $recovery
    })
    $id = 'verification-' + [Guid]::NewGuid().ToString('N')
    $operation = Join-Path $operationsBase $id
    $evidence = Join-Path $evidenceBase $id
    $owner = [Diagnostics.Process]::GetCurrentProcess()
    try { $marker = @{ Id = $id; Root = $operation; Pid = $PID; StartTicks = $owner.StartTime.ToUniversalTime().Ticks } }
    finally { $owner.Dispose() }
    $freeBefore = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($projectRoot)).AvailableFreeSpace
    [IO.Directory]::CreateDirectory($evidence) | Out-Null
    Write-VerificationJson (Join-Path $evidence 'owned-test-root.json') $marker
    Write-VerificationJson (Join-Path $evidence 'recovery.json') $recoveries
    Write-Host "Verification evidence: $evidence"

    $identities = [Collections.Generic.List[object]]::new()
    $saved = @{}
    $state = 'Failed'
    $failure = $null
    $manualPath = $null
    $scope = $null
    try {
        [IO.Directory]::CreateDirectory($operation) | Out-Null
        [IO.File]::Copy((Join-Path $evidence 'owned-test-root.json'), (Join-Path $operation 'owned-test-root.json'))
        $temp = Join-Path $operation 'tmp'
        [IO.Directory]::CreateDirectory($temp) | Out-Null
        $dotnet = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
        if (!(Test-Path -LiteralPath $dotnet -PathType Leaf)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
        $settings = @{
            DOTNET_ROOT = Split-Path -Parent $dotnet
            DOTNET_HOST_PATH = $dotnet
            NUGET_PACKAGES = Join-Path $projectRoot '.local/nuget'
            DOTNET_CLI_HOME = Join-Path $projectRoot '.local/cli'
            MSBUILDDISABLENODEREUSE = '1'
            DOTNET_CLI_TELEMETRY_OPTOUT = '1'
            AVALONIA_TELEMETRY_OPTOUT = '1'
            PTENO_TEST_RUN_ID = $id
            PTENO_VERIFICATION_ROOT = $operation
            TEMP = $temp
            TMP = $temp
        }
        foreach ($key in $settings.Keys) {
            $saved[$key] = [Environment]::GetEnvironmentVariable($key)
            [Environment]::SetEnvironmentVariable($key, $settings[$key])
        }
        $source = Get-SourceFingerprint
        $buildArgs = @('build','PTenoLauncher.sln','-c','Release','--artifacts-path',$operation,
            '-p:RestoreLockedMode=true','-p:UseSharedCompilation=false','--disable-build-servers','-m:1','-nr:false','-v','minimal')
        if ($CachedPackages) { $buildArgs += '-p:NuGetAudit=false' }
        Invoke-VerificationPhase 'build' $buildArgs
        $testBinary = Join-Path $operation 'bin/PTenoLauncher.Tests/release/PTenoLauncher.Tests.dll'
        $smokeBinary = Join-Path $operation 'bin/PTenoLauncher.IntegrationSmoke/release/PTenoLauncher.IntegrationSmoke.dll'
        $appBinary = Join-Path $operation 'bin/PTenoLauncher.Desktop/release/PTenoLauncher.dll'
        $scope = @{
            Run = $id; Mode = $Mode; FocusedClasses = $FocusedClass; SourceFingerprint = $source
            TestBinary = $testBinary; TestBinarySha256 = (Get-FileHash -LiteralPath $testBinary).Hash
            AppBinarySha256 = (Get-FileHash -LiteralPath $appBinary).Hash; CachedPackages = $CachedPackages.IsPresent
        }
        Write-VerificationJson (Join-Path $evidence 'scope.json') $scope
        if ((Get-SourceFingerprint) -ne $source) { throw 'Source changed during build. Start a new verification operation.' }
        if ($VerifyStartup) {
            $retainedExe = Join-Path $evidenceBase 'manual/PTenoLauncher.exe'
            $candidateExe = Join-Path $operation 'bin/PTenoLauncher.Desktop/release/PTenoLauncher.exe'
            Invoke-VerificationPhase 'startup-retained' @($smokeBinary,'--startup-probe',$retainedExe,(Join-Path $temp 'startup-retained'))
            Invoke-VerificationPhase 'startup-candidate' @($smokeBinary,'--startup-probe',$candidateExe,(Join-Path $temp 'startup-candidate'))
        }
        switch ($Mode) {
            all {
                if ($FocusedClass) {
                    Invoke-Tests 'focused' $FocusedClass
                    if ((Get-SourceFingerprint) -ne $source) { throw 'Source changed after focused tests. Start a new operation.' }
                }
                if ($VerifyJeiNames) {
                    $live = Join-Path $temp 'modrinth-naming'
                    Invoke-VerificationPhase 'modrinth-live' @($smokeBinary,$live,'--modrinth','jei','1.21.1','f27XokoP')
                    $gameMods = Join-Path $live 'instances/T1/game/mods'
                    Assert-VerificationPath $gameMods
                    $shell = New-Object -ComObject Shell.Application
                    $namespace = $shell.NameSpace($gameMods)
                    $expected = $namespace.ParseName('jei-1.21.1-fabric-19.64.0.455.jar')
                    if (!$expected -or $expected.Path -cne (Join-Path $gameMods 'jei-1.21.1-fabric-19.64.0.455.jar')) {
                        throw 'Windows Explorer could not resolve the expected JEI file in game/mods.'
                    }
                    $installed = @(foreach ($file in (Get-ChildItem -LiteralPath $gameMods -Filter '*.jar' -File)) {
                        $item = $namespace.ParseName($file.Name)
                        if (!$item -or $item.Path -cne $file.FullName) { throw "Explorer path mismatch: $($file.FullName)" }
                        [pscustomobject]@{ ExplorerName = $item.Name; PhysicalPath = $item.Path }
                    })
                    if ($installed.Count -lt 2) { throw 'JEI and its required dependency were not both installed.' }
                    Write-VerificationJson (Join-Path $evidence 'explorer-modrinth-names.json') $installed
                }
                if ($VerifyVanillaCommands) {
                    Invoke-VerificationPhase 'vanilla-commands-live' @($smokeBinary,(Join-Path $temp 'local-servers'),'--vanilla-commands')
                }
                $forgeFailures = @()
                if ($VerifyForgeServer) {
                    try { Invoke-VerificationPhase 'forge-server-live' @($smokeBinary,(Join-Path $temp 'forge-servers'),'--forge-server') }
                    catch { if ($_.Exception -is [OperationCanceledException]) { throw }; $forgeFailures += $_.Exception.Message }
                }
                if ($VerifyForgeClient) {
                    foreach ($pair in @(@{ Game = '1.21.1'; Loader = '52.1.0' }, @{ Game = '1.21.4'; Loader = '54.1.0' })) {
                        $version = "$($pair.Game)-$($pair.Loader)"
                        $phase = "forge-client-$version-live"
                        try { Invoke-VerificationPhase $phase @($smokeBinary,(Join-Path $temp $phase),'--forge-install','Forge',$version,$pair.Game) }
                        catch { if ($_.Exception -is [OperationCanceledException]) { throw }; $forgeFailures += $_.Exception.Message }
                    }
                }
                if ($forgeFailures.Count) { throw ($forgeFailures -join [Environment]::NewLine) }
                if ($Package) { Test-PreviewPackage }
                try { Invoke-Tests 'full' @() }
                catch {
                    if ($_.Exception -is [OperationCanceledException] -or !$packageGateFailures.Count) { throw }
                    $packageGateFailures.Add($_.Exception.Message)
                }
                if ($packageGateFailures.Count) { throw ($packageGateFailures -join [Environment]::NewLine) }
            }
            ai { Invoke-Tests 'focused' @('*Ai*','*Translation*','*TestData*') }
            imports { Invoke-Tests 'focused' @('*InstancePack*','*MinecraftLauncher*','*ZipPack*','*Mrpack*') }
            offline-ui { Invoke-Tests 'focused' @('*OfflineCatalog*','*Localization*') }
            focused { Invoke-Tests 'focused' $FocusedClass }
            native { Invoke-VerificationPhase 'native-ai' @($smokeBinary,'--ai-native') }
            instance-native { Invoke-VerificationPhase 'native-instance' @($smokeBinary,'--instance-native') }
        }
        if (($Smoke -or $KeepManual) -and $Mode -ne 'instance-native') {
            Invoke-VerificationPhase 'native-instance' @($smokeBinary,'--instance-native')
        }
        if ($VerifyLocalServers) {
            Invoke-VerificationPhase 'local-servers-live' @($smokeBinary,(Join-Path $temp 'local-servers'),'--local-servers')
        }
        if ((Get-SourceFingerprint) -ne $source -or (Get-FileHash -LiteralPath $appBinary).Hash -ne $scope.AppBinarySha256) {
            throw 'Source or launcher binary changed during verification'
        }
        if ($KeepManual) { $manualPath = Keep-ManualBuild }
        $state = 'Passed'
    } catch {
        $failure = $_
        if ($_.Exception -is [OperationCanceledException]) { $state = 'Cancelled' }
    } finally {
        foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key]) }
        $cleanup = Remove-VerificationOperation $id -AllowSelf
        $summary = @{
            Run = $id; State = $state; Failure = if ($failure) { $failure.Exception.Message }
            Cleanup = $cleanup; Recovered = $recoveries; ManualBuild = $manualPath
            EvidenceBytes = (Read-VerificationTree $evidence).Bytes
            FreeBytesBefore = $freeBefore
            FreeBytesAfter = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($projectRoot)).AvailableFreeSpace
        }
        Write-VerificationJson (Join-Path $evidence 'result.json') $summary
    }
    if ($cleanup.Status -ne 'Removed') {
        Write-Warning "Retained $($cleanup.Path) ($($cleanup.RemainingBytes ?? 'unknown') bytes): $($cleanup.Reason)"
    }
    if ($failure) { throw $failure }
    if ($cleanup.Status -ne 'Removed') { throw "Verification cleanup incomplete. See $evidence/result.json" }
    Write-Host "Completed $id. Removed $($cleanup.RemovedBytes) temporary bytes. Evidence: $evidence"
    if ($manualPath) { Write-Host "Manual launcher: $manualPath" }
} finally { $lock.Dispose() }
