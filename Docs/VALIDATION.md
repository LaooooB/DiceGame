# 本次验证：24 格与三级／六级骰子分支

日期：2026-09-15。基于上一份完整城镇工程 ZIP。本次未修改 GitHub；`History/CampaignDeliveryVerification` 与 `History/CampaignDelivery_VALIDATION.md` 是上一份交付的历史证据，不代表本次远端 CI。

| 本次执行 | 结果 | 证据 |
|---|---|---|
| 冻结旧数值的原战斗回归 | 27 通过，0 失败 | `Verification/core-tests.json` |
| 城镇与区域回归（当前配置） | 45 通过，0 失败 | `Verification/campaign-tests.json` |
| 24 格／强制六骰／技能／迁移回归 | 55 通过，0 失败 | `Verification/skills-tests.json` |
| 原生鼠标键盘命中操作 | 23 通过，0 失败 | `Verification/native-input-checks.json` |
| 静态完整性与配置检查 | 58 通过，0 失败 | `Verification/package-checks.json` |
| 实际 Godot 界面 | 14 张截图及布局断言通过 | `../Artifacts/CampaignScreenshots/` |
| Godot 无界面游戏启动 | 通过 | `Verification/startup.log` |
| Godot 编辑器导入 | 资源扫描完成；C# 编辑器插件报缺少 SDK / Microsoft.Build，未记为通过 | `Verification/import.log` |
| C# 游戏与测试编译 | 通过真实 Roslyn 编译与 Godot 源生成器 | `Verification/game-compile.log`（编译器成功时无输出）、各套测试日志 |
| 六种随机合成容量实验 | 旧三骰、新九骰开局各 20,000 次 | `Verification/board-capacity.json` |
| 原资产、JavaScript、夹具与原截图 | 对输入 ZIP 逐文件 SHA256 相同 | `Verification/original-assets-integrity.json` |

## 真实执行范围

Linux x64；Godot `4.6.stable.mono.official.89cea1439`。使用 Xvfb 与 OpenGL Compatibility / Mesa llvmpipe，截图来自真实 Godot，不是浏览器重画。音频设备为 Dummy；原声波形用原 float32 夹具回归，不代表扬声器试听。

本地环境没有完整 .NET SDK，使用实际 Roslyn C# 12 编译器、net8.0 参考程序集、GodotSharp 及 Godot.SourceGenerators 4.6.0 编译并执行；未把词法扫描当编译。**本次没有执行标准 `dotnet build` / MSBuild，也没有跑新的 GitHub CI。** 编辑器导入过程中资源扫描完成，但 C# 插件明确报告缺少 SDK 与 Microsoft.Build，因此不将退出码 0 当作干净导入通过。真实游戏运行及十四界面验证使用本次已经编译好的程序集，运行日志无错误。本地工程中的 CI / PowerShell 验证脚本已加入技能测试，可供正常 SDK 环境复测。

旧 27 组测试固定使用 `Tests/LegacyBalanceData`，让原 179 个检查点、72 组属性、84 组音色继续与旧黄金夹具比较；没有把新输出写回黄金夹具。这验证共用算法兼容，不证明新伤害与旧伤害相同。当前新规则由 45+55 组测试覆盖。

技能测试包含真实碰撞穿透、爆炸、连锁、减速、碎冰、分裂子弹、墙伤保留与冲击；同时覆盖六级两段选择、错误/旧令牌、读档、保存失败不发布候选、迁移旧高点骰子不白送齐射、旧结算继续无尽补选、队列限制和随机状态不变。原生输入检查通过 `Input.ParseInputEvent` 及 Control 命中，包括第四排第 24 个格子召唤、三级实际随机结果选型、六级 A/B 后仍暂停、C/D 后才恢复。

截图包含默认战斗、24 格填满、三级选型、六级重选、六级最终选型、完成构筑和 130% 字号。字号最大时右侧说明可滚动，阵地及主操作不把整个窗口挤出屏幕。断言检查实际外框与所有格子在视口内。

24 颗六点带技能骰子、56 个固定目标的 CPU 模拟压测执行 1,200 固定步（10 秒模拟时间），过程检查活跃弹丸与队列上限。耗时、峰值见 `skills-stress.json`；这不是 GPU 帧率、长局性能保证或最坏场景上界。

## 复现

在装有 .NET 8 SDK 与 Godot 4.6 Mono 的机器上，从工程根目录执行：

```sh
dotnet run --project Tests/DiceGame.Tests.csproj
dotnet run --project Tests/Campaign/Campaign.Tests.csproj
dotnet run --project Tests/Skills/Skills.Tests.csproj
dotnet build DiceGame.csproj
godot --headless --editor --import --path . --quit
godot --path . -- --capture-campaign
python Tools/static_check.py
python Tools/analyze_board_capacity.py --trials 20000 --seed 20260915
python Tools/verify_source_manifest.py
```

`--capture-campaign` 使用隔离内存存档，不读写玩家进度；`--capture-campaign --layout-only` 只运行放大字号布局截图，不能冒充完整输入与十四界面检查。`--capture-reference` 是旧对照界面。

## 没有被这些测试替代的验收

未在 Windows 实际显卡与音频设备上测试；未导出并运行 Windows EXE；未进行长时真人难度、玩法乐趣、最优构筑或全部配置组合的平衡验收。容量实验不是胜率，24 格不是整局绝对不满的保证。交付是完整源工程，不包含编辑器、SDK、模板或系统字体。
