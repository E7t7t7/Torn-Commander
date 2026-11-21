$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderSales'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'InventorySales.cs') -Destination $project
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TEST_INVENTORY_SALES.cs.txt') -Destination (Join-Path $project 'Program.cs')
Set-Content -LiteralPath (Join-Path $project 'SalesTest.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'SalesTest.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Inventory sales regression failed.'}
