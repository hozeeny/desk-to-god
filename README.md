# desk to god · 桌面修仙传

Windows 桌面修仙桌宠与网络管理项目。V6.0「持续修仙」基于「骑士助手 v5.4」技术资产开发：桌面修士按真实运行时间持续修炼、巡游、战斗与积累修为，同时保留临时文件清理、网络流量监控、测速及程序网络管理能力。

V6.0 第一阶段加入：道号/灵根/境界/修为/功法的持续修仙体系（炼气至筑基及更高境界的数据结构，每境20层、第10层起可尝试破境、第20层保底）、无临时文件时也会出现虚拟妖物的挂机战斗、独立的第二版修仙存档（原子保存、备份恢复、兼容迁移第一版实验档案）。默认「纯修仙模式」：击败妖物只结算虚拟奖励；真实文件清理、进程工作集回收等系统维护需在修行面板中单独明示授权。程序默认以普通权限运行，仅网络管理功能需要管理员权限（界面内提供重启提权入口）。

详细行为见 [使用说明](源代码/使用说明.txt)，设计依据见 [V6.0 开发基线](设计/V6.0-持续修仙桌宠-开发基线.md)。

## 目录

| 路径 | 用途 |
| --- | --- |
| `源代码/` | 日常开发的源码，包含 `Assets/` 素材和 `验证工具/` |
| `正式版/` | 已有可运行版本、配套素材、源码快照与第三方许可 |
| `独立审查/` | 独立验证源码和历史验证记录 |
| `界面验证/`、`内存验证/` | v5.4 预览图与验证结果 |
| `怪物方向验证/`、`游戏验证/` | 保留的历史预览与验证结果 |
| `打包发布.ps1` | 更新正式版中的源码快照、校验清单和发布压缩包 |

开发时修改 `源代码/`；`正式版/源代码/` 是打包快照，由打包脚本同步。发布压缩包和本地 `bin/`、`obj/` 不纳入 Git。

## 构建和运行

需要 Windows 10/11 64 位、.NET Framework 4.8 和 PowerShell。构建使用系统自带的 C# 编译器，无需 Python、Node.js 或 NuGet。

在仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\源代码\build.ps1"
```

程序以普通权限启动（`asInvoker`），修仙、桌面冒险与临时文件功能开箱即用。网络监控、限速和断网需要管理员权限：普通权限运行时主界面底部会出现「以管理员身份重启 · 启用网络管理」按钮，重启后自动接管；也可以右键「以管理员身份运行」直接启动。运行网络管理功能还需要官方 WinDivert 2.2.2 的 x64 库与驱动；仓库已有正式版中的配套文件，可复制到构建输出目录：

```powershell
Copy-Item -LiteralPath ".\正式版\WinDivert.dll", ".\正式版\WinDivert64.sys" -Destination ".\源代码\bin"
Copy-Item -LiteralPath ".\正式版\第三方许可" -Destination ".\源代码\bin" -Recurse -Force
& ".\源代码\bin\骑士助手.exe"
```

完整保留 EXE 旁的 `Assets/`、`骑士助手.exe.config`、WinDivert 文件和第三方许可。详细功能与清理范围见 [使用说明](源代码/使用说明.txt)。

## 验证和打包

构建后，在管理员 PowerShell 中运行内置测试，并检查退出码：

```powershell
$executable = (Resolve-Path ".\源代码\bin\骑士助手.exe").Path
foreach ($mode in @("--selftest", "--adventure-smoke")) {
    $testProcess = Start-Process -FilePath $executable -ArgumentList $mode -Wait -PassThru -NoNewWindow
    if ($testProcess.ExitCode -ne 0) { throw "$mode 失败：$($testProcess.ExitCode)" }
    Write-Host "$mode 通过"
}
```

`--selftest` 使用纯逻辑和隔离文件测试；`--adventure-smoke` 检查合成战斗、屏幕外窗口及自建文件通知测试。其他测试入口和历史结果见 [测试记录](源代码/测试记录.txt)。历史独立审查工具中仍有旧版本路径，单独运行前需调整；日常验证优先使用内置测试入口。

发布前先把当前源码编译到正式版，再打包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\源代码\build.ps1" -OutputDirectory ".\正式版"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\打包发布.ps1"
```

打包脚本以所在仓库目录为根目录，生成 `骑士助手-v5.4.zip` 并核对 ZIP 内容的 SHA-256。版本号变化时需同步调整代码和发布文件名。

## 一起开发

```powershell
git clone https://github.com/hozeeny/desk-to-god.git
cd desk-to-god
git switch -c feature/你的功能名
```

每个功能或修复使用独立分支，通过 Pull Request 合并到 `main`。提交前完成构建和相关验证，PR 中写清修改内容、验证结果，界面变更附截图。开始新任务前先更新本地 `main`。

个人冒险存档在 `%LOCALAPPDATA%/KnightAssistant/adventure-v1.txt`（v5.4 旧档原样保留），修仙存档在 `%LOCALAPPDATA%/KnightAssistant/cultivation-v2.txt`（含 `.bak` 备份），真实维护授权在 `%LOCALAPPDATA%/KnightAssistant/maintenance-consent.txt`，网络记录在 `%LOCALAPPDATA%/NetBunny/`，无需提交到仓库。

WinDivert 的原始许可和源码包随 `正式版/第三方许可/` 保留。项目自身目前尚未指定开源许可证。
