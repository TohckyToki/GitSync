using Microsoft.VisualBasic.Logging;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.Text;

namespace GitSync
{
    public partial class MainForm : Form
    {
        #region Inner enum
        private enum LogKind
        {
            App, Info, Error
        }
        #endregion

        #region Fields
        private string[] watchFolders = Array.Empty<string>();
        private string logFileName = string.Empty;
        private CancellationTokenSource cancellationTokenSource = new();
        private List<Task> tasks = new();
        private bool needClose = false;
        #endregion

        #region Construct
        public MainForm()
        {
            InitializeComponent();
            ManuallyInitializeComponent();
            ApplyConfiguration();
        }

        private void ManuallyInitializeComponent()
        {
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ShowInTaskbar = false;
            this.Visible = false;
            this.WindowState = FormWindowState.Minimized;
            this.mainTimer.Enabled = true;
            this.numericUpDown.DecimalPlaces = 0;
            this.numericUpDown.Increment = 1;
            this.numericUpDown.ThousandsSeparator = false;
            this.numericUpDown.Minimum = 20;
            this.numericUpDown.Maximum = 43200;
            this.folderListBox.SelectionMode = SelectionMode.One;

            this.Load += MainFormLoad;
            this.FormClosing += MainFormFormClosing;
            this.enableToolStripMenuItem.CheckedChanged += EnableToolStripMenuItemCheckedChanged;
            this.notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
        }

        private void ApplyConfiguration()
        {
            this.numericUpDown.Value = Properties.Settings.Default.Interval;
            this.folderListBox.Items.Clear();
            this.folderListBox.Items.AddRange(Properties.Settings.Default.Folders.Cast<string>().ToArray());
        }
        #endregion

        #region Events
        private void MainFormLoad(object? sender, EventArgs e)
        {
            this.UpdateLogFile();
            this.LogText($"Application is running.", LogKind.App);

            this.mainTimer.Interval = Convert.ToInt32(this.numericUpDown.Value * 1000);
            this.watchFolders = this.folderListBox.Items.Cast<string>().ToArray();

            this.enableToolStripMenuItem.Checked= true;
        }

        private void MainFormFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!this.needClose)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
        {
            this.settingToolStripMenuItem.PerformClick();
        }

        private void EnableToolStripMenuItemCheckedChanged(object? sender, EventArgs e)
        {
            if (enableToolStripMenuItem.Checked)
            {
                this.mainTimer.Start();
                this.WatchAllGitRepository();
            }
            else
            {
                this.mainTimer.Stop();
                this.cancellationTokenSource.Cancel();
                this.cancellationTokenSource = new CancellationTokenSource();
                this.tasks.Clear();
            }
        }

        private void SettingToolStripMenuItemClick(object sender, EventArgs e)
        {
            this.ApplyConfiguration();
            this.ShowInTaskbar = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.WindowState = FormWindowState.Normal;
            this.Show();
        }

        private void AddButtonClick(object sender, EventArgs e)
        {
            var text = this.folderTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("​The folder path should not be empty.");
                return;
            }
            if (!Directory.Exists(text))
            {
                MessageBox.Show("​The folder path does not exist.");
                return;
            }
            if (!this.folderListBox.Items.Contains(text))
            {
                MessageBox.Show("The folder path has already been added.");
                return;
            }
            this.folderListBox.Items.Add(text);
            this.folderTextBox.Clear();
        }

        private void RemoveButtonClick(object sender, EventArgs e)
        {
            if (this.folderListBox.SelectedItem is null)
            {
                MessageBox.Show("There should be one item that has been selected at least.");
                return;
            }
            this.folderListBox.Items.Remove(this.folderListBox.SelectedItem);
        }

        private void SaveButtonClick(object sender, EventArgs e)
        {

            if (this.folderListBox.Items.Count < 1)
            {
                MessageBox.Show("");
                return;
            }

            this.mainTimer.Stop();
            this.cancellationTokenSource.Cancel();
            this.cancellationTokenSource = new CancellationTokenSource();
            this.tasks.Clear();

            this.mainTimer.Interval = Convert.ToInt32(this.numericUpDown.Value * 1000);
            this.watchFolders = this.folderListBox.Items.Cast<string>().ToArray();
            Properties.Settings.Default.Interval = this.numericUpDown.Value;
            var sc = new StringCollection();
            sc.AddRange(this.watchFolders);
            Properties.Settings.Default.Folders = sc;
            Properties.Settings.Default.Save();

            MessageBox.Show("The new setting has been applied.");

            if (enableToolStripMenuItem.Checked)
            {
                this.mainTimer.Start();
                this.WatchAllGitRepository();
            }
        }

        private void ExitToolStripMenuItemClick(object sender, EventArgs e)
        {
            this.needClose = true;
            cancellationTokenSource.Cancel();
            this.Close();
        }

        private void MainTimerTick(object sender, EventArgs e)
        {
            WatchAllGitRepository();
        }
        #endregion

        private void UpdateLogFile()
        {
            var logFolder = Path.Combine(Application.StartupPath, "logs");
            if (!File.Exists(logFolder))
            {
                Directory.CreateDirectory(logFolder);
            }

            var logFileName = $"{DateTime.Now.ToString("yyyy-MM-dd")}.log";
            var fullName = Path.Combine(logFolder, logFileName);
            if (!File.Exists(fullName))
            {
                File.Create(fullName).Close();
            }
            this.logFileName = fullName;
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
                File.AppendAllLines(this.logFileName, msgs, UTF8Encoding.UTF8);
            }
        }

        private void WatchAllGitRepository()
        {
            this.UpdateLogFile();

            if (this.tasks.Any())
            {
                this.cancellationTokenSource.Cancel();
                this.cancellationTokenSource = new CancellationTokenSource();
                this.tasks.Clear();
            }

            foreach (string folder in watchFolders)
            {
                var task = new Task(() =>
                {
                    this.WatchGitStatus(folder);
                }, cancellationTokenSource.Token);
                tasks.Add(task);
                task.ContinueWith(_ =>
                {
                    tasks.Remove(task);
                });
                task.Start();
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