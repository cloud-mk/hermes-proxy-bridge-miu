# Hermes Proxy Bridge v1.5.1

## 本版重点

- 修复 Electron 下载器未继承 V2rayN 代理，导致 Desktop 阶段 `socket hang up` 后卡住的问题。
- 连续 300 秒没有任何子进程输出时，自动结束整个安装进程树并在日志中写明原因；单阶段上限 45 分钟。
- 未启动 V2rayN 时给出明确弹窗和大字号红色警告，不再无说明退出。
- V2rayN 可用时显示加粗绿色状态条。
- 代理状态移到功能区顶部；主窗体改为窄外边距，页签和内容使用同一平面，不再有割裂的横线。

## 安装

解压 `HermesProxyBridge-v1.5.1-windows.zip`，运行 `HermesProxyBridge.exe`。版本选择需要跨电脑携带时，请同时复制 EXE 旁首次保存后生成的 `HermesProxyBridge.ini`。

程序未签名，Windows SmartScreen 可能显示“未知发布者”。

## 日志说明

原日志中的 npm advisory endpoint retirement 是 npm 的提示，不是“过期下载地址”。实际失败发生在 Electron 的 `install.js`，其下载器需要额外的代理开关。本版已经补齐该开关。

安装日志位于 `%LOCALAPPDATA%\HermesProxyBridge\logs\install-*.txt`。
