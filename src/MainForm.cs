using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CloseTimer
{
    internal sealed class MainForm : Form
    {
        // 配色
        private static readonly Color CInk = Color.FromArgb(31, 41, 55);
        private static readonly Color CInk2 = Color.FromArgb(107, 114, 128);
        private static readonly Color CAccent = Color.FromArgb(37, 99, 235);
        private static readonly Color CAccent2 = Color.FromArgb(29, 78, 216);
        private static readonly Color CCard = Color.FromArgb(248, 250, 252);
        private static readonly Color CCardLine = Color.FromArgb(226, 232, 240);
        private static readonly Color CHeaderTop = Color.FromArgb(17, 24, 39);
        private static readonly Color CHeaderBottom = Color.FromArgb(31, 41, 55);
        private static readonly Color CDanger = Color.FromArgb(220, 38, 38);
        private static readonly Color CGood = Color.FromArgb(21, 128, 61);

        // 控件
        private Panel pnlHeader, pnlSettings, pnlMain, pnlBarBg;
        private Label lblCount, lblSub;
        private FlowLayoutPanel presets;
        private TextBox txtTime;
        private CheckBox chkForce, chkSound, chkOnlyWindowed;
        private Button btnStart, btnCancel, btnRefresh, btnAll, btnNone, btnPower;
        private Button[] actionButtons;
        private ListView list;
        private ImageList icons;
        private Label lblListHint, lblStatus, lblTarget;
        private NotifyIcon tray;
        private System.Windows.Forms.Timer ticker, pulse;
        private Font fontBase, fontBody, fontSmall, fontNum;

        // 状态
        private readonly List<ProcEntry> entries = new List<ProcEntry>();
        private List<string> selectedNames = new List<string>();
        private DateTime endTime;
        private double totalSeconds;
        private bool running, taskRunning, exitRequested, cardFilled;
        private int actionMode, lastBeepSecond = int.MaxValue;
        private string lastHeadline = "已就绪";
        private Icon trayIconOwned;
        private ToolTip tips;

        public MainForm(bool startHidden)
        {
            App.Trace("MainForm 构造开始");
            BuildFonts();
            BuildUi();
            App.Trace("界面构建完成");
            tray.Visible = !startHidden;
            if (startHidden) WindowState = FormWindowState.Minimized;
            Load += delegate
            {
                App.Trace("Load 事件：开始读取进程列表");
                RefreshProcesses();
                App.Trace("Load 事件：进程列表读取完成");
            };
            Shown += delegate
            {
                App.Trace("Shown 事件：窗口已显示 " + Bounds);
                if (startHidden) { Hide(); tray.Visible = true; }
            };
        }

        private void BuildFonts()
        {
            string fam = "Microsoft YaHei UI";
            try
            {
                using (var f = new Font(fam, 9F)) { if (f.Name != fam) fam = "Microsoft YaHei"; }
            }
            catch { fam = "Microsoft YaHei"; }
            try
            {
                using (var f = new Font(fam, 9F)) { if (f.Name != fam) fam = "Segoe UI"; }
            }
            catch { fam = "Segoe UI"; }

            fontBase = new Font(fam, 9F);
            fontBody = new Font(fam, 10F);
            fontSmall = new Font(fam, 8.5F);
            fontNum = new Font(fam, 36F, FontStyle.Bold);
        }

        #region 界面搭建

        private void BuildUi()
        {
            SuspendLayout();

            Text = App.Title + " " + App.Version;
            Font = fontBase;
            BackColor = Color.White;
            ClientSize = new Size(860, 660);
            MinimumSize = new Size(820, 610);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            tips = new ToolTip();

            BuildHeader();
            BuildSettingsCard();
            BuildProcessCard();
            BuildBottomBar();
            BuildTray();

            Resize += delegate { LayoutBottom(); };

            ticker = new System.Windows.Forms.Timer { Interval = 1000 };
            ticker.Tick += TickerTick;
            pulse = new System.Windows.Forms.Timer { Interval = 250 };
            pulse.Tick += delegate { if (running) UpdateHeader((endTime - DateTime.Now).TotalSeconds, true); };

            ResumeLayout(true);
        }

        // ---------- 顶部倒计时横幅 ----------
        private void BuildHeader()
        {
            pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 108),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = CHeaderTop
            };
            pnlHeader.Paint += HeaderPaint;

            lblCount = new Label
            {
                Text = "已就绪",
                Font = fontNum,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(22, 34),
                Cursor = Cursors.Hand
            };
            tips.SetToolTip(lblCount, "倒计时过程中点这里也能取消");
            lblCount.Click += delegate { if (running) StopCountdown("已取消，程序不会关闭。"); };
            pnlHeader.Controls.Add(lblCount);

            lblSub = new Label
            {
                Text = "选好要关闭的程序，再点左下角的“开始倒计时”",
                Font = fontBody,
                ForeColor = Color.FromArgb(148, 163, 184),
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(ClientSize.Width - 44, 22),
                Location = new Point(22, 10),
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            pnlHeader.Controls.Add(lblSub);

            Controls.Add(pnlHeader);
        }

        private void HeaderPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = pnlHeader.ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;

            using (var b = new LinearGradientBrush(r, CHeaderTop, CHeaderBottom, LinearGradientMode.Vertical))
                g.FillRectangle(b, r);

            // 右上角淡淡的装饰圆
            using (var b = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                g.FillEllipse(b, r.Width - 170, -90, 240, 240);
        }

        // ---------- 时间设置卡片 ----------
        private void BuildSettingsCard()
        {
            pnlSettings = new Panel
            {
                Location = new Point(16, 124),
                Size = new Size(ClientSize.Width - 32, 100),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = CCard
            };
            pnlSettings.Paint += CardPaint;

            var lbl = new Label
            {
                Text = "1  设置时间",
                Font = new Font(fontBase.FontFamily, 9.5F, FontStyle.Bold),
                ForeColor = CInk,
                BackColor = Color.Transparent,
                Location = new Point(18, 14),
                AutoSize = true
            };
            pnlSettings.Controls.Add(lbl);

            presets = new FlowLayoutPanel
            {
                Location = new Point(16, 42),
                Size = new Size(pnlSettings.Width - 32, 44),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = CCard
            };
            pnlSettings.Controls.Add(presets);

            // 输入框先建好，预设按钮的回调才能安全引用
            txtTime = new TextBox
            {
                Text = "30 分钟",
                Size = new Size(112, 30),
                Margin = new Padding(2, 3, 2, 0),
                Font = new Font(fontBase.FontFamily, 11.5F),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = CInk
            };
            tips.SetToolTip(txtTime, "可以直接填：90 / 90分钟 / 1小时30分 / 01:30:00");

            foreach (int mins in new[] { 5, 10, 15, 30, 60, 120 })
            {
                int m = mins;
                Button b = FlatButton(mins < 60 ? mins + " 分钟" : (mins / 60) + " 小时", 74, 34);
                b.Click += delegate { txtTime.Text = FmtMinutes(m); };
                presets.Controls.Add(b);
            }

            presets.Controls.Add(new Label
            {
                Text = "自定义",
                Size = new Size(48, 30),
                Margin = new Padding(16, 7, 2, 0),
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = CInk2,
                BackColor = CCard
            });
            presets.Controls.Add(txtTime);

            presets.Controls.Add(new Label
            {
                Text = "写法 90 / 1小时30分 / 01:30:00",
                Size = new Size(224, 30),
                Margin = new Padding(14, 7, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = CInk2,
                BackColor = CCard
            });

            Controls.Add(pnlSettings);
        }

        // ---------- 程序列表卡片 ----------
        private void BuildProcessCard()
        {
            pnlMain = new Panel
            {
                Location = new Point(16, 236),
                Size = new Size(ClientSize.Width - 32, ClientSize.Height - 236 - 178),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = CCard
            };
            pnlMain.Paint += CardPaint;

            var lbl = new Label
            {
                Text = "2  选择要关闭的程序",
                Font = new Font(fontBase.FontFamily, 9.5F, FontStyle.Bold),
                ForeColor = CInk,
                BackColor = Color.Transparent,
                Location = new Point(18, 14),
                AutoSize = true
            };
            pnlMain.Controls.Add(lbl);

            btnRefresh = FlatButton("刷新列表", 84, 30);
            btnRefresh.Location = new Point(16, 38);
            btnRefresh.Click += delegate { RefreshProcesses(); };
            pnlMain.Controls.Add(btnRefresh);

            btnAll = FlatButton("全选", 58, 30);
            btnAll.Location = new Point(108, 38);
            btnAll.Click += delegate { SetAllChecked(true); };
            pnlMain.Controls.Add(btnAll);

            btnNone = FlatButton("全不选", 66, 30);
            btnNone.Location = new Point(172, 38);
            btnNone.Click += delegate { SetAllChecked(false); };
            pnlMain.Controls.Add(btnNone);

            lblListHint = new Label
            {
                Text = "",
                Font = fontSmall,
                ForeColor = CInk2,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(240, 30),
                Location = new Point(248, 38),
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlMain.Controls.Add(lblListHint);

            // 右侧：到点执行什么
            actionButtons = new Button[] { null, null, null, null };
            var powers = new[] { "关机", "重启", "睡眠" };
            for (int i = powers.Length - 1; i >= 0; i--)
            {
                int act = i + 1;
                Button b = FlatButton(powers[i], 62, 30);
                b.Location = new Point(pnlMain.Width - 16 - 62 - (powers.Length - 1 - i) * 68, 38);
                b.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                b.Tag = act;
                b.Click += delegate { SetAction(act); };
                actionButtons[act] = b;
                pnlMain.Controls.Add(b);
            }

            actionButtons[0] = FlatButton("关闭选中程序", 124, 30);
            actionButtons[0].Location = new Point(pnlMain.Width - 16 - 62 * 3 - 6 * 2 - 8 - 124, 38);
            actionButtons[0].Anchor = AnchorStyles.Top | AnchorStyles.Right;
            actionButtons[0].Tag = 0;
            actionButtons[0].Click += delegate { SetAction(0); };
            pnlMain.Controls.Add(actionButtons[0]);

            icons = new ImageList { ImageSize = new Size(20, 20), ColorDepth = ColorDepth.Depth32Bit };

            list = new ListView
            {
                Location = new Point(16, 78),
                Size = new Size(pnlMain.Width - 32, pnlMain.Height - 118),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = false,
                MultiSelect = true,
                HideSelection = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = CInk,
                Font = fontBody,
                SmallImageList = icons,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            list.Columns.Add("程序", 180);
            list.Columns.Add("内存", 92, HorizontalAlignment.Right);
            list.Columns.Add("窗口标题", 454);
            list.ItemChecked += delegate { SyncSelection(); };
            list.DoubleClick += delegate { StartCountdown(true); };
            pnlMain.Controls.Add(list);

            Controls.Add(pnlMain);

            lblTarget = new Label
            {
                Text = "到点执行：关闭选中程序",
                Font = new Font(fontBase.FontFamily, 9.5F, FontStyle.Bold),
                ForeColor = CAccent,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(400, 24),
                Location = new Point(18, pnlMain.Height - 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlMain.Controls.Add(lblTarget);

            Controls.Add(pnlMain);
        }

        // ---------- 底部操作区 ----------
        private void BuildBottomBar()
        {
            pnlBarBg = new Panel
            {
                Location = new Point(16, ClientSize.Height - 168),
                Size = new Size(ClientSize.Width - 32, 5),
                BackColor = Color.FromArgb(235, 239, 245),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            pnlBarBg.Paint += BarPaint;
            Controls.Add(pnlBarBg);

            chkOnlyWindowed = LinkCheck("只列有窗口的程序", 150, 26);
            chkOnlyWindowed.Checked = true;
            chkOnlyWindowed.Location = new Point(16, ClientSize.Height - 153);
            chkOnlyWindowed.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            chkOnlyWindowed.CheckedChanged += delegate { RefreshProcesses(); };
            Controls.Add(chkOnlyWindowed);

            chkForce = LinkCheck("到点后强制结束（不等它自己退出）", 232, 26);
            chkForce.Location = new Point(180, ClientSize.Height - 153);
            chkForce.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(chkForce);

            chkSound = LinkCheck("最后 10 秒提示音", 150, 26);
            chkSound.Checked = true;
            chkSound.Location = new Point(424, ClientSize.Height - 153);
            chkSound.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            Controls.Add(chkSound);

            btnStart = PrimaryButton("开始倒计时", 186, 48);
            btnStart.Location = new Point(16, ClientSize.Height - 118);
            btnStart.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnStart.Click += delegate { StartCountdown(true); };
            Controls.Add(btnStart);

            btnCancel = FlatButton("取消并重置", 124, 48);
            btnCancel.Location = new Point(212, ClientSize.Height - 118);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            btnCancel.Enabled = false;
            btnCancel.Click += delegate { StopCountdown("已取消，程序不会关闭。"); };
            Controls.Add(btnCancel);

            btnPower = FlatButton("最小化到托盘", 124, 48);
            btnPower.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnPower.Click += delegate { HideToTray(); };
            Controls.Add(btnPower);

            lblStatus = new Label
            {
                Text = "就绪",
                Font = fontBase,
                ForeColor = CInk2,
                BackColor = Color.Transparent,
                AutoSize = false,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblStatus);

            LayoutBottom();
        }

        private void LayoutBottom()
        {
            if (btnPower == null) return;
            btnPower.Location = new Point(ClientSize.Width - 16 - btnPower.Width, ClientSize.Height - 118);
            lblStatus.Location = new Point(20, ClientSize.Height - 190);
            lblStatus.Size = new Size(ClientSize.Width - 40, 22);
        }

        private void BarPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, pnlBarBg.ClientSize.Width - 1, pnlBarBg.ClientSize.Height - 1);
            if (r.Width < 4 || r.Height < 2) return;

            int h = r.Height;
            if (h >= 4)
            {
                using (var path = Round(new Rectangle(0, 0, r.Width, h), 2))
                using (var brush = new SolidBrush(Color.FromArgb(233, 238, 245)))
                    g.FillPath(brush, path);

                int w = (int)Math.Round(r.Width * barPercent / 100.0);
                if (w > 2)
                {
                    using (var path = Round(new Rectangle(0, 0, w, h), 2))
                    using (var brush = new SolidBrush(barColor))
                        g.FillPath(brush, path);
                }
            }
            else
            {
                // 高度还没被布局撑开时，先画一条细线
                using (var brush = new SolidBrush(Color.FromArgb(226, 232, 240)))
                    g.FillRectangle(brush, r);
            }
        }

        private double barPercent;
        private Color barColor = CAccent;

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (radius * 2 > r.Height) radius = r.Height / 2;
            if (radius < 1) radius = 1;
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---------- 托盘 ----------
        private void BuildTray()
        {
            try
            {
                var proc = Process.GetCurrentProcess();
                if (proc.MainModule != null) trayIconOwned = Icon.ExtractAssociatedIcon(proc.MainModule.FileName);
            }
            catch { trayIconOwned = null; }

            var menu = new ContextMenuStrip { Font = fontBase };
            menu.Items.Add("打开主窗口", null, delegate { RestoreFromTray(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("取消倒计时", null, delegate { StopCountdown("已取消，程序不会关闭。"); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { RealExit(); });

            tray = new NotifyIcon
            {
                Icon = trayIconOwned != null ? trayIconOwned : SystemIcons.Application,
                Text = App.Title,
                ContextMenuStrip = menu,
                Visible = false
            };
            tray.DoubleClick += delegate { RestoreFromTray(); };
        }

        #endregion

        #region 自绘控件小工具

        private static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        private void CardPaint(object sender, PaintEventArgs e)
        {
            var p = (Panel)sender;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, p.ClientSize.Width - 1, p.ClientSize.Height - 1);
            if (r.Width < 4 || r.Height < 4) return;
            using (var path = Round(r, 10))
            {
                using (var b = new SolidBrush(CCard)) g.FillPath(b, path);
                using (var pen = new Pen(CCardLine)) g.DrawPath(pen, path);
            }
            if (!cardFilled)
            {
                // 卡片内部控件也刷成卡片底色（Panel 不给子控件做透明）
                FillChildren(p);
                cardFilled = true;
            }
        }

        private void FillChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is Label || c is FlowLayoutPanel || c is CheckBox)
                {
                    if (c.BackColor == Color.White || c.BackColor == SystemColors.Control) c.BackColor = CCard;
                }
                if (c.HasChildren) FillChildren(c);
            }
        }

        private Button FlatButton(string text, int w, int h)
        {
            var b = new Button
            {
                Text = text,
                Size = new Size(w, h),
                Font = fontBase,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = CInk,
                Cursor = Cursors.Hand,
                TabStop = true,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = CCardLine;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 243, 252);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(219, 232, 252);
            b.Resize += delegate { RoundRegion(b, Math.Min(8, b.Height / 3)); };
            return b;
        }

        private void RoundRegion(Control c, int radius)
        {
            try
            {
                if (c.Width <= 0 || c.Height <= 0) return;
                using (var path = Round(new Rectangle(0, 0, c.Width, c.Height), radius))
                    c.Region = new Region(path);
            }
            catch
            {
                try { c.Region = null; } catch { }
            }
        }

        private Button PrimaryButton(string text, int w, int h)
        {
            var b = FlatButton(text, w, h);
            b.Font = new Font(fontBase.FontFamily, 11F, FontStyle.Bold);
            b.BackColor = CAccent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = CAccent2;
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(23, 63, 176);
            return b;
        }

        private CheckBox LinkCheck(string text, int w, int h)
        {
            return new CheckBox
            {
                Text = text,
                Size = new Size(w, h),
                Font = fontBase,
                ForeColor = CInk,
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static string FmtMinutes(int mins)
        {
            if (mins < 60) return mins + " 分钟";
            if (mins % 60 == 0) return (mins / 60) + " 小时";
            return (mins / 60) + " 小时 " + (mins % 60) + " 分钟";
        }

        private void SetAction(int action)
        {
            if (actionButtons == null) return;
            actionMode = action;
            for (int i = 0; i < actionButtons.Length; i++)
            {
                Button b = actionButtons[i];
                if (b == null) continue;
                bool on = i == action;
                b.BackColor = on ? CAccent : Color.White;
                b.ForeColor = on ? Color.White : CInk;
                b.FlatAppearance.BorderSize = on ? 0 : 1;
                b.FlatAppearance.BorderColor = CCardLine;
                b.FlatAppearance.MouseOverBackColor = on ? CAccent2 : Color.FromArgb(238, 243, 252);
                b.Font = on ? new Font(fontBase.FontFamily, 9F, FontStyle.Bold) : fontBase;
            }
            if (lblTarget != null) lblTarget.Text = "到点执行：" + ActionLabel(action);
        }

        private string ActionLabel(int action)
        {
            switch (action)
            {
                case 1: return "关机";
                case 2: return "重启";
                case 3: return "睡眠";
                case 4: return "休眠";
                default: return "关闭选中程序";
            }
        }

        /// <summary>供自检调用：强制刷新一次列表。</summary>
        public void RefreshListForTest() { RefreshProcesses(); }

        /// <summary>供自检调用：当前列表里的程序数量。</summary>
        public int ListCountForTest { get { return list == null ? 0 : list.Items.Count; } }

        /// <summary>供自检调用：临时清空列表，用来验证“空列表”时的提示逻辑。</summary>
        public void ClearListForTest()
        {
            list.Items.Clear();
            UpdateEmptyHint(true);
        }

        /// <summary>供自检调用：列表右上角那行提示的当前文字。</summary>
        public string ListHintForTest { get { return lblListHint == null ? "" : lblListHint.Text; } }

        /// <summary>供自检调用：状态行的当前文字。</summary>
        public string StatusTextForTest { get { return lblStatus == null ? "" : lblStatus.Text; } }

        /// <summary>供自检调用：判断提示标签是否可见。</summary>
        public bool EmptyHintVisible { get { return lblStatus != null && lblStatus.Text.Contains("没有检测到"); } }

        #endregion

        #region 进程列表

        private void RefreshProcesses()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                var all = App.EnumerateAll();
                entries.Clear();
                foreach (ProcEntry e in all)
                {
                    if (chkOnlyWindowed.Checked && !e.HasWindow) continue;
                    entries.Add(e);
                }

                icons.Images.Clear();
                list.BeginUpdate();
                list.Items.Clear();
                foreach (ProcEntry e in entries)
                {
                    e.IconIndex = -1;
                    if (e.Icon == null) e.Icon = App.ExtractIcon(e.Name + ".exe");
                    if (e.Icon == null)
                    {
                        Image bmp = MakeLetterIcon(e.Name);
                        if (bmp != null) e.Icon = bmp;
                    }
                    if (e.Icon != null)
                    {
                        try { icons.Images.Add(e.Icon); e.IconIndex = icons.Images.Count - 1; }
                        catch { }
                    }

                    var it = new ListViewItem(e.Name);
                    if (e.IconIndex >= 0) it.ImageIndex = e.IconIndex;
                    it.SubItems.Add(e.MemoryMB.ToString("0.0") + " MB");
                    it.SubItems.Add(string.IsNullOrEmpty(e.WindowTitle) ? "（无窗口）" : e.WindowTitle);
                    it.Tag = e;
                    if (!e.HasWindow) it.ForeColor = Color.FromArgb(150, 150, 150);
                    list.Items.Add(it);
                }
                list.EndUpdate();
                lblListHint.Text = entries.Count == 0 ? "列表为空"
                    : "共 " + entries.Count + " 个程序（按内存占用排序）";

                // 空列表时把“怎么办”写到状态行（ListView 是原生控件，叠在它上面的东西会被盖住）
                UpdateEmptyHint(entries.Count == 0);
            }
            catch (Exception ex)
            {
                lblListHint.Text = "读取失败：" + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
                SyncSelection();
            }
        }

        /// <summary>列表为空时给出“怎么办”的指引；恢复有数据时把状态行复位。</summary>
        private void UpdateEmptyHint(bool isEmpty)
        {
            if (lblListHint != null && isEmpty) lblListHint.Text = "列表为空";
            if (isEmpty)
            {
                SetStatus(chkOnlyWindowed != null && chkOnlyWindowed.Checked
                    ? "没有检测到“有窗口的程序”。取消勾选“只列有窗口的程序”可以看到全部进程。"
                    : "没有检测到任何进程。", CInk2);
            }
            else if (lblStatus != null && lblStatus.Text.StartsWith("没有检测到"))
            {
                SetStatus("就绪", CInk2);
            }
        }

        /// <summary>没有图标时画一个首字母圆角块，避免列表出现空白。</summary>
        private Image MakeLetterIcon(string name)
        {
            try
            {
                string letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
                var bmp = new Bitmap(20, 20);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    using (var path = Round(new Rectangle(0, 0, 19, 19), 5))
                    using (var b = new SolidBrush(Color.FromArgb(226, 232, 240)))
                        g.FillPath(b, path);
                    using (var f = new Font(fontBase.FontFamily, 9F, FontStyle.Bold))
                        TextRenderer.DrawText(g, letter, f, new Rectangle(0, 0, 20, 20), CInk2,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                return bmp;
            }
            catch { return null; }
        }

        private void SetAllChecked(bool check)
        {
            list.BeginUpdate();
            foreach (ListViewItem it in list.Items) it.Checked = check;
            list.EndUpdate();
            SyncSelection();
        }

        private void SyncSelection()
        {
            selectedNames.Clear();
            foreach (ListViewItem it in list.Items)
                if (it.Checked) selectedNames.Add(((ProcEntry)it.Tag).Name);
            if (lblListHint != null && entries.Count > 0)
                lblListHint.Text = "共 " + entries.Count + " 个程序，已选 " + selectedNames.Count + " 个";
        }

        #endregion

        #region 倒计时

        private void SetBusy(bool busy)
        {
            btnStart.Enabled = !busy;
            btnCancel.Enabled = busy;
            presets.Enabled = !busy;
            txtTime.Enabled = !busy;
            actionButtons[0].Enabled = !busy;
            actionButtons[1].Enabled = !busy;
            actionButtons[2].Enabled = !busy;
            actionButtons[3].Enabled = !busy;
            btnStart.Text = busy ? "倒计时进行中…" : "开始倒计时";
        }

        private void StartCountdown(bool interactive)
        {
            if (running) { SetStatus("已经在倒计时了，先点“取消并重置”再改设置。", CInk2); return; }

            TimeSpan span;
            string err;
            if (!App.TryParseDuration(txtTime.Text, out span, out err))
            {
                if (interactive)
                {
                    MessageBox.Show(this, err, App.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    txtTime.Focus();
                    txtTime.SelectAll();
                }
                else SetStatus(err, CDanger);
                return;
            }
            if (span.TotalSeconds < 5)
            {
                if (interactive) MessageBox.Show(this, "至少 5 秒，太短容易来不及取消。", App.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SyncSelection();
            var action = (TimerAction)actionMode;

            if (action == TimerAction.CloseApp && selectedNames.Count == 0)
            {
                if (interactive)
                    MessageBox.Show(this, "还没有勾选任何程序。\n\n· 要关闭程序：在上面列表里勾选\n· 要关机/重启：点右上方的“关机 / 重启 / 睡眠”",
                        App.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                else SetStatus("没有勾选任何程序，无法执行。", CDanger);
                return;
            }

            string what = action == TimerAction.CloseApp
                ? string.Join("、", selectedNames.ToArray())
                : ActionLabel(actionMode);

            if (interactive)
            {
                var r = MessageBox.Show(this,
                    "将在 " + App.DescribeSeconds((long)span.TotalSeconds) + "后（" + DateTime.Now.Add(span).ToString("HH:mm:ss") + "）执行：\n\n" + what +
                    "\n\n倒计时期间随时可以点“取消并重置”。确认开始吗？",
                    App.Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return;
            }

            endTime = DateTime.Now.Add(span);
            totalSeconds = span.TotalSeconds;
            running = true;
            taskRunning = true;
            lastBeepSecond = int.MaxValue;
            barColor = CAccent;

            SetBusy(true);
            App.KeepAwake(true);
            tray.Visible = true;
            UpdateHeader(span.TotalSeconds, true);
            barPercent = 0;
            pnlBarBg.Invalidate();
            ticker.Start();
            pulse.Start();

            SetStatus("已开始倒计时，随时可以取消。", CInk2);
            try
            {
                tray.BalloonTipTitle = App.Title;
                tray.BalloonTipText = "已开始倒计时，" + App.DescribeSeconds((long)span.TotalSeconds) + "后执行：" + what;
                tray.ShowBalloonTip(3000);
            }
            catch { }
        }

        private void StopCountdown(string message)
        {
            if (!running) { SetStatus(message, CInk2); return; }
            ticker.Stop();
            pulse.Stop();
            running = false;
            taskRunning = false;
            App.KeepAwake(false);
            SetBusy(false);
            barPercent = 0;
            pnlBarBg.Invalidate();
            lastHeadline = "已取消";
            lblCount.Text = "已取消";
            lblSub.Text = message;
            SetStatus(message, CInk2);
            tray.Text = App.Title;
        }

        private void TickerTick(object sender, EventArgs e)
        {
            double remain = (endTime - DateTime.Now).TotalSeconds;
            if (remain <= 0)
            {
                ticker.Stop();
                pulse.Stop();
                DoAction();
                return;
            }
            UpdateHeader(remain, false);
        }

        private void UpdateHeader(double remain, bool fromPulse)
        {
            string left = App.FormatCountdown(remain);
            string head = remain >= 1 ? left : "0";
            if (head != lastHeadline)
            {
                lastHeadline = head;
                lblCount.Text = head;
            }

            lblSub.Text = "距离执行还有 " + App.DescribeSeconds((long)remain) +
                          "   ·   预计 " + endTime.ToString("HH:mm:ss") + " 执行" +
                          (running ? "   ·   点这里或“取消并重置”可取消" : "");

            barPercent = App.Percent(totalSeconds, remain);
            pnlBarBg.Invalidate();
            tray.Text = Truncate(App.Title + " - 剩余 " + left, 63);
            if (btnStart.Enabled) btnStart.Text = "开始倒计时"; else btnStart.Text = "倒计时 " + left;

            // 最后 60 秒换成更醒目的节奏
            if (remain <= 60) barColor = CDanger; else barColor = CAccent;
            if (fromPulse || remain % 1 < 0.3) pnlBarBg.Invalidate();

            if (remain <= 30 && endTime - DateTime.Now > TimeSpan.Zero) RestoreFromTray(true);
            if (remain <= 10.5 && chkSound.Checked)
            {
                int sec = (int)Math.Ceiling(remain);
                if (sec != lastBeepSecond)
                {
                    lastBeepSecond = sec;
                    try { if (sec <= 3) Console.Beep(1200, 120); else Console.Beep(880, 95); } catch { }
                }
            }
        }

        private static string Truncate(string s, int max)
        {
            return s.Length <= max ? s : s.Substring(0, max);
        }

        private void DoAction()
        {
            App.KeepAwake(false);
            var action = (TimerAction)actionMode;
            var names = new List<string>(selectedNames);
            bool force = chkForce.Checked;

            if (action != TimerAction.CloseApp)
            {
                string err = App.ExecutePower(action, force);
                if (err != null) Finish("执行失败：" + err + "（有些操作需要以管理员身份运行本程序）", CDanger);
                else Finish("已发出" + ActionLabel(actionMode) + "请求。", CGood);
                return;
            }

            SetStatus("正在关闭：" + string.Join("、", names.ToArray()) + " …", CInk2);
            btnStart.Text = "正在关闭…";
            Application.DoEvents();

            var items = App.Resolve(names);
            App.RequestClose(items);          // 第一步：等价于点窗口右上角的 ×
            App.Wait(force ? 1500 : 4000);    // 给它时间自己退出

            var alive = App.AliveNames(items);
            int forceKilled = 0;
            string lastError = null;
            if (alive.Count > 0)
            {
                bool doForce = force;
                if (!doForce)
                {
                    var r = MessageBox.Show(this,
                        "还有 " + alive.Count + " 个程序没有自己退出：" + Join(alive) +
                        "\n\n要强制结束它们吗？",
                        App.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    doForce = (r == DialogResult.Yes);
                }
                if (doForce)
                {
                    forceKilled = App.ForceKill(items, out lastError);
                    App.Wait(700);
                    alive = App.AliveNames(items);
                }
            }

            string msg;
            if (alive.Count == 0) msg = "已关闭：" + string.Join("、", names.ToArray()) + "。";
            else msg = "已完成，仍在运行：" + Join(alive) + "。";
            if (forceKilled > 0) msg += "（其中强制结束 " + forceKilled + " 个进程）";
            if (lastError != null) msg += " 提示：" + lastError + "（可能需要以管理员身份运行）";
            Finish(msg, alive.Count == 0 ? CGood : CDanger);
        }

        private void Finish(string message, Color color)
        {
            running = false;
            taskRunning = false;
            SetBusy(false);
            barPercent = 100;
            pnlBarBg.Invalidate();

            lastHeadline = "已完成";
            lblCount.Text = "已完成";
            lblSub.Text = message;
            SetStatus(message, color);

            tray.Text = Truncate(App.Title + " - 已完成", 63);
            try
            {
                tray.BalloonTipTitle = App.Title;
                tray.BalloonTipText = message;
                tray.ShowBalloonTip(8000);
            }
            catch { }
            if (exitRequested) RealExit();
        }

        private void SetStatus(string text, Color color)
        {
            if (lblStatus == null) return;
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        private static string Join(HashSet<string> set)
        {
            var sb = new System.Text.StringBuilder();
            foreach (string s in set)
            {
                if (sb.Length > 0) sb.Append("、");
                sb.Append(s);
            }
            return sb.ToString();
        }

        #endregion

        #region 托盘 / 退出

        private void HideToTray()
        {
            tray.Visible = true;
            Hide();
            try
            {
                tray.BalloonTipTitle = App.Title;
                tray.BalloonTipText = running ? "倒计时继续进行中，双击托盘图标可以打开窗口。" : "程序已最小化到托盘，双击图标可以打开窗口。";
                tray.ShowBalloonTip(2500);
            }
            catch { }
        }

        private void RestoreFromTray() { RestoreFromTray(false); }

        private void RestoreFromTray(bool silent)
        {
            if (Visible && WindowState != FormWindowState.Minimized) return;
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            if (!silent)
            {
                try { tray.BalloonTipText = ""; } catch { }
            }
        }

        private void RealExit()
        {
            exitRequested = true;
            if (running)
            {
                var r = MessageBox.Show(this, "倒计时还在进行中，确定要退出吗？", App.Title,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { exitRequested = false; return; }
            }
            try { ticker.Stop(); pulse.Stop(); } catch { }
            App.KeepAwake(false);
            try { tray.Visible = false; tray.Dispose(); } catch { }
            Application.Exit();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !exitRequested)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { if (trayIconOwned != null) trayIconOwned.Dispose(); } catch { }
                try { if (fontBase != null) fontBase.Dispose(); } catch { }
                try { if (fontBody != null) fontBody.Dispose(); } catch { }
                try { if (fontSmall != null) fontSmall.Dispose(); } catch { }
                try { if (fontNum != null) fontNum.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        #endregion

        #region 对外接口（命令行/自检复用）

        public bool IsRunning { get { return taskRunning; } }

        /// <summary>不弹确认框，直接按给定参数开始倒计时（供命令行调用）。</summary>
        public void StartSilently(TimeSpan span, List<string> names, TimerAction action, bool force)
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { StartSilently(span, names, action, force); });
                return;
            }
            selectedNames = new List<string>(names);
            chkForce.Checked = force;
            SetAction(action == TimerAction.CloseApp ? 0 : (int)action);
            txtTime.Text = App.DescribeSeconds((long)span.TotalSeconds);
            StartCountdown(false);
        }

        /// <summary>自检：界面起来后自动勾选指定进程并开始倒计时（不弹确认框）。</summary>
        public void ScheduleSelfTest(string processName, double seconds, string shotPath)
        {
            var t = new System.Windows.Forms.Timer { Interval = 1500 };
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                try
                {
                    RefreshProcesses();
                    bool found = false;
                    foreach (ListViewItem it in list.Items)
                    {
                        var e = (ProcEntry)it.Tag;
                        if (string.Equals(e.Name, processName, StringComparison.OrdinalIgnoreCase))
                        {
                            it.Checked = true;
                            it.Selected = true;
                            it.EnsureVisible();
                            found = true;
                            break;
                        }
                    }
                    if (!found) SetStatus("自检：列表里没找到 " + processName, CDanger);
                    if (!string.IsNullOrEmpty(shotPath)) TakeShot(shotPath);
                    StartSilently(TimeSpan.FromSeconds(seconds), new List<string> { processName }, TimerAction.CloseApp, false);
                }
                catch (Exception ex) { SetStatus("自检失败：" + ex.Message, CDanger); }
            };
            t.Start();
        }

        /// <summary>延时截一张窗口图，用来确认界面长得对不对。</summary>
        public void ScheduleShot(string path, int delayMs)
        {
            var t = new System.Windows.Forms.Timer { Interval = delayMs };
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                TakeShot(path);
            };
            t.Start();
        }

        /// <summary>
        /// 把窗口自己渲染成 PNG（不需要屏幕，可在自检里离线跑）。
        /// 先真实显示一次让布局稳定，再 DrawToBitmap。
        /// </summary>
        public static string RenderPreview(string pngPath, bool countdownPreview)
        {
            MainForm f = null;
            try
            {
                f = new MainForm(false);
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, -4000);   // 挪到屏幕外，避免闪一下
                f.Show();
                Application.DoEvents();

                if (countdownPreview)
                {
                    // 造一个“正在倒计时”的样子，方便检查高亮状态下的界面
                    f.txtTime.Text = "30 分钟";
                    foreach (ListViewItem it in f.list.Items)
                    {
                        if (it.Index < 3) it.Checked = true;
                    }
                    f.SyncSelection();
                    f.endTime = DateTime.Now.AddSeconds(1234);
                    f.totalSeconds = 1234;
                    f.running = true;
                    f.SetBusy(true);
                    f.barPercent = App.Percent(1234, 823);
                    f.barColor = CAccent;
                    f.pnlBarBg.Invalidate();
                    f.UpdateHeader(823, true);
                    f.SetStatus("运行中：这是自检渲染出来的界面预览。", CInk2);
                    Application.DoEvents();
                }

                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                try { if (f != null) { f.running = false; f.Hide(); f.Dispose(); } } catch { }
            }
        }

        private void TakeShot(string path)
        {
            try
            {
                var r = Bounds;
                using (var bmp = new Bitmap(r.Width, r.Height))
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                    bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
                SetStatus("截图已保存：" + path, CInk2);
            }
            catch (Exception ex) { SetStatus("截图失败：" + ex.Message, CDanger); }
        }

        #endregion
    }
}
