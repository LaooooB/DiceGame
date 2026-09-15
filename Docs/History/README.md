# 骰子回响 · Godot 4.6 stable Mono / .NET 桌面工程

将本次对话中的 `DiceGame-project.zip` 迁移为原生 Godot C# 工程。没有使用 WebView、浏览器内核、JavaScript 运行时或微信适配层。没有修改 GitHub 仓库。

**验收状态：源码、场景、原版资源和对照测试已交付；交付环境没有 Godot/.NET，尚未执行 C# 编译、Godot 启动和原生画面对比。不能把这个工程说成“已实测与原版完全一致”。详见 `Docs/VALIDATION.md`。**

## 打开方式

1. 将整个压缩包解压到本地目录，例如 `D:\Games\DiceGame-Godot`。不要只复制 `project.godot`。
2. 使用 **Godot 4.6 stable .NET（Mono）**，在项目管理器点击“导入”，选择本目录的 `project.godot`。
3. 电脑须有 **64 位 .NET 8 SDK**，不是只有 Runtime。工程使用 `Godot.NET.Sdk/4.6.0`、`net8.0`。首次构建需要还原 Godot SDK 的 NuGet 包，或者本机已有对应缓存。
4. 打开项目后，点击右上角“构建”，然后按 **F5**。主场景已指定为 `Scenes/Main.tscn`。

这是可编辑源码工程，不是预编译 EXE。运行游戏本身不需要 Node、Python、npm、微信开发者工具、AppID 或网络服务。工程不捆绑引擎、SDK、导出模板或字体文件。

Godot 官方 C# 前提说明：
https://docs.godotengine.org/en/4.6/tutorials/scripting/c_sharp/c_sharp_basics.html

## 桌面操作

| 操作 | 功能 |
|---|---|
| 战场内按住鼠标左键 | 持续调整瞄准方向，不自动发射 |
| 在战场内松开左键 | 所有已装填骰子按该方向齐射 |
| 拖动骰子 | 移动到空格；拖到同种同点骰子上合成 |
| 单击骰子 | 暂停并查看数值，可选择回收 |
| 空格 | 召唤骰子 |
| Esc | 暂停、继续或关闭当前弹窗 |
| M | 音效开关 |
| F11 | 窗口 / 全屏切换 |
| 鼠标右键、移出窗口 | 取消当前瞄准或拖动 |
| 切换到其他程序 | 取消输入；战斗暂停；保存进度 |

只接入鼠标和键盘；禁用触摸模拟。不提供手机、微信或 Web 导出预设。默认窗口 1200×960，最小 960×640。

## 保持原效果的迁移边界

**没有改数值，没有把战场拉成宽屏，没有换成 Godot 默认物理弹球。**

游戏仍在原来的 432×864 逻辑画布中绘制；窗口只缩放整张画布并提供桌面侧栏。战场宽度、发射点、敌阵间距、鼠标角度计算、弹射速度和相同状态下的碰撞顺序都沿用原实现。

- 八格阵地，卡组最多六种，只有同种类且同点数才能合成，上限六点。
- 高点数的单轮威力、装填时间、回收价格、合成随机池沿用原 JSON。
- 速射、爆破、雷电、冰霜、分裂、回弹都保留；合成即强化齐射；已发射及排队的旧弹丸保留自身快照。
- 无尽波次、首领、爆炸方块、击杀能量、被动能量、三选一强化和存档续局都保留。
- 原版骰子高光、阴影、点数和图标由原渲染器导出为 PNG 图集，不另找近似素材。
- 拖尾、光晕、爆炸环、碎片、电弧、合成脉冲、能量引线、连锁提示、命中数字、震屏和闪光移植为原生绘制。
- 音效和可选背景音沿用原版波形公式及浮点样本，通过 `AudioStreamGenerator` 播放，而不是替换成另一套采样。

“沿用参数和公式”不等于“已经逐帧验收相同”。原生字体、抗锯齿、透明混合、缩放和设备音频仍必须在实际 Godot 运行时核对。

## 项目结构

```
project.godot / DiceGame.csproj / DiceGame.sln
Scenes/Main.tscn                 主场景
Scripts/App/GameRoot.cs          Godot 桌面入口、原生视口、输入和窗口生命周期
Scripts/App/GameApp.cs           菜单、瞄准/松手、拖动合成、暂停、存档流程
Scripts/Core/                   不依赖引擎的双精度战斗模拟、配置和存档校验
Scripts/Presentation/           原生 UI、骰子、特效、声音
Scripts/Platform/                桌面存档和随机种子来源
Data/                           原版三份 JSON，字节保持一致
Assets/Reference/               原版导出的 PNG 图集
Shaders/                        视口圆角遮罩
Tests/                          固定种子对照数据、纯 C# 测试、原版参考源码/截图
Tools/                          本机验证、原版截图、差异图工具
Docs/                           迁移清单、架构、验收记录、原版数值说明
```

主要界面和战斗节点由 `GameRoot` 创建，运行时可以在 Godot 的“远程”场景树中检查。`Main.tscn` 本身较小是有意设计，不是漏放场景。UI 用原生 CanvasItem 绘制，并非一组现成的可拖拽 Control 排版。

## 本机核对

最简单：双击 `Verify.cmd`。它会先执行 .NET 核心测试，再编译 Godot 工程；未设置 `GODOT4` 时会询问引擎可执行文件路径。

包含原生截图的验证：

```powershell
.\Verify.cmd -Godot "D:\Godot\Godot_v4.6-stable_mono_win64.exe" -Capture
```

路径是示例，改成你电脑中真实的 Godot 文件。脚本不会安装软件、上传文件、修改注册表、写 GitHub 或变更永久执行策略。

只执行不依赖 Godot 的核心对照测试：

```powershell
dotnet run --project Tests/DiceGame.Tests.csproj --configuration Release
```

截图对比工具：

```powershell
python Tools/compare_screenshots.py
```

该工具需要 Pillow 和 NumPy；正常玩游戏不需要。`Tests/ReferenceScreenshots` 是原版浏览器截图，不是 Godot 截图。先运行原生截图，才会有 `Artifacts/Native` 可供比较。不能以启动通过或一张截图相近代替完整效果验收。

## 存档与导出

桌面存档使用 `user://dice_ricochet_save_v1.json`，保留一个 `.bak` 备份。Godot 使用自定义用户目录 `DiceRicochetGodot`；Windows 通常是 `%APPDATA%\DiceRicochetGodot`。写入临时文件完成后再替换正式文件。

旧浏览器 localStorage 不会被桌面程序自动读取；本次没有迁移玩家旧浏览器进度的功能，也不需要清空浏览器数据。

包含 Windows x86_64 导出预设。导出前在编辑器安装 **4.6 stable 对应的 .NET 导出模板**，在“项目 → 导出”选择 `Windows Desktop`。`Tests`、`Tools`、`Docs` 和 `Artifacts` 不进入发行包。这里未提供已经验证的 Windows EXE，也未验证其他桌面操作系统。
