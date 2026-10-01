# Hermes Proxy Bridge v1.5.1 修复审计

日期：2026-10-01（Asia/Shanghai）

## 用户日志结论

`install-20261001-205629.txt` 已经进入 Desktop 阶段。`npm ci` 随后在 `apps\desktop\node_modules\electron` 的 `install.js` 中报出 `RequestError: socket hang up`；官方安装脚本接着尝试 `npm install`，之后长期没有新输出。

日志里“advisory endpoint is being retired”是 npm 安全审计接口的迁移提示，不是 Hermes 或 Electron 使用了过期下载地址，也不是本次停止的直接原因。

## 根因

Hermes Desktop 使用 Electron。该依赖版本的 `@electron/get` 只有检测到 `ELECTRON_GET_USE_PROXY` 时才初始化代理器。旧版 Bridge 已注入 `HTTP_PROXY`、`HTTPS_PROXY` 和 npm 代理变量，因此 npm 包请求可走代理；但没有设置 Electron 的额外开关，Electron 二进制下载仍可能直连。

## 修改

- 增加 `ELECTRON_GET_USE_PROXY=1`。
- 同时设置 `GLOBAL_AGENT_HTTP_PROXY` 和 `GLOBAL_AGENT_HTTPS_PROXY`。
- 每个阶段连续 300 秒无 stdout/stderr 时终止整个进程树；每分钟写入等待心跳，单阶段最长 45 分钟。
- 未检测到 V2rayN 时显示红色大字号警告并弹窗提示；快捷方式入口改为打开 Bridge，不再直接退出。
- 可用状态改为加粗绿色状态条并移到“代理状态”区顶部。
- 页签改为无分隔线的同平面布局，缩小主界面外边距。

## 安全验证

- `--self-test`：代理解析、版本输入、精确提交命令和 Electron 代理环境全部通过。
- `--self-test-timeout`：仅启动 `Start-Sleep`，不联网、不安装；2 秒后成功终止进程树，无残留进程。
- 离屏渲染三种界面：V2rayN 可用、不可用红色警告、指定版本页。

本次没有再次执行 Hermes 全量安装，避免改动当前机器上已安装的 Hermes；验证范围覆盖导致本次故障的代理环境分支、超时终止机制和界面状态。
