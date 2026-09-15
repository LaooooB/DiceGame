# 骰子回响 · Dice Ricochet

Godot **4.6 stable .NET / Mono**、C# / .NET 8 桌面工程。默认 **1920×1080**，仅电脑端。入口为 `Scenes/Main.tscn`。

## 已接通的游戏循环

**城镇准备 → 选择骰子与区域 → 局内成长 → 胜利或失败 → 回城建设、解锁 → 再选择下一局。**

城镇弹射采集木材、石料并推进建筑施工；建筑消耗金币/材料，完工后授予配置中的骰子、机制或永久成长。区域界面显示开放条件、通关记录、蓝图和下一步目标；卡组强制携带六种不同骰子，第一位是本次主骰。每个区域是一场有限远征，不是八个区域连在同一局。

附带八个区域，每个区域三个阶段、两个小头目和一个最终头目。必须实际击败头目，不能靠计时跳过。不同主骰通关为当前深潜层提供不同的齿轮记录；一套六骰卡组不会一次领取六份推进奖励。同一主骰重打仍可获得资源，但不重复产出该通关齿轮。失败和主动撤回保留已获得的物资及蓝图，不授予胜利奖励。通过配置解锁无尽延伸和下一层深潜。

`Data/campaign.json` 中的区域名字、敌群、时长、建筑、奖励和成长数值是**可替换内容**，用于贯通现有六种骰子的完整流程，不代表制作人最终内容设计，也不声称与 BALL x PIT 的具体内容或数值相同。循环机制独立于这些内容。

## 战斗构筑

阵地为 **24 格（6×4）**。强制带六种不同骰子；同类同点数合成后仍从这六种中随机产生结果。

- 到 3 点：为实际随机结果选择该种类的 A 或 B。
- 到 4/5 点：沿用落点材料骰子的 A/B 字母，但使用新种类对应的技能，不继承异种技能。
- 到 6 点：清除旧分支，为最终种类重新选择 A/B，然后选择 C/D。每颗骰子独立记录。

选择期间暂停战斗、弹丸与装填；确认完才发合成齐射。单骰基础伤害乘以 1/3，开场骰子 3→9，召唤能量 24→8，保持同类型组成下的开局直接火力预算。技能和条件增伤另算。格子分析、完整 24 个分支、存档规则见 `Docs/COMBAT_UPGRADE.md`。

## 打开与运行

安装 Godot 4.6 stable 的 .NET 版，以及 .NET 8 SDK。导入根目录 `project.godot`，点击右上角“构建”，按 F6/F5 运行场景/项目。不要使用没有 C# 支持的标准版编辑器。

```sh
dotnet build DiceGame.csproj
dotnet run --project Tests/DiceGame.Tests.csproj
dotnet run --project Tests/Campaign/Campaign.Tests.csproj
dotnet run --project Tests/Skills/Skills.Tests.csproj
```

Windows 可运行 `Verify.cmd`，或：

```powershell
powershell -ExecutionPolicy Bypass -File Tools/verify.ps1 -Godot "C:\Godot\Godot_v4.6-stable_mono_win64.exe" -Capture
```

不需要下载额外游戏素材，不含浏览器或 JavaScript 运行时。`Tests/Original` 中的 JavaScript 仅用于离线回归对照，不进入发行包。

## 操作

长按战场瞄准，松开发射；移出战场松手或按鼠标右键取消。拖动骰子到空格移动，只有同种类、同点数且未达六点的骰子能合成。点击骰子查看与回收，点击空位可定点召唤。Space 召唤、Esc 暂停、F11 切换显示模式；按键可在设置中改绑。

城镇单击地块选择施工位置；长按空白位置瞄准并松手派遣，或使用派遣按钮。存在未结束的远征时禁止同时施工/开新局；可继续、保存离开或明确撤回结算。

设置包括显示模式、分辨率、垂直同步、帧率上限、UI 字体比例、主音量/音效/音乐、震屏/闪光/瞄准线、动作键改绑、存档目录、备份及二次确认重置。显示模式改变有 15 秒确认回退。默认兼容渲染器不支持的 MSAA 不会伪装成可生效选项。

## 实现入口

| 功能 | 主要文件 |
|---|---|
| 内容与开放关系 | `Data/campaign.json` / `Scripts/Core/CampaignCatalog.cs` |
| 区域、建筑、持久化模型 | `Scripts/Core/CampaignModels.cs` |
| 齿轮、结算、解锁、建设事务 | `Scripts/Core/CampaignProgression.cs` |
| 城镇弹射采集与施工 | `Scripts/Core/TownSimulation.cs` |
| 原战斗与有限区域接入 | `Scripts/Core/Simulation.cs` |
| 流程与存档事务 | `Scripts/App/GameApp.Campaign.cs` |
| 原生桌面界面 | `Scenes/UI/*` / `Scripts/UI/*` |
| 引擎、音频、输入、设置连接 | `Scripts/App/GameRoot.cs` |
| 原效果绘制适配 | `Scripts/Presentation/*` |

UI 是 Godot 原生 Control、PanelContainer、Button、Label、ProgressBar、TextureRect 和容器节点；棋盘格有独立 `.tscn` 模板，方便换美术。弹丸、敌人、碰撞、粒子继续由原模拟及绘制层管理，不改成每颗弹丸一个物理刚体。旧窄画布菜单仅保留在显式 `--capture-reference` 工具入口，不是正常游戏入口。

## 存档与兼容

仍使用 `user://dice_ricochet_save_v1.json` 文件名，内部版本升级为 3。原版合法存档保留已拥有骰子、主骰顺序、记录与未完成战斗；不足六种的旧卡组补齐基础骰子，棋盘原位扩展为 24 格，不把旧玩家重新锁成新玩家。旧无尽战斗保留原无尽波次规则，骰子采用新的统一平衡；旧高点骰子首次继续时补选分支而不赠送合成齐射，不凭空补发区域通关。永久状态与当局状态分开存储；新局持有区域/成长快照，改区域、成长和技能配置不会悄悄改变已经开始的局；基础伤害与召唤平衡为版本级规则，升级版本后统一生效。

新局、建设和奖励先写入磁盘再发布结果。结算使用远征标识和递增序号，重启或重复点击不能重复领奖。损坏存档不会自动覆盖成新档；原始文件可导出备份。手动重置必须确认并先做校验备份。

## 验证

原战斗测试 27 组（含 179 个回放检查点、72 组属性与原音效样本）；循环测试 45 组，技能/棋盘测试 55 组。`--capture-campaign` 在隔离内存存档中执行原生鼠标/键盘交互检查并生成十四个实际界面截图（含 130% 字号检查）。运行该开关不会读取/修改玩家存档。

```sh
godot --path . -- --capture-campaign
python Tools/static_check.py
```

详细实现、配置、测试边界见 `Docs/CAMPAIGN.md`、`Docs/VALIDATION.md`。自动化通过不是人工数值平衡、声音听感或 Windows 显卡实测结论。原美术/测试夹具保留原字节；工程不分发任何系统字体。
