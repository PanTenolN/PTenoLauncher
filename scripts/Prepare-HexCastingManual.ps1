param()
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manual=[IO.Path]::GetFullPath((Join-Path $repo 'manual-testing\HexCasting'))
if(-not $manual.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Outside workspace'}
# No broad copy and no automatic deletion: this directory is a retained user deliverable.
foreach($segment in @($repo,(Join-Path $repo 'manual-testing'),$manual)) {
    if(Test-Path -LiteralPath $segment){if((Get-Item -LiteralPath $segment -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('Link refused: '+$segment)}}
}
$source=Join-Path $repo 'src\PTenoLauncher.Desktop\bin\Release\net10.0'
$files=@(Get-ChildItem -LiteralPath $source -File | Where-Object {$_.Extension -ne '.pdb'})
$files+=@(Get-ChildItem -LiteralPath (Join-Path $source 'runtimes\win-x64') -Recurse -File | Where-Object {$_.Extension -ne '.pdb'})
foreach($file in $files){
    for($check=$file.FullName;$check -and $check.StartsWith($repo,[StringComparison]::OrdinalIgnoreCase);$check=[IO.Path]::GetDirectoryName($check)) {
        if((Get-Item -LiteralPath $check -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw ('Source link refused: '+$check)}
    }
}
$bytes=($files | Measure-Object Length -Sum).Sum
if($bytes -gt 200MB){throw 'Manual bundle exceeds its 200 MiB budget'}
if(([IO.DriveInfo]::new([IO.Path]::GetPathRoot($manual))).AvailableFreeSpace -lt $bytes+64MB){throw 'Not enough free space'}
if((Test-Path -LiteralPath $manual) -and -not (Test-Path -LiteralPath (Join-Path $manual 'KEEP_FOR_USER.json'))){throw 'Unrecognized manual directory; preserve it'}
New-Item -ItemType Directory -Path $manual -Force | Out-Null
$app=Join-Path $manual 'app'
if(Test-Path -LiteralPath $app){throw 'Retained app already exists; do not overwrite a user build. Review it explicitly.'}
@{purpose='KEEP_FOR_USER';project='PTenoLauncher';kind='Hex Casting manual translation';automaticCleanupAllowed=$false;created=[DateTime]::UtcNow;inputJarProvided=$false;minecraftVersionProvided=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $manual 'KEEP_FOR_USER.json') -Encoding utf8
New-Item -ItemType Directory -Path $app -Force | Out-Null
$inventory=@()
foreach($file in $files){
    $relative=[IO.Path]::GetRelativePath($source,$file.FullName)
    $target=[IO.Path]::GetFullPath((Join-Path $app $relative))
    if(-not $target.StartsWith($app+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Target outside app'}
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $hash){throw 'Copy integrity failed'}
    $inventory+=@{path=$relative;bytes=$file.Length;sha256=$hash}
}
@{frameworkDependent=$true;platform='win-x64';testTrust=$false;bytes=$bytes;files=$inventory} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $manual 'app-inventory.json') -Encoding utf8
New-Item -ItemType Directory -Path (Join-Path $manual 'inputs'),(Join-Path $manual 'data') -Force | Out-Null
$start=@'
@echo off
setlocal
set "DOTNET_ROOT=%~dp0..\..\.tools\dotnet"
set "PTENO_APP=%~dp0app\PTenoLauncher.dll"
if not exist "%DOTNET_ROOT%\dotnet.exe" (
  echo Local .NET runtime was not found. Keep this folder inside the PTenoLauncher project.
  pause
  exit /b 1
)
"%DOTNET_ROOT%\dotnet.exe" "%PTENO_APP%" --data-root "%~dp0data" --open-translations
if errorlevel 1 pause
endlocal
'@
$start | Set-Content -LiteralPath (Join-Path $manual 'Start-HexCasting-Test.cmd') -Encoding ascii
Write-Output ('Retained manual bundle: '+$manual)
Write-Output ('Verified application bytes: '+$bytes)
