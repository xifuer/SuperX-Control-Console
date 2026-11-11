param(
    [switch]$NoIcon
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path $csc)) { throw "找不到 csc.exe：$csc" }

$refs = @(
    '/r:System.dll',
    '/r:System.Drawing.dll',
    '/r:System.Management.dll',
    '/r:System.Windows.Forms.dll'
)

$cscArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    "/out:$root\RyzenTuner.exe"
) + $refs

if (-not $NoIcon -and (Test-Path "$root\app.ico")) {
    $cscArgs += "/win32icon:$root\app.ico"
}

$cscArgs += @(
    "$root\src\Core.cs",
    "$root\src\Rgb.cs",
    "$root\src\Lighting.cs",
    "$root\src\Pad.cs",
    "$root\src\Bridge.cs",
    "$root\src\VendorPipe.cs",
    "$root\src\Raw.cs",
    "$root\src\Ui.cs"
)

Write-Host "编译 RyzenTuner.exe ..." -ForegroundColor Cyan
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }

$exe = Get-Item "$root\RyzenTuner.exe"
Write-Host ("完成：{0}  {1:N0} 字节" -f $exe.Name, $exe.Length) -ForegroundColor Green
