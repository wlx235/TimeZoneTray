using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TimeZoneTray {
    public sealed class SettingsForm : Form {
        readonly AppConfig draft;
        readonly ListView list;
        readonly TextBox alias;
        readonly CheckBox startup;
        readonly Action<AppConfig,bool> save;
        bool binding;
        public SettingsForm(AppConfig config, bool startupEnabled, bool startupBlocked, bool preview, Action<AppConfig,bool> save) {
            SuspendLayout();
            this.save=save; draft=ConfigStore.Normalize(config);
            Text="时区切换器 · 设置"+(preview ? "（预览）" : "");
            Font=new Font("Microsoft YaHei UI",9F);
            AutoScaleDimensions=new SizeF(96F,96F); AutoScaleMode=AutoScaleMode.Dpi; ClientSize=new Size(730,535);
            FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false;
            StartPosition=FormStartPosition.CenterScreen; BackColor=Color.FromArgb(247,249,251);
            var header=new Panel { Dock=DockStyle.Top, Height=90, BackColor=Color.FromArgb(20,49,63) };
            header.Controls.Add(new Label { Text="常用时区", ForeColor=Color.White, Font=new Font(Font.FontFamily,18,FontStyle.Bold), AutoSize=true, Location=new Point(23,15) });
            header.Controls.Add(new Label { Text="把常用地点放进托盘菜单，一次点击即可切换。", ForeColor=Color.FromArgb(189,211,218), AutoSize=true, Location=new Point(25,55) });
            Controls.Add(header);
            Controls.Add(new Label { Text="菜单按下方顺序显示；首次使用只包含当前时区。",AutoSize=true,Location=new Point(24,108) });
            list=new ListView { Name="favorites", View=View.Details, FullRowSelect=true, HideSelection=false, MultiSelect=false, Location=new Point(24,137), Size=new Size(555,228), HeaderStyle=ColumnHeaderStyle.Nonclickable };
            list.Columns.Add("显示名称",245); list.Columns.Add("当前偏移",112); list.Columns.Add("Windows 时区",180);
            list.SelectedIndexChanged+=delegate { BindSelection(); }; Controls.Add(list);
            var add=Button("添加…","add",595,137,111); add.Click+=delegate {
                using(var picker=new ZonePicker(draft.Zones.Select(z=>z.Id))) {
                    if(picker.ShowDialog(this)==DialogResult.OK) { draft.Zones.Add(picker.Entry); Reload(draft.Zones.Count-1); }
                }
            };
            var remove=Button("移除","remove",595,179,111); remove.Click+=delegate {
                int i=Selected; if(i<0) return; draft.Zones.RemoveAt(i); Reload(Math.Min(i,draft.Zones.Count-1));
            };
            var up=Button("上移 ↑","up",595,237,111); up.Click+=delegate { MoveEntry(-1); };
            var down=Button("下移 ↓","down",595,279,111); down.Click+=delegate { MoveEntry(1); };
            Controls.Add(new Label { Text="显示名称",AutoSize=true,Location=new Point(24,387) });
            alias=new TextBox { Name="alias",Location=new Point(105,382),Size=new Size(474,28),MaxLength=40,Enabled=false };
            alias.TextChanged+=delegate {
                if(binding || Selected<0) return;
                var entry=draft.Zones[Selected];
                entry.Name=ZoneCatalog.SafeName(alias.Text,ZoneCatalog.DefaultName(TimeZoneInfo.FindSystemTimeZoneById(entry.Id)));
                list.SelectedItems[0].Text=entry.Name;
            };
            Controls.Add(alias);
            startup=new CheckBox { Name="startup",Text="开机自启动（登录 Windows 后在后台运行）",AutoSize=true,Location=new Point(24,429),Checked=startupEnabled,Enabled=!preview };
            Controls.Add(startup);
            var note=new Label { Name="note",AutoSize=false,Location=new Point(24,458),Size=new Size(680,27),ForeColor=Color.FromArgb(95,110,120),Text=preview ? "预览模式：不会修改系统时区或开机启动项。" : "关闭此窗口不会退出程序；可在托盘菜单中选择「退出」。" };
            if(startupBlocked && !preview) { note.Text="Windows 已禁用此启动项，请在「设置 → 应用 → 启动」中重新开启。"; note.ForeColor=Color.FromArgb(153,94,24); }
            Controls.Add(note);
            var cancel=Button("取消","cancel",488,489,100); cancel.DialogResult=DialogResult.Cancel;
            cancel.Click+=delegate { Close(); };
            var apply=Button("保存","save",603,489,103); apply.BackColor=Color.FromArgb(15,112,119); apply.ForeColor=Color.White; apply.FlatStyle=FlatStyle.Flat;
            apply.Click+=delegate {
                try { save(ConfigStore.Normalize(draft),startup.Checked); DialogResult=DialogResult.OK; Close(); }
                catch(Exception ex) { MessageBox.Show(this,"设置未能保存：\n"+ex.Message,"保存失败",MessageBoxButtons.OK,MessageBoxIcon.Error); }
            };
            CancelButton=cancel;
            Reload(draft.Zones.Count>0 ? 0 : -1);
            ResumeLayout(false); PerformLayout();
        }
        protected override void OnLoad(EventArgs e) {
            base.OnLoad(e);
            float formScale=ClientSize.Width/730F;
            // Fixed-size Label can inherit an extra font scale before form DPI scaling.
            Controls["note"].Size=new Size((int)(680*formScale),(int)(27*formScale));
            // ColumnHeader widths do not participate in WinForms DPI autoscaling.
            float scale=list.ClientSize.Width/555F;
            list.Columns[0].Width=(int)(220*scale);
            list.Columns[1].Width=(int)(112*scale);
            list.Columns[2].Width=Math.Max(120,list.ClientSize.Width-list.Columns[0].Width-list.Columns[1].Width-6);
        }
        Button Button(string text,string name,int x,int y,int width) {
            var button=new Button { Text=text,Name=name,Location=new Point(x,y),Size=new Size(width,33),UseVisualStyleBackColor=true };
            Controls.Add(button); return button;
        }
        int Selected { get { return list.SelectedIndices.Count==0 ? -1 : list.SelectedIndices[0]; } }
        void BindSelection() {
            binding=true; alias.Enabled=Selected>=0; alias.Text=Selected>=0 ? draft.Zones[Selected].Name : ""; binding=false;
            Controls["remove"].Enabled=Selected>=0; Controls["up"].Enabled=Selected>0; Controls["down"].Enabled=Selected>=0 && Selected<draft.Zones.Count-1;
        }
        void Reload(int selected) {
            list.BeginUpdate(); list.Items.Clear();
            foreach(var zone in draft.Zones) list.Items.Add(new ListViewItem(new[] { zone.Name,ZoneCatalog.Offset(TimeZoneInfo.FindSystemTimeZoneById(zone.Id),DateTimeOffset.UtcNow),zone.Id }));
            if(selected>=0) { list.Items[selected].Selected=true; list.Items[selected].Focused=true; list.EnsureVisible(selected); }
            list.EndUpdate(); BindSelection();
        }
        void MoveEntry(int delta) {
            int from=Selected, to=from+delta; if(from<0 || to<0 || to>=draft.Zones.Count) return;
            var entry=draft.Zones[from]; draft.Zones.RemoveAt(from); draft.Zones.Insert(to,entry); Reload(to);
        }
    }

    public sealed class ZonePicker : Form {
        sealed class Choice {
            public TimeZoneInfo Zone;
            public override string ToString() { return Zone.DisplayName; }
        }
        readonly List<TimeZoneInfo> zones;
        readonly TextBox search, alias;
        readonly ListBox list;
        readonly Button add;
        public ZoneEntry Entry { get; private set; }
        public ZonePicker(IEnumerable<string> existing) {
            SuspendLayout();
            Text="添加时区"; Font=new Font("Microsoft YaHei UI",9F); AutoScaleDimensions=new SizeF(96F,96F); AutoScaleMode=AutoScaleMode.Dpi;
            ClientSize=new Size(670,451); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; MinimizeBox=false; StartPosition=FormStartPosition.CenterParent;
            var ids=new HashSet<string>(existing,StringComparer.OrdinalIgnoreCase);
            zones=TimeZoneInfo.GetSystemTimeZones().Where(z=>!ids.Contains(z.Id)).ToList();
            Controls.Add(new Label { Text="搜索城市、时区名称或 UTC 偏移",AutoSize=true,Location=new Point(20,18) });
            search=new TextBox { Name="search",Location=new Point(20,46),Size=new Size(630,27) }; Controls.Add(search);
            list=new ListBox { Name="zones",Location=new Point(20,87),Size=new Size(630,238),HorizontalScrollbar=true,IntegralHeight=false }; Controls.Add(list);
            Controls.Add(new Label { Text="显示名称",AutoSize=true,Location=new Point(20,346) });
            alias=new TextBox { Name="newAlias",Location=new Point(103,341),Size=new Size(547,27),MaxLength=40 }; Controls.Add(alias);
            var cancel=new Button { Text="取消",Location=new Point(431,396),Size=new Size(100,33),DialogResult=DialogResult.Cancel }; Controls.Add(cancel);
            add=new Button { Name="confirmAdd",Text="添加",Location=new Point(546,396),Size=new Size(104,33) }; Controls.Add(add);
            add.Click+=delegate {
                var selected=list.SelectedItem as Choice; if(selected==null) return;
                Entry=new ZoneEntry { Id=selected.Zone.Id,Name=ZoneCatalog.SafeName(alias.Text,ZoneCatalog.DefaultName(selected.Zone)) }; DialogResult=DialogResult.OK; Close();
            };
            list.SelectedIndexChanged+=delegate {
                var selected=list.SelectedItem as Choice; add.Enabled=selected!=null;
                alias.Text=selected==null ? "" : ZoneCatalog.DefaultName(selected.Zone);
            };
            list.DoubleClick+=delegate { if(add.Enabled) add.PerformClick(); };
            search.TextChanged+=delegate { Filter(); }; AcceptButton=add; CancelButton=cancel; Filter();
            ResumeLayout(false); PerformLayout();
        }
        void Filter() {
            string query=search.Text.Trim(); list.BeginUpdate(); list.Items.Clear();
            foreach(var zone in zones) {
                string haystack=zone.DisplayName+" "+zone.Id+" "+zone.StandardName+" "+ZoneCatalog.Offset(zone,DateTimeOffset.UtcNow)+" "+Aliases(zone.Id);
                if(haystack.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0) list.Items.Add(new Choice { Zone=zone });
            }
            if(list.Items.Count>0) list.SelectedIndex=0;
            else { add.Enabled=false; alias.Text=""; }
            list.EndUpdate();
        }
        protected override void OnLoad(EventArgs e) {
            base.OnLoad(e);
            // Framework ListBox can scale its requested height twice when its font changes.
            list.Height=(int)Math.Round(238*ClientSize.Width/670.0);
        }
        static string Aliases(string id) {
            switch(id) {
                case "China Standard Time": return "北京 上海 中国 Beijing Shanghai China";
                case "Pacific Standard Time": return "洛杉矶 旧金山 西雅图 美西 Los Angeles San Francisco Seattle";
                case "Eastern Standard Time": return "纽约 波士顿 美东 New York Boston";
                case "GMT Standard Time": return "伦敦 英国 London UK";
                case "Tokyo Standard Time": return "东京 日本 Tokyo Japan";
                case "Singapore Standard Time": return "新加坡 Singapore";
                default: return "";
            }
        }
    }
}
