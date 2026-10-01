# v1.4 Change Audit

## 最新稳定版

实现使用 GitHub 官方 `GET /repos/NousResearch/hermes-agent/releases/latest`。除依赖 API 的 latest 语义外，程序还会本地拒绝：

- `draft: true`
- `prerelease: true`
- 非 `vYYYY.M.D` 风格标签
- 非 HTTPS 或非 `github.com` 的发布页链接

查询使用与安装相同的代理检测逻辑。代理开启时，Bridge 自己的 `curl.exe` 子进程同时接收显式 `--proxy` 和代理环境变量。

## 自动填入

只有成功核对后，“填入此稳定版”按钮才启用。填入的是 `tag_name`，例如 `v2026.9.24`；版本框原有的 `TextChanged` 持久化逻辑会把它写入 EXE 同目录 `HermesProxyBridge.ini`。检测本机版本及其持久化行为未被移除。

## UI

- 代理状态、常用操作、指定版本安装、运行日志分为四个 GroupBox。
- 主界面采用 10pt 基础字体，标题 18pt，区块标题 11pt，按钮 10pt 粗体，说明 9～9.5pt，日志为 9.5pt Consolas。
- 自绘按钮包含上下渐变、描边、阴影和按下位移；禁用状态为灰色。
- 窗口和主要输入、状态、日志控件支持水平缩放锚定。

## 安全性

新增稳定版功能不运行 Hermes、不写 Hermes 目录，也不自动开始安装。它只下载 GitHub JSON；用户仍需显式点击“填入此稳定版”和“按指定版本安装”，后者还有原有确认窗口。
