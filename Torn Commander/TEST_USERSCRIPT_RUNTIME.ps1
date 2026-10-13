$ErrorActionPreference='Stop'
$project=Join-Path $env:RUNNER_TEMP ('CommanderUserscript'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $project -Force | Out-Null
$source=Get-Content (Join-Path $PSScriptRoot 'BrowserTab.cs') -Raw
$start=$source.IndexOf('    private static string WrapUserscriptForMetadata')
$end=$source.IndexOf('    private bool IsAllowed', $start)
$methods=$source.Substring($start,$end-$start).Replace('private static string WrapUserscriptForMetadata','public static string WrapUserscriptForMetadata')
Set-Content (Join-Path $project 'Wrapper.cs') -Encoding UTF8 -Value ("using System.Text.Json; using System.Text.RegularExpressions; public static class Wrapper {"+$methods+"}")
$program=@'
using System.Text.Json;
var body="window.runs=(window.runs||0)+1; GM_registerMenuCommand('Open fixture',()=>window.commandRan=true);";
var meta="// ==UserScript==\n// @match https://www.torn.com/*\n";
var scripts=new[]{
 Wrapper.WrapUserscriptForMetadata(meta+body,"end"),
 Wrapper.WrapUserscriptForMetadata(meta+"// @run-at document-start\n"+body,"start"),
 Wrapper.WrapUserscriptForMetadata(meta+"// @exclude https://www.torn.com/excluded*\n"+body,"exclude"),
 Wrapper.WrapUserscriptForMetadata(meta+"// @noframes\n"+body,"frames"),
 Wrapper.WrapUserscriptForMetadata(body,"raw"),
 Wrapper.WrapUserscriptForMetadata(meta+"GM_xmlhttpRequest({url:'https://example.com',onload:r=>window.httpStatus=r.status,onerror:()=>window.networkError=true});","http")
};
File.WriteAllText(args[0],JsonSerializer.Serialize(scripts));
'@
Set-Content (Join-Path $project 'Program.cs') -Value $program -Encoding UTF8
Set-Content (Join-Path $project 'Fixture.csproj') -Value '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>' -Encoding UTF8
$wrappers=Join-Path $project 'wrappers.json'
dotnet run --project (Join-Path $project 'Fixture.csproj') -c Release -- $wrappers
if($LASTEXITCODE -ne 0){throw 'Wrapper compilation failed.'}
node (Join-Path $PSScriptRoot 'TEST_USERSCRIPT_RUNTIME.js') $wrappers
if($LASTEXITCODE -ne 0){throw 'Userscript execution regression failed.'}
