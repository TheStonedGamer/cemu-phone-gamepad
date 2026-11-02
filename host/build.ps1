$ErrorActionPreference = 'Stop'
$output = Join-Path $PSScriptRoot '../dist/windows'
if (Test-Path $output) { Remove-Item $output -Force -Recurse }
dotnet publish (Join-Path $PSScriptRoot 'PhoneGamePad.Host.csproj') -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed' }
Copy-Item (Join-Path $PSScriptRoot '../README.md') $output
Copy-Item (Join-Path $PSScriptRoot '../LICENSE') $output
Copy-Item (Join-Path $PSScriptRoot '../THIRD_PARTY_NOTICES.md') $output
Copy-Item (Join-Path $PSScriptRoot '../docs/NAudio-LICENSE.txt') $output
Copy-Item (Join-Path $PSScriptRoot '../docs/CsWinRT-LICENSE.txt') $output
Compress-Archive -Path "$output/*" -DestinationPath (Join-Path $PSScriptRoot '../dist/Cemu-Phone-GamePad-Windows-x64.zip') -Force
