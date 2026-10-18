using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CloseTimer
{
    internal static class Program
    {
        private const string AppName = "定时关闭程序";

        [STAThread]
        private static int Main(string[] args)
        {
            // 命令行模式：全部参数都能解析时走控制台流程，否则打开图形界面
            bool wantHelp;
            string parseError;
            var opt = ParseArgs(args, out wantHelp, out parseError);
            if (opt != null && opt.TracePath != null) App.SetTrace(opt.TracePath);
            App.Trace("Main 进入，参数 " + string.Join(" ", args) + "；解析结果 " + (opt == null ? "null(图形界面)" : "命令行模式") +
                      (parseError == null ? "" : " 解析错误:" + parseError));

            if (wantHelp)
            {
                if (App.HookConsole()) ConsoleRunner.PrintUsage();
                else MessageBox.Show(UsageText(), AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            if (opt != null && opt.SelfTest)
            {
                App.HookConsole(opt.LogPath);
                return SelfTest.Run();
            }

            if (opt != null && parseError == null)
            {
                if (App.HookConsole(opt.LogPath)) return ConsoleRunner.Run(opt);
                if (opt.LogPath != null) return ConsoleRunner.Run(opt);   // 至少写进日志
                // 连控制台都拿不到（例如从资源管理器直接双击带参数运行）→ 退回图形界面
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            App.Trace("准备创建主窗口");
            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
            {
                LogCrash(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                LogCrash(e.ExceptionObject as Exception);
            };
            try
            {
                var form = new MainForm(false);
                App.Trace("主窗口创建完成");
                if (opt != null && !string.IsNullOrEmpty(opt.TestProcess))
                    form.ScheduleSelfTest(opt.TestProcess, opt.TestSeconds, opt.ShotPath);
                else if (opt != null && !string.IsNullOrEmpty(opt.ShotPath))
                    form.ScheduleShot(opt.ShotPath, 3000);
                Application.Run(form);
                App.Trace("消息循环结束，退出");
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                if (opt == null || opt.LogPath == null)
                    MessageBox.Show("程序启动失败：" + ex.Message + "\n\n详细信息已写入：%TEMP%\\定时关闭程序_错误.log",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            return 0;
        }

        /// <summary>图形界面里的异常没法打印到控制台，写到临时文件里方便排查。</summary>
        internal static void LogCrash(Exception ex)
        {
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "定时关闭程序_错误.log");
                System.IO.File.AppendAllText(path,
                    "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ====" + Environment.NewLine +
                    (ex == null ? "(无异常对象)" : ex.ToString()) + Environment.NewLine + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        internal sealed class Options
        {
            public TimeSpan Span;
            public List<string> Names = new List<string>();
            public TimerAction Action = TimerAction.CloseApp;
            public bool Force;
            public bool List;
            public bool NoBalloon;
            public bool SelfTest;          // --selftest：内部自检
            public string LogPath;
            public string TracePath;       // --trace 路径：内部跟踪（排查启动问题）
            public string ShotPath;        // --shot 路径：仅用于自检/截图
            public string TestProcess;     // --test-close 进程名：仅用于自检
            public double TestSeconds = 8;
        }

        internal static Options ParseArgs(string[] args, out bool help, out string error)
        {
            help = false;
            error = null;
            var o = new Options();
            string timeText = null;
            bool initialized = false;
            bool cliIntent = false;      // 出现了“只有命令行模式才讲得通”的参数

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.IsNullOrWhiteSpace(a)) continue;
                string key, val;
                int eq = a.IndexOf('=');
                if (a.StartsWith("-") && eq > 0) { key = a.Substring(0, eq); val = a.Substring(eq + 1); }
                else { key = a; val = null; }

                switch (key.ToLowerInvariant())
                {
                    case "-h": case "-?": case "/?": case "--help":
                        help = true; return null;

                    case "--list":
                        o.List = true; cliIntent = true; break;

                    case "--selftest":
                        o.SelfTest = true; cliIntent = true; break;

                    case "--gui":
                        return null;                       // 强制图形界面

                    case "--force": case "-f":
                        o.Force = true; cliIntent = true; break;

                    case "--no-balloon":
                        o.NoBalloon = true; break;

                    case "--log":
                        if (val == null && i + 1 < args.Length) val = args[++i];
                        o.LogPath = val; break;

                    case "--trace":
                        if (val == null && i + 1 < args.Length) val = args[++i];
                        o.TracePath = val; break;

                    case "--shot":
                        if (val == null && i + 1 < args.Length) val = args[++i];
                        o.ShotPath = val; break;

                    case "--test-close":
                        if (val == null && i + 1 < args.Length) val = args[++i];
                        o.TestProcess = val; break;

                    case "--test-seconds":
                        if (val == null && i + 1 < args.Length) val = args[++i];
                        double ts;
                        if (val != null && double.TryParse(val, out ts) && ts > 0) o.TestSeconds = ts;
                        break;

                    case "--time": case "-t":
                        if (val == null && i + 1 < args.Length) val = args[++i];
                        timeText = val; cliIntent = true; break;

                    case "--process": case "-p":
                        if (val == null && i + 1 < args.Length) val = args[++i];
                        cliIntent = true;
                        if (!string.IsNullOrEmpty(val))
                            foreach (string s in val.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                                o.Names.Add(Unquote(s.Trim()));
                        break;

                    case "--action": case "-a":
                        if (val == null && i + 1 < args.Length) val = args[++i];
                        cliIntent = true;
                        val = (val ?? "").Trim().ToLowerInvariant();
                        if (val == "close" || val == "closeapp" || val == "关闭") o.Action = TimerAction.CloseApp;
                        else if (val == "shutdown" || val == "poweroff" || val == "关机") o.Action = TimerAction.Shutdown;
                        else if (val == "restart" || val == "reboot" || val == "重启") o.Action = TimerAction.Restart;
                        else if (val == "sleep" || val == "睡眠") o.Action = TimerAction.Sleep;
                        else if (val == "hibernate" || val == "休眠") o.Action = TimerAction.Hibernate;
                        else if (val == "lock" || val == "锁定") o.Action = TimerAction.Lock;
                        else { error = "无法识别的 --action 值：" + val; return o; }
                        break;

                    default:
                        if (key.StartsWith("-")) { error = "无法识别的参数：" + key + "（可用 --help 查看用法）"; return o; }
                        if (!initialized) { timeText = a; initialized = true; }   // 第一个裸参数当作时间
                        else o.Names.Add(Unquote(a));                             // 其余当作进程名
                        break;
                }
            }

            if (o.List)
            {
                if (string.IsNullOrWhiteSpace(timeText)) o.Span = TimeSpan.Zero;
                else if (!App.TryParseDuration(timeText, true, out o.Span, out error)) return o;
                return o;
            }

            if (string.IsNullOrWhiteSpace(timeText) && !cliIntent) return null;    // 纯图形界面
            if (string.IsNullOrWhiteSpace(timeText))
            {
                error = "命令行模式必须给时间，例如 --time 30分钟（可用 --help 查看用法）";
                return o;
            }

            string err;
            if (!App.TryParseDuration(timeText, true, out o.Span, out err)) { error = err; return o; }
            if (o.Span.TotalSeconds < 1) { error = "时间必须大于 0。"; return o; }
            return o;
        }

        private static string Unquote(string s)
        {
            if (s.Length >= 2 &&
                ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '\'' && s[s.Length - 1] == '\'')))
                return s.Substring(1, s.Length - 2);
            return s;
        }

        internal static string UsageText()
        {
            return
                AppName + " " + App.Version + Environment.NewLine + Environment.NewLine +
                "不带任何参数运行 = 打开图形界面。" + Environment.NewLine +
                "带 --time 参数运行 = 纯倒计时（命令行/计划任务模式），不会再弹窗口。" + Environment.NewLine + Environment.NewLine +
                "参数：" + Environment.NewLine +
                "  --time  <时间>      必填。5 / 5分钟 / 1小时30分 / 01:30:00（纯数字按分钟算）" + Environment.NewLine +
                "  --process <名字>    要关闭的进程名（不带 .exe），多个用逗号分隔" + Environment.NewLine +
                "  --action <动作>     close(默认) / shutdown / restart / sleep / hibernate / lock" + Environment.NewLine +
                "  --force             时间一到直接强制结束，不等程序自己退出" + Environment.NewLine +
                "  --log <文件>        把过程写进日志文件（做计划任务时很有用）" + Environment.NewLine +
                "  --list              只列出当前有窗口的程序（可以作为时间参数）" + Environment.NewLine +
                "  --gui               强制打开图形界面" + Environment.NewLine +
                "  --help              显示这份说明" + Environment.NewLine + Environment.NewLine +
                "示例：" + Environment.NewLine +
                "  定时关闭程序.exe --list" + Environment.NewLine +
                "  定时关闭程序.exe --time 30分钟 --process chrome,notepad --force" + Environment.NewLine +
                "  定时关闭程序.exe --time 2小时 --action shutdown --force";
        }
    }
}
