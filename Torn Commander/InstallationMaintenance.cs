using System.Diagnostics;
using System.Text.RegularExpressions;
namespace TornCommander;
internal static class InstallationMaintenance
{
 internal static void Run()
 {
  try {
   var exe=Environment.ProcessPath;
   if(exe is null || !Path.GetFileName(exe).Equals("TornCommander.exe",StringComparison.OrdinalIgnoreCase)) return;
   var dist=Path.GetDirectoryName(exe)!;
   if(!Regex.IsMatch(Path.GetFileName(dist),@"^dist(?:-v[0-9]+[.][0-9]+[.][0-9]+-[a-fA-F0-9]{8})?$")) return;
   var project=Path.GetDirectoryName(dist)!;
   var script=Path.Combine(project,"SYNC_COMMANDER_INSTALL.ps1");
   if(!File.Exists(script))return;
   var start=new ProcessStartInfo("powershell.exe"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetTempPath(),RedirectStandardOutput=true,RedirectStandardError=true};
   foreach(var argument in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",script,"-ProjectRoot",project})start.ArgumentList.Add(argument);
   using var process=Process.Start(start);
   if(process is null)return;
   var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
   if(!process.WaitForExit(60000)){process.Kill();Program.TraceStartup("Installation maintenance timed out; startup continues.");return;}
   Task.WhenAll(output,error).GetAwaiter().GetResult();
   Program.TraceStartup("Installation maintenance exit code: "+process.ExitCode);
  }catch(Exception ex){Program.TraceStartup("Installation maintenance deferred: "+ex.GetType().Name);}
 }
}
