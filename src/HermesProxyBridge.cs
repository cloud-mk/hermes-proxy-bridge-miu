// ============================================================================
//  Hermes Proxy Bridge v1.4
//  ---------------------------------------------------------------------------
//  基于 v1.1 原版源码（HermesProxyBridge.cs，484 行，未改动原文件）新增：
//    ① 「选择安装版本」输入框：可填提交号或版本标签(如 v2026.9.24)。
//       版本单独写进程序旁的 HermesProxyBridge.ini，复制整个文件夹即可带到新机。
//    ② 「检测本机版本」：只读 Git/安装完成标记，不运行 hermes，可复制/自动填入。
//       —— 老机上点一下 → 关掉 → 拷到新机 → 打开 → 直接按指定版本安装。
//    ③ 「按指定版本安装（走代理）」：下载目标提交自带的官方 install.ps1，
//       版本标签会先解析成 40 位提交号，再带 -Branch main -Commit 参数，
//       并沿用 v1.1 的代理注入（12 个变量）；原「选择并启动安装程序」等按钮全部保留。
//    ④ 查询 GitHub 最新稳定 Release，可查看发布页并一键填入版本框。
//    ⑤ 「预览命令（不执行）」：只构造命令，不联网、不写文件；日志可一键清空。
//  编译：见同目录 build.ps1（用 Windows 自带 .NET Framework 编译器，无需联网）
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Hermes Proxy Bridge")]
[assembly: AssemblyDescription("Route Hermes install, update and Desktop process trees through the active V2rayN proxy.")]
[assembly: AssemblyCompany("Community build")]
[assembly: AssemblyProduct("Hermes Proxy Bridge")]
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]
[assembly: AssemblyInformationalVersion("1.4.0")]

namespace HermesProxyBridge
{
    internal sealed class Settings
    {
        public bool Enabled;
        public string ManualProxy = "";
        public string LastInstaller = "";
        // 版本号单独保存在程序旁，避免把机器专属代理配置带到另一台电脑。
        public string InstallVersion = "";
        public string SaveWarning = "";

