# 发布到 GitHub

仓库目录已经整理好，以下命令在装好 Git 的机器上执行：

```powershell
cd D:\395work\RyzenPowerTuner
git init
git add -A
git commit -m "feat: 功耗控制台 1.0.0"

# 在 GitHub 上先创建空仓库 RyzenPowerTuner，然后：
git remote add origin https://github.com/<你的用户名>/RyzenPowerTuner.git
git branch -M main
git push -u origin main
```

## 建议的仓库设置

- **Description**：Power & RGB control panel for ONEXPLAYER SUPER X (Ryzen AI MAX+ 395)
- **Topics**：`ryzen` `strix-halo` `onexplayer` `handheld` `tdp` `rgb` `winforms` `csharp`
- **License**：MIT（已包含 `LICENSE`）

## 发布 Release

先构建，再上传产物：

```powershell
.\build.ps1
.\installer\build-installer.ps1
```

`Setup-1.0.0.exe` 即发布附件；`RyzenTuner.exe` 可作为免安装版单独提供。

建议在 Release 说明里注明：

- 支持的机型与 BIOS 版本
- 是否需要安装 `inpoutx64` 驱动（受影响的只有 Type-C / DC-IN 区分）
- 未签名，首次运行需在 SmartScreen 中选择「仍要运行」

## 截图

`assets/` 下已有三张界面图，可直接用于 README 预览。
