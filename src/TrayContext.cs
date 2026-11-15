using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TimeZoneTray {
    internal sealed class TrayContext : ApplicationContext {
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer;
        readonly EventWaitHandle settingsSignal;
        readonly ConfigStore store;
        readonly StartupManager startup;
        readonly bool preview;
        readonly Icon icon;
        AppConfig config;
        SettingsForm settings;
        bool switching, disposed;
        int ticks;
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);

        public TrayContext(bool preview, EventWaitHandle signal) {
            this.preview=preview; settingsSignal=signal;
            string folder=preview ? Path.Combine(Path.GetTempPath(),"TimeZoneTray-preview") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TimeZoneTray");
            string path=Path.Combine(folder,"settings.json"); bool firstLaunch=!File.Exists(path);
            store=new ConfigStore(path);
            startup=new StartupManager(Application.ExecutablePath,StartupManager.RunKey,StartupManager.ValueName);
            string warning; config=store.Load(ZoneService.Current().Id,out warning);
            // Register once. Ordinary relaunch must not override external startup choices.
            if(firstLaunch) {
                if(!preview) {
                    try { startup.SetEnabled(true); }
                    catch(Exception ex) { warning="无法启用开机自启动，可稍后在设置中重试。\n"+ex.Message; }
                }
                store.Save(config);
            }
            icon=CreateIcon();
            menu=new ContextMenuStrip { Font=new Font("Microsoft YaHei UI",9F),ShowImageMargin=false,ShowCheckMargin=true };
            menu.Opening+=delegate { BuildMenu(); };
            tray=new NotifyIcon { Icon=icon,Visible=true,ContextMenuStrip=menu };
            tray.MouseUp+=delegate(object sender,MouseEventArgs e) {
                if(e.Button==MouseButtons.Left) {
                    if(menu.Visible) { menu.Close(); return; }
                    menu.Show(Cursor.Position); SetForegroundWindow(menu.Handle);
                }
            };
            timer=new System.Windows.Forms.Timer { Interval=500 };
            timer.Tick+=delegate {
                if(!switching && settingsSignal.WaitOne(0)) ShowSettings();
                if(++ticks%10==0) RefreshStatus();
            };
            timer.Start(); RefreshStatus();
            if(!String.IsNullOrEmpty(warning)) tray.ShowBalloonTip(7000,"时区切换器",warning,ToolTipIcon.Warning);
        }
        internal static Icon CreateIcon() {
            using(var bitmap=new Bitmap(32,32)) {
                using(var g=Graphics.FromImage(bitmap)) {
                    g.SmoothingMode=SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                    using(var b=new SolidBrush(Color.FromArgb(13,139,148))) g.FillEllipse(b,1,1,30,30);
                    using(var pen=new Pen(Color.White,2.5F)) {
                        g.DrawEllipse(pen,6,6,20,20); pen.StartCap=LineCap.Round; pen.EndCap=LineCap.Round;
                        g.DrawLine(pen,16,16,16,10); g.DrawLine(pen,16,16,21,19);
                    }
                }
                IntPtr handle=bitmap.GetHicon();
                try { using(var temp=Icon.FromHandle(handle)) return (Icon)temp.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        void RefreshStatus() {
            if(disposed) return;
            try {
                var current=ZoneService.Current();
                string title=CurrentName(current)+" · "+ZoneCatalog.Offset(current,DateTimeOffset.UtcNow);
                tray.Text=title.Length>63 ? title.Substring(0,63) : title;
            } catch(Exception ex) { tray.Text="时区读取失败"; Debug.WriteLine(ex); }
        }
        string CurrentName(TimeZoneInfo current) {
            var entry=config.Zones.FirstOrDefault(z=>String.Equals(z.Id,current.Id,StringComparison.OrdinalIgnoreCase));
            return entry==null ? ZoneCatalog.DefaultName(current) : entry.Name;
        }
        void BuildMenu() {
            while(menu.Items.Count>0) { var item=menu.Items[0]; menu.Items.RemoveAt(0); item.Dispose(); }
            var current=ZoneService.Current(); var now=DateTimeOffset.UtcNow;
            menu.Items.Add(new ToolStripMenuItem("当前："+CurrentName(current)+" · "+ZoneCatalog.Offset(current,now)) { Enabled=false });
            if(preview) menu.Items.Add(new ToolStripMenuItem("预览模式 · 不更改系统") { Enabled=false });
            menu.Items.Add(new ToolStripSeparator());
            if(config.Zones.Count==0) menu.Items.Add(new ToolStripMenuItem("请在设置中添加常用时区") { Enabled=false });
            foreach(var zone in config.Zones) {
                var captured=zone;
                var item=new ToolStripMenuItem(ZoneCatalog.Label(zone,now).Replace("&","&&")) {
                    Checked=String.Equals(current.Id,zone.Id,StringComparison.OrdinalIgnoreCase),Enabled=!switching
                };
                item.Click+=async delegate { await Switch(captured); }; menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var settingsItem=new ToolStripMenuItem("设置…") { Enabled=!switching }; settingsItem.Click+=delegate { ShowSettings(); }; menu.Items.Add(settingsItem);
            var exit=new ToolStripMenuItem(switching ? "正在切换…" : "退出") { Enabled=!switching }; exit.Click+=delegate { ExitThread(); }; menu.Items.Add(exit);
            RefreshStatus();
        }
        async Task Switch(ZoneEntry entry) {
            if(switching) return;
            if(preview) { tray.ShowBalloonTip(2500,"预览模式","正式运行时会切换到「"+entry.Name+"」。",ToolTipIcon.Info); return; }
            if(String.Equals(ZoneService.Current().Id,entry.Id,StringComparison.OrdinalIgnoreCase)) return;
            switching=true;
            try {
                var result=await Task.Run(()=>ZoneService.Set(entry.Id));
                if(!result.Success && result.PermissionDenied) {
                    if(MessageBox.Show("Windows 要求更高权限才能切换到「"+entry.Name+"」。\n是否继续并打开系统授权提示？","切换时区",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes) {
                        int exitCode=await Task.Run(()=>ElevatedSwitch(entry.Id));
                        result.Success=exitCode==0 && String.Equals(ZoneService.Current().Id,entry.Id,StringComparison.OrdinalIgnoreCase);
                        result.Error="未能完成时区切换。授权可能被取消，或系统策略不允许此操作。";
                    } else return;
                }
                if(!result.Success) ShowError(result.Error);
            } catch(Win32Exception ex) { if(ex.NativeErrorCode!=1223) ShowError(ex.Message); }
            catch(Exception ex) { ShowError(ex.Message); }
            finally { switching=false; RefreshStatus(); }
        }
        static int ElevatedSwitch(string id) {
            var zone=TimeZoneInfo.FindSystemTimeZoneById(id);
            using(var process=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--set-time-zone \""+zone.Id+"\"") {
                UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden
            })) { process.WaitForExit(); return process.ExitCode; }
        }
        public void ShowSettings() {
            if(switching || disposed) return;
            if(settings!=null && !settings.IsDisposed) { ShowWindow(settings.Handle,1); settings.Activate(); return; }
            settings=new SettingsForm(config,!preview && startup.Enabled,!preview && startup.DisabledByWindows,preview,delegate(AppConfig updated,bool enabled) {
                if(preview) store.Save(updated);
                else startup.SaveWithRollback(enabled, delegate { store.Save(updated); });
                config=updated; RefreshStatus();
            });
            settings.Icon=icon; settings.FormClosed+=delegate { settings=null; }; settings.Show();
            // A launcher may supply SW_HIDE; an explicit settings request must still be visible.
            ShowWindow(settings.Handle,1); settings.Activate();
        }
        public void ShowError(string message) {
            MessageBox.Show(settings,"操作未完成：\n\n"+message,"时区切换器",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
        protected override void ExitThreadCore() { if(settings!=null) settings.Close(); base.ExitThreadCore(); }
        protected override void Dispose(bool disposing) {
            if(disposing && !disposed) {
                disposed=true; timer.Stop(); timer.Dispose();
                if(settings!=null) settings.Dispose(); tray.Visible=false; tray.Dispose(); menu.Dispose(); icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
