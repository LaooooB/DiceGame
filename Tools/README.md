# 工具说明

正常打开、运行、修改游戏不需要 Python、Node、Playwright。

- `verify.ps1`：在本机依次执行纯 C# 对照测试、Godot 工程编译、资源导入、启动检查；加 `-Capture` 会真实打开 Godot 窗口，导出原生截图。不会访问或写入 GitHub。
- `compare_screenshots.py`：比较原版浏览器截图与 Godot 原生截图，输出叠图、差异图、统计；不设置一个宽松阈值冒充验收通过。
- `generate_reference_fixtures.cjs`：直接运行归档的原版 JS，重新生成逻辑、特效和声音的参考数据。
- `capture_original.py`：用原版 JS 的渲染器重现九个固定场景。Windows 上可以重新生成，以使用相同系统字体。
- `extract_reference_assets.py`：重新导出原版程序绘制的骰子、图标、光晕和背景。正常运行不需要执行，PNG 已包含。

生成工具默认从 `Tests/Original` 读取原版，未读取或替换新的 C# 实现。截图工具需要 `playwright` 及 Chromium；可用 `CHROMIUM_PATH` 指向已有 Chrome/Chromium。比较工具需要 `Pillow` 和 `numpy`。这些是开发工具依赖，不是游戏运行依赖。
