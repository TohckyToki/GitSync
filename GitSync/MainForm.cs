using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;

namespace GitSync
{
    public partial class MainForm : Form
    {
        #region Log kind enum
        private enum LogKind
        {
            App, Info, Error
        }
        #endregion

        #region Fields
        private string[] watchFolders = Array.Empty<string>();
        private string logFileName = string.Empty;
        private CancellationTokenSource cancellationTokenSource = new();
        private readonly List<Task> tasks = new();
        private bool requestClose = false;
        #endregion

        #region Construct
        public MainForm()
        {
            this.InitializeComponent();
            this.ManuallyInitializeComponent();
            this.ApplyConfiguration();
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

            this.Load += this.MainFormLoad;
            this.FormClosing += this.MainFormFormClosing;
            this.enableToolStripMenuItem.CheckedChanged += this.EnableToolStripMenuItemCheckedChanged;
            this.notifyIcon.DoubleClick += this.NotifyIconDoubleClick;
            this.numericUpDown.Leave += this.NumericUpDownLeave;
        }

        private void ApplyConfiguration()
        {
            this.numericUpDown.Value = Properties.Settings.Default.Interval;
            this.numericUpDown.Controls[1].Text = this.numericUpDown.Value.ToString();
            this.folderListBox.Items.Clear();
            this.folderListBox.Items.AddRange(Properties.Settings.Default.Folders.Cast<string>().ToArray());
        }
        #endregion

        #region Events
        private void MainFormLoad(object? sender, EventArgs e)
        {
            this.EnsureLogFolderExists();
            this.UpdateLogFile();
            this.LogText(LogKind.App, "Application is running.");

            this.mainTimer.Interval = Convert.ToInt32(this.numericUpDown.Value * 1000);
            this.watchFolders = this.folderListBox.Items.Cast<string>().ToArray();
        }

        private void MainFormFormClosing(object? sender, FormClosingEventArgs e)
        {
            if (!this.requestClose)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        private void NotifyIconDoubleClick(object? sender, EventArgs e)
        {
            this.settingsToolStripMenuItem.PerformClick();
        }

        private void EnableToolStripMenuItemCheckedChanged(object? sender, EventArgs e)
        {
            if (this.watchFolders.Length == 0)
            {
                this.enableToolStripMenuItem.Checked = false;
                return;
            }

            if (this.enableToolStripMenuItem.Checked)
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
            string text = this.folderTextBox.Text;
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
            if (this.folderListBox.Items.Contains(text))
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
                MessageBox.Show("There should be at least one item that has been selected.");
                return;
            }
            this.folderListBox.Items.Remove(this.folderListBox.SelectedItem);
        }

        private void NumericUpDownLeave(object? sender, EventArgs e)
        {
            if (this.numericUpDown.Controls[1].Text == string.Empty)
            {
                this.numericUpDown.Controls[1].Text = this.numericUpDown.Value.ToString();
            }
        }

        private void ResetButtonClick(object sender, EventArgs e)
        {
            this.ApplyConfiguration();
        }

        private void SaveButtonClick(object sender, EventArgs e)
        {

            if (this.folderListBox.Items.Count < 1)
            {
                MessageBox.Show("There should be at least one item that has been added.");
                return;
            }

            this.mainTimer.Stop();
            this.cancellationTokenSource.Cancel();
            this.cancellationTokenSource = new CancellationTokenSource();
            this.tasks.Clear();

            this.mainTimer.Interval = Convert.ToInt32(this.numericUpDown.Value * 1000);
            this.watchFolders = this.folderListBox.Items.Cast<string>().ToArray();
            Properties.Settings.Default.Interval = this.numericUpDown.Value;
            StringCollection sc = new();
            sc.AddRange(this.watchFolders);
            Properties.Settings.Default.Folders = sc;
            Properties.Settings.Default.Save();

            MessageBox.Show("The new setting has been applied.");

            if (this.enableToolStripMenuItem.Checked)
            {
                this.mainTimer.Start();
                this.WatchAllGitRepository();
            }
        }

