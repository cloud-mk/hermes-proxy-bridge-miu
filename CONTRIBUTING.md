# Contributing

1. 在 Windows 10/11 上修改 `src/HermesProxyBridge.cs`。
2. 运行 `build.ps1`。
3. 运行 `HermesProxyBridge.exe --self-test`。
4. 涉及网络逻辑时，使用隔离的 `HERMES_PROXY_BRIDGE_HOME` 并运行相应安全预检，不要在开发验证中执行真实 Hermes 安装或更新。
5. 提交时不要加入 `artifacts/`、`test-results/`、本机 `settings.ini` 或便携选择文件。

界面改动应同时运行 `--render-ui-screenshot <png路径>`，检查高 DPI 下的文字截断、控件重叠和按钮状态。
