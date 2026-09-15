# 当前交付验证

日期：2026-09-15。`Docs/History/VALIDATION.md` 是上传 ZIP 原有的历史状态，不代表本次执行结果。

| 检查 | 本次结果 | 证据 |
|---|---|---|
| 上传 ZIP 的 CRC | 通过 | 解压前完整校验 |
| 原战斗 C# 回归 | 27 通过，0 失败 | `Verification/core-tests.json` |
| 新增循环 C# 回归 | 45 通过，0 失败 | `Verification/campaign-tests.json` |
| 文件/配置/图集静态完整性 | 49 通过，0 失败 | `Verification/package-checks.json` |
| 原生鼠标/键盘流程 | 14 通过，0 失败 | `Verification/native-input-checks.json` |
| 原生界面运行及截图 | 8 个界面完成 | `Verification/native-capture.json` |
| 原美术、JS 对照、原截图、测试夹具 | 逐文件 SHA256 与上传 ZIP 一致 | `Verification/original-assets-integrity.json` |
| 整个游戏 C# 编译 | 成功：真实 .NET 8 Roslyn + Godot 4.6 GodotSharp + 源生成器 | 本地编译日志；GitHub 提交前另执行实际 SDK/MSBuild |

## 执行环境

Linux x64，Godot `4.6.stable.mono.official.89cea1439`。图形测试使用 Xvfb + OpenGL Compatibility + Mesa llvmpipe，是真实 Godot 渲染，不是浏览器截图。音频设备使用 Dummy，声音数值通过原始 float32 夹具验证，不作为真实扬声器听感验收。

本地容器没有完整 SDK，使用真实 Roslyn 编译器、net8.0 参考程序集、GodotSharp 与 Godot.SourceGenerators 编译；不是词法扫描。GitHub 上传作业会再执行标准 `dotnet build`、两套 `dotnet run`、Godot 导入及启动，任何一步失败都不会提交 main。

## 覆盖的关键情况

原测试包含 17 场景/179 检查点、72 组骰子属性、随机序列、扫掠碰撞、瞄准轨迹、粒子与 84 组音色。基准来自上传 ZIP 的原 JavaScript，并未用新 C# 输出覆盖基准。

新增测试包含区域三阶段通关、头目不可超时跳过、头目突破判负、最终头目击败凭证、未解锁阻断、主骰身份固定、单局只给一个齿轮、重复主骰不刷齿轮、失败保留战利品、零击杀撤回、无尽增量结算、施工支付与完工解锁、存档失败回滚、崩溃恢复后的重复领奖保护、旧档迁移、下一层保留成长、区域与解锁可达性、强化池耗尽不死锁等。

原生输入检查通过实际 `Input.ParseInputEvent` 和 Control 命中，覆盖城镇选区域、区域选卡组、开局、长按不发射、松手一次发射、移出取消、右键取消、拖拽合成、空位召唤、Esc 暂停、继续与结算回城。不是只直接调用按钮业务方法。

## 复测

使用 .NET 8 SDK 和 Godot 4.6 Mono：

```sh
dotnet run --project Tests/DiceGame.Tests.csproj
dotnet run --project Tests/Campaign/Campaign.Tests.csproj
dotnet build DiceGame.csproj
godot --headless --editor --import --path . --quit
godot --path . -- --capture-campaign
python Tools/static_check.py
```

截图输出到 `Artifacts/CampaignScreenshots`。`--capture-campaign` 使用隔离内存存档，不读写玩家档。旧画面对照入口仍是 `--capture-reference`；它不能用于宣称新的桌面布局与旧竖版 UI 逐像素一致。

## 不在自动化结论内

区域数值的人工难度/节奏平衡、长时真人游戏体验、Windows 实际显卡与音频设备、不同系统字体表现、Windows 导出可执行文件实测没有由这些测试替代。附带八区域/建筑数值是制作人可替换的贯通内容，不是参考游戏的精确数值。

本次交付是完整可构建的源工程，不附 Godot 编辑器、SDK、导出模板或任何环境字体文件。
