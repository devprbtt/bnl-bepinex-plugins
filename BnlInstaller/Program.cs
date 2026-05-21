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
        static void Main(string[] args)
        {
            string? gamePath = null;
            string? currentVersion = null;
            bool checkUpdates = false;
            bool silentNoUpdate = false;

            foreach (string arg in args)
            {
                if (arg.StartsWith("--game-path=", StringComparison.OrdinalIgnoreCase))
                {
                    gamePath = arg.Substring("--game-path=".Length).Trim('"');
                }
                else if (arg.StartsWith("--current-version=", StringComparison.OrdinalIgnoreCase))
                {
                    currentVersion = arg.Substring("--current-version=".Length).Trim('"');
                }
                else if (string.Equals(arg, "--check-updates", StringComparison.OrdinalIgnoreCase))
                {
                    checkUpdates = true;
                }
                else if (string.Equals(arg, "--silent-no-update", StringComparison.OrdinalIgnoreCase))
                {
                    silentNoUpdate = true;
                }
                else if (!arg.StartsWith("--", StringComparison.Ordinal) && string.IsNullOrEmpty(gamePath))
                {
                    gamePath = arg;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(gamePath, checkUpdates, silentNoUpdate, currentVersion));
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
        private CheckBox _chkCfgManager = null!;
        private Label _lblStatus = null!;
        private ProgressBar _progressBar = null!;
        private Button _btnInstall = null!;

        private readonly string? _initialGamePath;
        private readonly bool _checkUpdatesOnStart;
        private readonly bool _silentNoUpdate;
        private readonly string? _currentVersionOverride;

        public MainForm(string? initialGamePath, bool checkUpdatesOnStart, bool silentNoUpdate, string? currentVersionOverride)
        {
            _initialGamePath = initialGamePath;
            _checkUpdatesOnStart = checkUpdatesOnStart;
            _silentNoUpdate = silentNoUpdate;
            _currentVersionOverride = currentVersionOverride;
            InitializeComponent();
            AutoDetectGameFolder();
            Shown += MainForm_Shown;
        }

        private void MainForm_Shown(object? sender, EventArgs e)
        {
            if (_checkUpdatesOnStart)
                BeginInvoke(new Action(CheckForUpdatesOnly));
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
                Size = new Size(470, 145),
                Location = new Point(15, 155)
            };

            int y = 22;
            _chkCardTextures = MakeCheckBox("Card Textures (custom perk images) - REQUIRED", y, true, false); y += 25;
            _chkBepInEx = MakeCheckBox("BepInEx (mod loader) - REQUIRED", y, true, false); y += 25;
            _chkLauncher = MakeCheckBox("Community Launcher (server connect + EAC bypass) - REQUIRED", y, true, false); y += 25;
            _chkCfgManager = MakeCheckBox("Configuration Manager (in-game settings, press `)", y, true, true);

            _groupPlugins.Controls.Add(_chkCardTextures);
            _groupPlugins.Controls.Add(_chkBepInEx);
            _groupPlugins.Controls.Add(_chkLauncher);
            _groupPlugins.Controls.Add(_chkCfgManager);

            // Status
            _lblStatus = new Label
            {
                Text = "",
                Size = new Size(470, 35),
                Location = new Point(15, 308)
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

                    var assetUrl = FindAssetUrl(latestJson, ".zip");

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

                if (IsGameRunning(gamePath))
                {
                    throw new Exception("Block N Load is currently running.\n\nClose the game first, then run the installer again.");
                }

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

        private void CheckForUpdatesOnly()
        {
            try
            {
                string currentVersion = _currentVersionOverride;
                if (string.IsNullOrWhiteSpace(currentVersion))
                {
                    var gamePath = _txtPath.Text.Trim();
                    if (!string.IsNullOrEmpty(gamePath))
                    {
                        string versionPath = Path.Combine(gamePath, "Win64", "BepInEx", "plugins", "Launcher", "version.txt");
                        if (File.Exists(versionPath))
                            currentVersion = File.ReadAllText(versionPath).Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(currentVersion))
                    currentVersion = "0.0.0";

                string latestVersion;
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "BNL-Installer");
                    latestVersion = client.DownloadString(
                        $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/master/latest-version.txt").Trim();
                }

                latestVersion = latestVersion.Trim();
                if (latestVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    latestVersion = latestVersion.Substring(1);
                if (currentVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    currentVersion = currentVersion.Substring(1);

                if (!IsNewerVersion(latestVersion, currentVersion))
                {
                    if (_silentNoUpdate)
                    {
                        Close();
                        return;
                    }

                    MessageBox.Show(
                        $"You're up to date.\n\nInstalled: v{currentVersion}\nLatest: v{latestVersion}",
                        "BNL Community Launcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    Close();
                    return;
                }

                _lblStatus.Text = $"Update available: v{latestVersion} (installed v{currentVersion}). Close the game before installing.";
                _btnInstall.Text = "Update";
                Activate();
            }
            catch (Exception ex)
            {
                if (_silentNoUpdate)
                {
                    Close();
                    return;
                }

                MessageBox.Show(
                    "Update check failed:\n" + ex.Message,
                    "BNL Community Launcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                Close();
            }
        }

        private static string? FindAssetUrl(string json, string requiredExtension)
        {
            const string marker = "\"browser_download_url\":\"";
            int searchFrom = 0;
            while (true)
            {
                int start = json.IndexOf(marker, searchFrom, StringComparison.Ordinal);
                if (start < 0)
                    return null;

                start += marker.Length;
                int end = json.IndexOf("\"", start, StringComparison.Ordinal);
                if (end < 0)
                    return null;

                string candidate = json.Substring(start, end - start);
                if (candidate.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
                    return candidate;

                searchFrom = end + 1;
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

        private static bool IsNewerVersion(string latest, string current)
        {
            try
            {
                string[] latestParts = latest.Split('.');
                string[] currentParts = current.Split('.');
                int maxLen = Math.Max(latestParts.Length, currentParts.Length);

                for (int i = 0; i < maxLen; i++)
                {
                    int l = i < latestParts.Length ? int.Parse(latestParts[i]) : 0;
                    int c = i < currentParts.Length ? int.Parse(currentParts[i]) : 0;
                    if (l > c) return true;
                    if (l < c) return false;
                }

                return false;
            }
            catch
            {
                return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0;
            }
        }
    }
}
