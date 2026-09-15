# 当前架构

当前权威说明见 [CAMPAIGN.md](CAMPAIGN.md)。`Docs/History` 保留 ZIP 迁移时期的旧说明，仅作历史参考，不作为当前功能或未完成项。

Core 不依赖 Godot。`GameApp` 是纯 C# 应用层；`GameRoot` 是 Godot 引擎连接。`CampaignUi` 原生 Control 消费应用状态并发出命令。`CampaignProgression` 是永久成长与结算权威，`TownSimulation` 是城镇弹射，`Simulation` 是当局战斗权威。

新增局外系统不重排原碰撞/连锁/随机调用。显示缩放不改战斗坐标。旧 `UiLayout` 仅由历史对照入口使用，正常原生界面走 Control 命中。
