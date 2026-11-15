using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Win32;
using TimeZoneTray;

internal static class CoreTests {
    static int passed, failed;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Run(string name, Action action) {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    [STAThread] static int Main() {
        string dir = Path.Combine(Path.GetTempPath(), "TimeZoneTray-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Run("First launch contains only current zone", delegate {
            string warning; var c = new ConfigStore(Path.Combine(dir,"fresh.json")).Load("China Standard Time", out warning);
            Check(c.Zones.Count == 1 && c.Zones[0].Id == "China Standard Time", "Wrong initial list");
            Check(warning == null, "Unexpected warning");
        });
        Run("Unicode names and order survive two atomic saves", delegate {
            var store = new ConfigStore(Path.Combine(dir,"roundtrip.json"));
            var config = new AppConfig { Zones = new List<ZoneEntry> {
                new ZoneEntry { Id="Pacific Standard Time", Name="洛杉矶 & 工作" },
                new ZoneEntry { Id="China Standard Time", Name="北京" } } };
            store.Save(config); store.Save(config);
            string warning; var c=store.Load("UTC",out warning);
            Check(c.Zones.Count==2 && c.Zones[0].Name=="洛杉矶 & 工作" && c.Zones[1].Id=="China Standard Time", "Roundtrip changed data");
        });
        Run("Corrupt file is backed up and recovery reported", delegate {
            string path=Path.Combine(dir,"broken.json"); File.WriteAllText(path,"{broken");
            string warning; var c=new ConfigStore(path).Load("UTC",out warning);
            Check(c.Zones.Count==1 && !String.IsNullOrEmpty(warning),"Missing recovery");
            Check(Directory.GetFiles(dir,"broken.json.corrupt-*").Length==1,"Original not preserved");
            string warningAgain; var recovered=new ConfigStore(path).Load("UTC",out warningAgain);
            Check(warningAgain==null && recovered.Zones.Count==1,"Recovery was not persisted");
            Check(Directory.GetFiles(dir,"broken.json.corrupt-*").Length==1,"Repeated backup");
        });
        Run("Unknown and duplicate entries filtered, empty list retained", delegate {
            var c=ConfigStore.Normalize(new AppConfig { Zones=new List<ZoneEntry> {
                null, new ZoneEntry { Id="not-a-zone",Name="bad" },
                new ZoneEntry { Id="UTC",Name="  通用  " }, new ZoneEntry { Id="UTC",Name="duplicate" } } });
            Check(c.Zones.Count==1 && c.Zones[0].Name=="通用", "Bad normalization");
            Check(ConfigStore.Normalize(new AppConfig { Zones=new List<ZoneEntry>() }).Zones.Count==0, "Empty list repopulated");
        });
        Run("Names remain readable and single line", delegate {
            Check(ZoneCatalog.SafeName("  北京\r\n时间  ","UTC")=="北京时间", "Control characters retained");
            Check(ZoneCatalog.SafeName(new string('a',100),"UTC").Length==40,"Name not capped");
            Check(ZoneCatalog.SafeName(" ","UTC")=="UTC","Empty fallback lost");
        });
        Run("DST and fractional offsets use the requested instant", delegate {
            var winter=new DateTimeOffset(2026,1,15,12,0,0,TimeSpan.Zero);
            var summer=new DateTimeOffset(2026,7,15,12,0,0,TimeSpan.Zero);
            var la=TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            Check(ZoneCatalog.Offset(la,winter)=="UTC−08:00","Winter offset");
            Check(ZoneCatalog.Offset(la,summer)=="UTC−07:00","Summer offset");
            Check(ZoneCatalog.Offset(TimeZoneInfo.FindSystemTimeZoneById("Nepal Standard Time"),summer)=="UTC+05:45","Quarter hour offset");
        });
        Run("Startup enable/disable uses quoted executable and isolated key", delegate {
            string key="Software\\TimeZoneTray.Tests\\"+Guid.NewGuid().ToString("N");
            try {
                var s=new StartupManager("C:\\a folder\\时区.exe",key,"Test");
                Check(!s.Enabled,"Unexpected startup"); s.SetEnabled(true); Check(s.Enabled,"Enable failed");
                using(var k=Registry.CurrentUser.OpenSubKey(key)) Check((string)k.GetValue("Test")=="\"C:\\a folder\\时区.exe\"","Unquoted path");
                s.SetEnabled(false); Check(!s.Enabled,"Disable failed");
            } finally { Registry.CurrentUser.DeleteSubKeyTree(key,false); }
        });
        Run("Failed settings save preserves exact previous startup value", delegate {
            string key="Software\\TimeZoneTray.Tests\\"+Guid.NewGuid().ToString("N");
            try {
                using(var k=Registry.CurrentUser.CreateSubKey(key)) k.SetValue("Test", "%TEMP%\\old.exe", RegistryValueKind.ExpandString);
                var startup=new StartupManager("C:\\new.exe",key,"Test");
                var method=typeof(StartupManager).GetMethod("SaveWithRollback");
                Check(method!=null,"Save operation does not preserve the original startup value");
                try { method.Invoke(startup,new object[] { true, (Action)delegate { throw new IOException("simulated failure"); } }); }
                catch(System.Reflection.TargetInvocationException ex) { Check(ex.InnerException is IOException,"Wrong error"); }
                using(var k=Registry.CurrentUser.OpenSubKey(key)) {
                    Check((string)k.GetValue("Test",null,RegistryValueOptions.DoNotExpandEnvironmentNames)=="%TEMP%\\old.exe", "Original path changed");
                    Check(k.GetValueKind("Test")==RegistryValueKind.ExpandString,"Original value kind changed");
                }
            } finally { Registry.CurrentUser.DeleteSubKeyTree(key,false); }
        });
        Console.WriteLine("RESULT " + passed + " passed, " + failed + " failed");
        // The unique temporary test directory is intentionally retained for inspection.
        return failed==0 ? 0 : 1;
    }
}
