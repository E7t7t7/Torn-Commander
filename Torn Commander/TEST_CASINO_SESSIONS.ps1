$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderCasino'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CasinoTracking.cs') -Destination $project
$models=Get-Content (Join-Path $PSScriptRoot 'Models.cs') -Raw
$casino=[regex]::Match($models,'(?s)public sealed class CasinoReading.*?(?=public sealed class RacingNote)').Value
if(!$casino){throw 'Casino models missing.'}
Set-Content -LiteralPath (Join-Path $project 'Models.cs') -Value ("namespace TornCommander;"+[Environment]::NewLine+$casino) -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TEST_CASINO_SESSIONS.cs.txt') -Destination (Join-Path $project 'Program.cs')
Set-Content -LiteralPath (Join-Path $project 'CasinoTest.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'CasinoTest.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Casino session regression failed.'}
