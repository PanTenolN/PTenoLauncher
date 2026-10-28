$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$version=(Get-Content -LiteralPath (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json).sdk.version
$metadata=Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$sdk=$metadata.releases.sdks | Where-Object version -eq $version | Select-Object -First 1
if (!$sdk) { $sdk=$metadata.releases.sdk | Where-Object version -eq $version | Select-Object -First 1 }
$file=$sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name.EndsWith('.zip') } | Select-Object -First 1
if (!$file) { throw 'Pinned SDK not found in official metadata.' }
$uri=[uri]$file.url
if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'builds.dotnet.microsoft.com') { throw 'Unexpected SDK source.' }
$tools=Join-Path $repo '.tools'
New-Item -ItemType Directory -Path $tools -Force | Out-Null
$archive=Join-Path $tools 'dotnet-sdk.zip'
Invoke-WebRequest -Uri $uri -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $file.hash) { throw 'SDK SHA512 verification failed.' }
$destination=Join-Path $tools 'dotnet'
if (Test-Path -LiteralPath $destination) { throw 'Local SDK directory already exists; refusing to overwrite.' }
Expand-Archive -LiteralPath $archive -DestinationPath $destination
& (Join-Path $destination 'dotnet.exe') --info
