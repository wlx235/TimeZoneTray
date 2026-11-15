using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace TimeZoneTray {
    internal static class Program {
        [STAThread] static int Main(string[] args) {
            // The elevated helper changes only a validated time zone and exits.
            if (args.Length==2 && args[0]=="--set-time-zone") {
                try { return ZoneService.Set(args[1]).Success ? 0 : 1; } catch { return 2; }
            }
            bool preview=args.Contains("--preview");
            string suffix=WindowsIdentity.GetCurrent().User.Value + (preview ? ".Preview" : "");
            bool created;
            using (var mutex=new Mutex(true,@"Local\TimeZoneTray."+suffix,out created)) {
                if (!created) {
                    if (args.Contains("--settings")) {
                        try { using(var signal=EventWaitHandle.OpenExisting(@"Local\TimeZoneTray.Settings."+suffix)) signal.Set(); }
                        catch (WaitHandleCannotBeOpenedException) { }
                    }
                    return 0;
                }
                try {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var signal=new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\TimeZoneTray.Settings."+suffix))
                    using (var context=new TrayContext(preview, signal)) {
                        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { context.ShowError(e.Exception.Message); };
                        if (args.Contains("--settings") || preview) context.ShowSettings();
                        Application.Run(context);
                    }
                    return 0;
                } catch (Exception ex) {
                    MessageBox.Show("时区切换器无法启动：\n\n"+ex.Message,"时区切换器",MessageBoxButtons.OK,MessageBoxIcon.Error);
                    return 1;
                } finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
