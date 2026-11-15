using System;
using System.Collections.Generic;
using System.Windows.Forms;
using TimeZoneTray;

internal static class UiTests {
    static int passed, failed;
    static void Check(bool value,string message) { if(!value) throw new Exception(message); }
    static void Run(string name,Action action) {
        try { action(); passed++; Console.WriteLine("PASS "+name); }
        catch(Exception ex) { failed++; Console.WriteLine("FAIL "+name+": "+ex); }
    }
    static AppConfig Initial() { return new AppConfig { Zones=new List<ZoneEntry> {
        new ZoneEntry { Id="China Standard Time",Name="北京" }, new ZoneEntry { Id="Pacific Standard Time",Name="洛杉矶" } } }; }
    [STAThread] static int Main() {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Run("Settings keeps minimum logical layout after showing",delegate {
            using(var form=new SettingsForm(Initial(),true,false,false,delegate {})) {
                form.Show(); Application.DoEvents();
                Check(form.ClientSize.Width>=730,"Layout shrank to "+form.ClientSize);
                var alias=(TextBox)form.Controls["alias"];
                Check(alias.Height>=TextRenderer.MeasureText("北京",alias.Font).Height,"Alias text clipped");
                var header=(Panel)form.Controls[0];
                Console.WriteLine("LAYOUT "+form.ClientSize+" / header "+header.Bounds+" / title "+header.Controls[0].Bounds+" / subtitle "+header.Controls[1].Bounds+" / alias "+alias.Bounds);
                Check(header.Controls[0].Bottom<=header.Controls[1].Top,"Header text overlaps");
                Check(header.Controls[1].Bottom<=header.Height,"Subtitle clipped");
                Check(form.Controls["note"].Bottom<=form.Controls["save"].Top,"Footer note overlaps buttons: "+form.Controls["note"].Bounds+" / "+form.Controls["save"].Bounds);
                foreach(Control control in form.Controls) {
                    Check(control.Bottom<=form.ClientSize.Height,"Control outside window: "+control.Name+" "+control.Bounds);
                    if(control is Button) Check(control.Height>=TextRenderer.MeasureText(control.Text,control.Font).Height+10,"Button text clipped: "+control.Name+" "+control.Bounds);
                }
                var list=(ListView)form.Controls["favorites"];
                Check(list.Columns[1].Width>=TextRenderer.MeasureText("UTC−08:00",list.Font).Width+12,"Offset column is clipped at high DPI");
            }
        });
        Run("Reorder, rename and startup edits apply only on Save",delegate {
            var original=Initial(); AppConfig saved=null; bool enabled=true;
            using(var form=new SettingsForm(original,true,false,false,delegate(AppConfig c,bool e) { saved=c; enabled=e; })) {
                form.Show(); Application.DoEvents();
                ((Button)form.Controls["down"]).PerformClick();
                ((TextBox)form.Controls["alias"]).Text="上海";
                ((CheckBox)form.Controls["startup"]).Checked=false;
                Check(original.Zones[0].Name=="北京","Editing mutated live config");
                ((Button)form.Controls["save"]).PerformClick();
                Check(saved!=null && saved.Zones[0].Id=="Pacific Standard Time" && saved.Zones[1].Name=="上海" && !enabled,"Wrong saved state");
            }
        });
        Run("Remove all favorites and save empty list",delegate {
            AppConfig saved=null;
            using(var form=new SettingsForm(Initial(),false,false,false,delegate(AppConfig c,bool e) { saved=c; })) {
                form.Show(); Application.DoEvents();
                ((Button)form.Controls["remove"]).PerformClick(); ((Button)form.Controls["remove"]).PerformClick();
                Check(!form.Controls["alias"].Enabled,"Alias should be disabled for empty list");
                ((Button)form.Controls["save"]).PerformClick(); Check(saved.Zones.Count==0,"Empty list not saved");
            }
        });
        Run("Cancel preserves original and never invokes save",delegate {
            bool invoked=false; var original=Initial();
            using(var form=new SettingsForm(original,true,false,true,delegate { invoked=true; })) {
                form.Show(); Application.DoEvents();
                Check(!form.Controls["startup"].Enabled,"Preview changes startup");
                ((TextBox)form.Controls["alias"]).Text="Unsaved"; ((Button)form.Controls["cancel"]).PerformClick();
                Check(!form.Visible,"Cancel must close the modeless settings window");
            }
            Check(!invoked && original.Zones[0].Name=="北京","Cancel changed config");
        });
        Run("Picker searches Chinese city aliases and excludes existing zone",delegate {
            using(var picker=new ZonePicker(new[]{"China Standard Time"})) {
                picker.Show(); Application.DoEvents();
                foreach(Control control in picker.Controls) Check(control.Bottom<=picker.ClientSize.Height,"Picker control outside window: "+control.Name+" "+control.Bounds+" client "+picker.ClientSize);
                Check(picker.Controls["zones"].Bottom<picker.Controls["newAlias"].Top,"Picker list overlaps alias");
                var search=(TextBox)picker.Controls["search"]; var list=(ListBox)picker.Controls["zones"];
                search.Text="北京"; Check(list.Items.Count==0 && !picker.Controls["confirmAdd"].Enabled,"Duplicate zone available");
                search.Text="洛杉矶"; Check(list.Items.Count==1,"City search failed");
                ((TextBox)picker.Controls["newAlias"]).Text="美西";
                ((Button)picker.Controls["confirmAdd"]).PerformClick();
                Check(picker.Entry.Id=="Pacific Standard Time" && picker.Entry.Name=="美西","Wrong picker result");
            }
        });
        Console.WriteLine("RESULT "+passed+" passed, "+failed+" failed"); return failed==0 ? 0 : 1;
    }
}
