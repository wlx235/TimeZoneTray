param([Parameter(Mandatory=$true)][string]$Exe)
$ErrorActionPreference = 'Stop'
$app = Start-Process -FilePath $Exe -WindowStyle Hidden -PassThru
try {
    Start-Sleep -Seconds 2
    $app.Refresh()
    if ($app.HasExited -or $app.MainWindowHandle -ne 0) { throw 'Normal launch must stay alive with no visible main window.' }
    'PASS silent normal launch'
    $second = Start-Process -FilePath $Exe -WindowStyle Hidden -PassThru
    if (-not $second.WaitForExit(5000) -or $second.ExitCode -ne 0) { throw 'Second instance did not exit cleanly.' }
    'PASS duplicate instance exits cleanly'
    $runValue = Get-ItemPropertyValue -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TimeZoneTray'
    if ($runValue -ne ('"' + $Exe + '"')) { throw 'Startup path does not match final executable.' }
    'PASS startup registered to final executable'
    "RUNNING_PID=$($app.Id)"
} catch {
    if (-not $app.HasExited) { Stop-Process -Id $app.Id }
    throw
}
