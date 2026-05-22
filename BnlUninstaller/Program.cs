using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BnlUninstaller
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args.Length > 0 ? args[0] : null));
        }
    }

    public class MainForm : Form
    {
        private Label _titleLabel = null!;
        private Label _descLabel = null!;
        private GroupBox _groupGame = null!;
        private TextBox _txtPath = null!;
        private Button _btnBrowse = null!;
        private GroupBox _groupRemove = null!;
        private CheckBox _chkLauncher = null!;
        private CheckBox _chkCardTextures = null!;
        private CheckBox _chkConfigManager = null!;
        private CheckBox _chkLauncherConfig = null!;
        private CheckBox _chkBepInExRuntime = null!;
        private Label _lblStatus = null!;
        private ProgressBar _progressBar = null!;
        private Button _btnUninstall = null!;

        private readonly string? _initialGamePath;

        public MainForm(string? initialGamePath)
        {
            _initialGamePath = initialGamePath;
            InitializeComponent();
            AutoDetectGameFolder();
        }

        private void InitializeComponent()
        {
            Text = "BNL Community Launcher - Uninstaller";
            ClientSize = new Size(540, 470);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            _titleLabel = new Label
            {
                Text = "Remove Block N Load Community Launcher",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                Size = new Size(500, 30),
                Location = new Point(15, 12)
            };

            _descLabel = new Label
            {
                Text = "Removes launcher-related files. BepInEx runtime removal is optional so unfinished plugins can stay installed.",
                Size = new Size(500, 40),
                Location = new Point(15, 48)
            };

            _groupGame = new GroupBox
            {
                Text = "Game Folder",
                Size = new Size(500, 55),
                Location = new Point(15, 95)
            };

            _txtPath = new TextBox
            {
                Size = new Size(405, 23),
                Location = new Point(10, 22),
                Text = "Detecting..."
            };

            _btnBrowse = new Button
            {
                Text = "Browse...",
                Size = new Size(80, 23),
                Location = new Point(418, 21)
            };
            _btnBrowse.Click += BtnBrowse_Click;

            _groupGame.Controls.Add(_txtPath);
            _groupGame.Controls.Add(_btnBrowse);

            _groupRemove = new GroupBox
            {
                Text = "Remove",
                Size = new Size(500, 180),
                Location = new Point(15, 160)
            };

            int y = 24;
            _chkLauncher = MakeCheckBox("Community launcher plugin (BnlPlugins.Launcher.dll)", y, true); y += 26;
            _chkCardTextures = MakeCheckBox("Launcher card overrides folder (BepInEx\\plugins\\Launcher\\CardTextures)", y, true); y += 26;
            _chkLauncherConfig = MakeCheckBox("Launcher config, version files, and installer exe", y, true); y += 26;
            _chkConfigManager = MakeCheckBox("Configuration Manager plugin", y, false); y += 26;
            _chkBepInExRuntime = MakeCheckBox("BepInEx runtime and Doorstop files (disables all plugins)", y, false);

            _groupRemove.Controls.Add(_chkLauncher);
            _groupRemove.Controls.Add(_chkCardTextures);
            _groupRemove.Controls.Add(_chkLauncherConfig);
            _groupRemove.Controls.Add(_chkConfigManager);
            _groupRemove.Controls.Add(_chkBepInExRuntime);

            _lblStatus = new Label
            {
                Text = "",
                Size = new Size(500, 40),
                Location = new Point(15, 348)
            };

            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Visible = false,
                Size = new Size(500, 20),
                Location = new Point(15, 395)
            };

            _btnUninstall = new Button
            {
                Text = "Uninstall",
                Size = new Size(110, 32),
                Location = new Point(215, 425),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnUninstall.Click += BtnUninstall_Click;

            Controls.Add(_titleLabel);
            Controls.Add(_descLabel);
            Controls.Add(_groupGame);
            Controls.Add(_groupRemove);
            Controls.Add(_lblStatus);
            Controls.Add(_progressBar);
            Controls.Add(_btnUninstall);
        }

        private static CheckBox MakeCheckBox(string text, int y, bool checked_)
        {
            return new CheckBox
            {
                Text = text,
                Checked = checked_,
                Size = new Size(480, 20),
                Location = new Point(10, y)
            };
        }

        private void AutoDetectGameFolder()
        {
            if (!string.IsNullOrEmpty(_initialGamePath) &&
                File.Exists(Path.Combine(_initialGamePath, "Win64", "BlockNLoad.exe")))
            {
                _txtPath.Text = _initialGamePath;
                return;
            }

            string? path = FindBnlFolder();
            if (path != null)
                _txtPath.Text = path;
            else
            {
                _txtPath.Text = "";
                _lblStatus.Text = "Could not auto-detect game folder. Click Browse.";
            }
        }

        private static string? FindBnlFolder()
        {
            var libraryFolders = new List<string>();
            string? steamPath = null;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                    steamPath = key?.GetValue("InstallPath") as string;
            }
            catch { }
            if (steamPath == null)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                        steamPath = key?.GetValue("InstallPath") as string;
                }
                catch { }
            }

            if (steamPath != null)
            {
                libraryFolders.Add(steamPath);
                string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    try
                    {
                        var vdf = File.ReadAllText(vdfPath);
                        foreach (var match in System.Text.RegularExpressions.Regex.Matches(
                            vdf, @"""\d+""\s+""([^""]+)"""))
                        {
                            var libPath = ((System.Text.RegularExpressions.Match)match).Groups[1].Value
                                .Replace(@"\\", @"\");
                            libraryFolders.Add(libPath);
                        }
                    }
                    catch { }
                }
            }

            libraryFolders.Add(@"C:\Program Files (x86)\Steam");
            libraryFolders.Add(@"C:\Program Files\Steam");
            libraryFolders.Add(@"D:\Steam");
            libraryFolders.Add(@"E:\Steam");

            foreach (var lib in libraryFolders.Distinct())
            {
                var bnlPath = Path.Combine(lib, "steamapps", "common", "BlockNLoad");
                if (File.Exists(Path.Combine(bnlPath, "Win64", "BlockNLoad.exe")))
                    return Path.GetFullPath(bnlPath);
            }

            return null;
        }

        private void BtnBrowse_Click(object? sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog
            {
                Description = "Select your Block N Load folder (contains Win64\\BlockNLoad.exe)"
            })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                    _txtPath.Text = dialog.SelectedPath;
            }
        }

        private void BtnUninstall_Click(object? sender, EventArgs e)
        {
            var gamePath = _txtPath.Text.Trim();
            if (!File.Exists(Path.Combine(gamePath, "Win64", "BlockNLoad.exe")))
            {
                MessageBox.Show(
                    $"Could not find BlockNLoad.exe in:\n{gamePath}\\Win64\\\n\nPlease select the correct Block N Load folder.",
                    "Game Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!_chkLauncher.Checked && !_chkCardTextures.Checked && !_chkLauncherConfig.Checked &&
                !_chkConfigManager.Checked && !_chkBepInExRuntime.Checked)
            {
                MessageBox.Show("Select at least one thing to remove.", "Nothing Selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (IsGameRunning(gamePath))
            {
                MessageBox.Show("Block N Load is currently running.\n\nClose the game first, then run the uninstaller again.",
                    "Game Running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "This will remove the selected launcher files.\n\nContinue?",
                "Confirm Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;

            _btnUninstall.Enabled = false;
            _progressBar.Visible = true;
            _lblStatus.Text = "Removing files...";
            Refresh();

            try
            {
                string win64Dir = Path.Combine(gamePath, "Win64");
                string bepinexDir = Path.Combine(win64Dir, "BepInEx");
                string pluginsDir = Path.Combine(bepinexDir, "plugins");

                if (_chkLauncher.Checked)
                {
                    SafeDeleteFile(Path.Combine(pluginsDir, "BnlPlugins.Launcher.dll"));
                }

                if (_chkCardTextures.Checked)
                {
                    SafeDeleteDirectory(Path.Combine(pluginsDir, "Launcher", "CardTextures"));
                    SafeDeleteDirectoryIfEmpty(Path.Combine(pluginsDir, "Launcher"));
                }

                if (_chkLauncherConfig.Checked)
                {
                    SafeDeleteFile(Path.Combine(bepinexDir, "config", "BnlPlugins.Launcher.cfg"));
                    SafeDeleteFile(Path.Combine(pluginsDir, "Launcher", "version.txt"));
                    SafeDeleteFile(Path.Combine(pluginsDir, "Launcher", "last_check.txt"));
                    SafeDeleteFile(Path.Combine(pluginsDir, "Launcher", "BNL-Installer.exe"));
                    SafeDeleteFile(Path.Combine(pluginsDir, "Launcher", "BNL-Installer.next.exe"));
                    SafeDeleteFile(Path.Combine(pluginsDir, "Launcher", "release-manifest.json"));
                    SafeDeleteDirectoryIfEmpty(Path.Combine(pluginsDir, "Launcher"));
                }

                if (_chkConfigManager.Checked)
                {
                    SafeDeleteDirectory(Path.Combine(pluginsDir, "ConfigurationManager"));
                }

                if (_chkBepInExRuntime.Checked)
                {
                    SafeDeleteFile(Path.Combine(win64Dir, "winhttp.dll"));
                    SafeDeleteFile(Path.Combine(win64Dir, ".doorstop_version"));
                    SafeDeleteFile(Path.Combine(win64Dir, "doorstop_config.ini"));
                    SafeDeleteFile(Path.Combine(win64Dir, "changelog.txt"));
                    SafeDeleteFile(Path.Combine(win64Dir, "servers.txt"));

                    SafeDeleteDirectory(Path.Combine(bepinexDir, "core"));
                    SafeDeleteDirectory(Path.Combine(bepinexDir, "patchers"));
                }

                SafeDeleteDirectoryIfEmpty(Path.Combine(pluginsDir, "Launcher"));
                SafeDeleteDirectoryIfEmpty(pluginsDir);
                SafeDeleteDirectoryIfEmpty(Path.Combine(bepinexDir, "config"));
                SafeDeleteDirectoryIfEmpty(bepinexDir);

                _progressBar.Visible = false;
                _lblStatus.Text = "";

                MessageBox.Show(
                    "Selected launcher files were removed.",
                    "Uninstall Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);

                Close();
            }
            catch (Exception ex)
            {
                _progressBar.Visible = false;
                _btnUninstall.Enabled = true;
                _lblStatus.Text = "Error: " + ex.Message;
                MessageBox.Show("Uninstall failed:\n" + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsGameRunning(string gamePath)
        {
            string targetExe = Path.Combine(gamePath, "Win64", "BlockNLoad.exe");
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("BlockNLoad"))
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, targetExe, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { }
            }

            return false;
        }

        private static void SafeDeleteFile(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private static void SafeDeleteDirectory(string path)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }

        private static void SafeDeleteDirectoryIfEmpty(string path)
        {
            if (!Directory.Exists(path))
                return;

            if (!Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path, false);
        }
    }
}
