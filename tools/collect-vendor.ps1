<#
  从已安装的 OneXConsole 采集厂商硬件层，放入本程序的 vendor 目录。
  TURBO 按键依赖 CompatLayerCT.exe；仓库不附带该二进制，需本机自行采集。

  用法：
    powershell -ExecutionPolicy Bypass -File tools\collect-vendor.ps1
    powershell -ExecutionPolicy Bypass -File tools\collect-vendor.ps1 -OneX "D:\Games\OneXConsole" -Target "C:\Program Files\RyzenTuner"

  前置条件：OneXConsole 至少成功启动过一次，这样 resources\resources 里的
  base_build.7z 才会被它自己解压成完整的一堆 DLL。
#>
param(
    [string]$OneX = "C:\Program Files\OneXConsole",
    [string]$Target = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$res = Join-Path $OneX 'resources\resources'
if (-not (Test-Path (Join-Path $res 'CompatLayerCT.exe'))) {
    Write-Host "找不到 $res\CompatLayerCT.exe" -ForegroundColor Red
    Write-Host "请先安装并启动一次 OneXConsole，再运行本脚本。" -ForegroundColor Yellow
    exit 1
}

$vendor = Join-Path $Target 'vendor'
New-Item -ItemType Directory -Path $vendor -Force | Out-Null

# 采集 CompatLayerCT 及其已解压的依赖
$files = Get-ChildItem $res -File | Where-Object { $_.Name -notlike '*.7z' }
foreach ($f in $files) {
    Copy-Item $f.FullName (Join-Path $vendor $f.Name) -Force
    Write-Host ("  + " + $f.Name)
}

# 7z 里还有一批 DLL，OneXConsole 运行过就已经解压到同目录；没解压则提示
$build = Join-Path $res 'base_build.7z'
if (Test-Path $build) {
    Write-Host ""
    Write-Host "仍存在 base_build.7z，说明 OneXConsole 还没解压过依赖。" -ForegroundColor Yellow
    Write-Host "请启动一次 OneXConsole 让它自动解压，然后重新运行本脚本。" -ForegroundColor Yellow
}

# 我们的程序自身需要的端口驱动
foreach ($n in @('WinRing0x64.dll', 'WinRing0x64.sys', 'inpoutx64.dll')) {
    $src = Join-Path $Target $n
    $dst = Join-Path $vendor $n
    if ((Test-Path $src) -and -not (Test-Path $dst)) {
        Copy-Item $src $dst -Force
        Write-Host ("  + " + $n + " (来自程序目录)")
    }
}

$count = (Get-ChildItem $vendor -File | Measure-Object).Count
Write-Host ""
Write-Host ("完成：vendor 目录共 " + $count + " 个文件 -> " + $vendor) -ForegroundColor Green
Write-Host "重启本程序后 TURBO 即可用。" -ForegroundColor Green
Write-Host ""
Write-Host "注意：这些是厂商专有二进制，仅限本机使用，请勿再次分发。" -ForegroundColor DarkYellow
