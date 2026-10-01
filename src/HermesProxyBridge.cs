// ============================================================================
//  Hermes Proxy Bridge v1.5.3
//  ---------------------------------------------------------------------------
//  使用本机 V2rayN 代理安装、更新或启动 Hermes Agent，并支持指定版本安装、
//  本机版本检测、GitHub 稳定版查询、分阶段进度和完整日志。
//  编译：见同目录 build.ps1（用 Windows 自带 .NET Framework 编译器，无需联网）
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
[assembly: AssemblyVersion("1.5.3.0")]
[assembly: AssemblyFileVersion("1.5.3.0")]
[assembly: AssemblyInformationalVersion("1.5.3")]

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
        private const int InactivityTimeoutSeconds = 300;
        private const int StageHardTimeoutMinutes = 45;

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
            // @electron/get ignores HTTP_PROXY unless this opt-in flag is present.
            // Without it npm itself uses the proxy, while Electron's binary download
            // can still connect directly and hang with "socket hang up".
            Set(psi, "ELECTRON_GET_USE_PROXY", "1");
            Set(psi, "GLOBAL_AGENT_HTTP_PROXY", proxy);
            Set(psi, "GLOBAL_AGENT_HTTPS_PROXY", proxy);
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
                "--user-agent \"HermesProxyBridge/1.5.3\" --output " + QuoteForCmd(target) + " ";
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

        public static bool SupportsStageProtocol(string path)
        {
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                return text.IndexOf("[switch]$Manifest", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    text.IndexOf("[string]$Stage", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    text.IndexOf("[switch]$NonInteractive", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    text.IndexOf("[switch]$Json", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
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
            return "-NoProfile -ExecutionPolicy Bypass -File " + QuoteForCmd(script) + pinArgs + " -IncludeDesktop -NonInteractive";
        }

        private static string BuildStageArguments(string script, InstallTarget targetInfo, string stage)
        {
            string pinText;
            string args = BuildInstallArguments(script, targetInfo, out pinText);
            if (targetInfo.Kind != VersionKind.Branch)
            {
                try
                {
                    string text = File.ReadAllText(script, Encoding.UTF8);
                    if (text.IndexOf("[switch]$ForceCommit", StringComparison.OrdinalIgnoreCase) >= 0)
                        args += " -ForceCommit";
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(stage)) args += " -Stage " + QuoteForCmd(stage) + " -Json";
            return args;
        }

        private static string PowerShellPath()
        {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(powershell) ? powershell : "powershell.exe";
        }

        private static int RunCaptured(string arguments, string proxy, bool enabled, Action<string> output, out string combined)
        {
            return RunCapturedCore(arguments, proxy, enabled, output, out combined, InactivityTimeoutSeconds);
        }

        private static int RunCapturedCore(string arguments, string proxy, bool enabled, Action<string> output, out string combined, int inactivityTimeoutSeconds)
        {
            StringBuilder capture = new StringBuilder();
            object gate = new object();
            DateTime lastActivityUtc = DateTime.UtcNow;
            DateTime startedUtc = lastActivityUtc;
            int lastHeartbeatMinute = 0;
            ProcessStartInfo psi = new ProcessStartInfo(PowerShellPath());
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.Arguments = arguments;
            // The pinned installer first clones main and then checks out the requested
            // commit. A user's global core.autocrlf=true can dirty the fresh checkout
            // before that switch and make git abort. Isolate Git's "global" config to
            // a Bridge-owned file so we neither depend on nor modify the user's config.
            string isolatedGitConfig = Path.Combine(Settings.DirectoryPath, "installer-gitconfig");
            Directory.CreateDirectory(Settings.DirectoryPath);
            File.WriteAllText(isolatedGitConfig,
                "[core]" + Environment.NewLine +
                "\tautocrlf = false" + Environment.NewLine +
                "\tlongpaths = true" + Environment.NewLine +
                "[windows]" + Environment.NewLine +
                "\tappendAtomically = false" + Environment.NewLine,
                new UTF8Encoding(false));
            psi.EnvironmentVariables["GIT_CONFIG_GLOBAL"] = isolatedGitConfig;
            if (enabled) ApplyProxyEnvironment(psi, proxy);
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                    if (e.Data == null) return;
                    lock (gate) { capture.AppendLine(e.Data); lastActivityUtc = DateTime.UtcNow; }
                    if (output != null) output(e.Data);
                };
                p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) {
                    if (e.Data == null) return;
                    lock (gate) { capture.AppendLine("[stderr] " + e.Data); lastActivityUtc = DateTime.UtcNow; }
                    if (output != null) output("[stderr] " + e.Data);
                };
                if (!p.Start()) throw new InvalidOperationException("无法启动 PowerShell 安装进程。");
                if (output != null) output("进程 PID " + p.Id);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(1000))
                {
                    DateTime last;
                    lock (gate) last = lastActivityUtc;
                    int quietSeconds = (int)(DateTime.UtcNow - last).TotalSeconds;
                    int elapsedMinutes = (int)(DateTime.UtcNow - startedUtc).TotalMinutes;
                    int quietMinute = quietSeconds / 60;
                    if (quietMinute > lastHeartbeatMinute && quietMinute > 0)
                    {
                        lastHeartbeatMinute = quietMinute;
                        if (output != null) output("[Bridge] 子进程已连续 " + quietSeconds + " 秒没有输出；达到 " + inactivityTimeoutSeconds + " 秒将自动停止。");
                    }
                    if (quietSeconds >= inactivityTimeoutSeconds || elapsedMinutes >= StageHardTimeoutMinutes)
                    {
                        string reason = quietSeconds >= inactivityTimeoutSeconds
                            ? "子进程连续 " + quietSeconds + " 秒没有任何输出"
                            : "本阶段运行已超过 " + StageHardTimeoutMinutes + " 分钟";
                        if (output != null) output("[Bridge] 超时：" + reason + "，正在终止本阶段进程树。");
                        KillProcessTree(p);
                        try { p.WaitForExit(10000); } catch { }
                        lock (gate) combined = capture.ToString();
                        throw new TimeoutException(reason + "，Bridge 已停止安装，避免无限卡死。请把本次安装日志交给 Agent 分析。");
                    }
                }
                p.WaitForExit();
                lock (gate) combined = capture.ToString();
                return p.ExitCode;
            }
        }

        public static bool SelfTestTimeoutGuard(Action<string> output)
        {
            string ignored;
            try
            {
                RunCapturedCore("-NoProfile -NonInteractive -Command \"Start-Sleep -Seconds 15\"", "", false, output, out ignored, 2);
                return false;
            }
            catch (TimeoutException)
            {
                return true;
            }
        }

        private static void KillProcessTree(Process process)
        {
            try
            {
                ProcessStartInfo kill = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe"));
                kill.UseShellExecute = false;
                kill.CreateNoWindow = true;
                kill.Arguments = "/PID " + process.Id + " /T /F";
                using (Process killer = Process.Start(kill))
                {
                    if (killer != null) killer.WaitForExit(10000);
                }
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(); } catch { }
            }
        }

        public static IList<string> ReadInstallStages(string script, InstallTarget targetInfo, string proxy, bool enabled, Action<string> output)
        {
            string args = BuildStageArguments(script, targetInfo, "");
            // Manifest only describes the plan. Remove execution-only flags while retaining IncludeDesktop.
            args = Regex.Replace(args, @"\s+-NonInteractive", "", RegexOptions.IgnoreCase) + " -Manifest";
            if (output != null) output("读取官方安装阶段清单…");
            string text;
            int code = RunCaptured(args, proxy, enabled, output, out text);
            if (code != 0) throw new InvalidOperationException("读取安装阶段清单失败（退出码 " + code + "）。");
            List<string> stages = new List<string>();
            foreach (Match m in Regex.Matches(text, "\\\"name\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                if (!stages.Contains(m.Groups[1].Value)) stages.Add(m.Groups[1].Value);
            if (stages.Count == 0) throw new InvalidDataException("官方脚本返回了空的安装阶段清单。");
            return stages;
        }

        public static void RunPinnedInstallTracked(string script, InstallTarget targetInfo, string proxy, bool enabled, Action<string> output, Action<int, int, string> progress)
        {
            if (!SupportsStageProtocol(script))
            {
                if (output != null) output("目标版本脚本不支持分阶段协议，改用兼容模式并完整捕获输出。");
                if (progress != null) progress(0, 1, "兼容模式安装");
                string pin;
                string text;
                int code = RunCaptured(BuildInstallArguments(script, targetInfo, out pin), proxy, enabled, output, out text);
                if (code != 0) throw new InvalidOperationException("官方安装脚本失败（退出码 " + code + "）。请复制日志分析具体错误。");
                if (progress != null) progress(1, 1, "完成");
                return;
            }

            IList<string> stages = ReadInstallStages(script, targetInfo, proxy, enabled, output);
            for (int i = 0; i < stages.Count; i++)
            {
                string stage = stages[i];
                if (progress != null) progress(i, stages.Count, stage);
                if (output != null) output("========== 阶段 " + (i + 1) + "/" + stages.Count + "：" + stage + " ==========");
                string text;
                int code = RunCaptured(BuildStageArguments(script, targetInfo, stage), proxy, enabled, output, out text);
                if (code != 0)
                    throw new InvalidOperationException("安装在阶段「" + stage + "」失败（退出码 " + code + "）。请复制日志，其中包含该阶段的 stdout/stderr。");
                if (output != null) output("阶段完成：" + stage);
            }
            if (progress != null) progress(stages.Count, stages.Count, "完成");
        }

        /// <summary>直接启动 PowerShell，避免 cmd.exe 的二次解析。</summary>
        public static Process StartPinnedInstall(string script, InstallTarget targetInfo, string proxy, bool enabled, out string commandLine)
        {
            string pinText;
            string args = BuildInstallArguments(script, targetInfo, out pinText);
            string powershell = PowerShellPath();
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
        public Color AccentColor { get; set; }

        public RaisedButton()
        {
            AccentColor = SystemColors.Control;
            FlatStyle = FlatStyle.Standard;
            UseVisualStyleBackColor = true;
            BackColor = SystemColors.Control;
            ForeColor = SystemColors.ControlText;
            Cursor = Cursors.Default;
        }
    }

    internal sealed class MainForm : Form
    {
        // The active tab and its page intentionally use exactly the same blue.
        // Keep enough contrast from both the white section cards and inactive tab.
        private static readonly Color SelectedTabSurface = Color.FromArgb(210, 228, 250);
        private static readonly Color InactiveTabSurface = Color.FromArgb(178, 192, 211);
        private Settings settings;
        private readonly CheckBox enabledBox = new CheckBox();
        private readonly TextBox proxyBox = new TextBox();
        private readonly Label detectLabel = new Label();
        private readonly Label safetyLabel = new Label();
        private readonly TextBox logBox = new TextBox();
        private readonly TextBox versionLogBox = new TextBox();
        private readonly RaisedButton installerButton = new RaisedButton();
        private readonly RaisedButton updateButton = new RaisedButton();
        private readonly RaisedButton desktopButton = new RaisedButton();
        // ---- 指定版本与稳定版控件 ----
        private readonly Label versionLabel = new Label();
        private readonly TextBox versionBox = new TextBox();
        private readonly RaisedButton versionDetectButton = new RaisedButton();
        private readonly Label versionInfoLabel = new Label();
        private readonly Label localVersionLabel = new Label();
        private readonly RaisedButton pinnedInstallButton = new RaisedButton();
        private readonly RaisedButton clearLogButton = new RaisedButton();
        private readonly RaisedButton latestCheckButton = new RaisedButton();
        private readonly LinkLabel latestReleaseLabel = new LinkLabel();
        private readonly ProgressBar installProgress = new ProgressBar();
        private readonly Label installStatusLabel = new Label();
        private readonly RaisedButton detailsButton = new RaisedButton();
        private readonly TextBox installDetailBox = new TextBox();
        private TableLayoutPanel versionHost;
        private readonly Button proxyTabButton = new Button();
        private readonly Button versionTabButton = new Button();
        private Panel proxyPage;
        private Panel versionPage;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private DetectionResult detection = new DetectionResult();
        private StableReleaseInfo latestRelease;
        private bool latestCheckBusy;
        private bool installBusy;
        private string sessionLogPath;
        private string currentInstallLogPath;

        public MainForm(Settings s, bool suppressStartupWarning = false)
        {
            settings = s;
            Directory.CreateDirectory(Path.Combine(Settings.DirectoryPath, "logs"));
            sessionLogPath = Path.Combine(Settings.DirectoryPath, "logs", "bridge-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");

            Text = "Hermes代理安装工具 miu edition v1.5.3";
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = Color.FromArgb(239, 243, 248);
            ClientSize = new Size(980, 760);
            MinimumSize = new Size(920, 680);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(5, 5, 5, 7), ColumnCount = 1, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(BuildHeader(), 0, 0);

            TableLayoutPanel tabSurface = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = SelectedTabSurface, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            tabSurface.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            tabSurface.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            FlowLayoutPanel tabBar = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(4, 3, 0, 0), Margin = new Padding(0) };
            ConfigureTabButton(proxyTabButton, "代理与常用操作");
            ConfigureTabButton(versionTabButton, "指定版本安装");
            proxyTabButton.Click += delegate { SwitchPage(false); };
            versionTabButton.Click += delegate { SwitchPage(true); };
            tabBar.Controls.AddRange(new Control[] { proxyTabButton, versionTabButton });
            Panel pageHost = new Panel { Dock = DockStyle.Fill, BackColor = SelectedTabSurface, Margin = new Padding(0), Padding = new Padding(6, 0, 6, 6) };
            proxyPage = BuildMainTab();
            versionPage = BuildVersionTab();
            pageHost.Controls.Add(versionPage);
            pageHost.Controls.Add(proxyPage);
            tabSurface.Controls.Add(tabBar, 0, 0);
            tabSurface.Controls.Add(pageHost, 0, 1);
            root.Controls.Add(tabSurface, 0, 1);
            Controls.Add(root);
            SwitchPage(false);

            if (!suppressStartupWarning) FormClosing += delegate { SaveUi(); };
            timer.Interval = 3000;
            timer.Tick += delegate { if (!installBusy) RefreshDetection(); };
            timer.Start();
            RefreshDetection();
            UpdateControls();
            Log("Hermes代理安装工具 miu edition v1.5.3 已启动");
            Log("本次完整日志文件：" + sessionLogPath);
            Log("版本选择保存在程序旁：" + Settings.PortableFilePath);
            if (!string.IsNullOrEmpty(settings.SaveWarning)) Log("警告：" + settings.SaveWarning);
            if (!suppressStartupWarning)
            {
                Shown += delegate {
                    if (detection == null || !detection.V2rayNRunning || !detection.PortOpen)
                        MessageBox.Show(this, "没有启动代理。\r\n\r\n请先启动 V2rayN 并开启系统代理，然后点击“重新检测”。", "Hermes Proxy Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
            }
        }

        private static void ConfigureTabButton(Button button, string text)
        {
            button.Text = text;
            button.Width = 180;
            button.Height = 39;
            button.Margin = new Padding(0, 0, 3, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.UseVisualStyleBackColor = false;
            button.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Cursor = Cursors.Hand;
        }

        private void SwitchPage(bool showVersion)
        {
            if (proxyPage == null || versionPage == null) return;
            proxyPage.Visible = !showVersion;
            versionPage.Visible = showVersion;
            if (showVersion) versionPage.BringToFront(); else proxyPage.BringToFront();
            Button selected = showVersion ? versionTabButton : proxyTabButton;
            Button other = showVersion ? proxyTabButton : versionTabButton;
            selected.BackColor = SelectedTabSurface;
            selected.ForeColor = Color.FromArgb(35, 64, 105);
            selected.FlatAppearance.MouseOverBackColor = SelectedTabSurface;
            selected.FlatAppearance.MouseDownBackColor = SelectedTabSurface;
            other.BackColor = InactiveTabSurface;
            other.ForeColor = Color.FromArgb(78, 88, 101);
            other.FlatAppearance.MouseOverBackColor = Color.FromArgb(193, 205, 222);
            other.FlatAppearance.MouseDownBackColor = Color.FromArgb(163, 179, 201);
        }

        private Control BuildHeader()
        {
            TableLayoutPanel p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(2) };
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 39F));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 31F));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 3F));
            p.Controls.Add(new Label { Text = "Hermes代理安装工具 miu edition", Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold), ForeColor = Color.FromArgb(35, 64, 105), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            p.Controls.Add(new Label { Text = "使用本机V2rayN代理安装/更新Hermes Agent的工具", Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 9.5F), ForeColor = Color.FromArgb(76, 88, 104), TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            p.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(53, 103, 190) }, 0, 2);
            return p;
        }

        private Panel BuildMainTab()
        {
            Panel tab = NewTab();
            TableLayoutPanel page = NewPage(3);
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 235F));
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.Controls.Add(BuildProxyGroup(), 0, 0);
            page.Controls.Add(BuildActionsGroup(), 0, 1);
            page.Controls.Add(BuildLogGroup(logBox), 0, 2);
            tab.Controls.Add(page);
            return tab;
        }

        private Panel BuildVersionTab()
        {
            Panel tab = NewTab();
            versionHost = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = SelectedTabSurface, ColumnCount = 2, RowCount = 1 };
            versionHost.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            versionHost.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0F));
            versionHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            TableLayoutPanel page = NewPage(2);
            page.RowStyles.Add(new RowStyle(SizeType.Absolute, 340F));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            page.Controls.Add(BuildVersionGroup(), 0, 0);
            page.Controls.Add(BuildLogGroup(versionLogBox), 0, 1);
            versionHost.Controls.Add(page, 0, 0);
            versionHost.Controls.Add(BuildInstallDetailsPanel(), 1, 0);
            tab.Controls.Add(versionHost);
            return tab;
        }

        private static Panel NewTab()
        {
            return new Panel { Dock = DockStyle.Fill, BackColor = SelectedTabSurface, Padding = new Padding(6) };
        }

        private static TableLayoutPanel NewPage(int rows)
        {
            TableLayoutPanel page = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = SelectedTabSurface, ColumnCount = 1, RowCount = rows };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return page;
        }

        private GroupBox NewSection(string text)
        {
            return new GroupBox { Text = "  " + text + "  ", Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold), ForeColor = Color.FromArgb(35, 64, 105), BackColor = Color.White, Margin = new Padding(3, 3, 3, 6) };
        }

        private RaisedButton NewButton(string text, int width)
        {
            return new RaisedButton { Text = text, Width = width, Height = 40, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), Margin = new Padding(5), UseVisualStyleBackColor = true };
        }

        private Control BuildProxyGroup()
        {
            GroupBox group = NewSection("代理状态");
            TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 7, 10, 6), ColumnCount = 4, RowCount = 5 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15F));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 63F)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 47F)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 39F)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            detectLabel.Dock = DockStyle.Fill; detectLabel.TextAlign = ContentAlignment.MiddleLeft; detectLabel.AutoEllipsis = true; detectLabel.Padding = new Padding(10, 4, 8, 4); detectLabel.Margin = new Padding(0, 0, 0, 5);
            grid.Controls.Add(detectLabel, 0, 0); grid.SetColumnSpan(detectLabel, 4);
            enabledBox.Text = "启动 Hermes 专用代理"; enabledBox.Appearance = Appearance.Button; enabledBox.FlatStyle = FlatStyle.Standard; enabledBox.UseVisualStyleBackColor = true; enabledBox.TextAlign = ContentAlignment.MiddleCenter; enabledBox.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold); enabledBox.Dock = DockStyle.Fill; enabledBox.Margin = new Padding(0, 0, 8, 5); enabledBox.Checked = settings.Enabled;
            enabledBox.CheckedChanged += delegate { settings.Enabled = enabledBox.Checked; SaveUi(); RefreshDetection(); Log(enabledBox.Checked ? "Hermes 专用代理已启动" : "Hermes 专用代理已关闭"); };
            grid.Controls.Add(enabledBox, 0, 1); grid.SetColumnSpan(enabledBox, 3);
            RaisedButton refresh = NewButton("重新检测", 110); refresh.Dock = DockStyle.Fill; refresh.Margin = new Padding(0, 0, 0, 5); refresh.Click += delegate { RefreshDetection(); };
            grid.Controls.Add(refresh, 3, 1);
            Label proxyLabel = new Label { Text = "代理地址（留空时自动读取 V2rayN 设置的 Windows 代理）", Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 9.5F), ForeColor = Color.FromArgb(65, 75, 88), TextAlign = ContentAlignment.BottomLeft };
            grid.Controls.Add(proxyLabel, 0, 2); grid.SetColumnSpan(proxyLabel, 4);
            proxyBox.Dock = DockStyle.Fill; proxyBox.Font = new Font("Segoe UI", 10.5F); proxyBox.Text = settings.ManualProxy; proxyBox.Margin = new Padding(0, 3, 0, 5); proxyBox.Leave += delegate { SaveUi(); RefreshDetection(); };
            grid.Controls.Add(proxyBox, 0, 3); grid.SetColumnSpan(proxyBox, 4);
            safetyLabel.Dock = DockStyle.Fill; safetyLabel.Font = new Font("Microsoft YaHei UI", 9F); safetyLabel.ForeColor = Color.FromArgb(88, 99, 113); safetyLabel.Text = "请先启动代理，再进行安装/更新";
            grid.Controls.Add(safetyLabel, 0, 4); grid.SetColumnSpan(safetyLabel, 4);
            group.Controls.Add(grid);
            return group;
        }

        private Control BuildActionsGroup()
        {
            GroupBox group = NewSection("代理操作");
            FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 9, 8, 5), AutoScroll = true, WrapContents = true };
            SetupActionButton(installerButton, "运行官方安装器", 225, OnStartInstaller);
            SetupActionButton(updateButton, "运行 hermes update", 215, delegate { RunHermes("update", true); });
            SetupActionButton(desktopButton, "运行桌面用户端", 225, delegate { RunHermes("desktop", false); });
            flow.Controls.AddRange(new Control[] { installerButton, updateButton, desktopButton });
            group.Controls.Add(flow);
            return group;
        }

        private void SetupActionButton(RaisedButton button, string text, int width, EventHandler handler)
        {
            button.Text = text; button.Width = width; button.Height = 43; button.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold); button.Margin = new Padding(5); button.UseVisualStyleBackColor = true; button.Click += handler;
        }

        private Control BuildVersionGroup()
        {
            GroupBox group = NewSection("版本选择与安装");
            TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 8, 14, 8), ColumnCount = 2, RowCount = 8 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245F));
            // Keep the fixed rows compact so the final install row remains visible
            // after Windows high-DPI scaling (125%-200%).
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            latestReleaseLabel.Text = "官网最新稳定版本号：尚未获取"; latestReleaseLabel.Dock = DockStyle.Fill; latestReleaseLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold); latestReleaseLabel.LinkColor = Color.FromArgb(37, 91, 177); latestReleaseLabel.LinkBehavior = LinkBehavior.HoverUnderline; latestReleaseLabel.LinkArea = new LinkArea(0, 0); latestReleaseLabel.Enabled = false; latestReleaseLabel.TextAlign = ContentAlignment.MiddleLeft; latestReleaseLabel.LinkClicked += OnOpenLatestRelease; grid.Controls.Add(latestReleaseLabel, 0, 0);
            latestCheckButton.Text = "获取官方最新稳定版本号"; latestCheckButton.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold); latestCheckButton.Dock = DockStyle.Fill; latestCheckButton.Margin = new Padding(8, 2, 0, 3); latestCheckButton.Click += OnCheckLatestStable; grid.Controls.Add(latestCheckButton, 1, 0);
            localVersionLabel.Text = "本机当前版本号：尚未获取"; localVersionLabel.Dock = DockStyle.Fill; localVersionLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold); localVersionLabel.ForeColor = Color.FromArgb(45, 67, 92); localVersionLabel.TextAlign = ContentAlignment.MiddleLeft; localVersionLabel.AutoEllipsis = true; grid.Controls.Add(localVersionLabel, 0, 1);
            versionDetectButton.Text = "获取本机当前版本号"; versionDetectButton.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold); versionDetectButton.Dock = DockStyle.Fill; versionDetectButton.Margin = new Padding(8, 2, 0, 3); versionDetectButton.Click += OnDetectVersion; grid.Controls.Add(versionDetectButton, 1, 1);
            versionInfoLabel.Dock = DockStyle.Fill; versionInfoLabel.Font = new Font("Microsoft YaHei UI", 9F); versionInfoLabel.ForeColor = Color.DarkSlateGray; versionInfoLabel.Text = "获取本机当前版本号。该功能用于：记录本机使用的hermes版本，将本软件拷贝到新机后该版本号不消失（存入随附的ini文件），作为新机指定安装的版本。"; versionInfoLabel.TextAlign = ContentAlignment.MiddleLeft; grid.Controls.Add(versionInfoLabel, 0, 2); grid.SetColumnSpan(versionInfoLabel, 2);
            Panel divider = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(190, 205, 224), Margin = new Padding(0, 6, 0, 6) }; grid.Controls.Add(divider, 0, 3); grid.SetColumnSpan(divider, 2);
            versionLabel.Text = "指定版本安装（可填入40位提交号、vYYYY.M.D标签；分支请写branch:main）"; versionLabel.Dock = DockStyle.Fill; versionLabel.Font = new Font("Microsoft YaHei UI", 9.5F); versionLabel.ForeColor = Color.FromArgb(65, 75, 88); versionLabel.TextAlign = ContentAlignment.BottomLeft; grid.Controls.Add(versionLabel, 0, 4); grid.SetColumnSpan(versionLabel, 2);
            versionBox.Dock = DockStyle.Fill; versionBox.Font = new Font("Segoe UI", 10.5F); versionBox.Text = settings.InstallVersion; versionBox.Margin = new Padding(0, 4, 0, 4); versionBox.Leave += delegate { SaveUi(); }; versionBox.TextChanged += delegate { settings.InstallVersion = versionBox.Text.Trim(); SaveUi(); if (!string.IsNullOrEmpty(settings.SaveWarning)) { versionInfoLabel.ForeColor = Color.DarkRed; versionInfoLabel.Text = "版本号无法保存到随附的 ini 文件：" + settings.SaveWarning; } }; grid.Controls.Add(versionBox, 0, 5); grid.SetColumnSpan(versionBox, 2);
            TableLayoutPanel progressRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) }; progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F)); progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F)); progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F)); installStatusLabel.Text = "尚未开始安装"; installStatusLabel.Dock = DockStyle.Fill; installStatusLabel.Font = new Font("Microsoft YaHei UI", 9F); installStatusLabel.ForeColor = Color.FromArgb(65, 75, 88); installStatusLabel.TextAlign = ContentAlignment.MiddleLeft; progressRow.Controls.Add(installStatusLabel, 0, 0); installProgress.Dock = DockStyle.Fill; installProgress.Minimum = 0; installProgress.Maximum = 100; installProgress.Margin = new Padding(5, 11, 5, 10); progressRow.Controls.Add(installProgress, 1, 0); detailsButton.Text = "安装进度 ▶"; detailsButton.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold); detailsButton.Dock = DockStyle.Fill; detailsButton.Margin = new Padding(5, 3, 0, 3); detailsButton.Click += delegate { ToggleInstallDetails(); }; progressRow.Controls.Add(detailsButton, 2, 0); grid.Controls.Add(progressRow, 0, 6); grid.SetColumnSpan(progressRow, 2);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 0), WrapContents = false }; SetupActionButton(pinnedInstallButton, "安装上述指定版本", 285, OnPinnedInstall); buttons.Controls.Add(pinnedInstallButton); grid.Controls.Add(buttons, 0, 7); grid.SetColumnSpan(buttons, 2);
            group.Controls.Add(grid);
            return group;
        }

        private Control BuildInstallDetailsPanel()
        {
            GroupBox group = NewSection("安装进度");
            TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 2 };
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            installDetailBox.Dock = DockStyle.Fill; installDetailBox.Multiline = true; installDetailBox.ReadOnly = true; installDetailBox.WordWrap = false; installDetailBox.ScrollBars = ScrollBars.Both; installDetailBox.ShortcutsEnabled = true; installDetailBox.Font = new Font("Consolas", 8.5F); installDetailBox.BackColor = Color.FromArgb(250, 250, 250);
            grid.Controls.Add(installDetailBox, 0, 0);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            RaisedButton copy = NewButton("复制细节", 105); copy.Click += delegate { try { Clipboard.SetText(installDetailBox.Text); } catch (Exception ex) { ShowError(ex); } };
            RaisedButton open = NewButton("打开日志", 105); open.Click += delegate { if (!string.IsNullOrEmpty(currentInstallLogPath) && File.Exists(currentInstallLogPath)) Process.Start("notepad.exe", "\"" + currentInstallLogPath + "\""); };
            actions.Controls.AddRange(new Control[] { copy, open }); grid.Controls.Add(actions, 0, 1); group.Controls.Add(grid);
            return group;
        }

        private void ToggleInstallDetails()
        {
            if (versionHost == null) return;
            bool collapsed = versionHost.ColumnStyles[1].Width < 1F;
            if (collapsed)
            {
                if (Width < 1120) Width = 1180;
                versionHost.ColumnStyles[1].Width = 330F;
            }
            else versionHost.ColumnStyles[1].Width = 0F;
            detailsButton.Text = collapsed ? "收起 ◀" : "安装进度 ▶";
        }

        internal void ExpandInstallDetailsForTest()
        {
            if (versionHost != null && versionHost.ColumnStyles[1].Width < 1F) ToggleInstallDetails();
        }

        internal void SelectVersionPageForTest()
        {
            SwitchPage(true);
        }

        private Control BuildLogGroup(TextBox box)
        {
            GroupBox group = NewSection("运行日志");
            TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 7, 10, 9), ColumnCount = 2, RowCount = 1 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125F));
            box.Dock = DockStyle.Fill; box.Multiline = true; box.ReadOnly = true; box.ShortcutsEnabled = true; box.ScrollBars = ScrollBars.Both; box.WordWrap = false; box.Font = new Font("Consolas", 9.5F); box.BackColor = Color.White; box.ForeColor = Color.FromArgb(42, 49, 58);
            ContextMenuStrip menu = new ContextMenuStrip(); menu.Items.Add("复制", null, delegate { if (box.SelectionLength > 0) box.Copy(); }); menu.Items.Add("全选", null, delegate { box.SelectAll(); }); box.ContextMenuStrip = menu;
            grid.Controls.Add(box, 0, 0);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(7, 0, 0, 0) };
            RaisedButton copy = NewButton("复制全部日志", 110); copy.Click += delegate { CopyAllLogs(); };
            RaisedButton clear = NewButton("清空显示", 110); clear.Click += delegate { logBox.Clear(); versionLogBox.Clear(); };
            RaisedButton open = NewButton("打开日志文件夹", 110); open.Click += delegate { Process.Start("explorer.exe", "/select,\"" + sessionLogPath + "\""); };
            actions.Controls.AddRange(new Control[] { copy, clear, open }); grid.Controls.Add(actions, 1, 0); group.Controls.Add(grid);
            return group;
        }

        private void CopyAllLogs()
        {
            try { Clipboard.SetText(logBox.Text); Log("日志已复制到剪贴板（完整记录仍保存在日志文件中）"); }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnCheckLatestStable(object sender, EventArgs e)
        {
            try
            {
                string proxy = CurrentProxyOrThrow();
                latestCheckBusy = true;
                latestRelease = null;
                latestCheckButton.Enabled = false;
                latestReleaseLabel.Text = "官网最新稳定版本号：正在获取…";
                latestReleaseLabel.Enabled = false;
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
                            latestReleaseLabel.Text = "官网最新稳定版本号：" + info.DisplayText;
                            latestReleaseLabel.Enabled = true;
                            latestReleaseLabel.Links.Clear();
                            latestReleaseLabel.Links.Add(0, latestReleaseLabel.Text.Length, info.Url);
                            versionBox.Text = info.Tag;
                            SaveUi();
                            UpdateControls();
                            Log("官网最新稳定版：" + info.DisplayText + "；已自动填入并保存版本号 " + info.Tag);
                        }));
                    }
                    catch (Exception ex)
                    {
                        if (IsDisposed) return;
                        BeginInvoke(new Action(delegate
                        {
                            latestCheckBusy = false;
                            latestReleaseLabel.Text = "官网最新稳定版本号：获取失败（" + ex.Message + "）";
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
            ApplyDetectionDisplay();
            UpdateControls();
        }

        private void ApplyDetectionDisplay()
        {
            if (detection.V2rayNRunning && detection.PortOpen && !string.IsNullOrEmpty(detection.ProxyUrl))
            {
                if (settings.Enabled)
                {
                    detectLabel.Text = "Hermes 专用代理：已启动" + Environment.NewLine + "使用本机 V2rayN  " + detection.ProxyUrl + " 为安装/更新提供代理。";
                    detectLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
                    detectLabel.BackColor = Color.FromArgb(45, 145, 92);
                    detectLabel.ForeColor = Color.White;
                }
                else
                {
                    detectLabel.Text = "已找到本机代理软件：V2rayN  " + detection.ProxyUrl + Environment.NewLine + "未启动给 Hermes 安装/更新使用。";
                    detectLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
                    detectLabel.BackColor = Color.FromArgb(255, 232, 169);
                    detectLabel.ForeColor = Color.FromArgb(92, 60, 9);
                }
            }
            else
            {
                string reason = detection.V2rayNRunning ? "V2rayN 已运行，但代理端口不可连接" : "没有启动代理：未检测到可用的 V2rayN";
                detectLabel.Text = reason + Environment.NewLine + "请启动 V2rayN、开启系统代理，再点击“重新检测”。";
                detectLabel.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
                detectLabel.BackColor = Color.FromArgb(198, 55, 61);
                detectLabel.ForeColor = Color.White;
            }
        }

        internal void ShowProxyUnavailableForTest()
        {
            detection = new DetectionResult { V2rayNRunning = false, PortOpen = false, ProxyUrl = "", Detail = "测试状态" };
            ApplyDetectionDisplay();
            UpdateControls();
        }

        private void UpdateControls()
        {
            enabledBox.Text = settings.Enabled ? "关闭 Hermes 专用代理" : "启动 Hermes 专用代理";
            enabledBox.BackColor = SystemColors.Control;
            enabledBox.ForeColor = settings.Enabled ? Color.FromArgb(24, 108, 76) : SystemColors.ControlText;
            bool ready = settings.Enabled && detection != null && detection.V2rayNRunning && detection.PortOpen && !string.IsNullOrEmpty(detection.ProxyUrl);
            installerButton.Enabled = ready;
            updateButton.Enabled = ready;
            desktopButton.Enabled = ready;
            pinnedInstallButton.Enabled = ready && !installBusy;
            versionDetectButton.Enabled = !installBusy;
            latestCheckButton.Enabled = ready && !latestCheckBusy;
        }

        private string CurrentProxyOrThrow()
        {
            if (!settings.Enabled) return "";
            RefreshDetection();
            if (!detection.V2rayNRunning || string.IsNullOrEmpty(detection.ProxyUrl)) throw new InvalidOperationException("没有启动代理。请先启动 V2rayN 并开启系统代理，再点击“重新检测”。");
            if (!detection.PortOpen) throw new InvalidOperationException("V2rayN 已启动，但代理端口当前不可连接：" + detection.ProxyUrl);
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
                Log("检测本机版本：" + summary.Replace(Environment.NewLine, " | "));
                string commit = VersionProbe.HeadCommit();
                if (string.IsNullOrEmpty(commit))
                {
                    localVersionLabel.Text = "本机当前版本号：未检测到可用提交号";
                    Log("没读到当前提交号；可手动填写版本标签，如 v2026.9.24");
                    return;
                }
                localVersionLabel.Text = "本机当前版本号：" + commit;
                versionBox.Text = commit;          // 自动填入
                SaveUi();
                if (!string.IsNullOrEmpty(settings.SaveWarning)) Log("警告：" + settings.SaveWarning);
                Log("本机版本号已自动填入并保存到随附的 ini 文件：" + commit);
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void OnPinnedInstall(object sender, EventArgs e)
        {
            try
            {
                string raw = versionBox.Text.Trim();
                string v = Launcher.NormalizeVersionInput(raw);
                if (string.IsNullOrEmpty(v)) { Log("请先在「指定版本安装」里填版本号，或点击上方任一获取按钮。"); return; }
                if (!v.Equals(raw, StringComparison.Ordinal))
                    Log("已清洗版本输入：" + raw + " → " + v);

                string proxy = CurrentProxyOrThrow();
                DialogResult dr = MessageBox.Show(this,
                    "即将安装 Hermes，输入版本：\n\n" + v + "\n\n" +
                    "程序会读取官方阶段清单，逐阶段执行并实时记录全部输出；同时安装 Desktop。\n" +
                    "代理：" + (settings.Enabled ? proxy : "（未启用注入）") + "\n\n" +
                    "会克隆代码并安装依赖，耗时较长。确定继续吗？",
                    "确认按指定版本安装", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (dr != DialogResult.OK) { Log("已取消安装。"); return; }

                installBusy = true;
                UpdateControls();
                currentInstallLogPath = Path.Combine(Settings.DirectoryPath, "logs", "install-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                installDetailBox.Clear();
                installProgress.Value = 0;
                installStatusLabel.Text = "准备安装…";
                Log("开始指定版本安装：" + v);
                Log("完整安装日志：" + currentInstallLogPath);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        InstallTarget targetInfo = Launcher.ResolveInstallTarget(v, proxy);
                        Log("已锁定目标：" + targetInfo.Description);
                        string how;
                        string script = Launcher.ResolveInstallScript(targetInfo, proxy, out how);
                        Log("使用安装脚本：" + script + "（" + how + "）");
                        if (Launcher.SupportsStageProtocol(script))
                            Log("脚本支持阶段协议：将逐阶段运行、记录退出码，并以 -NonInteractive 避免隐藏等待。");
                        else
                            Log("脚本不支持阶段协议：将使用兼容模式运行，但仍完整捕获输出。");
                        Log("Git 使用 Bridge 隔离配置（autocrlf=false、longpaths=true），不会读取或改写用户的全局 Git 配置。");
                        Launcher.RunPinnedInstallTracked(script, targetInfo, proxy, settings.Enabled,
                            delegate(string line) { InstallDetail(line); Log("[安装] " + line); },
                            delegate(int done, int total, string stage) { UpdateInstallProgress(done, total, stage); });
                        string command = Launcher.FindHermesCommand();
                        string installDir = VersionProbe.HermesInstallDir();
                        string desktopX64 = string.IsNullOrEmpty(installDir) ? "" : Path.Combine(installDir, "apps", "desktop", "release", "win-unpacked", "Hermes.exe");
                        string desktopArm = string.IsNullOrEmpty(installDir) ? "" : Path.Combine(installDir, "apps", "desktop", "release", "win-arm64-unpacked", "Hermes.exe");
                        Log("========== 安装后核验 ==========");
                        Log("Hermes 命令文件：" + (string.IsNullOrEmpty(command) ? "未找到" : command));
                        Log("Desktop：" + (File.Exists(desktopX64) ? desktopX64 : (File.Exists(desktopArm) ? desktopArm : "未找到")));
                        Log("本机版本：" + VersionProbe.Summary().Replace(Environment.NewLine, " | "));
                        if (string.IsNullOrEmpty(command) || (!File.Exists(desktopX64) && !File.Exists(desktopArm)))
                            throw new InvalidOperationException("官方脚本已返回成功，但安装后核验未找到 CLI 或 Desktop。请复制本页完整日志进行分析。");
                        Log("安装与核验完成。请打开一个新的 CMD/PowerShell 再运行 hermes；已打开的旧终端不会自动刷新 PATH。");
                        if (!IsDisposed) BeginInvoke(new Action(delegate { installBusy = false; installProgress.Value = 100; installStatusLabel.Text = "安装完成"; UpdateControls(); MessageBox.Show(this, "指定版本安装完成，CLI 与 Desktop 均已找到。\n\n请新开一个 CMD/PowerShell 再运行 hermes。\n\n日志：" + currentInstallLogPath, "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Information); }));
                    }
                    catch (Exception ex)
                    {
                        Log("安装失败：" + ex.Message);
                        if (!IsDisposed) BeginInvoke(new Action(delegate { installBusy = false; installStatusLabel.Text = "安装失败（右侧为完整细节）"; if (versionHost != null && versionHost.ColumnStyles[1].Width < 1F) ToggleInstallDetails(); UpdateControls(); MessageBox.Show(this, ex.Message + "\n\n完整日志：\n" + currentInstallLogPath, "指定版本安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }));
                    }
                });
            }
            catch (Exception ex) { ShowError(ex); }
        }

        private void UpdateInstallProgress(int completed, int total, string stage)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<int, int, string>(UpdateInstallProgress), completed, total, stage); } catch { }
                return;
            }
            int value = total <= 0 ? 0 : Math.Max(0, Math.Min(100, completed * 100 / total));
            installProgress.Value = value;
            installStatusLabel.Text = completed >= total ? "安装完成" : "正在安装：" + stage + "（" + (completed + 1) + "/" + total + "）";
        }

        private void InstallDetail(string message)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(InstallDetail), message); } catch { }
                return;
            }
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine;
            AppendLogBox(installDetailBox, line);
            try { if (!string.IsNullOrEmpty(currentInstallLogPath)) File.AppendAllText(currentInstallLogPath, line, new UTF8Encoding(false)); } catch { }
        }

        private void Log(string message)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(Log), message); } catch { }
                return;
            }
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine;
            AppendLogBox(logBox, line);
            AppendLogBox(versionLogBox, line);
            try { File.AppendAllText(sessionLogPath, line, new UTF8Encoding(false)); } catch { }
        }

        private static void AppendLogBox(TextBox box, string line)
        {
            const int maxChars = 500000;
            if (box.TextLength > maxChars)
                box.Text = box.Text.Substring(box.TextLength - maxChars / 2);
            box.AppendText(line);
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
            if (args.Length > 0 && (args[0] == "--self-test" || args[0] == "--self-test-timeout" || args[0] == "--detect-version" || args[0] == "--print-saved-version" || args[0] == "--verify-install-source" || args[0] == "--check-latest-stable" || args[0] == "--inspect-install-stages")) EnableConsoleOutput();
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest();
            if (args.Length > 0 && args[0] == "--self-test-timeout")
            {
                bool passed = Launcher.SelfTestTimeoutGuard(delegate(string line) { Console.WriteLine(line); });
                Console.WriteLine(passed ? "PASS: inactivity timeout stopped the process tree" : "FAIL: inactivity timeout did not fire");
                return passed ? 0 : 8;
            }
            if (args.Length > 0 && args[0] == "--detect-version") { Console.WriteLine("== " + VersionProbe.Summary().Replace(Environment.NewLine, " | ")); Console.WriteLine("== hermes 路径: " + (Launcher.FindHermesCommand() == "" ? "(未找到)" : Launcher.FindHermesCommand())); Console.WriteLine("== install dir: " + VersionProbe.HermesInstallDir()); return 0; }
            if (args.Length > 0 && args[0] == "--print-saved-version") { Console.WriteLine(Settings.Load().InstallVersion); return 0; }
            if (args.Length > 1 && args[0] == "--verify-install-source") return VerifyInstallSource(args[1]);
            if (args.Length > 0 && args[0] == "--check-latest-stable") return CheckLatestStable();
            if (args.Length > 1 && args[0] == "--inspect-install-stages") return InspectInstallStages(args[1]);
            if (args.Length > 1 && args[0] == "--render-ui-screenshot") return RenderUiScreenshot(args[1]);
            Settings s = Settings.Load();
            bool shortcutProxyWarningShown = false;
            if (args.Length > 0 && (args[0] == "--desktop" || args[0] == "--update"))
            {
                DetectionResult shortcutDetection = ProxyDetector.Detect(s.ManualProxy);
                if (s.Enabled && (!shortcutDetection.V2rayNRunning || !shortcutDetection.PortOpen || string.IsNullOrEmpty(shortcutDetection.ProxyUrl)))
                {
                    MessageBox.Show("没有启动代理。\r\n\r\n请先启动 V2rayN 并开启系统代理。Bridge 将打开设置界面，检测正常后再重试。", "Hermes Proxy Bridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    shortcutProxyWarningShown = true;
                }
                else
                try
                {
                    Launcher.StartHermesCommand(args[0] == "--desktop" ? "desktop" : "update", shortcutDetection.ProxyUrl, s.Enabled, args[0] == "--update");
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
            Application.Run(new MainForm(s, shortcutProxyWarningShown));
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

        private static int InspectInstallStages(string script)
        {
            try
            {
                InstallTarget target = new InstallTarget { Kind = VersionKind.Commit, Requested = "639823919ced15640924e905bacab3858c5afd19", Commit = "639823919ced15640924e905bacab3858c5afd19", Branch = "main" };
                IList<string> stages = Launcher.ReadInstallStages(Path.GetFullPath(script), target, "", false, delegate(string line) { Console.WriteLine(line); });
                Console.WriteLine("PASS: " + stages.Count + " stages: " + string.Join(", ", new List<string>(stages).ToArray()));
                return stages.Count > 0 ? 0 : 7;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 7; }
        }

        private static int RenderUiScreenshot(string path)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Settings renderSettings = Settings.Load();
                string renderName = Path.GetFileNameWithoutExtension(path);
                if (renderName.IndexOf("proxy-disabled", StringComparison.OrdinalIgnoreCase) >= 0) renderSettings.Enabled = false;
                if (renderName.IndexOf("proxy-enabled", StringComparison.OrdinalIgnoreCase) >= 0) renderSettings.Enabled = true;
                using (MainForm form = new MainForm(renderSettings, true))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-32000, -32000);
                    form.Show();
                    Application.DoEvents();
                    if (Path.GetFileNameWithoutExtension(path).IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        form.SelectVersionPageForTest();
                        Application.DoEvents();
                    }
                    if (Path.GetFileNameWithoutExtension(path).IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        form.ShowProxyUnavailableForTest();
                        Application.DoEvents();
                    }
                    if (Path.GetFileNameWithoutExtension(path).IndexOf("details", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        form.ExpandInstallDetailsForTest();
                        Application.DoEvents();
                    }
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
            if (!built.Contains("-Branch \"main\" -Commit \"639823919ced15640924e905bacab3858c5afd19\" -IncludeDesktop -NonInteractive"))
            {
                Console.Error.WriteLine("FAIL: exact commit command builder");
                return 1;
            }
            Console.WriteLine("PASS: exact commit command builder");

            ProcessStartInfo proxyInfo = new ProcessStartInfo("cmd.exe");
            Launcher.ApplyProxyEnvironment(proxyInfo, "http://127.0.0.1:10808");
            if (proxyInfo.EnvironmentVariables["ELECTRON_GET_USE_PROXY"] != "1" ||
                proxyInfo.EnvironmentVariables["GLOBAL_AGENT_HTTP_PROXY"] != "http://127.0.0.1:10808" ||
                proxyInfo.EnvironmentVariables["HTTPS_PROXY"] != "http://127.0.0.1:10808")
            {
                Console.Error.WriteLine("FAIL: Electron proxy environment");
                return 1;
            }
            Console.WriteLine("PASS: Electron proxy environment");
            return 0;
        }
    }
}
