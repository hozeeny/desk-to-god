# Demo 素材机械准备

桌面短回归另见 `verify-desktop-ecology.ps1 -Suite Journey`（成长引导、目标来源、预警与阅读稳定性）及 `verify-feature-panel-usability.ps1`（四功能面板窄屏布局、洞天配方、库存与控件复用）。`-Suite All -Baseline` 包含 Journey；这些工具使用私有夹具，不启动八小时长测。

`verify-desktop-panel-layering.ps1` 是约5秒的可见原生窗口夹具，检查新演员不抢焦点、前台手札及其所属弹窗命中、切换前台、隐藏与最小化；其中洋红方块只是故意覆盖按钮的测试窗口，不是游戏素材。不要与其他可见桌面验收并行。`verify-dongtian-mirage-loop.ps1` 检查重复蜃楼奖励到镜花入库、炼丹、战斗增益和私有存档恢复；它使用停止时钟与就绪炉位夹具，不能用作自然成长或等待30分钟的证明。

仅处理独立美术 Demo。原图不会被改写；工具不会重绘、抠背景、替色或把完整人物裁块称为骨骼。输入必须已具备真实 alpha；无透明背景的输入会报错。

使用 bundled Python（已含 Pillow）：

```powershell
& 'C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' '美术Demo/tools/prepare-assets.py' --manifest '美术Demo/asset-inputs.json' --output-dir '源代码/ArtDemoAssets' --qa-dir '美术Demo/qa/assets'
```

单张原图也可直接处理：

```powershell
& 'C:/Users/Administrator/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' '美术Demo/tools/prepare-assets.py' --source '美术Demo/generated/hero-mortal-original.png' --name hero-mortal --kind hero --output-dir '源代码/ArtDemoAssets' --qa-dir '美术Demo/qa/mortal'
```

`--dry-run` 只读核对。`--overwrite` 允许替换已有衍生输出，仍会拒绝覆盖任何输入原图或输入 manifest。`--max-size 512 512` 等比缩小；默认不放大。默认运行 PNG 上限为 512×512；输入 manifest 可为 hero 指定 `max_size: [768,1152]`、地标指定 `[768,768]`，保留更清晰的生产尺寸。

JSON 示例见 `asset-manifest.example.json`。`source` 相对输入 manifest 所在目录解析。`rect` 为 `[left, top, width, height]`；`grid` 使用零起始、逐行排序的 `index`，也可用零起始 `cell: [column, row]`，可明确 `margin: [x,y]` 和 `gutter: [x,y]`。网格必须整除原图，不能同时提供 rect 与 grid；不推测格线。

明确授权的多矩形图集分离可用 `keep_rects: [[left,top,width,height], ...]`，坐标均为原图绝对坐标，必须处于外层 rect 内。工具仅将这些矩形逐像素复制到同位置透明画布，不应用 alpha 阈值、蒙版修边或重绘；报告保留全部 ROI 与隔离说明。合法不透明背景可用 `allow_opaque: true`；同时指定 `copy_source: true` 及原尺寸 max_size 时，PNG 原字节直接复制，报告校验 source/output SHA 完全一致。

默认保留完整 source/cell 画布。可选 `trim_transparent: true` 或 `--trim-transparent` 仅裁去 alpha 全为 0 的外边空白，保留全部 alpha>0 像素。alpha>24 的 bounds 始终只用于 measurement，报告明确标为 `measurement_only`，不会将阈值蒙版写回图片。

输出包括运行 PNG、包内 `asset-manifest.json`，以及 QA `asset-report.json`、每张 220×240 透明预览、棋盘背景素材联系表。报告包含原图与成品 SHA-256、矩形来源、alpha 分布、透明边裁切记录、尺寸和边缘接触警示。预览的额外放大仅用于检查，不影响运行 PNG。

固定运行文件名：

- `hero-mortal.png`、`hero-qishi.png`、`hero-zhuji.png`
- `monster-guardian.png`（skeleton）、`monster-slime.png`、`monster-imp.png`、`monster-bat.png`、`monster-mushroom.png`
- `elite-guardian.png`、`elite-mushroom.png`
- `mirror-shard.png`、`mirror-wraith.png`、`mirror-boss.png`
- `weapon-mortal.png`、`weapon-qishi.png`、`weapon-zhuji.png`
- `item-spirit-stone.png`、`item-pill.png`、`item-material.png`、`item-scroll.png`、`item-marrow.png`
- `landmark-dongtian.png`
