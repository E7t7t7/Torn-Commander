$ErrorActionPreference='Stop'
$project=Join-Path $env:TEMP ('CommanderCrimeTests'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'CrimeLoop.cs') $project
Copy-Item (Join-Path $PSScriptRoot 'TEST_CRIME_LOOP.cs.txt') (Join-Path $project 'Program.cs')
Set-Content (Join-Path $project 'Tests.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>'
dotnet run --project (Join-Path $project 'Tests.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Crime planner tests failed.'}
