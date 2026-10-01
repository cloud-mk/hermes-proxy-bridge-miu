# Hermes Proxy Bridge

Windows 10/11 上的 Hermes 安装、更新和 Desktop 专用代理启动器。它只向自己启动的进程树注入代理环境变量，不修改 V2rayN 配置、Windows 系统代理或 WinHTTP。

当前仓库版本：**v1.5.1**

![Hermes Proxy Bridge v1.5.1 界面](docs/ui-v1.5.1.png)

## 主要功能

- 自动读取 V2rayN 写入的当前用户 Windows 代理，也可手动填写 HTTP/mixed 代理地址。
- 通过代理启动官方 `Hermes-Setup.exe`、`hermes update` 和 Hermes Desktop。
- 输入 7～40 位 Git 提交号、Hermes 发布标签或显式分支，安装指定版本。
- 只读检测本机 Hermes 的源码版本、当前提交、分支、精确标签、安装标记和未提交改动。
- 版本选择保存在 EXE 同目录的 `HermesProxyBridge.ini`，可随整个文件夹复制到另一台电脑。
- 查询 GitHub 官方 `/releases/latest`：
  - 显示最新稳定版名称和标签；
  - 排除 `draft` 和 `prerelease`；
  - 可打开官方发布页；
  - 一键把稳定版标签填入安装版本框并持久化。
- 标签/短提交通过代理解析成 40 位 SHA；下载该提交自己携带的官方 `scripts/install.ps1`，校验关键参数后再执行。
- 指定版本安装使用官方脚本的阶段协议，逐阶段显示“当前安装内容”、进度条和退出结果，不再依赖外部 CMD 黑框判断状态。
- “安装细节”可在右侧展开，实时显示 PowerShell、Git、uv、pip、npm 和 Desktop 构建的 stdout/stderr；完整 UTF-8 日志另存为 `%LOCALAPPDATA%\HermesProxyBridge\logs\install-*.txt`。
- 安装脚本使用 Bridge 专属 Git 配置（`autocrlf=false`、`longpaths=true`），避免精确提交在新克隆后因换行或长路径失败，同时不修改用户全局 Git 配置。
- 为 Electron 的 `@electron/get` 下载器显式设置代理开关，避免 npm 走代理、Electron 二进制却直连而出现 `socket hang up`。
- 每个安装阶段连续 300 秒无输出会自动终止整个子进程树并写明超时原因；单阶段最长 45 分钟。
- 未检测到 V2rayN 时以大字号红色状态条和弹窗提示“没有启动代理”，不会无说明退出。
- 两个功能页都保留可选择、可 `Ctrl+C`、可一键复制的运行日志。

## 使用

1. 启动 V2rayN，并开启系统代理；也可以在 Bridge 中填写 V2rayN 的 HTTP/mixed 地址，例如 `http://127.0.0.1:10808`。
2. 打开发布目录中的 `HermesProxyBridge.exe`。
3. 开启“启用 Hermes 专用代理”，确认界面显示本地端口可连接。
4. 安装指定版本时：
   - 点击“检测本机版本”，复制当前电脑的精确提交；或
   - 点击“核对官网最新稳定版”，再点击“填入此稳定版”；或
   - 手动输入提交号/标签。
5. 点击“按指定版本安装（走代理）”。执行前仍会显示确认窗口。
6. 安装时可点“安装细节”展开右侧输出；失败时细节会自动展开，并提示完整日志文件位置。

“选择并启动官方安装器”不会读取版本框，它仍按官方安装器的默认逻辑安装。版本框只作用于“按指定版本安装（走代理）”。

## 跨电脑携带版本选择

检测或输入版本后，程序会在 EXE 旁生成 `HermesProxyBridge.ini`。复制时请复制整个程序文件夹，至少同时包含：

- `HermesProxyBridge.exe`
- `HermesProxyBridge.ini`

代理开关、手动代理地址和上次安装器路径属于机器专属设置，保存在 `%LOCALAPPDATA%\HermesProxyBridge\settings.ini`，不会跟随便携版本文件迁移。

## 构建

无需 Visual Studio、NuGet 或联网。使用 Windows 自带的 .NET Framework C# 编译器：

```powershell
.\build.ps1
```

输出文件：`artifacts\HermesProxyBridge.exe`

## 安全预检

以下命令不会执行 Hermes 安装或更新：

```powershell
.\artifacts\HermesProxyBridge.exe --self-test
.\artifacts\HermesProxyBridge.exe --self-test-timeout
.\artifacts\HermesProxyBridge.exe --detect-version
.\artifacts\HermesProxyBridge.exe --check-latest-stable
.\artifacts\HermesProxyBridge.exe --print-install-command "v2026.9.24"
.\artifacts\HermesProxyBridge.exe --verify-install-source "v2026.9.24"
```

`--verify-install-source` 会解析版本、下载并校验官方脚本，但不会执行该脚本。

详细测试边界见 [docs/TESTING.md](docs/TESTING.md)，代理及安全模型见 [SECURITY.md](SECURITY.md)。

## 已知限制

- Bridge 自身查询 GitHub 和下载脚本需要 HTTP 或 mixed 代理端口；纯 SOCKS 地址不适用于这两步。
- 精确提交必须属于官方安装脚本所选的 `main` 分支，否则官方脚本会安全拒绝。
- 很老的 Hermes 提交若没有兼容的 `-Commit`、`-Branch` 或 `-IncludeDesktop` 参数，会被 Bridge 拒绝。
- 安装旧版本后再执行 `hermes update` 或 Desktop 内更新，可能升级离开旧提交；“指定版本安装”不是永久版本锁。
- 程序未做代码签名，Windows SmartScreen 可能显示“未知发布者”。

## 仓库结构

```text
.
├── .github/workflows/build.yml
├── docs/
│   ├── AUDIT-v1.4.md
│   ├── AUDIT-v1.5.md
│   ├── AUDIT-v1.5.1.md
│   └── TESTING.md
├── releases/v1.4.0/
├── releases/v1.4.1/
├── releases/v1.5.0/
├── releases/v1.5.1/
├── src/HermesProxyBridge.cs
├── .gitignore
├── CHANGELOG.md
├── CONTRIBUTING.md
├── README.md
├── SECURITY.md
└── build.ps1
```
