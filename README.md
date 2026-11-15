# 时区切换器

一个只待在 Windows 托盘里的时区小工具。需要的时候点一下就好，平时本龙会让它安静工作。🐉

## 下载

[下载 TimeZoneTray.exe](https://github.com/wlx235/TimeZoneTray/raw/refs/heads/main/dist/TimeZoneTray.exe)。无需其他随附文件；需要 Windows 上已有 .NET Framework 4.8 或兼容版本。

## 使用

1. 将 `TimeZoneTray.exe` 放在准备长期保留的位置，双击启动。默认没有窗口。
2. 在任务栏右侧找到青绿色的时钟图标。如果暂时看不到，展开「隐藏的图标」的小箭头；也可以把它拖到任务栏可见区域。
3. 左键或右键点击图标，查看当前时区并切换常用时区。
4. 选择「设置…」添加、移除、排序或重命名。搜索支持 Windows 时区名称、英文 ID、UTC 偏移及常见城市别名。修改后点击「保存」；取消或关闭窗口会放弃修改。
5. 「开机自启动」默认开启，在当前用户登录 Windows 后启动。在设置中取消勾选并保存即可关闭。退出程序不会取消下次登录自启动。

首次使用只包含当前系统时区；其余时区由你选择。显示的偏移随夏令时变化。改变的是系统时区，不是手动调整系统 UTC 时钟。

如果程序图标被 Windows 收进隐藏区域，这是系统托盘的显示设置，不影响后台运行。

## 配置与移除

- 配置：`%LOCALAPPDATA%\TimeZoneTray\settings.json`。
- 自启动：当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 中的 `TimeZoneTray` 项。
- 若在任务管理器里禁用了启动项，请在 Windows「设置 → 应用 → 启动」重新开启；程序不会暗中覆盖这个禁用操作。
- 移动程序后，运行新位置的程序，在设置中保存一次，更新自启动路径。
- 移除：先在设置关闭自启动并保存，再从托盘退出，删除程序即可。需要清除偏好时再删除上述配置目录。

## 兼容性与说明

面向 Windows 10/11，使用 .NET Framework 4.8，无第三方依赖。已在本机验证。正常运行不需要管理员身份；只有系统拒绝切换权限时，才会询问是否打开 Windows 授权提示。

如果 Windows 开启了「自动设置时区」，系统可能随后自动改回时区；需要手动切换时请在 Windows 日期和时间设置中关闭该选项。程序不会反复强制覆盖系统自动设置。

## 构建和测试

源码目录中运行 `powershell -ExecutionPolicy Bypass -File .\build.ps1`。使用 Windows 自带的 Framework C# 编译器。产物在 `build\TimeZoneTray.exe`。脚本运行配置、夏令时、隔离启动项及设置表单测试；不会更改系统时区或真实开机启动项。

- `TimeZoneTray.exe --settings`：打开设置，已运行时唤起现有实例。
- `TimeZoneTray.exe --preview`：使用临时预览配置，禁止修改系统时区及启动项。
- `tests/SystemCheck.cs` 是单独的系统集成检查：临时切换到另一个时区，并在 finally 中恢复原时区及夏令时标记。不会随常规构建自动执行。

实现参考：[Windows tzutil](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/tzutil)、[当前用户启动项](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)。
