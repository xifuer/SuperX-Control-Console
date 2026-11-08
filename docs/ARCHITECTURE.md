# 架构

## 目录

```
src/
  Core.cs      设置读写、引擎封装（libryzenadj）、键盘钩子、开机自启、EC 供电识别
  Ui.cs        托盘逻辑（TrayApp）、右侧面板（MainForm）、主题（Theme）、主题窗口、按键学习、灯效页
  Rgb.cs       HID 设备封装与报文构造（RgbDevice / RgbProtocol / RgbState）
  Lighting.cs  灯效控制器：模式切换、呼吸/音乐律动定时器、音频峰值采集
  Raw.cs       Raw Input 窗口（HID 触发、手柄输入）
  Pad.cs       XInput 手柄按键名映射
installer/
  Setup.cs     安装程序（解包、快捷方式、计划任务、卸载注册）
```

## 运行时结构

```
Program.Main
 ├─ 单实例互斥体 Local\RyzenTuner
 ├─ 非管理员时以 runas 重新启动
 └─ Application.Run(new TrayApp(...))
      ├─ Engine        libryzenadj 封装，负责下发 PPT/Tctl
      ├─ KeyHook       WH_KEYBOARD_LL，匹配 TURBO 键
      ├─ RawInputForm  隐藏窗口，接收 HID / 手柄事件
      ├─ LightingController  HID 灯效
      ├─ NotifyIcon    托盘图标与菜单
      ├─ Timer         1 秒轮询供电状态并自动应用档位
      └─ MainForm      按需创建、隐藏而不是销毁
```

## 供电判定流程

```
SourceOverride != auto ?
 ├─ 是 → 直接使用用户指定
 └─ 否 → 读 EC 0xFE
          ├─ 0x01 → battery
          ├─ 0x03 → typec
          ├─ 0x05 → dcin
          └─ 失败 → 退回 ACLineStatus（电池 / 外接，外接默认按 AcSource 处理）
```

## 档位应用

`DesiredKey()` 把「模式 + 供电 + 三个数值」拼成一个字符串作为缓存键，值变化才下发，避免每秒重复写 SMU。

- 自动模式：按当前供电取对应档位
- 手动模式：`ManualW` 同时作为 FAST/SLOW，温度墙取 DC-IN 档位的值

## 灯效

`LightingController.Configure()` 按模式分支：

- 常亮：`0xFD` 状态 → `0xFE` 单色
- 呼吸：定时器按正弦缩放 RGB，反复下发单色
- 内置特效：`0xFD` 状态 → 特效编号命令

`RgbDevice` 使用两个句柄：同步句柄负责写入，`FILE_FLAG_OVERLAPPED` 句柄负责读回执，避免 `HidD_SetOutputReport` 在部分设备上返回 `ERROR_GEN_FAILURE`。

## 主题

`Theme` 是可变调色板。切换主题后整窗重建（`TrayApp.RebuildUi()`），因为 WinForms 控件颜色在构造时确定，重建比逐个刷新更可靠。

## 安装程序

`Setup.cs` 把运行时文件打成 `payload.zip` 作为嵌入资源，安装时解包到目标目录，再用 `WScript.Shell` 创建快捷方式、用 `schtasks` 注册计划任务，最后写入 `HKLM\...\Uninstall`。
