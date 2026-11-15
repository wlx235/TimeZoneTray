using System.IO;
using TimeZoneTray;
internal static class IconWriter {
    static void Main(string[] args) {
        using(var icon=TrayContext.CreateIcon())
        using(var output=File.Create(args[0])) icon.Save(output);
    }
}
