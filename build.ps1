param([switch]$TestOnly)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$buildDir = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null
$refs = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Runtime.Serialization.dll')
if (-not $TestOnly) {
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
    & $compiler /nologo /target:exe /main:IconWriter "/out:$buildDir\IconWriter.exe" @refs @sources "$PSScriptRoot\tools\IconWriter.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Icon tool compilation failed.' }
    & "$buildDir\IconWriter.exe" "$buildDir\app.ico"
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
    & $compiler /nologo /target:winexe /optimize+ /platform:anycpu /utf8output "/win32icon:$buildDir\app.ico" "/win32manifest:$PSScriptRoot\app.manifest" "/out:$buildDir\TimeZoneTray.exe" @refs @sources
    if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
}
& $compiler /nologo /target:exe /utf8output "/out:$buildDir\CoreTests.exe" @refs "$PSScriptRoot\src\Core.cs" "$PSScriptRoot\tests\CoreTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& "$buildDir\CoreTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
if (-not $TestOnly) {
    & $compiler /nologo /target:exe /utf8output "/win32manifest:$PSScriptRoot\app.manifest" "/out:$buildDir\UiTests.exe" @refs "/r:$buildDir\TimeZoneTray.exe" "$PSScriptRoot\tests\UiTests.cs"
    if ($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
    & "$buildDir\UiTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'UI tests failed.' }
}
