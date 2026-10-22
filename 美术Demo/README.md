# 《桌面修仙传》透明桌面美术 Demo

正式 Demo 默认直接进入 Windows 透明桌面模式。主角与妖物是独立的小型分层窗口，透明区域穿透鼠标，无常驻游戏场景窗、无整屏透明覆盖层。玩法复用正式 AdventureController，只在美术分支增加接入和隔离。

2026-10-22 正在按玩家反馈整改可玩性。旧版的自动验收结论只证明链路存在；本轮补首开引导、真正切换目标、持续动作反馈以及功法／百业／行囊入口。最新进展见 [可玩性整改记录](可玩性整改记录-20261022.md)。

## 启动

Windows x64，.NET Framework 4.8；无需管理员权限、API Key 或外部图像服务。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\美术Demo\launch-demo.ps1
```

构建后直接双击 `美术Demo/runtime-desktop/桌面修仙-透明桌面Demo.exe`。必须保留旁边 Assets、ArtDemoAssets 目录；默认隔离修士保存在同目录 `profiles/player`，退出后继续同一修士。此目录不是正式存档。

便携交付为 `桌面修仙-透明桌面Demo.zip`，完整解压后双击同名 EXE。包内不附带验收档或真实桌面原始录屏。实际画面见 `qa/desktop-delivery/`，完整结论和已知问题见 [验收记录](验收记录.md)。

## 桌面操作

1. 新凡人首开会显示可收起的修行手札。未选中的普通妖物只巡游，不会趁阅读说明围攻；凡人未选敌时不会自行跑开，仍可拖动移动。点击桌面妖物选敌，修士会接近；进入射程后再次点击出一招。凡人每招需玩家操作，点击另一只会立即切换当前目标。
2. 点击修士格挡，冷却沿用正式规则。拖动修士移动，拖动不会触发格挡。右键修士/妖物或点击托盘图标打开修行操作。
3. 十次有效击杀后，在修行面板主动点击「灵根觉醒」。实际修仙状态进入炼气，外观同步切换，自动作战默认开启。
4. 关闭/最小化功能面板只收起界面，桌面角色继续活动、自动战斗与修炼。托盘可隐藏/恢复演员；隐藏也不停止修为增长。
5. 手札「修行与洞天」中可改修已有功法、打开百业、破境、灵域、洞天；「行囊与设置」查看真实兵器属性、已有材料及屏幕位置。百业仍默认关闭，只有玩家在道碑内明确选择后才开始识别。
6. 托盘或修行面板点击「保存并退出」。保存失败会明确提示，正常交互退出暂不执行，以保留本次进度。

没有全局强制快捷键，没有开机自启注册，没有系统维护授权入口。仅用户主动打开的功能面板会获得焦点，角色与妖物窗口本身不激活其他应用。

## 独立测试档

```powershell
# 名称须全新；同名已有档不会被测试种子覆盖。
powershell -NoProfile -ExecutionPolicy Bypass -File .\美术Demo\launch-demo.ps1 -Profile verify-qishi-manual -Seed qishi-full
powershell -NoProfile -ExecutionPolicy Bypass -File .\美术Demo\launch-demo.ps1 -Profile verify-zhuji-manual -Seed zhuji
```

种子明确标注为测试档，预置境界、机缘或设施材料，用来验证既有突破与设施链路，不代表自然养成成果。正常游玩没有验收快进；自动验收显式使用×6时间，仅加速流逝，不减少十杀要求、不注入战斗HP或奖励。

## 美术与隔离

凡人身份采用已确认的 mortal-M2-polish-v2，炼气/筑基选用同人A组。新资源全部位于 `源代码/ArtDemoAssets`；原 Assets 保留。三阶段新增4×3图集、11独立部件+完整参考以及 .rects / .anchors，复用原有步态与IK实现必要的走路、攻击、格挡和调息表现。

五份业务存档及维护授权路径全部显式指向本 Demo 私有目录；正常游玩不读取或写入正式用户档、授权和长测档。显式自动验收仅只读真实档哈希以核对未改变。清理、内存回收、无人值守维护、网络探测和临时文件扫描始终关闭；战斗只产生虚拟妖物。主分支与正式EXE不参与打包覆盖。

提示词、高清源图、A/B候选继续保留在 prompts/、generated/ 与两个生成来源JSON；原24张资源保留。素材处理规则见 asset-inputs.json，分件生成工具见 tools/prepare-hero-rigs.py。

## 预览与验收

原带场景窗口保留为开发预览，通过 `launch-art-preview.ps1` 显式启动，不作为正式桌面入口。

自动验收需要允许本 Demo 对自己的角色、妖物和面板执行鼠标点击，并保留可见桌面。只在新输出目录和 accept- / verify- 私有测试档执行：

```powershell
& .\美术Demo\runtime-desktop\桌面修仙-透明桌面Demo.exe --desktop-demo-accept .\美术Demo\qa\desktop-accept-new --profile accept-new
# 正常速率新玩家流程：20秒无输入阅读、真实换目标、较慢的主动出招、自然调息。
& .\美术Demo\runtime-desktop\桌面修仙-透明桌面Demo.exe --desktop-demo-accept .\美术Demo\qa\desktop-accept-normal-new --profile accept-normal-new --normal-speed
```

真实桌面结果、录像、存档与性能记录见本目录交付验收记录。离屏动作图只能作为美术证据，不能代替桌面玩法验收；中途遮挡、关闭或空白画面不得写成通过。
