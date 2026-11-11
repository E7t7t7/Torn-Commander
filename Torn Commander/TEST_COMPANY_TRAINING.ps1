$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderCompanyTraining'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
foreach($file in @('CompanyTraining.cs','CompanyIntelligence.cs','CompanyStaffAdvisor.cs','CompanyStaffLayouts.cs','CompanyMarket.cs','CompanyAdvertisingTrial.cs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $project}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TEST_COMPANY_TRAINING.cs.txt') -Destination (Join-Path $project 'Program.cs')
Set-Content -LiteralPath (Join-Path $project 'TrainTests.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'TrainTests.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Company training test failed.'}
