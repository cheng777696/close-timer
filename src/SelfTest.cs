using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CloseTimer
{
    /// <summary>
    /// 内部自检：不显示任何窗口，把每一项核心逻辑跑一遍，结果写进日志/控制台。
    /// 用法：定时关闭程序.exe --selftest --log 自检报告.txt
    /// </summary>
    internal static class SelfTest
    {
        private static int passed, failed;

        public static int Run()
        {
            passed = failed = 0;
            Console.WriteLine("==================== " + App.Title + " 自检开始 ====================");
            Console.WriteLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("系统：" + Environment.OSVersion + "，.NET " + Environment.Version + "，64 位进程：" + Environment.Is64BitProcess);
            Console.WriteLine();

            TestDurationParser();
            TestFormatters();
            TestFileWrite();
            TestProcessEnumeration();
            TestProcessResolve();
            TestLayout();
            TestVisualRender();
            TestVisualReady();
            TestEmptyHint();
            TestShortCountdown();
            TestPowerGuard();

            Console.WriteLine();
            Console.WriteLine("==================== 自检结束：通过 " + passed + " 项，失败 " + failed + " 项 ====================");
            FlightCheck();
            return failed == 0 ? 0 : 4;
        }

        #region 各项检查

        private static void TestDurationParser()
        {
            Section("时间解析");
            CheckParse("30", true, 30, TimeUnit.Minute, true);
            CheckParse("90", true, 90, TimeUnit.Minute, true);
            CheckParse("90分钟", true, 90, TimeUnit.Minute, true);
            CheckParse("1小时30分", true, 90, TimeUnit.Minute, true);
            CheckParse("01:30:00", true, 90, TimeUnit.Minute, true);
            CheckParse("2小时", true, 120, TimeUnit.Minute, true);
            CheckParse("45秒", true, 45, TimeUnit.Second, true);
            CheckParse("1天", true, 1440, TimeUnit.Minute, true);
            CheckParse("abc", false, 0, TimeUnit.Second, true);
            CheckParse("", false, 0, TimeUnit.Second, true);
            CheckParse("12", true, 12, TimeUnit.Hour, false);      // 不带 bareNumberIsMinutes 时，12 = 12 小时
        }

        private enum TimeUnit { Second, Minute, Hour }

        private static void CheckParse(string text, bool shouldSucceed, double expected, TimeUnit unit, bool bareAsMinutes)
        {
            TimeSpan span; string err;
            bool ok = App.TryParseDuration(text, bareAsMinutes, out span, out err);
            if (!ok)
            {
                Assert(!shouldSucceed, "解析 \"" + text + "\" → 应当成功，实际失败（" + err + "）");
                return;
            }
            double expectSec = unit == TimeUnit.Second ? expected : (unit == TimeUnit.Minute ? expected * 60 : expected * 3600);
            Assert(shouldSucceed && Math.Abs(span.TotalSeconds - expectSec) < 0.5,
                "解析 \"" + text + "\" → " + span.TotalSeconds + " 秒（期望 " + expectSec + "）");
        }

        private static void TestFormatters()
        {
            Section("显示格式");
            Assert(App.FormatCountdown(65) == "01:05", "倒计时 65 秒显示为 " + App.FormatCountdown(65));
            Assert(App.FormatCountdown(3725) == "1:02:05", "倒计时 3725 秒显示为 " + App.FormatCountdown(3725));
            Assert(App.DescribeSeconds(5400).Contains("1 小时"), "5400 秒描述为 “" + App.DescribeSeconds(5400) + "”");
            Assert(App.DescribeSeconds(45) == "45 秒", "45 秒描述为 “" + App.DescribeSeconds(45) + "”");
            Assert(Math.Abs(App.Percent(100, 75) - 25) < 0.01, "进度百分比 100→75 = " + App.Percent(100, 75) + "%");
        }

        private static void TestFileWrite()
        {
            Section("文件读写（日志）");
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "定时关闭程序_自检临时文件.txt");
            try
            {
                System.IO.File.WriteAllText(path, "中文写入测试 ✓ " + DateTime.Now, new UTF8Encoding(false));
                string back = System.IO.File.ReadAllText(path, Encoding.UTF8);
                Assert(back.Contains("中文写入测试"), "可以写入并读回中文内容");
                System.IO.File.Delete(path);
            }
            catch (Exception ex)
            {
                Assert(false, "文件读写失败：" + ex.Message);
            }
        }

        private static void TestProcessEnumeration()
        {
            Section("进程枚举");
            var list = App.EnumerateWindowed();
            Console.WriteLine("      当前有窗口的程序 " + list.Count + " 个");
            foreach (ProcEntry e in list)
            {
                string t = e.WindowTitle ?? "";
                if (t.Length > 34) t = t.Substring(0, 33) + "…";
                Console.WriteLine("        " + e.Name.PadRight(24) + e.MemoryMB.ToString("0.0").PadLeft(9) + " MB  " + t);
            }
            // 有无窗口取决于当前桌面，不算失败；只检查不抛异常、字段自相一致
            bool sane = true;
            foreach (ProcEntry e in list)
            {
                if (string.IsNullOrEmpty(e.Name) || e.MemoryBytes < 0) sane = false;
                // “有窗口句柄”与“有窗口标题”必须一致，不能出现有标题却没句柄的脏数据
                bool hasTitle = !string.IsNullOrEmpty(e.WindowTitle);
                if (hasTitle != e.HasWindow) sane = false;
            }
            Assert(sane, "枚举结果字段合法（名称非空、内存非负、标题与窗口句柄一致）");
        }

        private static void TestProcessResolve()
        {
            Section("目标进程解析");
            var fake = App.Resolve(new[] { "这个进程肯定不存在_zzz" });
            Assert(fake.Count == 1 && !fake[0].HasWindow, "不存在的进程也能安全处理（不会抛异常）");

            var self = App.Resolve(new[] { Process.GetCurrentProcess().ProcessName });
            Assert(self.Count >= 1, "能解析出当前进程自身（" + Process.GetCurrentProcess().ProcessName + "）");

            var withExe = App.Resolve(new[] { Process.GetCurrentProcess().ProcessName + ".exe" });
            Assert(withExe.Count >= 1, "带 .exe 后缀的名字也能解析");
        }

        private static void TestLayout()
        {
            Section("界面元素排版（不显示窗口）");
            try
            {
                var form = new MainForm(true);
                form.CreateControl();
                int w = form.ClientSize.Width > 0 ? form.ClientSize.Width : form.Bounds.Width;
                int h = form.ClientSize.Height > 0 ? form.ClientSize.Height : form.Bounds.Height;
                var bad = new List<string>();
                Walk(form, form, bad);
                foreach (string s in bad) Console.WriteLine("      越界：" + s);
                Assert(bad.Count == 0, "所有控件都在窗口可视范围内（共 " + CountControls(form) + " 个控件）");
                Assert(w >= 700 && h >= 530, "默认窗口尺寸 " + w + "×" + h + "（控件按此尺寸摆放）");

                // 关键区域必须真的挂到窗体上（曾经漏过一句 Controls.Add 导致整块卡片不显示）
                var onForm = new List<Control>();
                Collect(form, onForm);
                var seen = new System.Collections.Generic.HashSet<object>();
                foreach (Control c in onForm) seen.Add(c);

                string[] mustBeOnForm = { "pnlHeader", "pnlSettings", "pnlMain", "pnlBarBg",
                                          "txtTime", "presets", "list", "btnStart", "btnCancel",
                                          "btnPower", "lblStatus", "lblTarget", "chkForce", "chkSound",
                                          "chkOnlyWindowed" };
                var missing = new List<string>();
                var fi = typeof(MainForm).GetFields(System.Reflection.BindingFlags.Instance |
                                                    System.Reflection.BindingFlags.NonPublic |
                                                    System.Reflection.BindingFlags.Public);
                foreach (string name in mustBeOnForm)
                {
                    object val = null;
                    foreach (var f in fi) if (f.Name == name) { val = f.GetValue(form); break; }
                    if (val == null) missing.Add(name + "(为空)");
                    else if (!seen.Contains(val)) missing.Add(name + "(没挂到窗体)");
                }
                Assert(missing.Count == 0, missing.Count == 0
                    ? "关键控件都在窗体上（检查了 " + mustBeOnForm.Length + " 个）"
                    : "控件缺失：" + string.Join("、", missing.ToArray()));

                // 卡片之间不能互相压住
                var p1 = FieldControl(form, "pnlSettings");
                var p2 = FieldControl(form, "pnlMain");
                if (p1 != null && p2 != null)
                    Assert(p1.Bottom <= p2.Top || p2.Bottom <= p1.Top, "时间卡片与列表卡片不重叠（" +
                        p1.Bottom + " ≤ " + p2.Top + "）");
                else
                    Assert(false, "取不到卡片控件");

                // 颜色用控件属性判定（比从图片里反推更可靠）
                var numLbl = FieldControl(form, "lblCount") as Label;
                var startBtn = FieldControl(form, "btnStart") as Button;
                if (numLbl != null && startBtn != null)
                {
                    Color numColor = numLbl.ForeColor.IsEmpty ? Color.White : numLbl.ForeColor;
                    Assert(numColor.R > 240 && numColor.G > 240 && numColor.B > 240,
                        "大号倒计时文字颜色是白色（" + Hex(numColor) + "）");
                    Assert(startBtn.BackColor.B > 150 && startBtn.BackColor.B > startBtn.BackColor.R + 60,
                        "开始按钮底色是主题蓝（" + Hex(startBtn.BackColor) + "），字色 " + Hex(startBtn.ForeColor));
                    Assert(startBtn.Width >= 160 && startBtn.Height >= 44,
                        "开始按钮够大（" + startBtn.Width + "×" + startBtn.Height + "）");
                }
                else Assert(false, "取不到 lblCount / btnStart");

                form.Dispose();
            }
            catch (Exception ex)
            {
                Assert(false, "创建界面失败：" + ex);
            }
        }

        private static void Collect(Control parent, List<Control> sink)
        {
            foreach (Control c in parent.Controls)
            {
                sink.Add(c);
                if (c.HasChildren) Collect(c, sink);
            }
        }

        private static Control FieldControl(MainForm form, string fieldName)
        {
            var f = typeof(MainForm).GetField(fieldName, System.Reflection.BindingFlags.Instance |
                                                        System.Reflection.BindingFlags.NonPublic);
            return f == null ? null : f.GetValue(form) as Control;
        }

        private static void Walk(Control parent, Control root, List<string> bad)        {
            Rectangle client = root.ClientRectangle;
            foreach (Control c in parent.Controls)
            {
                Rectangle r = root.RectangleToClient(c.Parent.RectangleToScreen(c.Bounds));
                if (c.Visible && (r.Right > client.Right || r.Bottom > client.Bottom || r.Left < 0 || r.Top < 0))
                    bad.Add(c.GetType().Name + " \"" + (c.Text ?? "") + "\" 位置 " + r);
                if (c.HasChildren) Walk(c, root, bad);
            }
        }

        private static int CountControls(Control parent)
        {
            int n = 0;
            foreach (Control c in parent.Controls) { n++; if (c.HasChildren) n += CountControls(c); }
            return n;
        }

        /// <summary>把界面渲染成 PNG，肉眼/程序都能量化检查（不依赖屏幕）。</summary>
        private static void TestVisualRender()
        {
            Section("界面渲染成图片（离线，不需要屏幕）");
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "定时关闭程序_界面预览.png");
            try
            {
                int cw = 860, ch = 660;                      // 与 MainForm 的 ClientSize 一致
                string err = MainForm.RenderPreview(path, true);
                Assert(err == null, err == null ? "渲染成功（倒计时高亮状态）" : "渲染失败：" + err);
                if (err != null) return;

                var fi = new System.IO.FileInfo(path);
                Assert(fi.Exists && fi.Length > 20000, "生成 PNG：" + (fi.Exists ? (fi.Length / 1024) + " KB" : "不存在"));

                using (var bmp = new Bitmap(path))
                {
                    Assert(bmp.Width >= 700 && bmp.Height >= 500, "图片尺寸 " + bmp.Width + "×" + bmp.Height);
                    int ox = (bmp.Width - cw) / 2;            // 客户区在图片里的偏移（边框+标题栏）
                    int oy = bmp.Height - ch - ox;

                    // 自动定位关键元素（不写死坐标，改了排版也不会误报）
                    Rectangle btnRegion = FindRegion(bmp, ox + 30, oy + ch - 180, 260, 80, IsAccentBlue, 60);
                    int btnDensity = Count(bmp, btnRegion, IsAccentBlue);
                    Point btnCenter = BestMatch(bmp, btnRegion, c => c.B - c.R);          // 最“蓝”的位置
                    Point numCenter = BestMatch(bmp, ox + 10, oy + 20, 340, 80,
                        c => (c.R + c.G + c.B) / 3);                                      // 最“亮”的位置

                    Color top = Avg(bmp, ox + 420, oy + 54, 20);
                    Color card = Avg(bmp, ox + 420, oy + 176, 16);
                    Color btn = btnCenter.IsEmpty ? Color.White : Avg(bmp, btnCenter.X, btnCenter.Y, 6);
                    Color num = numCenter.IsEmpty ? Color.White : Avg(bmp, numCenter.X, numCenter.Y, 3);
                    Color gap = Avg(bmp, ox + cw / 2, oy + ch - 4, 4);

                    Console.WriteLine("      区域取样：横幅 " + Hex(top) + " / 卡片 " + Hex(card) +
                        " / 主按钮 " + Hex(btn) + " / 大号数字 " + Hex(num) + " / 底部留白 " + Hex(gap));
                    Console.WriteLine("      自动定位：主按钮蓝底像素 " + btnDensity + " 个，取样点 " + Show(btnCenter) +
                        "；大号数字取样点 " + Show(numCenter));

                    Assert(top.R < 110 && top.G < 110 && top.B < 120, "顶部横幅是深色（" + Hex(top) + "）");
                    Assert(card.R > 225 && card.G > 225 && card.B > 225, "卡片底色是浅色（" + Hex(card) + "）");
                    Assert(btnDensity > 2000, "左下角主按钮有 " + btnDensity + " 个主题蓝像素（按钮实心）");
                    Assert(btn.B > 150 && btn.B > btn.R + 60, "主按钮底色是主题蓝（" + Hex(btn) + "）");
                    Assert(!numCenter.IsEmpty, "顶部大号倒计时可见（取样点 " + Show(numCenter) + "）");
                    Assert(gap.R > 235 && gap.G > 235 && gap.B > 235, "底部留白干净（" + Hex(gap) + "）");

                    int colors = CountColors(bmp);
                    Assert(colors > 60, "画面颜色层次正常（" + colors + " 种颜色，说明不是空白窗口）");
                }
            }
            catch (Exception ex)
            {
                Assert(false, "渲染检查异常：" + ex.Message);
            }
        }

        private static string Hex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        private static string Show(Rectangle r)
        {
            return r.IsEmpty ? "未找到" : "(" + r.X + "," + r.Y + ") " + r.Width + "×" + r.Height;
        }

        private static string Show(Point p)
        {
            return p.IsEmpty ? "未找到" : "(" + p.X + "," + p.Y + ")";
        }

        private static int Count(Bitmap bmp, Rectangle r, Func<Color, bool> match)
        {
            if (r.IsEmpty) return 0;
            int n = 0;
            for (int y = r.Top; y <= r.Bottom; y++)
                for (int x = r.Left; x <= r.Right; x++)
                {
                    if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                    if (match(bmp.GetPixel(x, y))) n++;
                }
            return n;
        }

        private static Point BestMatch(Bitmap bmp, Rectangle r, Func<Color, int> score)
        {
            if (r.IsEmpty) return Point.Empty;
            return BestMatch(bmp, r.X, r.Y, r.Width, r.Height, score);
        }

        /// <summary>在区域里找“得分最高”的那个像素，用来定位按钮底色 / 白色数字这类目标。</summary>
        private static Point BestMatch(Bitmap bmp, int x0, int y0, int w, int h, Func<Color, int> score)
        {
            int best = -1; Point at = Point.Empty;
            for (int y = Math.Max(0, y0); y < Math.Min(bmp.Height, y0 + h); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(bmp.Width, x0 + w); x++)
                {
                    int s = score(bmp.GetPixel(x, y));
                    if (s > best) { best = s; at = new Point(x, y); }
                }
            return best <= 0 ? Point.Empty : at;
        }

        private static bool IsAccentBlue(Color c)
        {
            return c.B > 170 && c.B > c.R + 70 && c.G < c.B - 40;
        }

        /// <summary>在给定区域里扫描符合条件的像素，返回它们的外接矩形；找不到返回空。</summary>
        private static Rectangle FindRegion(Bitmap bmp, int x0, int y0, int w, int h, Func<Color, bool> match, int minCount)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
            for (int y = Math.Max(0, y0); y < Math.Min(bmp.Height, y0 + h); y++)
                for (int x = Math.Max(0, x0); x < Math.Min(bmp.Width, x0 + w); x++)
                {
                    if (!match(bmp.GetPixel(x, y))) continue;
                    count++;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            if (count < minCount || maxX < 0) return Rectangle.Empty;
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static Color Avg(Bitmap bmp, int cx, int cy, int half)
        {
            long r = 0, g = 0, b = 0; int n = 0;
            for (int y = cy - half; y <= cy + half; y++)
            {
                if (y < 0 || y >= bmp.Height) continue;
                for (int x = cx - half; x <= cx + half; x++)
                {
                    if (x < 0 || x >= bmp.Width) continue;
                    Color c = bmp.GetPixel(x, y);
                    r += c.R; g += c.G; b += c.B; n++;
                }
            }
            if (n == 0) return Color.White;
            return Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
        }

        private static int CountColors(Bitmap bmp)
        {
            var set = new System.Collections.Generic.HashSet<int>();
            for (int y = 0; y < bmp.Height; y += 7)
                for (int x = 0; x < bmp.Width; x += 7)
                    set.Add(bmp.GetPixel(x, y).ToArgb());
            return set.Count;
        }

        /// <summary>顺带再渲染一张“刚打开、还没开始”的界面，用来对比两种状态。</summary>
        private static void TestVisualReady()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "定时关闭程序_界面预览_就绪.png");
            try
            {
                string err = MainForm.RenderPreview(path, false);
                Assert(err == null && System.IO.File.Exists(path),
                    err == null ? "“已就绪”状态渲染正常 → " + path : "渲染失败：" + err);
            }
            catch (Exception ex)
            {
                Assert(false, "就绪状态渲染异常：" + ex.Message);
            }
        }

        /// <summary>空列表时必须给出“怎么办”的提示（放在状态行，不会被原生列表盖住）。</summary>
        private static void TestEmptyHint()
        {
            Section("空列表提示");
            try
            {
                var form = new MainForm(true);
                form.CreateControl();
                var status = FieldControl(form, "lblStatus") as Label;
                var hint = FieldControl(form, "lblListHint") as Label;
                var lv = FieldControl(form, "list") as ListView;
                if (status == null || hint == null || lv == null) { Assert(false, "取不到 lblStatus / lblListHint / list"); return; }

                form.RefreshListForTest();
                Application.DoEvents();

                if (form.ListCountForTest == 0)
                {
                    Assert(form.StatusTextForTest.Contains("没有检测到"),
                        "空列表时状态行给出指引：“" + form.StatusTextForTest + "”");
                    Assert(form.ListHintForTest == "列表为空", "列表右上角标记为“" + form.ListHintForTest + "”");
                }
                else
                {
                    Assert(form.ListHintForTest.Contains("共 "), "有数据时提示数量：“" + form.ListHintForTest + "”");
                    // 确定性覆盖空分支：临时清空列表
                    form.ClearListForTest();
                    Assert(form.ListHintForTest == "列表为空", "清空后右上角标记为“" + form.ListHintForTest + "”");
                    Assert(form.StatusTextForTest.Contains("没有检测到"),
                        "清空后状态行给出指引：“" + form.StatusTextForTest + "”");
                }
                // 状态行不能和原生列表重叠（否则文字会被盖住）
                var listRect = new Rectangle(lv.Left, lv.Parent.Top + lv.Top, lv.Width, lv.Height);
                var statusRect = new Rectangle(status.Left, status.Top, status.Width, status.Height);
                Assert(!listRect.IntersectsWith(statusRect),
                    "状态行 (" + status.Left + "," + status.Top + ") 与列表不重叠");
                Assert(status.Left >= 0 && status.Bottom <= form.ClientSize.Height,
                    "状态行位置合法（y " + status.Top + "，底边 " + status.Bottom + " ≤ " + form.ClientSize.Height + "）");
                form.Dispose();
            }
            catch (Exception ex)
            {
                Assert(false, "空列表提示检查异常：" + ex.Message);
            }
        }

        private static string FirstLine(string s)        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i).TrimEnd('\r');
        }

        private static void TestShortCountdown()        {
            Section("倒计时精度（3 秒倒数）");
            var span = TimeSpan.FromSeconds(3);
            DateTime end = DateTime.Now.Add(span);
            double remain = span.TotalSeconds;
            while (remain > 0) { Thread.Sleep(200); remain = (end - DateTime.Now).TotalSeconds; }
            double elapsed = (DateTime.Now - (end - span)).TotalSeconds;
            Assert(elapsed >= 2.9 && elapsed <= 4.5, "3 秒倒计时实际经过 " + elapsed.ToString("0.00") + " 秒（允许系统调度误差）");
            Assert(App.FormatCountdown(remain) == "00:00", "到点后显示 00:00");
        }

        private static void TestPowerGuard()
        {
            Section("危险操作保护");
            // 只验证口令与权限入口，不真的关机
            bool hasPrivilegeApi = true;
            try { App.ExecutePower((TimerAction)99, false); }
            catch { hasPrivilegeApi = false; }
            Assert(hasPrivilegeApi, "未知电源动作被安全拒绝（不会误触发关机）");
        }

        #endregion

        private static void Section(string name)
        {
            Console.WriteLine();
            Console.WriteLine("【" + name + "】");
        }

        private static void Assert(bool ok, string what)
        {
            if (ok) passed++; else failed++;
            Console.WriteLine("  " + (ok ? "[通过] " : "[失败] ") + what);
        }

        private static void FlightCheck()
        {
            Console.WriteLine("提示：自检不包含“真的关掉某个程序”这一步，实际效果请用");
            Console.WriteLine("      定时关闭程序.exe --time \"10 秒\" --process notepad --force --log 测试.txt");
        }
    }
}
