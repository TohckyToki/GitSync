using System.Diagnostics;
using System.Text;

namespace GitSync
{
    public partial class MainForm : Form
    {
        private string[] watchFolders = { "E:\\_\\2_s_develop" };
        private string LogFileName = string.Empty;
        private bool isRunning = false;

        private enum LogKind
        {
            App, Info, Error
        }

        public MainForm()
        {
            InitializeComponent();
            this.Visible = false;
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
        }

        private void ExitToolStripMenuItemClick(object sender, EventArgs e)
        {
            while (isRunning)
            {
                Task.Delay(2000);
            }
            this.Close();
        }

        private void MainFormLoad(object sender, EventArgs e)
        {
            this.UpdateLogFile();
            this.LogText($"Application is running.", LogKind.App);

            this.mainTimer.Enabled = true;
            this.mainTimer.Start();
        }

        private void MainTimerTick(object sender, EventArgs e)
        {
            isRunning = true;
            this.UpdateLogFile();

            foreach (string folder in watchFolders)
            {
                this.WatchGitStatus(folder);
            }
            this.mainTimer.Interval = 300000;
            isRunning = false;
        }

        private void UpdateLogFile()
        {
            var logFileName = $"{DateTime.Now.ToString("yyyy-MM-dd-HH")}.log";
            var fullName = Path.Combine(Application.StartupPath, logFileName);
            if (!File.Exists(fullName))
            {
                File.Create(fullName).Close();
            }
            this.LogFileName = fullName;
        }

        private void LogText(string? text, LogKind kind)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                var msgs = new string[] {
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss:ffffff}]",
                    $"[Log from {Enum.GetName(typeof(LogKind), kind)}]",
                    text,
                    Environment.NewLine
                };
                File.AppendAllLines(this.LogFileName, msgs, UTF8Encoding.UTF8);
            }
        }

        private void WatchGitStatus(string folder)
        {
            var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                WorkingDirectory = folder,
                FileName = "git",
                Arguments = "fetch",
                CreateNoWindow = true,
                UseShellExecute = false,
                StandardOutputEncoding = UTF8Encoding.UTF8,
                StandardErrorEncoding = UTF8Encoding.UTF8,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var output = "";
            var error = "";

            do
            {
                process.Start();
                process.WaitForExit(-1);
                output = process.StandardOutput.ReadToEnd();
                this.LogText(output, LogKind.Info);
                error = process.StandardError.ReadToEnd();
                this.LogText(error, LogKind.Error);
            } while (error.Contains("fatal:", StringComparison.OrdinalIgnoreCase));

            this.notifyIcon.ShowBalloonTip(5000, "GitSync", "Git fetching successfully.", ToolTipIcon.Info);

            process.StartInfo.Arguments = "status";
            process.Start();
            process.WaitForExit(-1);

            output = process.StandardOutput.ReadToEnd();
            this.LogText(output, LogKind.Info);
            error = process.StandardError.ReadToEnd();
            this.LogText(error, LogKind.Error);

            if (output.Contains("git pull", StringComparison.OrdinalIgnoreCase))
            {
                process.StartInfo.Arguments = "pull";
                process.Start();
                process.WaitForExit(-1);
                output = process.StandardOutput.ReadToEnd();
                this.LogText(output, LogKind.Info);
                error = process.StandardError.ReadToEnd();
                this.LogText(error, LogKind.Error);

                this.notifyIcon.ShowBalloonTip(5000, "GitSync", "Git pulling successfully.", ToolTipIcon.Info);
            }
        }
    }
}