using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CloseTimer
{
    /// <summary>枚举出来的一条进程信息。</summary>
    internal sealed class ProcEntry
    {
        public string Name;          // 进程名，不含 .exe，例如 chrome
        public long MemoryBytes;     // 工作集
        public string WindowTitle;   // 主窗口标题（可能为空）
        public IntPtr MainWindow;    // 主窗口句柄
        public int IconIndex = -1;   // 在 ImageList 中的图标下标
        public Image Icon;

        public bool HasWindow { get { return MainWindow != IntPtr.Zero; } }
        public double MemoryMB { get { return MemoryBytes / 1048576.0; } }
    }

    /// <summary>应用程序执行的动作。</summary>
    internal enum TimerAction
    {
        CloseApp = 0,   // 关闭选中的程序
        Shutdown = 1,   // 关机
        Restart = 2,    // 重启
        Sleep = 3,      // 睡眠
        Hibernate = 4,  // 休眠
        Lock = 5        // 锁定
    }

    internal static class App
    {
        public const string Title = "定时关闭程序";
        public const string Version = "v1.1";

        #region Win32

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private const uint WM_CLOSE = 0x0010;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        #endregion

        #region 进程枚举

        /// <summary>枚举当前有窗口的进程（按进程名合并，同名多窗口只保留最靠前的一个）。</summary>
        public static List<ProcEntry> EnumerateWindowed()
        {
            return Enumerate(true);
        }

        /// <summary>枚举当前正在运行的所有进程（按进程名合并内存占用）。</summary>
        public static List<ProcEntry> EnumerateAll()
        {
            return Enumerate(false);
        }

        private static List<ProcEntry> Enumerate(bool windowedOnly)
        {
            var list = new List<ProcEntry>();
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return list; }

            int selfId = Process.GetCurrentProcess().Id;
            var best = new Dictionary<string, ProcEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (Process p in all)
            {
                try
                {
                    if (p.Id == selfId) continue;
                    IntPtr h = p.MainWindowHandle;               // 顺带刷新缓存
                    bool hasWindow = h != IntPtr.Zero && IsWindowVisible(h);
                    string title = (p.MainWindowTitle ?? string.Empty).Trim();

                    if (windowedOnly && !hasWindow) continue;
                    if (hasWindow && title.Length == 0) hasWindow = false;   // 有句柄但没标题，当成无窗口

                    var e = new ProcEntry
                    {
                        Name = p.ProcessName,
                        MemoryBytes = p.WorkingSet64,
                        WindowTitle = title,
                        MainWindow = hasWindow ? h : IntPtr.Zero
                    };

                    ProcEntry old;
                    if (!best.TryGetValue(e.Name, out old))
                    {
                        best[e.Name] = e;
                    }
                    else if (old.HasWindow != e.HasWindow)
                    {
                        // 有的实例有窗口、有的没有：永远保留“有窗口”的那条，
                        // 避免出现“有标题但窗口句柄为空”的脏数据
                        if (e.HasWindow) best[e.Name] = e;
                    }
                    else
                    {
                        // 同类实例：内存累加即可，窗口信息保持不变
                        old.MemoryBytes += e.MemoryBytes;
                    }
                }
                catch
                {
                    // 权限不足或进程中途退出，跳过
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }

            list.AddRange(best.Values);
            list.Sort(delegate (ProcEntry a, ProcEntry b)
            {
                // 先按“有没有窗口”，再按占用内存
                if (a.HasWindow != b.HasWindow) return a.HasWindow ? -1 : 1;
                int c = b.MemoryBytes.CompareTo(a.MemoryBytes);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public struct Item
        {
            public string Name;
            public bool HasWindow;
            public override string ToString() { return Name; }
        }

        /// <summary>把选中的进程名转换成含窗口句柄的条目集合。</summary>
        public static List<Item> Resolve(IEnumerable<string> names)
        {
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                wanted.Add(n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? n.Substring(0, n.Length - 4) : n);
            }

            var result = new List<Item>();
            if (wanted.Count == 0) return result;

            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return result; }

            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process p in all)
            {
                try
                {
                    if (!wanted.Contains(p.ProcessName)) continue;
                    IntPtr h = p.MainWindowHandle;
                    result.Add(new Item { Name = p.ProcessName, HasWindow = h != IntPtr.Zero });
                    found.Add(p.ProcessName);
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }

            foreach (string n in wanted)
                if (!found.Contains(n)) result.Add(new Item { Name = n, HasWindow = false });  // 已经退出了
            return result;
        }

        #endregion

        #region 关闭逻辑

        /// <summary>对每个目标进程执行一次“优雅请求退出”（等于点窗口右上角的 × ）。</summary>
        public static int RequestClose(IEnumerable<Item> items)
        {
            int sent = 0;
            foreach (Item it in items)
            {
                Process[] ps;
                try { ps = Process.GetProcessesByName(it.Name); }
                catch { continue; }

                foreach (Process p in ps)
                {
                    try
                    {
                        IntPtr h = p.MainWindowHandle;
                        if (h != IntPtr.Zero && IsWindow(h))
                        {
                            IntPtr ret;
                            SendMessageTimeout(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 3000, out ret);
                            sent++;
                        }
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            return sent;
        }

        /// <summary>强制结束目标进程。</summary>
        public static int ForceKill(IEnumerable<Item> items, out string lastError)
        {
            int killed = 0;
            lastError = null;
            foreach (Item it in items)
            {
                Process[] ps;
                try { ps = Process.GetProcessesByName(it.Name); }
                catch { continue; }

                foreach (Process p in ps)
                {
                    try
                    {
                        p.Kill();
                        killed++;
                    }
                    catch (Exception ex) { lastError = ex.Message; }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            return killed;
        }

        /// <summary>当前仍在运行的进程名（去重）。</summary>
        public static HashSet<string> AliveNames(IEnumerable<Item> items)
        {
            var alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Item it in items)
            {
                try
                {
                    Process[] ps = Process.GetProcessesByName(it.Name);
                    foreach (Process p in ps)
                    {
                        alive.Add(it.Name);
                        try { p.Dispose(); } catch { }
                    }
                }
                catch { }
            }
            return alive;
        }

        /// <summary>等待若干毫秒（用于给程序自己退出的时间）。</summary>
        public static void Wait(int ms) { System.Threading.Thread.Sleep(ms); }

        #endregion

        #region 电源操作

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TokPriv1Luid
        {
            public int Count;
            public long Luid;
            public int Attr;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr h, int acc, ref IntPtr phtok);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool LookupPrivilegeValue(string host, string name, ref long pluid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr htok, bool disall, ref TokPriv1Luid newst, int len, IntPtr prev, IntPtr relen);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool LockWorkStation();

        private const int TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const int TOKEN_QUERY = 0x0008;
        private const int SE_PRIVILEGE_ENABLED = 0x0002;
        private const uint EWX_SHUTDOWN = 0x00000001;
        private const uint EWX_REBOOT = 0x00000002;
        private const uint EWX_FORCE = 0x00000004;
        private const uint EWX_POWEROFF = 0x00000008;

        private static bool EnablePrivilege(string name)
        {
            try
            {
                IntPtr tok = IntPtr.Zero;
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, ref tok)) return false;
                var tp = new TokPriv1Luid { Count = 1, Attr = SE_PRIVILEGE_ENABLED };
                if (!LookupPrivilegeValue(null, name, ref tp.Luid)) return false;
                return AdjustTokenPrivileges(tok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            }
            catch { return false; }
        }

        /// <summary>执行电源/锁定操作，失败时返回错误信息，成功返回 null。</summary>
        public static string ExecutePower(TimerAction action, bool force)
        {
            try
            {
                switch (action)
                {
                    case TimerAction.Lock:
                        if (!LockWorkStation()) return "锁定失败：" + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
                        return null;

                    case TimerAction.Sleep:
                        if (!SetSuspendState(false, force, false)) return "睡眠请求被系统拒绝。";
                        return null;

                    case TimerAction.Hibernate:
                        if (!SetSuspendState(true, force, false)) return "休眠请求被系统拒绝（可能未启用休眠）。";
                        return null;

                    case TimerAction.Shutdown:
                    case TimerAction.Restart:
                        EnablePrivilege("SeShutdownPrivilege");
                        uint flags = (action == TimerAction.Restart) ? EWX_REBOOT : (EWX_SHUTDOWN | EWX_POWEROFF);
                        if (force) flags |= EWX_FORCE;
                        if (!ExitWindowsEx(flags, 0))
                            return "关机请求被拒绝：" + new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
                        return null;
                }
            }
            catch (Exception ex) { return ex.Message; }
            return "未知操作。";
        }

        #endregion

        #region 输出重定向（WinExe 没有控制台，需要手动挂钩）

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleOutputCP(uint wCodePageID);

        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        /// <summary>尝试挂到调用者的控制台；成功返回 true。同时挂上 Ctrl+C 取消处理。</summary>
        public static bool HookConsole(string logPath = null)
        {
            bool haveConsole = false;
            bool haveLog = false;

            if (!string.IsNullOrEmpty(logPath))
            {
                try
                {
                    var sw = new System.IO.StreamWriter(logPath, true, new UTF8Encoding(false));
                    sw.AutoFlush = true;
                    Console.SetOut(sw);
                    Console.SetError(sw);
                    haveLog = true;
                }
                catch { haveLog = false; }
            }

            try
            {
                if (AttachConsole(ATTACH_PARENT_PROCESS) || AllocConsole())
                {
                    haveConsole = true;
                    SetConsoleOutputCP(65001);   // 代码页用 UTF-8，避免中文乱码
                    if (!haveLog)
                    {
                        var stdout = new System.IO.StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                        stdout.AutoFlush = true;
                        Console.SetOut(stdout);
                        var stderr = new System.IO.StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
                        stderr.AutoFlush = true;
                        Console.SetError(stderr);
                    }
                }
            }
            catch { haveConsole = false; }

            if (!haveConsole && !haveLog) return false;

            if (haveLog) Console.WriteLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====");

            try
            {
                Console.CancelKeyPress += delegate (object s, ConsoleCancelEventArgs e)
                {
                    e.Cancel = true;
                    App.KeepAwake(false);
                    Console.WriteLine();
                    Console.WriteLine("已取消（倒计时终止，不会执行任何操作）。");
                    Environment.Exit(130);
                };
            }
            catch { }

            if (haveConsole)
            {
                // 抢走控制台输入，避免父 shell（cmd 脚本）在我们退出后接着执行后续命令
                var drain = new System.Threading.Thread(delegate ()
                {
                    try
                    {
                        while (true)
                        {
                            if (Console.KeyAvailable) Console.ReadKey(true);
                            else System.Threading.Thread.Sleep(120);
                        }
                    }
                    catch { /* 输入被重定向时直接退出 */ }
                });
                drain.IsBackground = true;
                drain.Start();
            }

            return haveConsole;
        }

        #endregion

        #region 其它小工具

        // ---- 内部跟踪（排查启动问题用：--trace <文件>）----
        private static string tracePath;
        private static readonly object traceLock = new object();

        public static void SetTrace(string path) { tracePath = path; }

        public static void Trace(string message)
        {
            if (tracePath == null) return;
            try
            {
                lock (traceLock)
                {
                    System.IO.File.AppendAllText(tracePath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }

        /// <summary>把秒数变成 1小时20分 / 45秒 / 1天2小时 这样的中文描述。</summary>
        public static string DescribeSeconds(long sec)
        {
            if (sec < 0) sec = 0;
            if (sec < 60) return sec + " 秒";
            long d = sec / 86400, h = (sec % 86400) / 3600, m = (sec % 3600) / 60, s = sec % 60;
            var sb = new StringBuilder();
            if (d > 0) sb.Append(d).Append(" 天 ");
            if (h > 0) sb.Append(h).Append(" 小时 ");
            if (m > 0) sb.Append(m).Append(" 分 ");
            if (s > 0 && d == 0) sb.Append(s).Append(" 秒");
            return sb.ToString().Trim();
        }

        /// <summary>倒计时显示：小于 1 小时用 mm:ss，否则 hh:mm:ss。</summary>
        public static string FormatCountdown(double remaining)
        {
            if (remaining < 0) remaining = 0;
            long t = (long)Math.Ceiling(remaining);
            long h = t / 3600, m = (t % 3600) / 60, s = t % 60;
            if (h > 0) return string.Format("{0}:{1:00}:{2:00}", h, m, s);
            return string.Format("{0:00}:{1:00}", m, s);
        }

        /// <summary>倒计时进度条的百分比（0~100）。</summary>
        public static double Percent(double total, double remaining)
        {
            if (total <= 0) return 100;
            double used = (total - remaining) / total * 100.0;
            if (used < 0) used = 0;
            if (used > 100) used = 100;
            return used;
        }

        /// <summary>倒计时期间阻止系统自动睡眠。</summary>
        public static void KeepAwake(bool on)
        {
            try
            {
                SetThreadExecutionState(on ? (ES_CONTINUOUS | ES_SYSTEM_REQUIRED) : ES_CONTINUOUS);
            }
            catch { }
        }

        /// <summary>取出某个 exe 关联的图标，失败返回 null。</summary>
        public static Image ExtractIcon(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return null;
            try
            {
                using (Icon ic = Icon.ExtractAssociatedIcon(exePath))
                {
                    if (ic == null) return null;
                    return ic.ToBitmap();
                }
            }
            catch { return null; }
        }

        /// <summary>
        /// 把秒数（如 5400）解析成 TimeSpan；支持 "90"、"90m"、"1h30m"、"01:30:00"。
        /// bareNumberIsMinutes：纯数字按分钟理解（命令行用），否则 1~24 按小时（界面上更直观）。
        /// </summary>
        public static bool TryParseDuration(string text, out TimeSpan span, out string error)
        {
            return TryParseDuration(text, false, out span, out error);
        }

        public static bool TryParseDuration(string text, bool bareNumberIsMinutes, out TimeSpan span, out string error)
        {
            span = TimeSpan.Zero;
            error = null;
            string s = (text ?? string.Empty).Trim();
            if (s.Length == 0) { error = "请先填写时间。"; return false; }

            // 形式一：01:30:00 / 1:30 / 00:45
            if (s.Contains(":"))
            {
                string[] parts = s.Split(':');
                if (parts.Length > 3) { error = "时间格式看不懂，例如 90、90分钟、1小时30分、01:30:00。"; return false; }
                double[] nums = new double[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                    if (!double.TryParse(parts[i].Trim(), out nums[i]) || nums[i] < 0)
                    { error = "时间格式看不懂，例如 90、90分钟、1小时30分、01:30:00。"; return false; }
                double sec = 0;
                if (parts.Length == 3) sec = nums[0] * 3600 + nums[1] * 60 + nums[2];
                else if (parts.Length == 2) sec = nums[0] * 60 + nums[1];
                else sec = nums[0] * 60;
                span = TimeSpan.FromSeconds(sec);
                return true;
            }

            // 形式二：带单位的中文/英文写法
            var matches = System.Text.RegularExpressions.Regex.Matches(
                s, @"(\d+(?:\.\d+)?)\s*(天|日|d|小时|时|h|钟头|分钟|分|min|m|秒|s)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (matches.Count > 0)
            {
                double total = 0;
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    double v = double.Parse(m.Groups[1].Value);
                    string u = m.Groups[2].Value.ToLowerInvariant();
                    if (u == "天" || u == "日" || u == "d") total += v * 86400;
                    else if (u == "小时" || u == "时" || u == "h" || u == "钟头") total += v * 3600;
                    else if (u == "分钟" || u == "分" || u == "min" || u == "m") total += v * 60;
                    else total += v;
                }
                span = TimeSpan.FromSeconds(total);
                return true;
            }

            // 形式三：纯数字 —— 界面上 1~24 视为“小时”，更大视为“分钟”；命令行一律按“分钟”
            double n;
            if (double.TryParse(s, out n) && n > 0)
            {
                span = (bareNumberIsMinutes || n > 24) ? TimeSpan.FromMinutes(n) : TimeSpan.FromHours(n);
                return true;
            }

            error = "时间格式看不懂，例如 90、90分钟、1小时30分、01:30:00。";
            return false;
        }

        #endregion
    }
}
