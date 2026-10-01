# Security

## 安全边界

Hermes Proxy Bridge：

- 不修改 V2rayN 配置；
- 不修改 WinINet、WinHTTP 或系统服务代理；
- 不安装驱动、不注入 DLL；
- 不给已经运行的进程事后注入；
- 只给它启动的安装、更新和 Desktop 进程树设置代理环境变量；
- 查询最新版时只接受 `https://github.com/NousResearch/hermes-agent/releases/...` 发布页；
- 安装脚本只从 `raw.githubusercontent.com/NousResearch/hermes-agent/<commit>/scripts/install.ps1` 获取，并检查必要参数。

## 凭据

程序不读取、不存储 GitHub Token、Hermes API Key、模型密钥或 V2rayN 凭据。GitHub 公共 API 采用匿名请求，受 GitHub 匿名速率限制。

## 二进制签名

仓库附带的社区构建未做 Authenticode 签名。请用 `releases/v1.4.0/SHA256SUMS.txt` 核对文件，或从源码自行构建。

## 报告问题

公开发布仓库后，请通过仓库 Issue 报告普通缺陷。涉及敏感安全细节时，应使用仓库所有者提供的私密安全报告渠道；本地副本本身没有配置外部收件地址。
