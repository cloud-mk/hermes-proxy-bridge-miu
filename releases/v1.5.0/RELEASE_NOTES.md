# Hermes Proxy Bridge v1.5.0

## 本版重点

- 修复指定旧提交在全新安装时可能停在 `hermes-agent` 目录之后的问题。
- 指定版本安装不再使用不可观察的 CMD/PowerShell 黑框，改为在 Bridge 内逐阶段执行。
- 显示当前阶段、阶段计数和进度条。
- “安装细节”按钮可在右侧展开实时 stdout/stderr，失败时自动展开。
- 每次安装生成独立 UTF-8 文本日志：`%LOCALAPPDATA%\HermesProxyBridge\logs\install-*.txt`。
- 界面改为两个选项卡，两个页面底部都有可复制运行日志。
- 使用自适应布局和安全最小尺寸，修复窗口缩放后的控件重叠。
- Git 使用 Bridge 专属隔离配置，避免 `autocrlf` 和长路径导致精确 checkout 失败，不修改用户全局 Git 配置。

## 安装

解压 `HermesProxyBridge-v1.5.0-windows.zip`，运行 `HermesProxyBridge.exe`。版本选择需要跨电脑携带时，请同时复制 EXE 旁首次保存后生成的 `HermesProxyBridge.ini`。

程序未签名，Windows SmartScreen 可能显示“未知发布者”。

## 验证

已在独立目录完成 commit `639823919ced15640924e905bacab3858c5afd19` 的 16 阶段安装，直接运行 CLI 成功，并构建出 Desktop `Hermes.exe`。详细记录见仓库 `docs/AUDIT-v1.5.md`。

