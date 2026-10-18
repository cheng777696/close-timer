using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace CloseTimer
{
    /// <summary>命令行（无界面）模式。参数解析在 Program.ParseArgs 里完成。</summary>
    internal static class ConsoleRunner
    {
        public static void PrintUsage()
        {
            Console.WriteLine(Program.UsageText());
            Console.WriteLine("（结束，退出码 0）");
        }

        public static int Run(Program.Options opt)
        {
            int code = RunCore(opt);
            Console.WriteLine("（结束，退出码 " + code + "）");
            return code;
        }

        private static int RunCore(Program.Options opt)
        {
            if (opt.List) return ListProcesses(opt);

            var target = DescribeTarget(opt);
            Console.WriteLine(App.Title + " " + App.Version + "（命令行模式）");
            Console.WriteLine("  目标动作 : " + target);
            Console.WriteLine("  执行时间 : " + DateTime.Now.Add(opt.Span).ToString("yyyy-MM-dd HH:mm:ss") +
                              "  （还有 " + App.DescribeSeconds((long)opt.Span.TotalSeconds) + "）");
            Console.WriteLine("  想中止   : 按 Ctrl+C，或直接关掉这个窗口");
            Console.WriteLine(new string('-', 56));

            // 提前确认目标进程是否存在，不存在就直接提示（避免白等）
            if (opt.Action == TimerAction.CloseApp)
            {
                var items = App.Resolve(opt.Names);
                if (items.Count == 0)
                {
                    Console.WriteLine("提示：现在没有找到 " + string.Join("、", opt.Names.ToArray()) +
                                      " 正在运行；到点时会再查一次。");
                }
            }

            App.KeepAwake(true);
            DateTime end = DateTime.Now.Add(opt.Span);
            DateTime nextReport = DateTime.Now.AddSeconds(30);
            double remain = opt.Span.TotalSeconds;

            while (true)
            {
                remain = (end - DateTime.Now).TotalSeconds;
                if (remain <= 0) break;
                if (DateTime.Now >= nextReport)
                {
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] 剩余 " + App.FormatCountdown(remain));
                    nextReport = DateTime.Now.AddSeconds(30);
                }
                Thread.Sleep(250);
            }

            App.KeepAwake(false);
            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] 时间到，开始执行：" + target);
            return Execute(opt);
        }

        private static int Execute(Program.Options opt)
        {
            if (opt.Action != TimerAction.CloseApp)
            {
                string err = App.ExecutePower(opt.Action, opt.Force);
                if (err != null) { Console.WriteLine("执行失败：" + err); return 2; }
                Console.WriteLine("已发出" + ActionName(opt.Action) + "请求，本程序退出。");
                return 0;
            }

            var items = App.Resolve(opt.Names);
            App.RequestClose(items);                       // 等价于点窗口右上角的 ×
            Console.WriteLine("已发出关闭请求，等待程序自己退出…");
            Thread.Sleep(opt.Force ? 1500 : 4000);

            var alive = App.AliveNames(items);
            int killed = 0;
            if (alive.Count > 0 && opt.Force)
            {
                string lastError;
                killed = App.ForceKill(items, out lastError);
                Thread.Sleep(700);
                if (lastError != null) Console.WriteLine("强制结束时有错误：" + lastError);
                alive = App.AliveNames(items);
            }

            if (alive.Count == 0)
            {
                Console.WriteLine("完成：已关闭 " + string.Join("、", opt.Names.ToArray()) +
                                  (killed > 0 ? "（强制结束 " + killed + " 个进程）" : ""));
                return 0;
            }

            Console.WriteLine("仍在运行：" + Join(alive));
            if (!opt.Force)
                Console.WriteLine("提示：加 --force 参数可以直接强制结束它们。");
            else
                Console.WriteLine("提示：这些程序可能需要以管理员身份才能结束。");
            return 3;
        }

        private static int ListProcesses(Program.Options opt)
        {
            var list = App.EnumerateWindowed();
            if (list.Count == 0)
            {
                Console.WriteLine("没有找到带可见窗口的程序。");
                return 0;
            }

            Console.WriteLine("当前有窗口的程序（共 " + list.Count + " 个）：");
            Console.WriteLine(string.Format("{0,-26}{1,12}  {2}", "进程名", "内存", "窗口标题"));
            Console.WriteLine(new string('-', 76));
            foreach (ProcEntry e in list)
            {
                string name = e.Name.Length > 25 ? e.Name.Substring(0, 25) : e.Name;
                string title = e.WindowTitle ?? "";
                if (title.Length > 40) title = title.Substring(0, 39) + "…";
                Console.WriteLine(string.Format("{0,-26}{1,9:0.0} MB  {2}", name, e.MemoryMB, title));
            }
            Console.WriteLine();
            Console.WriteLine("用法示例： 定时关闭程序.exe --time 30分钟 --process " + list[0].Name + " --force");
            return 0;
        }

        private static string DescribeTarget(Program.Options opt)
        {
            if (opt.Action != TimerAction.CloseApp) return ActionName(opt.Action);
            if (opt.Names.Count == 0) return "（没有指定程序！请用 --process 指定）";
            return "关闭 " + string.Join("、", opt.Names.ToArray()) + (opt.Force ? "（强制结束）" : "（先请求正常退出）");
        }

        private static string ActionName(TimerAction a)
        {
            switch (a)
            {
                case TimerAction.Shutdown: return "关机";
                case TimerAction.Restart: return "重启";
                case TimerAction.Sleep: return "睡眠";
                case TimerAction.Hibernate: return "休眠";
                case TimerAction.Lock: return "锁定屏幕";
                default: return "关闭程序";
            }
        }

        private static string Join(HashSet<string> set)
        {
            var sb = new StringBuilder();
            foreach (string s in set)
            {
                if (sb.Length > 0) sb.Append("、");
                sb.Append(s);
            }
            return sb.ToString();
        }
    }
}
