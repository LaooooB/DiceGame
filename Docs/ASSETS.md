# 美术与声音来源

所有游戏美术来自本次对话提供的原版 `DiceGame-project.zip` 中 `renderer.js` 的程序绘制。没有另行下载素材，没有引入第三方图库许可。

`Assets/Reference/manifest.json` 记录原渲染器 SHA-256 和图集排布；`Tools/extract_reference_assets.py` 可重新生成 PNG。图集保留了原骰子颜色、斜角、高光、阴影和点数，不是依照截图重画。

背景 864×1728；图标图集 480×384；骰子图集采用 3 倍采样；六种光晕均为 48×48。动态火花、电弧、爆炸环、瞄准线、连锁数字和震屏没有烘焙成视频或预录动画，仍按实时战斗事件产生。

文字使用目标电脑的系统字体，通过 Godot SystemFont 读取。工程不包含、导出或分发任何字体文件。系统字体和原浏览器的字形选择仍需目标系统验收。

音效来自原版 `audio.js` 数学合成，移植到 `SoundSynth.cs`，不是新的音效包。`Tests/Fixtures/audio_reference.f32` 是原版函数实际输出的 670,677 个单精度参考样本，仅用于测试，不进入发行包。

`Tests/ReferenceScreenshots` 明确是原版 Chromium 截图，不能当作 Godot 实机截图。
