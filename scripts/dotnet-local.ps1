$ErrorActionPreference='Stop'
$ptenoRoot=Split-Path $PSScriptRoot -Parent
$ptenoArguments=@($args)
$env:DOTNET_CLI_HOME=Join-Path $ptenoRoot '.local/cli'
$env:NUGET_PACKAGES=Join-Path $ptenoRoot '.local/nuget'
$env:APPDATA=Join-Path $ptenoRoot '.local/appdata'
$env:USERPROFILE=Join-Path $ptenoRoot '.local/user'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:AVALONIA_TELEMETRY_OPTOUT='1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
$env:MSBUILDDISABLENODEREUSE='1'
Set-Location -LiteralPath $ptenoRoot
function ConvertTo-PtenoArgument {
    param([string]$Value)
    if ($Value -notmatch '[\s"]') { return $Value }
    $escaped=[regex]::Replace($Value,'(\\*)"','$1$1\"')
    $escaped=[regex]::Replace($escaped,'(\\+)$','$1$1')
    return '"' + $escaped + '"'
}
$info=New-Object System.Diagnostics.ProcessStartInfo
$info.FileName=Join-Path $ptenoRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $info.FileName)) { $info.FileName=(Get-Command dotnet -ErrorAction Stop).Source }
$info.Arguments=($ptenoArguments | ForEach-Object { ConvertTo-PtenoArgument $_ }) -join ' '
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
exit $code