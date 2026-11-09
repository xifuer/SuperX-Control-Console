# 硬件层（vendor）说明

本程序通过 **OneXConsole 的 `CompatLayerCT.exe`** 读取机身 TURBO 按键。
这个组件是厂商的专有二进制，**不随本仓库分发**，需要你在本机自行采集。

没有硬件层时：

- TURBO 键无法唤醒面板
- 「自定义唤醒键」仍然可用（键盘钩子，不依赖硬件层）
- 功耗、灯效、亮度音量、主题全部正常

## 采集方法

1. 安装 OneXConsole（官方安装包）
2. **至少启动一次**，让它把 `resources\resources\base_build.7z` 解压成完整依赖
3. 运行仓库里的采集脚本：

```powershell
powershell -ExecutionPolicy Bypass -File tools\collect-vendor.ps1
```

脚本会把下列内容复制到程序目录的 `vendor\`：

```
CompatLayerCT.exe / .config
hidapi.dll / HDRController.dll / PnPTool.dll / WCHBLEDLL.dll
XInputInterface.dll / PresentMonAPI.dll / RTSSSharedMemoryNET.dll
libryzenadj.dll / winfr.dll / ...
WinRing0x64.dll / .sys / inpoutx64.dll
```

4. 重启本程序，TURBO 即可用

之后即使卸载 OneXConsole 也不影响——程序用的是 `vendor\` 里的副本。

## 自定义路径

若硬件层放在别处，在 `settings.ini` 里指定：

```ini
VendorDir=D:\SomePath\OneXConsole\resources\resources
```

查找顺序：`VendorDir` → 程序目录 `vendor\` → `C:\Program Files\OneXConsole\resources\resources`

## 自检

```powershell
RyzenTuner.exe --probe out.txt
```

报告中会出现：

```
[厂商硬件层]
CompatLayerCT 运行=True
vendor 目录=True
```

日志里也会记录实际使用的路径：

```
硬件层: C:\Program Files\RyzenTuner\vendor
```

或

```
硬件层: 未找到（TURBO 需要 CompatLayerCT，见 docs/VENDOR.md）
```

## 许可

`CompatLayerCT.exe` 及其依赖属于壹号本 / One-Netbook，仅限在你自己的设备上配合本程序使用。
请勿将其提交到仓库、打包进公开下载、或再分发给他人。
