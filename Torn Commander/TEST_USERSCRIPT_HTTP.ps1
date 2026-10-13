$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderScriptHttp'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'UserscriptHttp.cs') $project
$program=@'
using TornCommander;
using System.Text.Json;
void Check(bool ok,string why){if(!ok)throw new Exception(why);}
using var headers=JsonDocument.Parse("""{"Content-Type":"application/json","Authorization":"Bearer fixture-only","Accept":"application/json"}""");
using var request=UserscriptHttp.Create("POST",new Uri("https://example.invalid/contracts"),"""{"label":"fixture café"}""",headers.RootElement);
Check(request.Content!.Headers.ContentType!.MediaType=="application/json","JSON header lost to text/plain.");
Check(request.Content.Headers.GetValues("Content-Type").Count()==1,"Duplicate Content-Type.");
Check((await request.Content.ReadAsStringAsync()).Contains("café"),"UTF-8 body changed.");
Check(request.Headers.Authorization?.Parameter=="fixture-only","Script authentication lost.");
Check(request.Headers.Accept.Single().MediaType=="application/json","Accept header lost.");
using var plain=UserscriptHttp.Create("POST",new Uri("https://example.invalid/"),"plain",default);
Check(plain.Content!.Headers.ContentType!.MediaType=="text/plain","Default content type changed.");
using var get=UserscriptHttp.Create("GET",new Uri("https://example.invalid/"),null,default);
Check(get.Content is null,"GET fabricated a request body.");
Console.WriteLine("USERSCRIPT HTTP PASSED: Loss X-change-shaped JSON POST, exact content type, script auth, UTF-8 body and bodyless GET.");
'@
Set-Content (Join-Path $project 'Program.cs') $program -Encoding UTF8
Set-Content (Join-Path $project 'Fixture.csproj') '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup></Project>' -Encoding UTF8
dotnet run --project (Join-Path $project 'Fixture.csproj') -c Release
if($LASTEXITCODE -ne 0){throw 'Userscript HTTP regression failed.'}
