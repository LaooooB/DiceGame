# 当前架构

当前权威说明见 [CAMPAIGN.md](CAMPAIGN.md) 和 [COMBAT_UPGRADE.md](COMBAT_UPGRADE.md)。`Docs/History` 保留 ZIP 迁移时期的旧说明，仅作历史参考，不作为当前功能或未完成项。

Core 不依赖 Godot。`GameApp` 是纯 C# 应用层；`GameRoot` 是 Godot 引擎连接。`CampaignUi` 原生 Control 消费应用状态并发出命令。`CampaignProgression` 是永久成长与结算权威，`TownSimulation` 是城镇弹射，`Simulation` 是当局战斗权威。

新增局外系统不重排原碰撞/连锁/随机调用。显示缩放不改战斗坐标。旧 `UiLayout` 仅由历史对照入口使用，正常原生界面走 Control 命中。

骰子分支状态在 Core 中，选择写盘事务在 GameApp.Skills，技能目录的本局快照不会读取 UI 状态。旧黄金回放固定使用 Tests/LegacyBalanceData，不能拿旧数值回放通过证明当前新数值已完成人工平衡。
