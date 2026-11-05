param([ValidateSet('build','ai','imports','offline-ui','all','native','instance-native')][string]$Mode = 'build')
$ErrorActionPreference = 'Stop'
$env:DOTNET_ROOT = Join-Path $PWD '.tools\dotnet'
$env:NUGET_PACKAGES = Join-Path $PWD '.local\nuget'
$env:DOTNET_CLI_HOME = Join-Path $PWD '.local\cli'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$tag = 'verification-' + $Mode + '-' + (Get-Date -Format yyyyMMdd-HHmmss)
$log = Join-Path $PWD ('artifacts\0.2.0\' + $tag + '.log')
$binary = 'tests\PTenoLauncher.Tests\bin\Release\net10.0\PTenoLauncher.Tests.dll'
@{Run=$tag;Mode=$Mode;Utc=[DateTime]::UtcNow;TestBinarySha256=$(if(Test-Path -LiteralPath $binary){(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash});Source=@(Get-ChildItem -LiteralPath src,tests -File -Recurse | Where-Object {$_.FullName -notmatch '\\(bin|obj)\\'} | ForEach-Object {@{Path=[IO.Path]::GetRelativePath($PWD,$_.FullName);Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($log + '.scope.json') -Encoding utf8
if ($Mode -eq 'build') {
    & .\.tools\dotnet\dotnet.exe build PTenoLauncher.sln --no-restore -c Release -p:UseSharedCompilation=false --disable-build-servers -m:1 -nr:false -v minimal *> $log
} else {
    $env:PTENO_TEST_RUN_ID = $tag
    if ($Mode -eq 'instance-native') {
        & .\.tools\dotnet\dotnet.exe tests\PTenoLauncher.IntegrationSmoke\bin\Release\net10.0\PTenoLauncher.IntegrationSmoke.dll --instance-native *> $log
    } elseif ($Mode -eq 'native') {
        & .\.tools\dotnet\dotnet.exe tests\PTenoLauncher.IntegrationSmoke\bin\Release\net10.0\PTenoLauncher.IntegrationSmoke.dll --ai-native *> $log
    } else {
    $testArgs = @('tests/PTenoLauncher.Tests/bin/Release/net10.0/PTenoLauncher.Tests.dll','-noLogo','-noColor','-maxThreads','2','-longRunning','30','-trx',('artifacts/0.2.0/tests/' + $tag + '.trx'))
    if ($Mode -eq 'ai') { $testArgs += @('-class','*Ai*','-class','*Translation*','-class','*TestData*') }
    if ($Mode -eq 'imports') { $testArgs += @('-class','*InstancePack*','-class','*MinecraftLauncher*','-class','*ZipPack*','-class','*Mrpack*') }
    if ($Mode -eq 'offline-ui') { $testArgs += @('-class','*OfflineCatalog*','-class','*Localization*') }
    & .\.tools\dotnet\dotnet.exe @testArgs *> $log
    }
}
$code = $LASTEXITCODE
if($Mode -ne 'build') {
    foreach($root in @(Get-ChildItem -LiteralPath .local\test-runs -Directory | Where-Object {$_.Name.Contains('-' + $tag + '-')})) {
        # Exact current-run roots only; C# verifies the durable owner and process manifests.
        & .\.tools\dotnet\dotnet.exe tests\PTenoLauncher.IntegrationSmoke\bin\Release\net10.0\PTenoLauncher.IntegrationSmoke.dll --owned-cleanup $root.FullName >> $log 2>&1
        if($LASTEXITCODE -ne 0 -and $code -eq 0) {$code=1}
    }
}
Get-Content -LiteralPath $log -Tail 65
Write-Output ('Evidence: ' + $log)
exit $code
