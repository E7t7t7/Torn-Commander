$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderFFSeller'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('FFSeller.cs','LossAccounting.cs','LossSettlement.cs','CredentialManager.cs','TornRoutes.cs','MedicalSnapshotReader.cs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project}
$models=Get-Content (Join-Path $PSScriptRoot 'Models.cs') -Raw
$loss=[regex]::Match($models,'(?s)public sealed class LossOrder\s*\{.*?(?=\r?\npublic sealed class TornSnapshot)').Value
Set-Content -LiteralPath (Join-Path $project 'LossOrder.cs') -Value ("namespace TornCommander;"+[Environment]::NewLine+$loss) -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TEST_FF_SELLER.cs.txt') -Destination (Join-Path $project 'Program.cs')
Set-Content -LiteralPath (Join-Path $project 'FFTest.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'FFTest.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'FF seller regression failed.'}
