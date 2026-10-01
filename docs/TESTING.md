# Testing

## v1.5.2 代理开关语义与文案

- 代理可用但 Hermes 专用代理关闭：黄色状态条显示已找到 V2rayN，但尚未给 Hermes 安装/更新使用；操作按钮禁用。
- Hermes 专用代理开启：绿色状态条显示已启动，按钮变为“关闭 Hermes 专用代理”，操作按钮启用。
- 指定版本页已移除命令预览入口，只保留“安装上述指定版本”。
- 创建代理快捷方式入口和实现均已移除。
- 离屏渲染五种状态并检查按钮文字、边界和分区布局；Windows Computer Use 在当前会话未返回可绑定的原生窗口，因此没有把离屏截图误写成真实窗口自动化结论。

## v1.5.1 Electron 代理、超时与界面回归

- 从用户安装日志定位到 Desktop 阶段：`npm ci` 在 Electron `install.js` 中报 `RequestError: socket hang up`，之后官方脚本重试 `npm install` 并长期无输出。
- 核对该版本 `@electron/get` 源码，确认只有存在 `ELECTRON_GET_USE_PROXY` 时才初始化代理；日志中的 npm advisory endpoint retirement 只是提示，不是失效下载地址。
- 内置自检确认 `ELECTRON_GET_USE_PROXY=1`、`GLOBAL_AGENT_HTTP_PROXY`、`GLOBAL_AGENT_HTTPS_PROXY` 和 `HTTPS_PROXY` 被注入。
- `--self-test-timeout` 启动一个只等待、不联网、不安装的 PowerShell 子进程，以 2 秒阈值验证超时；进程树被终止，退出码为 0，测试后无残留进程。
- 离屏渲染主页面和指定版本页面，检查绿色状态条、平面式页签、窄外边距和缩放布局。

## v1.5.0 分阶段安装与图形进度

- 编译及内置自检通过。
- 通过离屏截图检查三个状态：主页面、指定版本页面、右侧安装细节展开页面。
- 在独立 `HERMES_HOME` / `InstallDir` 中实际执行目标提交 `639823919ced15640924e905bacab3858c5afd19` 的全部 16 个官方阶段。
- 首次测试复现 `repository` 失败；确认原因为长路径和 Git 换行设置，随后加入 Bridge 专属 Git 配置。
- 使用与 Bridge 相同的 `autocrlf=false` / `longpaths=true` 隔离配置重跑后，16 个阶段全部成功。
- 独立 CLI `--version` 退出码 0；Desktop `Hermes.exe` 和 bootstrap marker 均存在。
- 测试前后真实 Hermes 的已有源码修改仍存在；测试中被第三方依赖安装器追加的用户 PATH 已恢复到测试前内容。

详细记录见 [AUDIT-v1.5.md](AUDIT-v1.5.md)。

## v1.4.1 高 DPI 按钮修复

- 删除全部按钮自绘代码，不再按固定像素绘制渐变、阴影或按下偏移。
- 所有按钮交给 Windows Forms 原生 `FlatStyle.Standard` 和系统主题绘制。
- 使用离屏截图重新检查按钮边界、禁用状态、文字完整性和区块布局。
- 代理、最新版查询及内置自检重新通过。

## v1.4 已执行的安全验证

测试日期：2026-10-01（Asia/Shanghai）

- 源码编译成功。
- 内置自检通过：代理解析、版本输入归一化、精确提交命令构造。
- `--check-latest-stable` 通过 `http://127.0.0.1:10808` 成功查询 GitHub 官方 API。
- 返回：
  - 名称：`Hermes Agent v0.21.5 (v2026.9.24)`
  - 标签：`v2026.9.24`
  - 发布页：`https://github.com/NousResearch/hermes-agent/releases/tag/v2026.9.24`
  - 发布时间：`2026-09-24T10:09:38Z`
- 网络监测捕获到 Bridge 的 `curl.exe` 子进程连接 `127.0.0.1:10808`。
- 测试前后，本机已安装 Hermes 的 HEAD 与 `git status --porcelain` 文本完全一致。
- 离屏渲染 PNG 已检查：区块、字号、按钮文字、日志框和高低层级均正常。

## v1.4 当时明确未执行

没有执行 Hermes 全量安装、`hermes update` 或 Desktop 启动，因为这些操作会改变现有 Hermes。验证边界停在：

`代理检测 → GitHub 稳定版查询 → 标签/提交解析 → 官方脚本下载与校验 → 最终命令构造`

## 可重复命令

```powershell
.\build.ps1
$exe = '.\artifacts\HermesProxyBridge.exe'
Start-Process $exe -ArgumentList '--self-test' -Wait -PassThru
Start-Process $exe -ArgumentList '--self-test-timeout' -Wait -PassThru
Start-Process $exe -ArgumentList '--check-latest-stable' -Wait -PassThru
Start-Process $exe -ArgumentList @('--render-ui-screenshot', '.\test-results\ui.png') -Wait -PassThru
```
