using System;
using System.Diagnostics;
using System.IO;
using TimeZoneTray;
internal static class SystemCheck {
    static int Main() {
        string tzutil=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tzutil.exe");
        string original;
        using(var p=Process.Start(new ProcessStartInfo(tzutil,"/g") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true })) {
            original=p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); if(p.ExitCode!=0 || original.Length==0) return 2;
        }
        bool passed=false, restored=false;
        try {
            string target=ZoneService.Current().Id=="UTC" ? "China Standard Time" : "UTC";
            var result=ZoneService.Set(target);
            passed=result.Success && ZoneService.Current().Id==target;
            Console.WriteLine((passed ? "PASS" : "FAIL")+" actual system switch: "+target+" "+result.Error);
        } finally {
            using(var p=Process.Start(new ProcessStartInfo(tzutil,"/s \""+original+"\"") { UseShellExecute=false,CreateNoWindow=true })) { p.WaitForExit(); restored=p.ExitCode==0; }
            using(var p=Process.Start(new ProcessStartInfo(tzutil,"/g") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true })) {
                string actual=p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); restored=restored && actual==original;
                Console.WriteLine((restored ? "PASS" : "FAIL")+" exact original zone/DST state restored: "+actual);
            }
        }
        return passed && restored ? 0 : 1;
    }
}
