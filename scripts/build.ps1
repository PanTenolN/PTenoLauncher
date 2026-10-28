param([switch]$Publish,[switch]$Installer)
$ErrorActionPreference='Stop'
$ptenoRoot=Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $ptenoRoot
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
Invoke-PtenoProcess -ArgumentList @('build','PTenoLauncher.sln','-c','Release','--no-restore','-m:1')
$testDirectory=Join-Path $ptenoRoot 'artifacts/tests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
# Use the same xUnit v3 executable runner as the verified local checkpoint.
# This avoids the separate VSTest data-collector handshake without skipping tests.
Invoke-PtenoProcess -ArgumentList @('tests/PTenoLauncher.Tests/bin/Release/net10.0/PTenoLauncher.Tests.dll','-noLogo','-noColor','-maxThreads','2','-longRunning','30','-trx',(Join-Path $testDirectory 'tests.trx'))
if ($Publish) {
    $publishDirectory=Join-Path $ptenoRoot 'artifacts/0.2.0/win-x64'
    if (Test-Path -LiteralPath $publishDirectory) { throw 'Use a clean artifacts/0.2.0/win-x64 directory for packaging.' }
    Invoke-PtenoProcess -ArgumentList @('publish','src/PTenoLauncher.Desktop','-c','Release','-r','win-x64','--self-contained','true','-o',$publishDirectory,'-p:PublishSingleFile=false','-p:PublishTrimmed=false','-p:DebugType=none','-p:RestoreLockedMode=true')
    foreach($name in @('README.md','THIRD_PARTY_NOTICES.md','RELEASE_NOTES.md','TEST_REPORT.md')) { Copy-Item -LiteralPath $name -Destination $publishDirectory }
    Copy-Item -LiteralPath docs -Destination (Join-Path $publishDirectory 'docs') -Recurse
    $portableDirectory=Join-Path $ptenoRoot 'artifacts/0.2.0/portable-staging'
    if (Test-Path -LiteralPath $portableDirectory) { throw 'Use a clean portable-staging directory.' }
    New-Item -ItemType Directory -Path $portableDirectory | Out-Null
    Copy-Item -Path "$publishDirectory/*" -Destination $portableDirectory -Recurse
    New-Item -ItemType File -Path (Join-Path $portableDirectory 'portable.flag') | Out-Null
    Compress-Archive -Path "$portableDirectory/*" -DestinationPath artifacts/PTenoLauncher-Portable-0.2.0-win-x64.zip
    if ($Installer) {
        $isccCommand=Get-Command ISCC.exe -ErrorAction SilentlyContinue
        $compiler=if ($isccCommand) { $isccCommand.Source } else { Join-Path $ptenoRoot '.tools/InnoSetup6/ISCC.exe' }
        if (!(Test-Path -LiteralPath $compiler)) { throw 'Inno Setup 6 ISCC.exe is required.' }
        Invoke-PtenoProcess -Executable $compiler -ArgumentList @('/DAppVersion=0.2.0',"/DPublishDir=$publishDirectory",'installer/PTenoLauncher.iss')
    }
    Get-ChildItem artifacts -File | Where-Object { $_.Extension -in '.zip','.exe' } | ForEach-Object {
        $hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    } | Set-Content -LiteralPath artifacts/SHA256SUMS.txt -Encoding utf8
}
