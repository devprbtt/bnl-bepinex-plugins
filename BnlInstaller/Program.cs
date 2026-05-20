using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BnlInstaller
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private const string RepoOwner = "devprbtt";
        private const string RepoName = "bnl-bepinex-plugins";

        private Label _titleLabel = null!;
        private Label _descLabel = null!;
        private GroupBox _groupGame = null!;
        private TextBox _txtPath = null!;
        private Button _btnBrowse = null!;
        private GroupBox _groupPlugins = null!;
        private CheckBox _chkCardTextures = null!;
        private CheckBox _chkBepInEx = null!;
        private CheckBox _chkLauncher = null!;
        private CheckBox _chkLongshot = null!;
        private CheckBox _chkCfgManager = null!;
        private Label _lblStatus = null!;
        private ProgressBar _progressBar = null!;
        private Button _btnInstall = null!;

        public MainForm()
        {
            InitializeComponent();
            AutoDetectGameFolder();
        }

        private void InitializeComponent()
        {
            Text = "BNL Community Launcher - Installer";
            ClientSize = new Size(500, 440);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            // Title
            _titleLabel = new Label
            {
                Text = "Block N Load Community Launcher",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                Size = new Size(470, 30),
                Location = new Point(15, 12)
            };

            // Description
            _descLabel = new Label
            {
                Text = "Installs BepInEx + community launcher plugin.\nNo game files are modified - safe for Steam.",
                Size = new Size(470, 35),
                Location = new Point(15, 48)
            };

            // Game folder group
            _groupGame = new GroupBox
            {
                Text = "Game Folder",
                Size = new Size(470, 55),
                Location = new Point(15, 92)
            };

            _txtPath = new TextBox
            {
                Size = new Size(375, 23),
                Location = new Point(10, 22),
                Text = "Detecting..."
            };

            _btnBrowse = new Button
            {
                Text = "Browse...",
                Size = new Size(80, 23),
                Location = new Point(388, 21)
            };
            _btnBrowse.Click += BtnBrowse_Click;

            _groupGame.Controls.Add(_txtPath);
            _groupGame.Controls.Add(_btnBrowse);

            // Plugins group
            _groupPlugins = new GroupBox
            {
                Text = "Components to Install",
                Size = new Size(470, 170),
                Location = new Point(15, 155)
            };

            int y = 22;
            _chkCardTextures = MakeCheckBox("Card Textures (custom perk images) - REQUIRED", y, true, false); y += 25;
            _chkBepInEx = MakeCheckBox("BepInEx (mod loader) - REQUIRED", y, true, false); y += 25;
            _chkLauncher = MakeCheckBox("Community Launcher (server connect + EAC bypass)", y, true, true); y += 25;
            _chkLongshot = MakeCheckBox("Longshot (recoil-free sniper mod)", y, false, true); y += 25;
            _chkCfgManager = MakeCheckBox("Configuration Manager (in-game settings, press F1)", y, true, true);

            _groupPlugins.Controls.Add(_chkCardTextures);
            _groupPlugins.Controls.Add(_chkBepInEx);
            _groupPlugins.Controls.Add(_chkLauncher);
            _groupPlugins.Controls.Add(_chkLongshot);
            _groupPlugins.Controls.Add(_chkCfgManager);

            // Status
            _lblStatus = new Label
            {
                Text = "",
                Size = new Size(470, 35),
                Location = new Point(15, 332)
            };

            // Progress
            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Visible = false,
                Size = new Size(470, 20),
                Location = new Point(15, 370)
            };

            // Install button
            _btnInstall = new Button
            {
                Text = "Install",
                Size = new Size(110, 32),
                Location = new Point(195, 398),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnInstall.Click += BtnInstall_Click;

            Controls.Add(_titleLabel);
            Controls.Add(_descLabel);
            Controls.Add(_groupGame);
            Controls.Add(_groupPlugins);
            Controls.Add(_lblStatus);
            Controls.Add(_progressBar);
            Controls.Add(_btnInstall);
        }

        private CheckBox MakeCheckBox(string text, int y, bool checked_, bool enabled)
        {
            return new CheckBox
            {
                Text = text,
                Checked = checked_,
                Enabled = enabled,
                Size = new Size(450, 20),
                Location = new Point(10, y)
            };
        }

        // ── Auto-detect ──────────────────────────────────────────────

        private void AutoDetectGameFolder()
        {
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

            // Steam registry
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
                // Parse libraryfolders.vdf
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

            // Common paths
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

        // ── Events ────────────────────────────────────────────────────

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

        private void BtnInstall_Click(object? sender, EventArgs e)
        {
            var gamePath = _txtPath.Text.Trim();
            if (!File.Exists(Path.Combine(gamePath, "Win64", "BlockNLoad.exe")))
            {
                MessageBox.Show(
                    $"Could not find BlockNLoad.exe in:\n{gamePath}\\Win64\\\n\nPlease select the correct Block N Load folder.",
                    "Game Not Found", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _btnInstall.Enabled = false;
            _progressBar.Visible = true;
            _lblStatus.Text = "Preparing...";
            Refresh();

            try
            {
                // Find release zip — local first, then GitHub
                string tempZip;
                var scriptDir = AppDomain.CurrentDomain.BaseDirectory;
                var localZip = Directory.GetFiles(scriptDir, "bnl-bepinex-plugins-v*.zip")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();

                if (localZip != null)
                {
                    tempZip = localZip.FullName;
                    _lblStatus.Text = $"Using local: {localZip.Name}";
                }
                else
                {
                    _lblStatus.Text = "Downloading latest release...";
                    Refresh();

                    string latestJson;
                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "BNL-Installer");
                        try
                        {
                            latestJson = client.DownloadString(
                                $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
                        }
                        catch
                        {
                            throw new Exception(
                                "Could not reach GitHub. Make sure you're online, or place the release zip next to this installer.\n\n" +
                                $"Download it from: https://github.com/{RepoOwner}/{RepoName}/releases");
                        }
                    }

                    // Simple JSON parsing
                    var tagName = ExtractJsonString(latestJson, "tag_name") ?? "";
                    var htmlUrl = ExtractJsonString(latestJson, "html_url") ?? "";
                    var assetUrl = ExtractJsonString(latestJson, "browser_download_url") ?? "";

                    // Find zip asset URL from full JSON
                    if (string.IsNullOrEmpty(assetUrl))
                    {
                        var assetsStart = latestJson.IndexOf("\"assets\":[", StringComparison.Ordinal);
                        if (assetsStart >= 0)
                        {
                            assetUrl = ExtractJsonString(
                                latestJson.Substring(assetsStart), "browser_download_url");
                        }
                    }

                    if (string.IsNullOrEmpty(assetUrl))
                        throw new Exception("No release zip found on GitHub.\n\n" +
                            $"Visit: https://github.com/{RepoOwner}/{RepoName}/releases");

                    tempZip = Path.Combine(Path.GetTempPath(), "bnl-update.zip");
                    _lblStatus.Text = "Downloading...";
                    Refresh();

                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "BNL-Installer");
                        client.DownloadFile(assetUrl, tempZip);
                    }
                }

                // Extract
                _lblStatus.Text = $"Installing to {gamePath}...";
                Refresh();

                var win64Dir = Path.Combine(gamePath, "Win64");
                var pluginsDir = Path.Combine(win64Dir, "BepInEx", "plugins");
                var cardDir = Path.Combine(pluginsDir, "Launcher", "CardTextures");

                Directory.CreateDirectory(Path.Combine(win64Dir, "BepInEx", "core"));
                Directory.CreateDirectory(Path.Combine(win64Dir, "BepInEx", "config"));
                Directory.CreateDirectory(pluginsDir);
                Directory.CreateDirectory(cardDir);

                using (var zip = ZipFile.OpenRead(tempZip))
                {
                    foreach (var entry in zip.Entries)
                    {
                        var relativePath = entry.FullName;
                        if (relativePath.StartsWith("Win64/", StringComparison.OrdinalIgnoreCase) ||
                            relativePath.StartsWith("Win64\\", StringComparison.OrdinalIgnoreCase))
                            relativePath = relativePath.Substring(6);

                        if (string.IsNullOrEmpty(relativePath) ||
                            relativePath.EndsWith("/") || relativePath.EndsWith("\\"))
                            continue;

                        // Filter optional components
                        if (relativePath.Contains("BnlPlugins.Longshot") && !_chkLongshot.Checked)
                            continue;
                        if (relativePath.Contains("ConfigurationManager") && !_chkCfgManager.Checked)
                            continue;

                        var destPath = Path.Combine(win64Dir, relativePath);
                        var destDir = Path.GetDirectoryName(destPath);
                        if (destDir != null)
                            Directory.CreateDirectory(destDir);

                        entry.ExtractToFile(destPath, true);
                    }
                }

                // Cleanup temp
                if (tempZip.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tempZip); } catch { }
                }

                _progressBar.Visible = false;
                _lblStatus.Text = "";

                var result = MessageBox.Show(
                    "BNL Community Launcher has been installed!\n\n" +
                    "Launch Block N Load through Steam to play on the community server.\n\n" +
                    "Open the CardTextures folder now?",
                    "Installation Complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                    System.Diagnostics.Process.Start("explorer.exe", cardDir);

                Close();
            }
            catch (Exception ex)
            {
                _progressBar.Visible = false;
                _btnInstall.Enabled = true;
                _lblStatus.Text = "Error: " + ex.Message;
                MessageBox.Show("Installation failed:\n" + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static string? ExtractJsonString(string json, string key)
        {
            var search = $"\"{key}\":\"";
            var start = json.IndexOf(search, StringComparison.Ordinal);
            if (start < 0)
            {
                search = $"\"{key}\": \"";
                start = json.IndexOf(search, StringComparison.Ordinal);
            }
            if (start < 0) return null;

            start += search.Length;
            var end = json.IndexOf("\"", start, StringComparison.Ordinal);
            if (end < 0) return null;

            return json.Substring(start, end - start);
        }
    }
}
