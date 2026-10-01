# Changelog

## 1.4.0 - 2026-10-01

### Added

- GitHub 官方最新稳定版查询。
- 最新稳定版名称、标签和发布页链接显示。
- “填入此稳定版”按钮，并复用便携版本持久化。
- 四区块视觉布局、较大字号、分层字体和自绘立体按钮。
- `--check-latest-stable` 安全检查命令。
- `--render-ui-screenshot` 离屏界面回归测试命令。
- 标准仓库结构和 GitHub Actions Windows 构建工作流。

### Retained

- 本机 Hermes 只读版本检测。
- 便携 `HermesProxyBridge.ini`。
- 指定提交/标签安装和 V2rayN 代理继承。
- 预览、清空日志、Desktop 代理快捷方式。

## 1.3.0 - 2026-10-01

- 修复版本选择不能随文件夹迁移的问题。
- 标签先解析为精确提交，安装脚本与目标提交保持一致。
- Bridge 自身下载改用系统 `curl.exe`。
- 版本检测不再执行 Hermes 本身。
- 增加纯预览、清空日志和输入校验。
