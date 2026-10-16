# desk to god · 桌面修仙传

Windows 桌面冒险与网络管理项目，当前代码基于「骑士助手 v5.4」。包含桌面角色、战斗与装备、临时文件清理、网络流量监控、测速及程序网络管理。

v5.4 加入实时临时文件战场、随等级调整的普通怪血量，以及随机皮肤与武器的邪恶分身 Boss。敌人最多 9 个普通怪加 2 个 Boss；击败内存 Boss 后尝试回收后台应用工作集，并显示实际采样反馈。详细行为见 [使用说明](源代码/使用说明.txt)。

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

输出位于 `源代码/bin/`。运行网络管理功能还需要官方 WinDivert 2.2.2 的 x64 库与驱动；仓库已有正式版中的配套文件，可复制到构建输出目录：

```powershell
Copy-Item -LiteralPath ".\正式版\WinDivert.dll", ".\正式版\WinDivert64.sys" -Destination ".\源代码\bin"
Copy-Item -LiteralPath ".\正式版\第三方许可" -Destination ".\源代码\bin" -Recurse -Force
& ".\源代码\bin\骑士助手.exe"
```

程序运行时请求管理员权限。完整保留 EXE 旁的 `Assets/`、`骑士助手.exe.config`、WinDivert 文件和第三方许可。详细功能与清理范围见 [使用说明](源代码/使用说明.txt)。

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

个人存档在 `%LOCALAPPDATA%/KnightAssistant/`，网络记录在 `%LOCALAPPDATA%/NetBunny/`，无需提交到仓库。

WinDivert 的原始许可和源码包随 `正式版/第三方许可/` 保留。项目自身目前尚未指定开源许可证。
