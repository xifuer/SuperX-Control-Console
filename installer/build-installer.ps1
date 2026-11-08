param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "找不到 csc.exe：$csc" }

$payload = Join-Path $root 'installer\payload'
$zip = Join-Path $root 'installer\payload.zip'
Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $payload -Force | Out-Null

# 先构建主程序
& (Join-Path $root 'build.ps1')

$files = @(
    @{ Src = "$root\RyzenTuner.exe";      Dst = 'RyzenTuner.exe' }
    @{ Src = "$root\libryzenadj.dll";     Dst = 'libryzenadj.dll' }
    @{ Src = "$root\WinRing0x64.dll";     Dst = 'WinRing0x64.dll' }
    @{ Src = "$root\WinRing0x64.sys";     Dst = 'WinRing0x64.sys' }
    @{ Src = "$root\inpoutx64.dll";       Dst = 'inpoutx64.dll' }
    @{ Src = "$root\assets\app.ico";      Dst = 'app.ico' }
)
foreach ($f in $files) {
    if (Test-Path $f.Src) { Copy-Item $f.Src (Join-Path $payload $f.Dst) -Force }
    else { Write-Warning "缺少 $($f.Dst)，安装包将不完整" }
}

$readme = @"
功耗控制台 (RyzenPowerTuner) v$Version

· 按 TURBO 键打开/关闭面板（默认 Ctrl+Win+Alt）
· 按供电自动切换功耗档位（电池 / Type-C / DC-IN）
· 灯效：常亮、呼吸、19 种内置特效
· 一键性能模式：120W + 高性能电源计划

设置保存在同目录 settings.ini，日志在 logs\tuner.log
卸载：运行目录中的 uninstall.exe
"@
Set-Content (Join-Path $payload 'README.txt') $readme -Encoding UTF8

Remove-Item $zip -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$out = Join-Path $root "Setup-$Version.exe"
& $csc /nologo /target:winexe /platform:x64 /optimize+ `
    "/out:$out" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    "/win32icon:$root\assets\app.ico" `
    "/win32manifest:$root\installer\setup.manifest" `
    "/resource:$zip,payload" `
    "$root\installer\Setup.cs"
if ($LASTEXITCODE -ne 0) { throw "安装包编译失败" }

Write-Host ("完成：{0}  {1:N0} 字节" -f (Split-Path -Leaf $out), (Get-Item $out).Length) -ForegroundColor Green
