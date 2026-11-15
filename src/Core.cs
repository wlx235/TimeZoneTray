using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace TimeZoneTray {
    [DataContract] public class ZoneEntry {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
    }
    [DataContract] public class AppConfig {
        [DataMember] public List<ZoneEntry> Zones { get; set; }
    }
    public sealed class ConfigStore {
        readonly string path;
        public ConfigStore(string path) { this.path = path; }
        public AppConfig Load(string currentId, out string warning) {
            warning = null;
            if (File.Exists(path)) {
                try {
                    using (var stream = File.OpenRead(path)) {
                        var config = (AppConfig)new DataContractJsonSerializer(typeof(AppConfig)).ReadObject(stream);
                        if (config == null || config.Zones == null) throw new SerializationException("缺少时区列表。");
                        var normalized = Normalize(config);
                        if (config.Zones.Count != normalized.Zones.Count) warning = "已忽略配置中重复或不再受支持的时区。";
                        return normalized;
                    }
                } catch (Exception ex) {
                    if (!(ex is SerializationException) && !(ex is System.Xml.XmlException) && !(ex is ArgumentException)) throw;
                    string backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                    File.Copy(path, backup, false);
                    warning = "配置无法读取，已恢复为当前时区。原文件已备份到：\n" + backup;
                }
            }
            var zone = TimeZoneInfo.FindSystemTimeZoneById(currentId);
            var fallback = new AppConfig { Zones = new List<ZoneEntry> { new ZoneEntry { Id = zone.Id, Name = ZoneCatalog.DefaultName(zone) } } };
            if (warning != null) Save(fallback);
            return fallback;
        }
        public void Save(AppConfig config) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    new DataContractJsonSerializer(typeof(AppConfig)).WriteObject(stream, Normalize(config));
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static AppConfig Normalize(AppConfig config) {
            var result = new AppConfig { Zones = new List<ZoneEntry>() };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in (config == null ? null : config.Zones) ?? new List<ZoneEntry>()) {
                if (entry == null || String.IsNullOrWhiteSpace(entry.Id)) continue;
                TimeZoneInfo zone;
                try { zone = TimeZoneInfo.FindSystemTimeZoneById(entry.Id); }
                catch (TimeZoneNotFoundException) { continue; }
                catch (InvalidTimeZoneException) { continue; }
                if (seen.Add(zone.Id)) result.Zones.Add(new ZoneEntry { Id = zone.Id, Name = ZoneCatalog.SafeName(entry.Name, ZoneCatalog.DefaultName(zone)) });
            }
            return result;
        }
    }
    public static class ZoneCatalog {
        public static string DefaultName(TimeZoneInfo zone) { return SafeName(zone.StandardName, zone.Id); }
        public static string Offset(TimeZoneInfo zone, DateTimeOffset instant) {
            var offset = zone.GetUtcOffset(instant);
            return "UTC" + (offset < TimeSpan.Zero ? "−" : "+") + offset.Duration().ToString(@"hh\:mm");
        }
        public static string SafeName(string name, string fallback) {
            string clean = new string((name ?? "").Where(c => !Char.IsControl(c)).ToArray()).Trim();
            if (clean.Length == 0) clean = fallback;
            if (clean.Length > 40) clean = clean.Substring(0, Char.IsHighSurrogate(clean[39]) ? 39 : 40);
            return clean;
        }
        public static string Label(ZoneEntry entry, DateTimeOffset instant) {
            return entry.Name + "    " + Offset(TimeZoneInfo.FindSystemTimeZoneById(entry.Id), instant);
        }
    }
    public sealed class StartupManager {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ValueName = "TimeZoneTray";
        readonly string path, key, name;
        public StartupManager(string path, string key, string name) { this.path=path; this.key=key; this.name=name; }
        public bool Enabled {
            get { using (var k=Registry.CurrentUser.OpenSubKey(key)) return k != null && k.GetValue(name) != null; }
        }
        public bool DisabledByWindows {
            get {
                using (var k=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")) {
                    var bytes = k == null ? null : k.GetValue(name) as byte[];
                    return bytes != null && bytes.Length > 0 && (bytes[0] == 3 || bytes[0] == 7);
                }
            }
        }
        public void SaveWithRollback(bool enabled, Action save) {
            using (var k=Registry.CurrentUser.CreateSubKey(key)) {
                object previous=k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryValueKind kind=previous == null ? RegistryValueKind.None : k.GetValueKind(name);
                if (enabled) k.SetValue(name, "\"" + path + "\"", RegistryValueKind.String);
                else k.DeleteValue(name, false);
                try { save(); }
                catch {
                    if (previous == null) k.DeleteValue(name, false);
                    else k.SetValue(name, previous, kind);
                    throw;
                }
            }
        }
        public void SetEnabled(bool enabled) {
            using (var k=Registry.CurrentUser.CreateSubKey(key)) {
                if (enabled) k.SetValue(name, "\"" + path + "\"", RegistryValueKind.String);
                else k.DeleteValue(name, false);
            }
        }
    }
    public sealed class SwitchResult {
        public bool Success; public string Error; public bool PermissionDenied;
    }
    public static class ZoneService {
        public static TimeZoneInfo Current() { TimeZoneInfo.ClearCachedData(); return TimeZoneInfo.Local; }
        public static SwitchResult Set(string id) {
            // Resolve to a known Windows ID before constructing the argument string.
            var zone=TimeZoneInfo.FindSystemTimeZoneById(id);
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tzutil.exe"), "/s \"" + zone.Id + "\"") {
                UseShellExecute=false, CreateNoWindow=true, RedirectStandardError=true, RedirectStandardOutput=true
            };
            using (var process=Process.Start(start)) {
                var output=process.StandardOutput.ReadToEndAsync();
                var error=process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000)) {
                    try { process.Kill(); process.WaitForExit(); } catch (InvalidOperationException) { }
                    return new SwitchResult { Error="Windows 修改时区超时。请检查系统设置后重试。" };
                }
                string details=(error.Result + " " + output.Result).Trim();
                bool success=process.ExitCode==0 && String.Equals(Current().Id, zone.Id, StringComparison.OrdinalIgnoreCase);
                bool denied=details.IndexOf("privilege",StringComparison.OrdinalIgnoreCase)>=0 || details.Contains("权限") || details.Contains("特权") || details.Contains("拒绝访问") || details.IndexOf("access is denied",StringComparison.OrdinalIgnoreCase)>=0;
                return new SwitchResult { Success=success, PermissionDenied=denied,
                    Error=success ? null : (details.Length > 0 ? details : "系统未确认目标时区。请检查 Windows 的自动设置时区功能。") };
            }
        }
    }
}
