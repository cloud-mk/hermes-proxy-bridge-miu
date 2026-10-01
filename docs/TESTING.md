# Testing

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

## 明确未执行

没有执行 Hermes 全量安装、`hermes update` 或 Desktop 启动，因为这些操作会改变现有 Hermes。验证边界停在：

`代理检测 → GitHub 稳定版查询 → 标签/提交解析 → 官方脚本下载与校验 → 最终命令构造`

## 可重复命令

```powershell
.\build.ps1
$exe = '.\artifacts\HermesProxyBridge.exe'
Start-Process $exe -ArgumentList '--self-test' -Wait -PassThru
Start-Process $exe -ArgumentList '--check-latest-stable' -Wait -PassThru
Start-Process $exe -ArgumentList @('--render-ui-screenshot', '.\test-results\ui.png') -Wait -PassThru
```
