# v1.5.0 审计与隔离安装验证

日期：2026-10-01

## 复现到的故障

v1.4.1 只启动一个不可观察的 PowerShell 安装进程，不捕获 stdout、stderr 或退出码。隔离测试在 `repository` 阶段复现了两类具体失败：

1. 安装根目录过深时，Git checkout 因 Windows 长路径失败，官方 ZIP 回退随后也失败。
2. 使用正常长度目录、从 `main` 克隆后再切换到指定旧提交时，用户 Git 配置中的 `core.autocrlf=true` 会使新工作区立即出现换行改动；`git checkout <commit>` 因“本地修改将被覆盖”退出 1。

这两种失败都发生在已经创建 `bin`、`git`、`hermes-agent`、`node` 等目录之后，与报告现象一致。旧版没有捕获输出，因此界面只表现为停止。

## 修复

- 使用目标提交自带 `install.ps1` 的 `-Manifest` / `-Stage` / `-Json` / `-NonInteractive` 协议逐阶段安装。
- 每一阶段单独记录名称、进度、PID、stdout、stderr 和退出码；失败后停止，不再继续伪装完成。
- 精确提交支持时追加 `-ForceCommit`，保证“指定版本”不会被官方脚本的防回退逻辑静默忽略。
- 为安装子进程设置 Bridge 专属 `GIT_CONFIG_GLOBAL`：
  - `core.autocrlf=false`
  - `core.longpaths=true`
  - `windows.appendAtomically=false`
- 该配置文件位于 `%LOCALAPPDATA%\HermesProxyBridge\installer-gitconfig`。官方脚本执行 `git config --global` 时也只会写这个隔离文件，不会改动用户自己的全局 Git 配置。
- 安装结束后检查 CLI、Desktop 可执行文件和本机版本，缺少任一核心产物都按失败处理。

## 隔离全流程结果

测试目标：commit `639823919ced15640924e905bacab3858c5afd19`。

为避免覆盖现有 Hermes，测试使用独立 `HERMES_HOME` 和 `InstallDir`；测试脚本副本仅屏蔽用户 PATH/HERMES_HOME 写入以及 Desktop/开始菜单快捷方式创建，安装代码、依赖和 Desktop 构建逻辑保持不变。

16 个官方阶段全部返回成功：

`uv → git → node → system-packages → repository → python → venv → dependencies → node-deps → desktop → path → config-templates → platform-sdks → bootstrap-marker → configure → gateway`

产物核验：

- Git HEAD：`639823919ced15640924e905bacab3858c5afd19`
- CLI：`Hermes Agent v0.21.3 (2026.9.14)`，独立路径直接运行退出码 0
- Python：3.11.16
- Desktop：`apps\desktop\release\win-unpacked\Hermes.exe`
- Desktop 大小：214,007,808 字节
- bootstrap marker：已生成
- 当前真实 Hermes 仓库仍位于原提交，已有 `agent/process_bootstrap.py` 修改仍保留

测试过程发现第三方 `cua-driver` 安装器和 uv 安装器仍可能尝试更新用户 PATH。测试前已保存用户 PATH，测试后恢复为原值。正式安装时这些属于官方 Hermes 依赖阶段自身的行为，Bridge 会把对应输出完整记录到安装日志。

完整测试输出保存在 `test-results/isolated-install-short2-v1.5.log`（仓库工作副本，不放入二进制发布 ZIP）。

## 界面验证

- 主窗口和指定版本页均通过离屏渲染检查。
- 指定版本页右侧细节面板的展开状态单独渲染检查。
- 最小窗口宽度被限制在布局安全范围；内部使用表格/流式布局，不再依赖会随 DPI 冲突的固定绝对坐标。
- 所有操作按钮使用 Windows 原生浅灰样式。

