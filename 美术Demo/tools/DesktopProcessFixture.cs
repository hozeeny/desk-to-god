using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NetBunny
{
    // This executable is the only process the process tests may request to close.
    internal sealed class DesktopProcessFixture : Form
    {
        readonly string marker, mode;
        readonly Timer timer = new Timer();
        bool stopping;
        protected override bool ShowWithoutActivation { get { return true; } }
        DesktopProcessFixture(string markerPath, string fixtureMode)
        {
            marker = markerPath; mode = fixtureMode;
            Text = "桌面修仙专用进程夹具 " + mode;
            Size = new Size(350, 130); StartPosition = FormStartPosition.Manual;
            Location = new Point(20, 20);
            Controls.Add(new Label { Dock = DockStyle.Fill, Text = "本项目自动测试窗口\r\n模式：" + mode + "\r\n测试结束后自行正常退出" });
            timer.Interval = 50;
            timer.Tick += delegate {
                if (File.Exists(marker + ".stop")) { stopping = true; Close(); }
            };
            Shown += delegate { File.WriteAllText(marker + ".ready", "ready"); timer.Start(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (stopping || mode == "close") return;
                File.WriteAllText(marker + ".requested", "normal-close-requested");
                e.Cancel = true; // User cancels saving: the app deliberately stays alive.
                if (mode == "prompt" && !File.Exists(marker + ".prompt"))
                {
                    File.WriteAllText(marker + ".prompt", "save-prompt-open");
                    BeginInvoke((MethodInvoker)delegate {
                        using (Form prompt = new Form { Text = "夹具：尚未保存", Size = new Size(290, 130), StartPosition = FormStartPosition.CenterParent })
                        {
                            prompt.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "保存提示夹具：取消保存会保留程序。" });
                            using (Timer closer = new Timer { Interval = 100 })
                            {
                                closer.Tick += delegate { if (File.Exists(marker + ".stop")) { prompt.Close(); } };
                                closer.Start(); prompt.ShowDialog(this);
                            }
                        }
                    });
                }
            };
        }
        [STAThread] static void Main(string[] args)
        {
            if (args.Length != 2) return;
            Application.Run(new DesktopProcessFixture(args[0], args[1]));
        }
    }
}