        public static string DirectoryPath
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("HERMES_PROXY_BRIDGE_HOME");
                if (!string.IsNullOrWhiteSpace(custom)) return Path.GetFullPath(custom.Trim());
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HermesProxyBridge");
            }
        }

        public static string FilePath { get { return Path.Combine(DirectoryPath, "settings.ini"); } }
        public static string PortableFilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "HermesProxyBridge.ini"); }
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        string key, value;
                        if (!TryParseLine(line, out key, out value)) continue;
                        if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase)) s.Enabled = value == "1";
                        else if (key.Equals("ManualProxy", StringComparison.OrdinalIgnoreCase)) s.ManualProxy = value;
                        else if (key.Equals("LastInstaller", StringComparison.OrdinalIgnoreCase)) s.LastInstaller = value;
                        // 兼容 v1.2：便携文件不存在时，从旧位置迁移版本。
                        else if (key.Equals("InstallVersion", StringComparison.OrdinalIgnoreCase)) s.InstallVersion = value;
                    }
                }
                if (File.Exists(PortableFilePath))
                {
                    foreach (string line in File.ReadAllLines(PortableFilePath, Encoding.UTF8))
                    {
                        string key, value;
                        if (TryParseLine(line, out key, out value) && key.Equals("InstallVersion", StringComparison.OrdinalIgnoreCase))
                            s.InstallVersion = value;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            SaveWarning = "";
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllLines(FilePath, new[] {
                "Enabled=" + (Enabled ? "1" : "0"),
                "ManualProxy=" + OneLine(ManualProxy),
                "LastInstaller=" + OneLine(LastInstaller)
            }, new UTF8Encoding(false));
            try
            {
                File.WriteAllLines(PortableFilePath, new[] {
                    "; Hermes Proxy Bridge portable selection - copy this file with the EXE",
                    "InstallVersion=" + OneLine(InstallVersion)
                }, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                // 只把版本号回退保存到本机；界面必须明确提示它不会随文件夹迁移。
                File.AppendAllLines(FilePath, new[] { "InstallVersion=" + OneLine(InstallVersion) }, new UTF8Encoding(false));
                SaveWarning = "无法写入程序旁的便携配置，版本号只保存在本机：" + ex.Message;
            }
        }

        private static bool TryParseLine(string line, out string key, out string value)
        {
            key = ""; value = "";
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith(";")) return false;
            int p = line.IndexOf('=');
            if (p <= 0) return false;
            key = line.Substring(0, p).Trim();
            value = line.Substring(p + 1).Trim();
            return true;
        }

        private static string OneLine(string value)
        {
            return (value ?? "").Replace("\r", "").Replace("\n", "").Trim();
        }
    }

    internal sealed class DetectionResult
    {
        public bool V2rayNRunning;
        public bool PortOpen;
        public string ProxyUrl = "";
        public string Detail = "";
    }

    internal static class ProxyDetector
    {
        private const string InternetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        public static DetectionResult Detect(string manual)
        {
            DetectionResult r = new DetectionResult();
            r.V2rayNRunning = IsV2rayNRunning();

            if (!string.IsNullOrWhiteSpace(manual))
            {
                r.ProxyUrl = NormalizeProxy(manual);
                r.Detail = "使用手动代理地址";
            }
            else
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(InternetSettings))
                    {
                        int enabled = Convert.ToInt32(key == null ? 0 : (key.GetValue("ProxyEnable", 0) ?? 0));
                        string server = Convert.ToString(key == null ? "" : (key.GetValue("ProxyServer", "") ?? ""));
                        string pac = Convert.ToString(key == null ? "" : (key.GetValue("AutoConfigURL", "") ?? ""));
                        if (enabled != 0 && !string.IsNullOrWhiteSpace(server))
                        {
                            r.ProxyUrl = ParseWindowsProxyServer(server);
                            r.Detail = "从 Windows 当前用户代理读取：" + server;
                        }
                        else if (!string.IsNullOrWhiteSpace(pac))
                        {
                            r.Detail = "检测到 PAC（" + pac + "），环境变量不能直接使用 PAC，请填写 V2rayN 的 HTTP 端口";
                        }
                        else
                        {
                            r.Detail = "Windows 当前用户代理未开启";
                        }
                    }
                }
                catch (Exception ex)
                {
                    r.Detail = "读取 Windows 代理失败：" + ex.Message;
                }
            }

            Uri uri;
            if (Uri.TryCreate(r.ProxyUrl, UriKind.Absolute, out uri) && uri.Port > 0)
                r.PortOpen = IsPortOpen(uri.Host, uri.Port, 600);
            return r;
        }

        public static string ParseWindowsProxyServer(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            raw = raw.Trim();
            if (raw.IndexOf('=') < 0) return NormalizeProxy(raw);

            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in raw.Split(';'))
            {
                int p = part.IndexOf('=');
                if (p > 0) map[part.Substring(0, p).Trim()] = part.Substring(p + 1).Trim();
            }
            string value;
            if (map.TryGetValue("http", out value)) return NormalizeProxy(value, "http");
            if (map.TryGetValue("https", out value)) return NormalizeProxy(value, "http");
            if (map.TryGetValue("socks", out value)) return NormalizeProxy(value, "socks5");
            return "";
        }

        public static string NormalizeProxy(string value) { return NormalizeProxy(value, "http"); }

        private static string NormalizeProxy(string value, string defaultScheme)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string v = value.Trim().TrimEnd('/');
            if (v.IndexOf("://", StringComparison.Ordinal) < 0) v = defaultScheme + "://" + v;
            Uri uri;
            if (!Uri.TryCreate(v, UriKind.Absolute, out uri)) return "";
            if (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "socks5" && uri.Scheme != "socks5h") return "";
            if (uri.Port <= 0 || string.IsNullOrWhiteSpace(uri.Host)) return "";
            return uri.GetLeftPart(UriPartial.Authority);
        }

        private static bool IsV2rayNRunning()
        {
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string n = p.ProcessName.ToLowerInvariant();
                        if (n == "v2rayn" || n.StartsWith("v2rayn.")) return true;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return false;
        }

        private static bool IsPortOpen(string host, int port, int timeoutMs)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    c.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }
    }

    // ---------------------------------------------------------------------
    //  本机 Hermes 版本探测：只读文件/Git，不启动 hermes 本身。
    // ---------------------------------------------------------------------
    internal static class VersionProbe
    {
        public static string HermesInstallDir()
        {
            string env = Environment.GetEnvironmentVariable("HERMES_HOME");
            string home = string.IsNullOrWhiteSpace(env)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "hermes")
                : env;
            return Path.Combine(home, "hermes-agent");
        }

        public static string RunHidden(string exe, string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                if (Path.GetFileNameWithoutExtension(exe).Equals("git", StringComparison.OrdinalIgnoreCase))
                {
                    psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
                    psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                }
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return ""; }
                    return (outp ?? "").Trim();
                }
            }
            catch { return ""; }
        }

        /// <summary>从源码清单读取显示版本；失败也不影响精确提交号检测。</summary>
        public static string VersionLine()
        {
            string path = Path.Combine(HermesInstallDir(), "pyproject.toml");
            try
            {
                if (!File.Exists(path)) return "";
                string text = File.ReadAllText(path, Encoding.UTF8);
                Match project = Regex.Match(text, @"(?ms)^\[project\]\s*$.*?^\[", RegexOptions.Multiline);
                string scope = project.Success ? project.Value : text;
                Match version = Regex.Match(scope, @"(?m)^version\s*=\s*[""']([^""']+)[""']");
                return version.Success ? version.Groups[1].Value.Trim() : "";
            }
            catch { return ""; }
        }

        /// <summary>本机代码实际检出的提交号（40 位）；拿不到返回空串。</summary>
        public static string HeadCommit()
        {
            string dir = HermesInstallDir();
            if (!Directory.Exists(Path.Combine(dir, ".git"))) return "";
            string outp = RunHidden("git", "-C \"" + dir + "\" rev-parse HEAD", 20000);
            if (outp != null && Regex.IsMatch(outp.Trim(), "^[0-9a-fA-F]{40}$")) return outp.Trim();
            return ReadHeadWithoutGit(dir);
        }

        public static string HeadBranch()
        {
            string dir = HermesInstallDir();
            if (!Directory.Exists(Path.Combine(dir, ".git"))) return "";
            string branch = RunHidden("git", "-C \"" + dir + "\" symbolic-ref --quiet --short HEAD", 20000).Trim();
            if (!string.IsNullOrEmpty(branch)) return branch;
            try
            {
                string head = File.ReadAllText(Path.Combine(dir, ".git", "HEAD"), Encoding.ASCII).Trim();
                const string prefix = "ref: refs/heads/";
                if (head.StartsWith(prefix, StringComparison.Ordinal)) return head.Substring(prefix.Length);
            }
            catch { }
            return "(detached)";
        }

        public static string ExactTag()
        {
            string dir = HermesInstallDir();
            if (!Directory.Exists(Path.Combine(dir, ".git"))) return "";
            return RunHidden("git", "-C \"" + dir + "\" describe --tags --exact-match HEAD", 20000).Trim();
        }

        public static bool HasLocalChanges()
        {
            string dir = HermesInstallDir();
            if (!Directory.Exists(Path.Combine(dir, ".git"))) return false;
            return !string.IsNullOrWhiteSpace(RunHidden("git", "-C \"" + dir + "\" status --porcelain", 20000));
        }

        public static string MarkerValue(string key)
        {
            string path = Path.Combine(HermesInstallDir(), ".hermes-bootstrap-complete");
            try
            {
                if (!File.Exists(path)) return "";
                Match m = Regex.Match(File.ReadAllText(path, Encoding.UTF8), "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                return m.Success ? m.Groups[1].Value.Trim() : "";
            }
            catch { return ""; }
        }

        private static string ReadHeadWithoutGit(string dir)
        {
            try
            {
                string gitDir = Path.Combine(dir, ".git");
                string head = File.ReadAllText(Path.Combine(gitDir, "HEAD"), Encoding.ASCII).Trim();
                if (Regex.IsMatch(head, "^[0-9a-fA-F]{40}$")) return head;
                if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return "";
                string reference = head.Substring(5).Trim();
                string loose = Path.Combine(gitDir, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(loose))
                {
                    string sha = File.ReadAllText(loose, Encoding.ASCII).Trim();
                    if (Regex.IsMatch(sha, "^[0-9a-fA-F]{40}$")) return sha;
                }
                string packed = Path.Combine(gitDir, "packed-refs");
                if (File.Exists(packed))
                {
                    foreach (string line in File.ReadAllLines(packed, Encoding.ASCII))
                    {
                        string suffix = " " + reference;
                        if (line.EndsWith(suffix, StringComparison.Ordinal) && line.Length >= 40)
                        {
                            string sha = line.Substring(0, 40);
                            if (Regex.IsMatch(sha, "^[0-9a-fA-F]{40}$")) return sha;
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        /// <summary>给界面用的一行摘要。</summary>
        public static string Summary()
        {
            string ver = VersionLine();
            string commit = HeadCommit();
            string branch = HeadBranch();
            string tag = ExactTag();
            string markerCommit = MarkerValue("pinnedCommit");
            string markerBranch = MarkerValue("pinnedBranch");
            if (string.IsNullOrEmpty(ver) && string.IsNullOrEmpty(commit) && string.IsNullOrEmpty(markerCommit))
                return "未检测到本机 Hermes（可能还没安装）";
            StringBuilder sb = new StringBuilder();
            sb.Append("源码版本：").Append(string.IsNullOrEmpty(ver) ? "(未知)" : ver);
            if (!string.IsNullOrEmpty(tag)) sb.Append("　标签：").Append(tag);
            if (!string.IsNullOrEmpty(branch)) sb.Append("　分支：").Append(branch);
            sb.Append(Environment.NewLine);
            sb.Append("当前提交：").Append(string.IsNullOrEmpty(commit) ? "(读取失败)" : commit);
            if (HasLocalChanges()) sb.Append("　（存在未提交改动；复制提交号不会包含这些改动）");
            if (!string.IsNullOrEmpty(markerCommit) && !markerCommit.Equals(commit, StringComparison.OrdinalIgnoreCase))
                sb.Append(Environment.NewLine).Append("安装标记：").Append(markerBranch).Append(" @ ").Append(markerCommit).Append("（已与当前提交不同）");
            return sb.ToString();
        }
    }

    internal enum VersionKind { Commit, Tag, Branch }

    internal sealed class InstallTarget
    {
        public VersionKind Kind;
        public string Requested = "";
        public string Commit = "";
        public string Branch = "main";

        public string Description
        {
            get
            {
                if (Kind == VersionKind.Tag) return Requested + " → " + Commit;
                if (Kind == VersionKind.Commit) return Commit;
                return "分支 " + Branch;
            }
        }
    }

    internal sealed class StableReleaseInfo
    {
        public string Name = "";
        public string Tag = "";
        public string Url = "";
        public string PublishedAt = "";

        public string DisplayText
        {
            get
            {
                string n = string.IsNullOrWhiteSpace(Name) ? "Hermes Agent" : Name.Trim();
                if (n.IndexOf(Tag, StringComparison.OrdinalIgnoreCase) >= 0) return n;
                return n + " (" + Tag + ")";
            }
        }
    }

    internal static class Launcher
    {
        public static void ApplyProxyEnvironment(ProcessStartInfo psi, string proxy)
        {
            string noProxy = "localhost,127.0.0.1,::1";
            Set(psi, "HTTP_PROXY", proxy);
            Set(psi, "HTTPS_PROXY", proxy);
            Set(psi, "ALL_PROXY", proxy);
            Set(psi, "http_proxy", proxy);
            Set(psi, "https_proxy", proxy);
            Set(psi, "all_proxy", proxy);
            Set(psi, "NO_PROXY", noProxy);
            Set(psi, "no_proxy", noProxy);
            Set(psi, "npm_config_proxy", proxy);
            Set(psi, "npm_config_https_proxy", proxy);
            Set(psi, "PIP_PROXY", proxy);
            Set(psi, "HERMES_PROXY_BRIDGE", "1");
        }

        private static void Set(ProcessStartInfo psi, string key, string value)
        {
            psi.EnvironmentVariables[key] = value;
        }

        public static Process StartInstaller(string path, string proxy, bool enabled)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("找不到安装程序", path);
            ProcessStartInfo psi = new ProcessStartInfo(path);
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Path.GetDirectoryName(path);
            if (enabled) ApplyProxyEnvironment(psi, proxy);
            return Process.Start(psi);
        }

        public static Process StartHermesCommand(string command, string proxy, bool enabled, bool keepConsole)
        {
            string hermes = FindHermesCommand();
            if (string.IsNullOrEmpty(hermes)) throw new FileNotFoundException("找不到 hermes 命令；请先安装 Hermes，或从安装程序按钮开始。 ");
            string comspec = ComSpec();
            ProcessStartInfo psi = new ProcessStartInfo(comspec);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = !keepConsole;
            psi.Arguments = (keepConsole ? "/k " : "/c ") + QuoteForCmd(hermes) + " " + command;
            if (enabled) ApplyProxyEnvironment(psi, proxy);
            return Process.Start(psi);
        }

        // ---------------- 按指定版本安装 ----------------

        /// <summary>从粘贴内容中提取提交号/版本标签；分支必须显式写成 branch:NAME。</summary>
        public static string NormalizeVersionInput(string raw)
        {
            if (raw == null) return "";
            string v = raw.Trim().Trim('"').Trim('\'');
            if (v.Length == 0) return "";
            Match m = Regex.Match(v, "[0-9a-fA-F]{40}");
            if (m.Success) return m.Value;                                   // 完整提交号优先
            m = Regex.Match(v, @"\bv\d{4}\.\d{1,2}\.\d{1,2}(?:[-+][A-Za-z0-9.-]+)?\b");
            if (m.Success) return m.Value;
            m = Regex.Match(v, @"\b[0-9a-fA-F]{7,40}\b");                    // 短提交号
            if (m.Success) return m.Value;
            return v;
        }

        public static bool LooksLikeCommit(string v)
        {
            return !string.IsNullOrEmpty(v) && Regex.IsMatch(v, "^[0-9a-fA-F]{7,40}$");
        }

        public static bool LooksLikeTag(string v)
        {
            return !string.IsNullOrEmpty(v) && Regex.IsMatch(v, @"^v\d{4}\.\d{1,2}\.\d{1,2}(?:[-+][A-Za-z0-9.-]+)?$");
        }

        public static InstallTarget ResolveInstallTarget(string version, string proxy)
        {
            string v = NormalizeVersionInput(version);
            if (LooksLikeCommit(v))
            {
                string full = v.Length == 40 ? v.ToLowerInvariant() : ResolveCommitViaGitHub(v, proxy);
                return new InstallTarget { Kind = VersionKind.Commit, Requested = v, Commit = full, Branch = "main" };
            }
            if (LooksLikeTag(v))
            {
                return new InstallTarget { Kind = VersionKind.Tag, Requested = v, Commit = ResolveTagViaGitHub(v, proxy), Branch = "main" };
            }
            if (v.StartsWith("branch:", StringComparison.OrdinalIgnoreCase))
            {
                string branch = v.Substring(7).Trim();
                ValidateBranch(branch);
                return new InstallTarget { Kind = VersionKind.Branch, Requested = v, Branch = branch };
            }
            throw new InvalidOperationException("版本格式无效。请填写 7～40 位 Git 提交号、版本标签（如 v2026.9.24），或显式分支 branch:main。");
        }

        private static void ValidateBranch(string branch)
        {
            bool invalid = string.IsNullOrEmpty(branch) || branch.Length > 200 ||
                !Regex.IsMatch(branch, @"^[A-Za-z0-9._/-]+$") ||
                branch.StartsWith("/") || branch.EndsWith("/") || branch.StartsWith(".") ||
                branch.EndsWith(".") || branch.Contains("..") || branch.Contains("//") ||
                branch.EndsWith(".lock", StringComparison.OrdinalIgnoreCase);
            if (invalid) throw new InvalidOperationException("分支名无效：" + branch);
        }

        private static string ResolveCommitViaGitHub(string value, string proxy)
        {
            string json = DownloadString("https://api.github.com/repos/NousResearch/hermes-agent/commits/" + Uri.EscapeDataString(value), proxy);
            Match m = Regex.Match(json, "\\\"sha\\\"\\s*:\\s*\\\"([0-9a-fA-F]{40})\\\"");
            if (!m.Success) throw new InvalidOperationException("GitHub 没有返回可用的提交号：" + value);
            return m.Groups[1].Value.ToLowerInvariant();
        }

        private static string ResolveTagViaGitHub(string tag, string proxy)
        {
            string url = "https://api.github.com/repos/NousResearch/hermes-agent/git/ref/tags/" + Uri.EscapeDataString(tag);
            string json = DownloadString(url, proxy);
            for (int i = 0; i < 4; i++)
            {
                Match obj = Regex.Match(json, "\\\"object\\\"\\s*:\\s*\\{(?:(?!\\}).)*\\\"sha\\\"\\s*:\\s*\\\"([0-9a-fA-F]{40})\\\"(?:(?!\\}).)*\\\"type\\\"\\s*:\\s*\\\"(commit|tag)\\\"", RegexOptions.Singleline);
                if (!obj.Success)
                {
                    // GitHub 的字段顺序不是 API 合同的一部分，反向顺序也接受。
                    obj = Regex.Match(json, "\\\"object\\\"\\s*:\\s*\\{(?:(?!\\}).)*\\\"type\\\"\\s*:\\s*\\\"(commit|tag)\\\"(?:(?!\\}).)*\\\"sha\\\"\\s*:\\s*\\\"([0-9a-fA-F]{40})\\\"", RegexOptions.Singleline);
                    if (!obj.Success) throw new InvalidOperationException("无法解析 GitHub 返回的标签信息：" + tag);
                    string reverseType = obj.Groups[1].Value;
                    string reverseSha = obj.Groups[2].Value;
                    if (reverseType == "commit") return reverseSha.ToLowerInvariant();
                    json = DownloadString("https://api.github.com/repos/NousResearch/hermes-agent/git/tags/" + reverseSha, proxy);
                    continue;
                }
                string sha = obj.Groups[1].Value;
                string type = obj.Groups[2].Value;
                if (type == "commit") return sha.ToLowerInvariant();
                json = DownloadString("https://api.github.com/repos/NousResearch/hermes-agent/git/tags/" + sha, proxy);
            }
            throw new InvalidOperationException("标签嵌套层数异常：" + tag);
        }

        /// <summary>GitHub /releases/latest 只返回最新的非草稿、非预发布 Release；仍在本地复核字段。</summary>
        public static StableReleaseInfo GetLatestStableRelease(string proxy)
        {
            string json = DownloadString("https://api.github.com/repos/NousResearch/hermes-agent/releases/latest", proxy);
            bool draft = Regex.IsMatch(json, "\\\"draft\\\"\\s*:\\s*true", RegexOptions.IgnoreCase);
            bool prerelease = Regex.IsMatch(json, "\\\"prerelease\\\"\\s*:\\s*true", RegexOptions.IgnoreCase);
            if (draft || prerelease) throw new InvalidDataException("GitHub 返回的 latest Release 不是稳定版，已拒绝自动选择。");

            StableReleaseInfo r = new StableReleaseInfo();
            r.Tag = JsonString(json, "tag_name");
            r.Name = JsonString(json, "name");
            r.Url = JsonString(json, "html_url");
            r.PublishedAt = JsonString(json, "published_at");
            if (!LooksLikeTag(r.Tag)) throw new InvalidDataException("GitHub 最新稳定版的标签格式异常：" + r.Tag);
            Uri releaseUri;
            if (!Uri.TryCreate(r.Url, UriKind.Absolute, out releaseUri) || releaseUri.Scheme != "https" ||
                !releaseUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GitHub 最新稳定版链接格式异常。");
            return r;
        }

        private static string JsonString(string json, string key)
        {
            Match m = Regex.Match(json, "\\\"" + Regex.Escape(key) + "\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"])*)\\\"");
            if (!m.Success) return "";
            string value = m.Groups[1].Value;
            value = value.Replace("\\/", "/").Replace("\\\"", "\"").Replace("\\\\", "\\");
            return Regex.Replace(value, @"\\u([0-9a-fA-F]{4})", delegate(Match x) {
                return ((char)Convert.ToInt32(x.Groups[1].Value, 16)).ToString();
            });
        }

        private static string DownloadString(string url, string proxy)
        {
            string temp = Path.Combine(Path.GetTempPath(), "HermesProxyBridge-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                DownloadFileWithCurl(url, temp, proxy);
                return File.ReadAllText(temp, Encoding.UTF8);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

        private static void DownloadFileWithCurl(string url, string target, string proxy)
        {
            string curl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
            if (!File.Exists(curl)) curl = "curl.exe";
            ProcessStartInfo psi = new ProcessStartInfo(curl);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.Arguments = "--fail --location --silent --show-error --connect-timeout 20 --max-time 120 " +
                "--user-agent \"HermesProxyBridge/1.4\" --output " + QuoteForCmd(target) + " ";
            if (!string.IsNullOrEmpty(proxy))
            {
                Uri u;
                if (!Uri.TryCreate(proxy, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https"))
                    throw new InvalidOperationException("Bridge 自身下载版本信息/脚本需要 V2rayN 的 HTTP 或 mixed 代理端口；当前地址是：" + proxy);
                psi.Arguments += "--proxy " + QuoteForCmd(proxy) + " ";
                ApplyProxyEnvironment(psi, proxy);
            }
            psi.Arguments += QuoteForCmd(url);
            using (Process p = Process.Start(psi))
            {
                string error = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(130000))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException("curl 下载超时：" + url);
                }
                if (p.ExitCode != 0) throw new InvalidOperationException("curl 下载失败（退出码 " + p.ExitCode + "）：" + error.Trim());
            }
        }

        /// <summary>目标提交的脚本与目标源码来自同一个 commit，避免“新脚本安装老代码”的兼容问题。</summary>
        public static string ResolveInstallScript(InstallTarget targetInfo, string proxy, out string how)
        {
            Directory.CreateDirectory(Settings.DirectoryPath);
            string key = targetInfo.Kind == VersionKind.Branch ? "branch-" + targetInfo.Branch.Replace('/', '_') : targetInfo.Commit.Substring(0, 12);
            string target = Path.Combine(Settings.DirectoryPath, "install-" + key + ".ps1");
            if (targetInfo.Kind != VersionKind.Branch && File.Exists(target) && ValidateInstallScript(target))
            {
                how = "目标提交的已验证缓存";
                return target;
            }
            string url = targetInfo.Kind == VersionKind.Branch
                ? "https://raw.githubusercontent.com/NousResearch/hermes-agent/" + Uri.EscapeDataString(targetInfo.Branch) + "/scripts/install.ps1"
                : "https://raw.githubusercontent.com/NousResearch/hermes-agent/" + targetInfo.Commit + "/scripts/install.ps1";
            string temp = target + ".download-" + Process.GetCurrentProcess().Id;
            try
            {
                DownloadFileWithCurl(url, temp, proxy);
                if (!ValidateInstallScript(temp))
                    throw new InvalidDataException("下载内容不是兼容的 Hermes install.ps1（缺少 Commit/Branch/IncludeDesktop 参数）");
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                how = "从目标版本官方源码下载并校验";
                return target;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "下载目标版本的官方 install.ps1 失败：" + ex.Message + Environment.NewLine + url, ex);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

        private static bool ValidateInstallScript(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                return text.Length > 1000 && text.IndexOf("[string]$Branch", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    text.IndexOf("[string]$Commit", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    text.IndexOf("[switch]$IncludeDesktop", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        public static string PreviewInstallScriptPath(InstallTarget targetInfo)
        {
            string key = targetInfo.Kind == VersionKind.Branch ? "branch-" + targetInfo.Branch.Replace('/', '_') : targetInfo.Commit.Substring(0, 12);
            return Path.Combine(Settings.DirectoryPath, "install-" + key + ".ps1");
        }

        public static string BuildInstallArguments(string script, InstallTarget targetInfo, out string pinText)
        {
            string pinArgs;
            if (targetInfo.Kind == VersionKind.Branch)
            {
                pinText = "-Branch " + targetInfo.Branch;
                pinArgs = " -Branch " + QuoteForCmd(targetInfo.Branch);
            }
            else
            {
                pinText = "-Branch main -Commit " + targetInfo.Commit;
                pinArgs = " -Branch \"main\" -Commit " + QuoteForCmd(targetInfo.Commit);
            }
            return "-NoProfile -ExecutionPolicy Bypass -File " + QuoteForCmd(script) + pinArgs + " -IncludeDesktop";
        }

        /// <summary>直接启动 PowerShell，避免 cmd.exe 的二次解析。</summary>
        public static Process StartPinnedInstall(string script, InstallTarget targetInfo, string proxy, bool enabled, out string commandLine)
        {
            string pinText;
            string args = BuildInstallArguments(script, targetInfo, out pinText);
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(powershell)) powershell = "powershell.exe";
            commandLine = QuoteForCmd(powershell) + " " + args;
            ProcessStartInfo psi = new ProcessStartInfo(powershell);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = false;
            psi.Arguments = args;
            if (enabled) ApplyProxyEnvironment(psi, proxy);
            return Process.Start(psi);
        }

        public static string ComSpec()
        {
            string comspec = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrEmpty(comspec))
                comspec = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            return comspec;
        }

        public static string FindHermesCommand()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] candidates = {
                Path.Combine(local, "hermes", "bin", "hermes.exe"),
                Path.Combine(local, "hermes", "bin", "hermes.cmd"),
                Path.Combine(local, "hermes", "hermes.exe")
            };
            foreach (string c in candidates) if (File.Exists(c)) return c;
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in path.Split(';'))
            {
                try
                {
                    string c = Path.Combine(dir.Trim(), "hermes.exe");
                    if (File.Exists(c)) return c;
                    c = Path.Combine(dir.Trim(), "hermes.cmd");
                    if (File.Exists(c)) return c;
                }
                catch { }
            }
            return "";
        }

        private static string QuoteForCmd(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    internal sealed class RaisedButton : Button
    {
        private bool pressed;
        public Color AccentColor { get; set; }

        public RaisedButton()
        {
            AccentColor = Color.FromArgb(52, 104, 196);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseDown(MouseEventArgs mevent) { pressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }
        protected override void OnMouseLeave(EventArgs e) { pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle shadow = new Rectangle(2, 4, Width - 4, Height - 6);
            using (GraphicsPath shadowPath = RoundedRect(shadow, 7))
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(70, 30, 45, 65)))
                e.Graphics.FillPath(shadowBrush, shadowPath);

            int offset = pressed ? 2 : 0;
            Rectangle face = new Rectangle(1, 1 + offset, Width - 3, Height - 6);
            Color top = Enabled ? Lighten(AccentColor, pressed ? 5 : 28) : Color.FromArgb(205, 209, 216);
            Color bottom = Enabled ? Darken(AccentColor, pressed ? 24 : 10) : Color.FromArgb(174, 180, 188);
            using (GraphicsPath facePath = RoundedRect(face, 7))
            using (LinearGradientBrush fill = new LinearGradientBrush(face, top, bottom, LinearGradientMode.Vertical))
            using (Pen border = new Pen(Enabled ? Darken(AccentColor, 42) : Color.Gray))
            {
                e.Graphics.FillPath(fill, facePath);
                e.Graphics.DrawPath(border, facePath);
            }
            Rectangle textRect = new Rectangle(face.Left + 6, face.Top, face.Width - 12, face.Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, textRect, Enabled ? Color.White : Color.FromArgb(105, 105, 105),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(face, -4, -4);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, Color.White, Color.Transparent);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static Color Lighten(Color c, int amount)
        {
            return Color.FromArgb(Math.Min(255, c.R + amount), Math.Min(255, c.G + amount), Math.Min(255, c.B + amount));
        }

        private static Color Darken(Color c, int amount)
        {
            return Color.FromArgb(Math.Max(0, c.R - amount), Math.Max(0, c.G - amount), Math.Max(0, c.B - amount));
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Settings settings;
        private readonly CheckBox enabledBox = new CheckBox();
        private readonly TextBox proxyBox = new TextBox();
        private readonly Label detectLabel = new Label();
        private readonly Label safetyLabel = new Label();
        private readonly TextBox logBox = new TextBox();
        private readonly RaisedButton installerButton = new RaisedButton();
        private readonly RaisedButton updateButton = new RaisedButton();
        private readonly RaisedButton desktopButton = new RaisedButton();
        private readonly RaisedButton shortcutButton = new RaisedButton();
        // ---- 指定版本与稳定版控件 ----
        private readonly Label versionLabel = new Label();
        private readonly TextBox versionBox = new TextBox();
        private readonly RaisedButton versionDetectButton = new RaisedButton();
        private readonly Label versionInfoLabel = new Label();
        private readonly RaisedButton pinnedInstallButton = new RaisedButton();
        private readonly RaisedButton previewButton = new RaisedButton();
        private readonly RaisedButton copyVersionButton = new RaisedButton();
        private readonly RaisedButton clearLogButton = new RaisedButton();
        private readonly RaisedButton latestCheckButton = new RaisedButton();
        private readonly RaisedButton latestUseButton = new RaisedButton();
        private readonly LinkLabel latestReleaseLabel = new LinkLabel();
        private readonly Label latestStatusLabel = new Label();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private DetectionResult detection = new DetectionResult();
        private StableReleaseInfo latestRelease;
        private bool latestCheckBusy;

        public MainForm(Settings s)
        {
            settings = s;
            SuspendLayout();
            Text = "Hermes Proxy Bridge v1.4";
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(239, 243, 248);
            ClientSize = new Size(900, 960);
            MinimumSize = new Size(840, 900);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;

            Font sectionFont = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            Font noteFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            Font buttonFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            Color navy = Color.FromArgb(35, 64, 105);
            Color primary = Color.FromArgb(53, 103, 190);
            Color green = Color.FromArgb(42, 137, 101);
            Color slate = Color.FromArgb(88, 103, 122);
            Color orange = Color.FromArgb(194, 112, 38);

            Label title = new Label { Text = "Hermes 安装 / 更新专用代理", Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold), ForeColor = navy, Left = 24, Top = 14, Width = 560, Height = 40 };
            Label intro = new Label { Text = "仅影响由本程序启动的 Hermes 进程树，不改动 V2rayN、系统代理或 WinHTTP。", Font = noteFont, ForeColor = Color.FromArgb(76, 88, 104), Left = 26, Top = 53, Width = 820, Height = 25 };
            Panel accentLine = new Panel { Left = 24, Top = 78, Width = 852, Height = 3, BackColor = primary, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            GroupBox proxyGroup = MakeSection("代理状态", 24, 92, 852, 220, sectionFont);
            enabledBox.Text = "启用 Hermes 专用代理";
            enabledBox.Appearance = Appearance.Button;
            enabledBox.FlatStyle = FlatStyle.Flat;
            enabledBox.FlatAppearance.BorderSize = 1;
            enabledBox.TextAlign = ContentAlignment.MiddleCenter;
            enabledBox.Font = buttonFont;
            enabledBox.Left = 20; enabledBox.Top = 30; enabledBox.Width = 230; enabledBox.Height = 46;
            enabledBox.Checked = settings.Enabled;
            enabledBox.CheckedChanged += delegate { settings.Enabled = enabledBox.Checked; SaveUi(); UpdateControls(); Log(enabledBox.Checked ? "专用代理已开启（只影响之后由本程序启动的进程）" : "专用代理已关闭（启动时不注入代理变量）"); };

            Label proxyLabel = new Label { Text = "代理地址（留空时自动读取 V2rayN 设置的 Windows 代理）", Font = noteFont, ForeColor = Color.FromArgb(65, 75, 88), Left = 20, Top = 83, Width = 610, Height = 24 };
            proxyBox.Left = 20; proxyBox.Top = 108; proxyBox.Width = 650; proxyBox.Height = 30; proxyBox.Font = new Font("Segoe UI", 10.5F); proxyBox.Text = settings.ManualProxy;
            proxyBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            RaisedButton refresh = new RaisedButton { Text = "重新检测", Left = 688, Top = 103, Width = 140, Height = 40, AccentColor = slate, Font = buttonFont, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            refresh.Click += delegate { RefreshDetection(); };
            proxyBox.Leave += delegate { SaveUi(); RefreshDetection(); };
            detectLabel.Left = 20; detectLabel.Top = 151; detectLabel.Width = 808; detectLabel.Height = 42; detectLabel.Font = noteFont; detectLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            safetyLabel.Left = 20; safetyLabel.Top = 194; safetyLabel.Width = 808; safetyLabel.Height = 23; safetyLabel.Font = new Font("Microsoft YaHei UI", 9F); safetyLabel.ForeColor = Color.FromArgb(88, 99, 113);
            safetyLabel.Text = "后台更新要继承代理，请从本程序或代理快捷方式启动 Hermes Desktop；无法给已运行进程事后注入。";
            proxyGroup.Controls.AddRange(new Control[] { enabledBox, proxyLabel, proxyBox, refresh, detectLabel, safetyLabel });

            GroupBox actionGroup = MakeSection("常用操作", 24, 326, 852, 148, sectionFont);
            ConfigureButton(installerButton, "选择并启动官方安装器", 20, 31, 250, 44, primary, buttonFont);
            installerButton.Click += OnStartInstaller;
            ConfigureButton(updateButton, "立即运行 hermes update", 286, 31, 240, 44, orange, buttonFont);
            updateButton.Click += delegate { RunHermes("update", true); };
            ConfigureButton(desktopButton, "通过代理启动 Desktop", 542, 31, 286, 44, green, buttonFont);
            desktopButton.Click += delegate { RunHermes("desktop", false); };
            ConfigureButton(shortcutButton, "创建“Hermes（V2rayN代理）”桌面快捷方式", 20, 87, 430, 44, slate, buttonFont);
            shortcutButton.Click += OnCreateShortcut;
            actionGroup.Controls.AddRange(new Control[] { installerButton, updateButton, desktopButton, shortcutButton });

            GroupBox versionGroup = MakeSection("指定版本安装", 24, 488, 852, 315, sectionFont);
            latestStatusLabel.Text = "官网最新稳定版：尚未核对";
            latestStatusLabel.Font = noteFont; latestStatusLabel.ForeColor = Color.FromArgb(74, 85, 101);
            latestStatusLabel.Left = 20; latestStatusLabel.Top = 30; latestStatusLabel.Width = 560; latestStatusLabel.Height = 24;
            latestReleaseLabel.Text = "点击右侧按钮从 GitHub 官方 Releases 获取";
            latestReleaseLabel.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            latestReleaseLabel.LinkColor = Color.FromArgb(37, 91, 177); latestReleaseLabel.ActiveLinkColor = Color.FromArgb(24, 65, 135);
            latestReleaseLabel.LinkBehavior = LinkBehavior.HoverUnderline;
            latestReleaseLabel.LinkArea = new LinkArea(0, 0);
            latestReleaseLabel.Enabled = false;
            latestReleaseLabel.Left = 20; latestReleaseLabel.Top = 55; latestReleaseLabel.Width = 560; latestReleaseLabel.Height = 30;
            latestReleaseLabel.LinkClicked += OnOpenLatestRelease;
            ConfigureButton(latestCheckButton, "核对官网最新稳定版", 602, 26, 226, 42, primary, buttonFont);
            latestCheckButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            latestCheckButton.Click += OnCheckLatestStable;
            ConfigureButton(latestUseButton, "填入此稳定版", 602, 75, 226, 42, green, buttonFont);
            latestUseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            latestUseButton.Enabled = false;
            latestUseButton.Click += OnUseLatestStable;
            Panel divider = new Panel { Left = 20, Top = 126, Width = 808, Height = 1, BackColor = Color.FromArgb(210, 218, 228), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };

            versionLabel.Text = "安装版本（40 位提交号、vYYYY.M.D 标签；分支请写 branch:main）";
            versionLabel.Font = noteFont; versionLabel.ForeColor = Color.FromArgb(65, 75, 88);
            versionLabel.Left = 20; versionLabel.Top = 137; versionLabel.Width = 610; versionLabel.Height = 24;
            versionBox.Left = 20; versionBox.Top = 162; versionBox.Width = 550; versionBox.Height = 31; versionBox.Font = new Font("Segoe UI", 10.5F); versionBox.Text = settings.InstallVersion;
            versionBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            versionBox.Leave += delegate { SaveUi(); };
            versionBox.TextChanged += delegate {
                settings.InstallVersion = versionBox.Text.Trim(); SaveUi();
                if (!string.IsNullOrEmpty(settings.SaveWarning)) { versionInfoLabel.ForeColor = Color.DarkRed; versionInfoLabel.Text = settings.SaveWarning + "；请把 EXE 移到可写文件夹后重试。"; }
            };
            ConfigureButton(versionDetectButton, "检测本机版本", 586, 155, 142, 42, slate, buttonFont);
            versionDetectButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; versionDetectButton.Click += OnDetectVersion;
            ConfigureButton(copyVersionButton, "复制版本", 738, 155, 90, 42, slate, buttonFont);
            copyVersionButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; copyVersionButton.Click += OnCopyVersion;
            versionInfoLabel.Left = 20; versionInfoLabel.Top = 204; versionInfoLabel.Width = 808; versionInfoLabel.Height = 45; versionInfoLabel.Font = new Font("Microsoft YaHei UI", 9F); versionInfoLabel.ForeColor = Color.DarkSlateGray;
            versionInfoLabel.Text = "检测本机版本会自动填入并复制当前提交；选择内容保存在程序旁的便携 INI。";
            ConfigureButton(pinnedInstallButton, "按指定版本安装（走代理）", 20, 255, 290, 44, green, buttonFont);
            pinnedInstallButton.Click += OnPinnedInstall;
            ConfigureButton(previewButton, "预览命令（不执行）", 326, 255, 220, 44, slate, buttonFont);
            previewButton.Click += OnPreviewCommand;
            versionGroup.Controls.AddRange(new Control[] { latestStatusLabel, latestReleaseLabel, latestCheckButton, latestUseButton, divider,
                versionLabel, versionBox, versionDetectButton, copyVersionButton, versionInfoLabel, pinnedInstallButton, previewButton });

            GroupBox logGroup = MakeSection("运行日志", 24, 817, 852, 119, sectionFont);
            logGroup.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            logBox.Left = 18; logBox.Top = 29; logBox.Width = 704; logBox.Height = 72; logBox.Multiline = true; logBox.ReadOnly = true; logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Font = new Font("Consolas", 9.5F); logBox.BackColor = Color.FromArgb(249, 251, 253); logBox.ForeColor = Color.FromArgb(42, 49, 58);
            logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            ConfigureButton(clearLogButton, "清空日志", 738, 29, 90, 42, slate, buttonFont);
            clearLogButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; clearLogButton.Click += delegate { logBox.Clear(); };
            logGroup.Controls.AddRange(new Control[] { logBox, clearLogButton });

            Controls.AddRange(new Control[] { title, intro, accentLine, proxyGroup, actionGroup, versionGroup, logGroup });
            FormClosing += delegate { SaveUi(); };
            timer.Interval = 3000; timer.Tick += delegate { RefreshDetection(); }; timer.Start();
            RefreshDetection(); UpdateControls();
            Log("v1.4 已启动；版本选择保存在程序旁：" + Settings.PortableFilePath);
            if (!string.IsNullOrEmpty(settings.SaveWarning)) Log("警告：" + settings.SaveWarning);
            ResumeLayout(false);
        }

        private static GroupBox MakeSection(string text, int left, int top, int width, int height, Font font)
        {
            return new GroupBox {
                Text = "  " + text + "  ", Left = left, Top = top, Width = width, Height = height,
                Font = font, ForeColor = Color.FromArgb(35, 64, 105), BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
        }

        private static void ConfigureButton(RaisedButton button, string text, int left, int top, int width, int height, Color color, Font font)
        {
            button.Text = text; button.Left = left; button.Top = top; button.Width = width; button.Height = height;
            button.AccentColor = color; button.Font = font;
        }

        private void OnCheckLatestStable(object sender, EventArgs e)
        {
            try
            {
                string proxy = CurrentProxyOrThrow();
                latestCheckBusy = true;
                latestRelease = null;
                latestCheckButton.Enabled = false;
                latestUseButton.Enabled = false;
                latestStatusLabel.Text = "官网最新稳定版：正在通过" + (settings.Enabled ? "代理" : "直连") + "核对…";
                latestReleaseLabel.Text = "正在访问 GitHub 官方 Releases API";
                Log("开始核对 GitHub 官网最新稳定版" + (settings.Enabled ? "，代理 " + proxy : "，未注入代理"));
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        StableReleaseInfo info = Launcher.GetLatestStableRelease(proxy);
                        if (IsDisposed) return;
                        BeginInvoke(new Action(delegate
                        {
                            latestRelease = info;
                            latestCheckBusy = false;
                            latestStatusLabel.Text = "官网最新稳定版（GitHub latest，已排除草稿/预发布）：";
                            latestReleaseLabel.Text = info.DisplayText;
                            latestReleaseLabel.Enabled = true;
                            latestReleaseLabel.Links.Clear();
                            latestReleaseLabel.Links.Add(0, latestReleaseLabel.Text.Length, info.Url);
                            latestUseButton.Enabled = true;
                            UpdateControls();
                            Log("官网最新稳定版：" + info.DisplayText + "，标签 " + info.Tag);
                        }));
                    }
                    catch (Exception ex)
                    {
                        if (IsDisposed) return;
                        BeginInvoke(new Action(delegate
                        {
                            latestCheckBusy = false;
                            latestStatusLabel.Text = "官网最新稳定版：核对失败";
                            latestReleaseLabel.Text = ex.Message;
                            latestReleaseLabel.Enabled = false;
                            latestReleaseLabel.Links.Clear();
                            latestReleaseLabel.LinkArea = new LinkArea(0, 0);
                            UpdateControls();
                            ShowError(ex);
                        }));
                    }
                });
            }
            catch (Exception ex) { latestCheckBusy = false; ShowError(ex); UpdateControls(); }
        }

        private void OnUseLatestStable(object sender, EventArgs e)
        {
            if (latestRelease == null || string.IsNullOrEmpty(latestRelease.Tag)) return;
            versionBox.Text = latestRelease.Tag;
            SaveUi();
            versionBox.Focus();
            versionBox.SelectAll();
            Log("已把官网稳定版标签填入安装版本：" + latestRelease.Tag);
        }

        private void OnOpenLatestRelease(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (latestRelease == null || string.IsNullOrEmpty(latestRelease.Url)) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(latestRelease.Url);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void SaveUi()
        {
            settings.ManualProxy = proxyBox.Text.Trim();
            settings.InstallVersion = versionBox.Text.Trim();
            settings.Save();
        }

        private void RefreshDetection()
        {
            detection = ProxyDetector.Detect(proxyBox.Text.Trim());
            string v = detection.V2rayNRunning ? "V2rayN：运行中" : "V2rayN：未检测到进程";
            string p = string.IsNullOrEmpty(detection.ProxyUrl) ? "代理：未解析到可用地址" : "代理：" + detection.ProxyUrl + (detection.PortOpen ? "（本地端口可连接）" : "（端口不可连接）");
            detectLabel.Text = v + "    " + p + Environment.NewLine + detection.Detail;
            detectLabel.ForeColor = detection.PortOpen ? Color.DarkGreen : Color.DarkRed;
            UpdateControls();
        }

        private void UpdateControls()
        {
            enabledBox.BackColor = settings.Enabled ? Color.PaleGreen : SystemColors.Control;
            bool ready = !settings.Enabled || (detection != null && detection.PortOpen && !string.IsNullOrEmpty(detection.ProxyUrl));
            installerButton.Enabled = ready;
            updateButton.Enabled = ready;
            desktopButton.Enabled = ready;
            pinnedInstallButton.Enabled = ready;
            latestCheckButton.Enabled = ready && !latestCheckBusy;
        }

        private string CurrentProxyOrThrow()
        {
            if (!settings.Enabled) return "";
            RefreshDetection();
            if (string.IsNullOrEmpty(detection.ProxyUrl)) throw new InvalidOperationException("没有解析到代理地址。请让 V2rayN 开启“设置系统代理”，或手动填写 HTTP/SOCKS5 地址。");
            if (!detection.PortOpen) throw new InvalidOperationException("代理端口当前不可连接：" + detection.ProxyUrl);
            return detection.ProxyUrl;
        }

        private void OnStartInstaller(object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = "Hermes 安装程序 (Hermes-Setup.exe)|Hermes-Setup.exe|可执行文件 (*.exe)|*.exe";
                    d.Title = "选择 Hermes 安装程序";
                    if (File.Exists(settings.LastInstaller)) d.InitialDirectory = Path.GetDirectoryName(settings.LastInstaller);
                    else d.InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    settings.LastInstaller = d.FileName;
                    settings.Save();
                    string proxy = CurrentProxyOrThrow();
                    Process p = Launcher.StartInstaller(d.FileName, proxy, settings.Enabled);
                    Log("已启动安装程序，PID " + p.Id + (settings.Enabled ? "，代理 " + proxy : "，未注入代理"));
                }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void RunHermes(string command, bool keepConsole)
        {
            try
            {
                string proxy = CurrentProxyOrThrow();
                Process p = Launcher.StartHermesCommand(command, proxy, settings.Enabled, keepConsole);
                Log("已启动 hermes " + command + "，PID " + p.Id + (settings.Enabled ? "，代理 " + proxy : "，未注入代理"));
            }
            catch (Exception ex) { ShowError(ex); }
        }

        // ---------------- 指定版本相关动作 ----------------

        private void OnDetectVersion(object sender, EventArgs e)
        {
            try
            {
                string summary = VersionProbe.Summary();
                versionInfoLabel.Text = summary.Replace(Environment.NewLine, "　");
                versionInfoLabel.ForeColor = Color.DarkSlateGray;
                Log("检测本机版本：" + summary.Replace(Environment.NewLine, " | "));
                string commit = VersionProbe.HeadCommit();
                if (string.IsNullOrEmpty(commit))
                {
                    Log("没读到当前提交号；可手动填写版本标签，如 v2026.9.24");
                    return;
                }
                versionBox.Text = commit;          // 自动填入
                SaveUi();
                if (!string.IsNullOrEmpty(settings.SaveWarning)) Log("警告：" + settings.SaveWarning);
                try { Clipboard.SetText(commit); Log("提交号已复制到剪贴板：" + commit); }
                catch { Log("复制到剪贴板失败（不影响已填入的版本框）"); }
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnCopyVersion(object sender, EventArgs e)
        {
            try
            {
                string v = Launcher.NormalizeVersionInput(versionBox.Text);
                if (string.IsNullOrEmpty(v)) { Log("版本框是空的，先检测或手动填写。"); return; }
                Clipboard.SetText(v);
                Log("已复制到剪贴板：" + v);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnPreviewCommand(object sender, EventArgs e)
        {
            try
            {
                string v = Launcher.NormalizeVersionInput(versionBox.Text);
                if (string.IsNullOrEmpty(v)) { Log("版本框是空的：请点「检测本机版本」或手动填写版本号。"); return; }
                Log("★ 预览（不联网、不写文件、不会安装）：输入=" + versionBox.Text.Trim() + " → " + v);
                if (Launcher.LooksLikeCommit(v) && v.Length == 40)
                {
                    InstallTarget targetInfo = new InstallTarget { Kind = VersionKind.Commit, Requested = v, Commit = v.ToLowerInvariant(), Branch = "main" };
                    string script = Launcher.PreviewInstallScriptPath(targetInfo);
                    string pinText;
                    string args = Launcher.BuildInstallArguments(script, targetInfo, out pinText);
                    Log("   目标：精确提交 " + targetInfo.Commit);
                    Log("   安装时下载该提交自带的官方 install.ps1 到：" + script);
                    Log("   powershell.exe " + args);
                }
                else if (Launcher.LooksLikeCommit(v))
                {
                    Log("   安装时先经代理向 GitHub 把短提交号解析为 40 位 SHA，再执行 -Branch main -Commit <完整SHA>。");
                }
                else if (Launcher.LooksLikeTag(v))
                {
                    Log("   安装时先经代理把标签 " + v + " 解析为精确提交，再执行 -Branch main -Commit <完整SHA>。");
                }
                else if (v.StartsWith("branch:", StringComparison.OrdinalIgnoreCase))
                {
                    string branch = v.Substring(7).Trim();
                    InstallTarget targetInfo = Launcher.ResolveInstallTarget("branch:" + branch, "");
                    string script = Launcher.PreviewInstallScriptPath(targetInfo);
                    string pinText;
                    Log("   powershell.exe " + Launcher.BuildInstallArguments(script, targetInfo, out pinText));
                }
                else throw new InvalidOperationException("版本格式无效。请填写提交号、vYYYY.M.D 标签，或 branch:NAME。");
                Log("   已有安装中的未提交修改由官方脚本负责暂存；精确提交不包含本机未提交修改。");
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnPinnedInstall(object sender, EventArgs e)
        {
            try
            {
                string raw = versionBox.Text.Trim();
                string v = Launcher.NormalizeVersionInput(raw);
                if (string.IsNullOrEmpty(v)) { Log("请先在「选择安装版本」里填版本号（或点「检测本机版本」）。"); return; }
                if (!v.Equals(raw, StringComparison.Ordinal))
                    Log("已清洗版本输入：" + raw + " → " + v);

                string proxy = CurrentProxyOrThrow();
                InstallTarget targetInfo = Launcher.ResolveInstallTarget(v, proxy);
                if (targetInfo.Kind == VersionKind.Tag || (Launcher.LooksLikeCommit(v) && v.Length < 40))
                    Log("已解析为精确提交：" + targetInfo.Description);
                string pinText = targetInfo.Kind == VersionKind.Branch ? "-Branch " + targetInfo.Branch : "-Branch main -Commit " + targetInfo.Commit;
                DialogResult dr = MessageBox.Show(this,
                    "即将安装 Hermes，目标版本：\n\n" + targetInfo.Description + "\n\n参数：" + pinText + " -IncludeDesktop\n" +
                    "代理：" + (settings.Enabled ? proxy : "（未启用注入）") + "\n\n" +
                    "会克隆代码并安装依赖，耗时较长。确定继续吗？",
                    "确认按指定版本安装", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (dr != DialogResult.OK) { Log("已取消安装。"); return; }

                string how;
                string script = Launcher.ResolveInstallScript(targetInfo, proxy, out how);
                Log("使用安装脚本：" + script + "（" + how + "）");
                string cmdline;
                Process p = Launcher.StartPinnedInstall(script, targetInfo, proxy, settings.Enabled, out cmdline);
                Log("已启动指定版本安装，PID " + p.Id + "，参数：" + pinText);
                Log("   命令：" + cmdline);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnCreateShortcut(object sender, EventArgs e)
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string linkPath = Path.Combine(desktop, "Hermes（V2rayN代理）.lnk");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                Type st = shortcut.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Application.ExecutablePath });
                st.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { "--desktop" });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "通过当前 V2rayN 代理启动 Hermes Desktop" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                Log("已创建桌面快捷方式：" + linkPath);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void Log(string message)
        {
            const int maxChars = 200000;
            if (logBox.TextLength > maxChars)
                logBox.Text = logBox.Text.Substring(logBox.TextLength - maxChars / 2);
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        }

        private void ShowError(Exception ex)
        {
            Log("错误：" + ex.Message);
            MessageBox.Show(this, ex.Message, "Hermes Proxy Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal static class Program
    {
        // 让 winexe 的「命令行模式」也能把结果打印到调用它的控制台（或重定向的管道里）。
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        private static void EnableConsoleOutput()
        {
            try
            {
                AttachConsole(-1);   // 附着到父进程的控制台；若父进程无控制台则失败（重定向输出仍可用）
                Encoding utf8 = new UTF8Encoding(false);
                Console.OutputEncoding = utf8;
                StreamWriter w = new StreamWriter(Console.OpenStandardOutput(), utf8);
                w.AutoFlush = true;
                Console.SetOut(w);
            }
            catch { }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && (args[0] == "--self-test" || args[0] == "--detect-version" || args[0] == "--print-install-command" || args[0] == "--print-saved-version" || args[0] == "--verify-install-source" || args[0] == "--check-latest-stable")) EnableConsoleOutput();
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest();
            if (args.Length > 0 && args[0] == "--detect-version") { Console.WriteLine("== " + VersionProbe.Summary().Replace(Environment.NewLine, " | ")); Console.WriteLine("== hermes 路径: " + (Launcher.FindHermesCommand() == "" ? "(未找到)" : Launcher.FindHermesCommand())); Console.WriteLine("== install dir: " + VersionProbe.HermesInstallDir()); return 0; }
            if (args.Length > 1 && args[0] == "--print-install-command") return PrintInstallCommand(args[1]);
            if (args.Length > 0 && args[0] == "--print-saved-version") { Console.WriteLine(Settings.Load().InstallVersion); return 0; }
            if (args.Length > 1 && args[0] == "--verify-install-source") return VerifyInstallSource(args[1]);
            if (args.Length > 0 && args[0] == "--check-latest-stable") return CheckLatestStable();
            if (args.Length > 1 && args[0] == "--render-ui-screenshot") return RenderUiScreenshot(args[1]);
            Settings s = Settings.Load();
            if (args.Length > 0 && (args[0] == "--desktop" || args[0] == "--update"))
            {
                try
                {
                    DetectionResult d = ProxyDetector.Detect(s.ManualProxy);
                    if (s.Enabled && (!d.PortOpen || string.IsNullOrEmpty(d.ProxyUrl)))
                        throw new InvalidOperationException("Hermes 专用代理已开启，但未检测到可连接的 V2rayN 代理。请打开 Hermes Proxy Bridge 检查设置。");
                    Launcher.StartHermesCommand(args[0] == "--desktop" ? "desktop" : "update", d.ProxyUrl, s.Enabled, args[0] == "--update");
                    return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Hermes Proxy Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(s));
            return 0;
        }

        /// <summary>打印「按指定版本安装」的静态预览；不联网、不写文件、不安装。</summary>
        private static int PrintInstallCommand(string version)
        {
            Settings s = Settings.Load();
            string v = Launcher.NormalizeVersionInput(version);
            Console.WriteLine("raw      : " + version);
            Console.WriteLine("cleaned  : " + v);
            InstallTarget targetInfo;
            if (Launcher.LooksLikeCommit(v) && v.Length == 40)
                targetInfo = new InstallTarget { Kind = VersionKind.Commit, Requested = v, Commit = v.ToLowerInvariant(), Branch = "main" };
            else if (v.StartsWith("branch:", StringComparison.OrdinalIgnoreCase))
                targetInfo = Launcher.ResolveInstallTarget(v, "");
            else
            {
                Console.WriteLine("resolve  : 安装时经代理解析为 40 位提交号");
                Console.WriteLine("proxyenv : " + (s.Enabled ? "开启（注入 12 个变量）" : "关闭（不注入）"));
                return (Launcher.LooksLikeCommit(v) || Launcher.LooksLikeTag(v)) ? 0 : 3;
            }
            string script = Launcher.PreviewInstallScriptPath(targetInfo);
            string pinText;
            string a = Launcher.BuildInstallArguments(script, targetInfo, out pinText);
            Console.WriteLine("pin      : " + pinText);
            Console.WriteLine("script   : " + script + "   [安装时下载目标版本官方脚本]");
            Console.WriteLine("cmdline  : powershell.exe " + a);
            Console.WriteLine("proxyenv : " + (s.Enabled ? "开启（注入 12 个变量）" : "关闭（不注入）"));
            return 0;
        }

        /// <summary>安全预检：解析版本并下载/校验对应官方脚本，但绝不启动脚本。</summary>
        private static int VerifyInstallSource(string version)
        {
            try
            {
                Settings s = Settings.Load();
                DetectionResult d = ProxyDetector.Detect(s.ManualProxy);
                string proxy = "";
                if (s.Enabled)
                {
                    if (!d.PortOpen || string.IsNullOrEmpty(d.ProxyUrl)) throw new InvalidOperationException("专用代理已开启，但代理端口不可用。");
                    proxy = d.ProxyUrl;
                }
                InstallTarget targetInfo = Launcher.ResolveInstallTarget(version, proxy);
                string how;
                string script = Launcher.ResolveInstallScript(targetInfo, proxy, out how);
                Console.WriteLine("PASS: target " + targetInfo.Description);
                Console.WriteLine("PASS: script " + script);
                Console.WriteLine("PASS: source " + how);
                Console.WriteLine("PASS: proxy  " + (s.Enabled ? proxy : "disabled"));
                Console.WriteLine("NOTE: script was verified and cached, not executed");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 4;
            }
        }

        private static int CheckLatestStable()
        {
            try
            {
                Settings s = Settings.Load();
                DetectionResult d = ProxyDetector.Detect(s.ManualProxy);
                string proxy = "";
                if (s.Enabled)
                {
                    if (!d.PortOpen || string.IsNullOrEmpty(d.ProxyUrl)) throw new InvalidOperationException("专用代理已开启，但代理端口不可用。");
                    proxy = d.ProxyUrl;
                }
                StableReleaseInfo info = Launcher.GetLatestStableRelease(proxy);
                Console.WriteLine("PASS: name      " + info.Name);
                Console.WriteLine("PASS: tag       " + info.Tag);
                Console.WriteLine("PASS: url       " + info.Url);
                Console.WriteLine("PASS: published " + info.PublishedAt);
                Console.WriteLine("PASS: proxy     " + (s.Enabled ? proxy : "disabled"));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 5;
            }
        }

        private static int RenderUiScreenshot(string path)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (MainForm form = new MainForm(Settings.Load()))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-32000, -32000);
                    form.Show();
                    Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.GetFullPath(path), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    form.Close();
                }
                return 0;
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine("FAIL: " + ex.Message); } catch { }
                return 6;
            }
        }

        private static int SelfTest()
        {
            string[] inputs = { "127.0.0.1:10809", "http=127.0.0.1:10809;https=127.0.0.1:10809", "socks=127.0.0.1:10808" };
            string[] expected = { "http://127.0.0.1:10809", "http://127.0.0.1:10809", "socks5://127.0.0.1:10808" };
            for (int i = 0; i < inputs.Length; i++)
            {
                string actual = ProxyDetector.ParseWindowsProxyServer(inputs[i]);
                if (!actual.Equals(expected[i], StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine("FAIL: proxy " + inputs[i] + " -> " + actual);
                    return 1;
                }
            }
            Console.WriteLine("PASS: proxy parser (3 cases)");

            // 版本输入清洗与类型判定
            string[,] vc = {
                { "639823919ced15640924e905bacab3858c5afd19", "639823919ced15640924e905bacab3858c5afd19", "commit" },
                { "  v2026.9.24  ", "v2026.9.24", "tag" },
                { "branch:main", "branch:main", "branch" },
                { "\"6398239\"", "6398239", "commit" },
                { "Hermes Agent v0.21.3 (2026.9.14) · local 639823919ced15640924e905bacab3858c5afd19", "639823919ced15640924e905bacab3858c5afd19", "commit" },
                { "安装 v2026.9.24 那份", "v2026.9.24", "tag" }
            };
            for (int i = 0; i < vc.GetLength(0); i++)
            {
                string got = Launcher.NormalizeVersionInput(vc[i, 0]);
                string kind = Launcher.LooksLikeCommit(got) ? "commit" : (Launcher.LooksLikeTag(got) ? "tag" : (got.StartsWith("branch:") ? "branch" : "invalid"));
                if (got != vc[i, 1] || kind != vc[i, 2])
                {
                    Console.Error.WriteLine("FAIL: version '" + vc[i, 0] + "' -> '" + got + "' (" + kind + ")，期望 '" + vc[i, 1] + "' (" + vc[i, 2] + ")");
                    return 1;
                }
            }
            Console.WriteLine("PASS: version input normalizer (6 cases)");

            InstallTarget exact = new InstallTarget { Kind = VersionKind.Commit, Requested = vc[0, 0], Commit = vc[0, 0], Branch = "main" };
            string pin;
            string built = Launcher.BuildInstallArguments(@"C:\Temp Folder\install.ps1", exact, out pin);
            if (!built.Contains("-Branch \"main\" -Commit \"639823919ced15640924e905bacab3858c5afd19\" -IncludeDesktop"))
            {
                Console.Error.WriteLine("FAIL: exact commit command builder");
                return 1;
            }
            Console.WriteLine("PASS: exact commit command builder");
            return 0;
        }
    }
}
