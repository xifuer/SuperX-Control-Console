# 贡献指南

## 提交问题

请附上：

- 机型、CPU、BIOS 版本
- `logs\tuner.log` 中相关片段
- 若是灯效或供电识别问题，说明是否安装了 OneXConsole、是否安装了 `inpoutx64` 驱动

## 适配新机型

大部分机型差异集中在两处：

1. **灯效协议**：`RgbProtocol` 里的 VID/PID、报文头、命令字、亮度上限
2. **供电识别**：`PowerSource` 里的 EC 寄存器地址与取值映射

抓包方法见 [docs/PROTOCOL.md](docs/PROTOCOL.md)。如果你能在其他机型上抓到厂商报文并对比 EC 快照，欢迎直接把结论写进文档。

## 代码风格

- C# 5 语法，兼容 `csc.exe`（.NET Framework 4.8），不引入 NuGet 依赖
- UI 使用 WinForms + 手写 DPI 缩放（`S()` 换算）
- 所有 P/Invoke 集中在各自的类里，不要在 UI 里直接调用

## 构建与自测

```powershell
.\build.ps1
.\RyzenTuner.exe --selftest out.txt     # 设置读写与逻辑自检
.\RyzenTuner.exe --source out.txt       # 打印 EC 读取结果
.\RyzenTuner.exe --shot ui.png          # 渲染界面截图
```

## 提交 PR

- 一个 PR 只做一件事
- 涉及协议的改动请附抓包或日志证据
- 不要在 PR 中包含 `settings.ini`、日志或编译产物
