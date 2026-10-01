# Changelog

## 1.5.0 - 2026-10-01

### Fixed

- 修复指定提交安装在新克隆后可能因 Git `core.autocrlf=true` 产生工作区改动，导致精确提交 checkout 失败的问题。
- 使用 Bridge 专属 `GIT_CONFIG_GLOBAL` 文件启用 `autocrlf=false`、`longpaths=true` 和 `windows.appendAtomically=false`，不读取或改写用户的全局 Git 配置。
- 指定版本安装改用官方 `install.ps1` 阶段协议和 `-NonInteractive`，消除隐藏提示等待与无反馈黑框。
- 安装完成后核验 Hermes CLI、Desktop 可执行文件和本机版本；旧终端 PATH 未刷新的情况给出明确提示。
- 窗口改为 DPI 自适应分页布局，并设置安全最小尺寸，修复缩放时按钮、文字框互相覆盖。

### Added

- 顶部“代理与常用操作 / 指定版本安装”两个选项卡，每页底部都有运行日志。
- 当前安装阶段、阶段计数和进度条。
- 可展开的右侧“安装进度细节”面板，使用较小等宽字体实时显示 stdout/stderr。
- 每次指定版本安装生成独立 UTF-8 `install-*.txt`，另有 Bridge 会话日志。
- 日志文本支持选择、`Ctrl+C`、右键复制/全选、一键复制全部和打开日志位置。

### Verified

- 在独立 `HERMES_HOME` / `InstallDir` 中完成 commit `639823919ced15640924e905bacab3858c5afd19` 的 16 个阶段安装。
- 实际生成并运行 Hermes CLI v0.21.3，构建出 214,007,808 字节的 Desktop `Hermes.exe`，且当前已安装 Hermes 仓库的用户改动保持不变。

## 1.4.1 - 2026-10-01

### Fixed

- 移除在高 DPI/显示缩放下会越过按钮边框的自绘渐变、阴影和按压位移。
- 所有操作按钮改用 Windows 原生浅灰按钮绘制，避免缩放残影和色块溢出。
- 保留统一字号、间距、分区布局和状态文字颜色，不改变任何代理或安装逻辑。

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