        private void OpenLogToolStripMenuItemClick(object sender, EventArgs e)
        {
            string? dir = Path.GetDirectoryName(this.logFileName);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Task.Run(() =>
                {
                    Process.Start("explorer", dir);
                });
            }
        }

        private void ExitToolStripMenuItemClick(object sender, EventArgs e)
        {
            this.requestClose = true;
            this.cancellationTokenSource.Cancel();
            this.Close();
        }

        private void MainTimerTick(object sender, EventArgs e)
        {
            this.WatchAllGitRepository();
        }
        #endregion

        #region Log Operating

        private void EnsureLogFolderExists()
        {
            string logFolder = Path.Combine(Application.StartupPath, "logs");
            if (!File.Exists(logFolder))
            {
                Directory.CreateDirectory(logFolder);
            }
        }

        private void UpdateLogFile()
        {
            string logFileName = $"{DateTime.Now.ToString("yyyy-MM-dd")}.log";
            string fullName = Path.Combine(Application.StartupPath, "logs", logFileName);
            if (!File.Exists(fullName))
            {
                File.Create(fullName).Close();
            }
            this.logFileName = fullName;
        }

        private void LogText(LogKind kind, string? text, string? workfolder = null)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                text = text.Trim();

                if (!string.IsNullOrWhiteSpace(workfolder))
                {
                    text = $"Workfolder: {workfolder}{Environment.NewLine}{text}";
                }

                string[] msgs = new string[] {
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss:ffffff}]",
                    $"[Log from {Enum.GetName(typeof(LogKind), kind)}]",
                    text,
                    Environment.NewLine
                };
                    File.AppendAllLines(this.logFileName, msgs, UTF8Encoding.UTF8);
                }
            }

            #endregion

            #region Watch Git Repositories

            private void WatchAllGitRepository()
            {
                this.UpdateLogFile();

                if (this.tasks.Any())
                {
                    this.cancellationTokenSource.Cancel();
                    this.cancellationTokenSource = new CancellationTokenSource();
                    this.tasks.Clear();
                }

                foreach (string folder in this.watchFolders)
                {
                    Task task = new(() =>
                    {
                        this.WatchGitStatus(folder);
                    }, this.cancellationTokenSource.Token);
                    this.tasks.Add(task);
                    task.ContinueWith(_ =>
                    {
                        this.tasks.Remove(task);
                    });
                    task.Start();
                }
            }

            private void WatchGitStatus(string folder)
            {
                Process process = new()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        WorkingDirectory = folder,
                        FileName = "git",
                        Arguments = "fetch",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    }
                };
                string? output;
                string? error;
                do
                {
                    process.Start();
                    process.WaitForExit(-1);
                    output = process.StandardOutput.ReadToEnd();
                    this.LogText(LogKind.Info, output, folder);
                    error = process.StandardError.ReadToEnd();
                    this.LogText(LogKind.Error, error, folder);
                } while (error.Contains("fatal:", StringComparison.OrdinalIgnoreCase));

                this.notifyIcon.ShowBalloonTip(5000, "GitSync", $"Workfolder: {folder}{Environment.NewLine}Git fetching successfully.", ToolTipIcon.Info);

                process.StartInfo.Arguments = "status";
                process.Start();
                process.WaitForExit(-1);

                output = process.StandardOutput.ReadToEnd();
                this.LogText(LogKind.Info, output, folder);
                error = process.StandardError.ReadToEnd();
                this.LogText(LogKind.Error, error, folder);

                if (output.Contains("git pull", StringComparison.OrdinalIgnoreCase))
                {
                    process.StartInfo.Arguments = "pull";
                    process.Start();
                    process.WaitForExit(-1);
                    output = process.StandardOutput.ReadToEnd();
                    this.LogText(LogKind.Info, output, folder);
                    error = process.StandardError.ReadToEnd();
                    this.LogText(LogKind.Error, error, folder);

                    this.notifyIcon.ShowBalloonTip(5000, "GitSync", $"Workfolder: {folder}{Environment.NewLine}Git pulling successfully.", ToolTipIcon.Info);
                }
            }

            #endregion

        }
    }