param([switch]$Publish,[switch]$Installer,[long]$ReleaseSequence=0,[string]$BuildId='',[ValidateSet('stable','preview')][string]$Channel='preview')
$ErrorActionPreference='Stop'
if ($Publish -and ($ReleaseSequence -le 0 -or [string]::IsNullOrWhiteSpace($BuildId))) { throw 'Publishing requires an explicit monotonic ReleaseSequence and unique BuildId.' }
if ($ReleaseSequence -gt 0 -and $BuildId -notmatch '^[A-Za-z0-9._-]{1,128}$') { throw 'BuildId must contain only letters, digits, dots, underscores and hyphens.' }
$ptenoIdentityArgs=if ($ReleaseSequence -gt 0) { @("-p:PTenoBuildSequence=$ReleaseSequence","-p:PTenoBuildId=$BuildId","-p:PTenoBuildChannel=$Channel") } else { @() }
$ptenoRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $ptenoRoot
$ptenoVersion=([xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw)).Project.PropertyGroup.Version | Where-Object { $_ }
if ($ptenoVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version.' }
$releaseDirectory=Join-Path $ptenoRoot "artifacts/$ptenoVersion"
$env:DOTNET_CLI_HOME=Join-Path $ptenoRoot '.local/cli'
$env:NUGET_PACKAGES=Join-Path $ptenoRoot '.local/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:AVALONIA_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:MSBUILDDISABLENODEREUSE='1'
if (!$env:APPDATA) { $env:APPDATA=Join-Path $ptenoRoot '.local/appdata' }
if (!$env:USERPROFILE) { $env:USERPROFILE=Join-Path $ptenoRoot '.local/user' }
$ptenoDotnet=Join-Path $ptenoRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $ptenoDotnet)) { $ptenoDotnet='dotnet' }
function ConvertTo-PtenoArgument {
    param([string]$Value)
    if ($Value -notmatch '[\s"]') { return $Value }
    $escaped=[regex]::Replace($Value,'(\\*)"','$1$1\"')
    $escaped=[regex]::Replace($escaped,'(\\+)$','$1$1')
    return '"' + $escaped + '"'
}
function Invoke-PtenoProcess {
    param([string[]]$ArgumentList,[string]$Executable=$script:ptenoDotnet)
    $info=New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName=$Executable
    $info.Arguments=($ArgumentList | ForEach-Object { ConvertTo-PtenoArgument $_ }) -join ' '
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true
    $info.RedirectStandardError=$true
    $process=[System.Diagnostics.Process]::Start($info)
    $stdout=$process.StandardOutput.ReadToEndAsync()
    $stderr=$process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    Write-Output $stdout.Result
    if ($stderr.Result) { Write-Output $stderr.Result }
    $code=$process.ExitCode
    $process.Dispose()
    if ($code -ne 0) { throw "Command failed ($code): $ArgumentList" }
}
Invoke-PtenoProcess -ArgumentList @('restore','PTenoLauncher.sln','--locked-mode','--configfile','NuGet.Config','--disable-parallel')
Invoke-PtenoProcess -ArgumentList (@('build','PTenoLauncher.sln','-c','Release','--no-restore','-m:1')+$ptenoIdentityArgs)
$testDirectory=Join-Path $releaseDirectory 'tests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
# Use the same xUnit v3 executable runner as the verified local checkpoint.
# This avoids the separate VSTest data-collector handshake without skipping tests.
Invoke-PtenoProcess -ArgumentList @('tests/PTenoLauncher.Tests/bin/Release/net10.0/PTenoLauncher.Tests.dll','-noLogo','-noColor','-maxThreads','2','-longRunning','30','-trx',(Join-Path $testDirectory 'tests.trx'))
if ($Publish) {
    $publishDirectory=Join-Path $releaseDirectory 'win-x64'
    if (Test-Path -LiteralPath $publishDirectory) { throw "Use a clean $publishDirectory directory for packaging." }
    Invoke-PtenoProcess -ArgumentList (@('publish','src/PTenoLauncher.Desktop','-c','Release','-r','win-x64','--self-contained','true','-o',$publishDirectory,'-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:DebugType=none','-p:RestoreLockedMode=true')+$ptenoIdentityArgs)
    Invoke-PtenoProcess -ArgumentList (@('publish','src/PTenoLauncher.UpdateHelper','-c','Release','-r','win-x64','--self-contained','true','-o',(Join-Path $publishDirectory 'updater'),'-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:DebugType=none','-p:RestoreLockedMode=true')+$ptenoIdentityArgs)
    foreach($name in @('README.md','THIRD_PARTY_NOTICES.md','RELEASE_NOTES.md','PROJECT_PROGRESS_SUMMARY.md','DEVELOPMENT_STATUS.md')) { Copy-Item -LiteralPath $name -Destination $publishDirectory }
    New-Item -ItemType Directory -Path (Join-Path $publishDirectory 'docs') | Out-Null
    Copy-Item -LiteralPath 'docs/PREVIEW_RELEASE.md','docs/PROCESS_LIFECYCLE_RELEASE_CHECKPOINT.md' -Destination (Join-Path $publishDirectory 'docs')
    Invoke-PtenoProcess -ArgumentList @('scripts/PTenoLauncher.UpdateTools/bin/Release/net10.0/PTenoLauncher.UpdateTools.dll','inventory',$publishDirectory)
    if ($Installer) {
        $isccCommand=Get-Command ISCC.exe -ErrorAction SilentlyContinue
        $compiler=if ($isccCommand) { $isccCommand.Source } else { Join-Path $ptenoRoot '.tools/InnoSetup6/ISCC.exe' }
        if (!(Test-Path -LiteralPath $compiler)) { throw 'Inno Setup 6 ISCC.exe is required.' }
        Invoke-PtenoProcess -Executable $compiler -ArgumentList @("/DAppVersion=$ptenoVersion","/DPublishDir=$publishDirectory","/O$releaseDirectory",'installer/PTenoLauncher.iss')
    }
    Invoke-PtenoProcess -Executable (Get-Command python -ErrorAction Stop).Source -ArgumentList @('scripts/package-preview.py')
}
