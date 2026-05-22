using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BnlInstaller
{
    static class Program
    {
        private static bool IsNewerVersion(string latest, string current)
        {
            try
            {
                string[] l = latest.Split('.');
                string[] c = current.Split('.');
                int len = Math.Max(l.Length, c.Length);
                for (int i = 0; i < len; i++)
                {
                    int lv = i < l.Length ? int.Parse(l[i]) : 0;
                    int cv = i < c.Length ? int.Parse(c[i]) : 0;
                    if (lv > cv) return true;
                    if (lv < cv) return false;
                }
                return false;
            }
            catch { return string.Compare(latest, current, StringComparison.OrdinalIgnoreCase) > 0; }
        }

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

            // For silent update checks, do the version comparison before creating
            // any window so there is no visible flash when no update is available.
            if (checkUpdates && silentNoUpdate)
            {
                try
                {
                    string installed = currentVersion ?? "0.0.0";
                    if (string.IsNullOrWhiteSpace(installed)) installed = "0.0.0";

                    // Also read from version.txt if no version was passed
                    if (string.Equals(installed, "0.0.0") && !string.IsNullOrEmpty(gamePath))
                    {
                        string vp = Path.Combine(gamePath, "Win64", "BepInEx", "plugins", "Launcher", "version.txt");
                        if (File.Exists(vp)) installed = File.ReadAllText(vp).Trim();
                    }

                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "BNL-Installer");
                        string latest = client.DownloadString(
                            "https://raw.githubusercontent.com/devprbtt/bnl-bepinex-plugins/master/latest-version.txt").Trim();
                        if (latest.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                            latest = latest.Substring(1);
                        if (installed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                            installed = installed.Substring(1);
                        if (!IsNewerVersion(latest, installed))
                            return; // up to date — exit with no window at all
                    }
                }
                catch
                {
                    return; // network error — fail silently, don't nag the user
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(gamePath, checkUpdates, silentNoUpdate, currentVersion));
        }
    }

    public class MainForm : Form
    {
        private const string ManifestEntryPath = "BepInEx/plugins/Launcher/release-manifest.json";
        private const string RepoOwner = "devprbtt";
        private const string RepoName = "bnl-bepinex-plugins";

        private Label _titleLabel = null!;
        private Label _titleShadowLabel = null!;
        private Label _descLabel = null!;
        private PictureBox _thumbnailBox = null!;
        private GroupBox _groupGame = null!;
        private TextBox _txtPath = null!;
        private Button _btnBrowse = null!;
        private GroupBox _groupPlugins = null!;
        private Panel _componentsPanel = null!;
        private CheckBox _chkCardTextures = null!;
        private CheckBox _chkBepInEx = null!;
        private CheckBox _chkLauncher = null!;
        private CheckBox _chkCfgManager = null!;
        private CheckBox _chkSteamLaunchOption = null!;
        private Label _lblStatus = null!;
        private ProgressBar _progressBar = null!;
        private Button _btnInstall = null!;
        private readonly Dictionary<string, CheckBox> _optionalComponentChecks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Control> _dynamicComponentControls = new List<Control>();

        private readonly string? _initialGamePath;
        private readonly bool _checkUpdatesOnStart;
        private readonly bool _silentNoUpdate;
        private readonly string? _currentVersionOverride;
        private readonly ReleaseManifest _defaultManifest;
        private PrivateFontCollection? _privateFonts;
        private readonly List<IntPtr> _fontMemoryHandles = new List<IntPtr>();

        public MainForm(string? initialGamePath, bool checkUpdatesOnStart, bool silentNoUpdate, string? currentVersionOverride)
        {
            _initialGamePath = initialGamePath;
            _checkUpdatesOnStart = checkUpdatesOnStart;
            _silentNoUpdate = silentNoUpdate;
            _currentVersionOverride = currentVersionOverride;
            _defaultManifest = BuildDefaultManifest();
            InitializeComponent();
            AutoDetectGameFolder();
            Shown += MainForm_Shown;
        }

        private void MainForm_Shown(object? sender, EventArgs e)
        {
            if (_checkUpdatesOnStart)
                BeginInvoke(new Action(CheckForUpdatesOnly));
            else
                BeginInvoke(new Action(TryPopulateOptionalComponentsFromLatestRelease));
        }

        private void InitializeComponent()
        {
            Text = "BNL Community Launcher - Installer";
            ClientSize = new Size(640, 650);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Segoe UI", 9f);

            _thumbnailBox = new PictureBox
            {
                Location = new Point(15, 16),
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadBundledIconImage("assets\\bnl-icon.ico")
            };

            _titleShadowLabel = new Label
            {
                Text = "BLOCK N LOAD COMMUNITY LAUNCHER",
                Font = BuildTitleFont(18f),
                ForeColor = Color.FromArgb(32, 24, 8),
                Size = new Size(490, 32),
                Location = new Point(62, 18)
            };

            // Title
            _titleLabel = new Label
            {
                Text = "BLOCK N LOAD COMMUNITY LAUNCHER",
                Font = BuildTitleFont(18f),
                ForeColor = ColorTranslator.FromHtml("#FFDB3A"),
                Size = new Size(490, 32),
                Location = new Point(60, 16)
            };

            // Description
            _descLabel = new Label
            {
                Text = "Installs BepInEx + community launcher plugin.\nNo game files are modified - safe for Steam.",
                Size = new Size(490, 35),
                Location = new Point(62, 50)
            };

            // Game folder group
            _groupGame = new GroupBox
            {
                Text = "Game Folder",
                Size = new Size(610, 55),
                Location = new Point(15, 92)
            };

            _txtPath = new TextBox
            {
                Size = new Size(515, 23),
                Location = new Point(10, 22),
                Text = "Detecting..."
            };

            _btnBrowse = new Button
            {
                Text = "Browse...",
                Size = new Size(80, 23),
                Location = new Point(528, 21)
            };
            _btnBrowse.Click += BtnBrowse_Click;

            _groupGame.Controls.Add(_txtPath);
            _groupGame.Controls.Add(_btnBrowse);

            // Plugins group
            _groupPlugins = new GroupBox
            {
                Text = "Components to Install",
                Size = new Size(610, 360),
                Location = new Point(15, 155)
            };

            _componentsPanel = new Panel
            {
                Location = new Point(8, 20),
                Size = new Size(594, 332),
                AutoScroll = true
            };

            int y = 22;
            _chkCardTextures = MakeCheckBox("Card Textures (required bundled perk images)", y, true, false); y += 25;
            _chkBepInEx = MakeCheckBox("BepInEx (mod loader) - REQUIRED", y, true, false); y += 25;
            _chkLauncher = MakeCheckBox("Community Launcher (server connect + EAC bypass) - REQUIRED", y, true, false); y += 25;
            _chkCfgManager = MakeCheckBox("Configuration Manager (in-game settings, press Home to toggle)", y, true, true); y += 25;
            _chkSteamLaunchOption = MakeCheckBox("Optional: set Steam launch options to start BlockNLoad.exe directly", y, false, true);

            _componentsPanel.Controls.Add(_chkCardTextures);
            _componentsPanel.Controls.Add(_chkBepInEx);
            _componentsPanel.Controls.Add(_chkLauncher);
            _componentsPanel.Controls.Add(_chkCfgManager);
            _componentsPanel.Controls.Add(_chkSteamLaunchOption);
            _groupPlugins.Controls.Add(_componentsPanel);

            // Status
            _lblStatus = new Label
            {
                Text = "",
                Size = new Size(610, 32),
                Location = new Point(15, 523)
            };

            // Progress
            _progressBar = new ProgressBar
            {
                Style = ProgressBarStyle.Marquee,
                Visible = false,
                Size = new Size(610, 20),
                Location = new Point(15, 565)
            };

            // Install button
            _btnInstall = new Button
            {
                Text = "Install",
                Size = new Size(130, 34),
                Location = new Point(255, 598),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            _btnInstall.Click += BtnInstall_Click;

            Controls.Add(_thumbnailBox);
            Controls.Add(_titleShadowLabel);
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
                Size = new Size(560, 20),
                Location = new Point(10, y)
            };
        }

        private Font BuildTitleFont(float size)
        {
            try
            {
                if (_privateFonts == null)
                {
                    byte[]? fontBytes = ReadBundledResourceBytes("BnlInstaller.assets.edo.ttf");
                    if (fontBytes != null && fontBytes.Length > 0)
                    {
                        _privateFonts = new PrivateFontCollection();
                        IntPtr mem = Marshal.AllocCoTaskMem(fontBytes.Length);
                        Marshal.Copy(fontBytes, 0, mem, fontBytes.Length);
                        _privateFonts.AddMemoryFont(mem, fontBytes.Length);
                        _fontMemoryHandles.Add(mem);
                    }
                    else
                    {
                        string fontPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "edo.ttf");
                        if (File.Exists(fontPath))
                        {
                            _privateFonts = new PrivateFontCollection();
                            _privateFonts.AddFontFile(fontPath);
                        }
                    }
                }

                if (_privateFonts != null && _privateFonts.Families.Length > 0)
                    return new Font(_privateFonts.Families[0], size, FontStyle.Regular);
            }
            catch { }

            return new Font("Segoe UI", size, FontStyle.Bold);
        }

        private static Image? LoadBundledImage(string relativePath)
        {
            try
            {
                byte[]? bytes = ReadBundledResourceBytes("BnlInstaller.assets.bnl-splash.png");
                if (bytes == null || bytes.Length == 0)
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
                    if (!File.Exists(path))
                        return null;

                    bytes = File.ReadAllBytes(path);
                }

                using (var ms = new MemoryStream(bytes))
                using (var image = Image.FromStream(ms))
                {
                    return new Bitmap(image);
                }
            }
            catch
            {
                return null;
            }
        }

        private static Image? LoadBundledIconImage(string relativePath)
        {
            try
            {
                byte[]? bytes = ReadBundledResourceBytes("BnlInstaller.assets.bnl-icon.ico");
                if (bytes != null && bytes.Length > 0)
                {
                    using (var ms = new MemoryStream(bytes))
                    using (var icon = new Icon(ms))
                    {
                        return icon.ToBitmap();
                    }
                }

                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
                if (File.Exists(path))
                {
                    using (var icon = new Icon(path))
                    {
                        return icon.ToBitmap();
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static byte[]? ReadBundledResourceBytes(string resourceName)
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        // ── Auto-detect ──────────────────────────────────────────────

        private void AutoDetectGameFolder()
        {
            if (!string.IsNullOrEmpty(_initialGamePath) &&
                File.Exists(Path.Combine(_initialGamePath, "Win64", "BlockNLoad.exe")))
            {
                _txtPath.Text = _initialGamePath!;
                ApplyOptionalComponentsToUi(_defaultManifest, _txtPath.Text.Trim());
                TryPopulateOptionalComponentsFromLocalZip();
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

            ApplyOptionalComponentsToUi(_defaultManifest, _txtPath.Text.Trim());
            TryPopulateOptionalComponentsFromLocalZip();
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
                {
                    _txtPath.Text = dialog.SelectedPath;
                    ApplyOptionalComponentsToUi(_defaultManifest, _txtPath.Text.Trim());
                    TryPopulateOptionalComponentsFromLocalZip();
                }
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
                // Always download the latest release zip from GitHub to ensure
                // we never install from a stale local copy.
                string tempZip;
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
                                "Could not reach GitHub. Make sure you're online.\n\n" +
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

                if (_chkSteamLaunchOption.Checked && IsSteamRunning())
                {
                    throw new Exception("Steam is currently running.\n\nClose Steam first if you want the installer to update Block N Load launch options.");
                }

                ReleaseManifest manifest = LoadManifestFromZip(tempZip) ?? _defaultManifest;
                var selectedComponents = BuildSelectedComponentSet(manifest);
                if (!UiCoversManifestOptionalComponents(manifest) &&
                    !PromptForAdditionalOptionalComponents(manifest, selectedComponents, gamePath))
                {
                    _progressBar.Visible = false;
                    _btnInstall.Enabled = true;
                    _lblStatus.Text = "";
                    return;
                }

                var win64Dir = Path.Combine(gamePath, "Win64");
                var pluginsDir = Path.Combine(win64Dir, "BepInEx", "plugins");
                var cardDir = Path.Combine(pluginsDir, "Launcher", "CardTextures");

                Directory.CreateDirectory(Path.Combine(win64Dir, "BepInEx", "core"));
                Directory.CreateDirectory(Path.Combine(win64Dir, "BepInEx", "config"));
                Directory.CreateDirectory(pluginsDir);
                Directory.CreateDirectory(cardDir);
                string currentInstallerPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                string? pendingInstallerTempPath = null;
                string? pendingInstallerTargetPath = null;

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

                        if (!IsPathSelectedByManifest(relativePath, selectedComponents, manifest))
                            continue;

                        var destPath = Path.Combine(win64Dir, relativePath);
                        var destDir = Path.GetDirectoryName(destPath);
                        if (destDir != null)
                            Directory.CreateDirectory(destDir);

                        if (!string.IsNullOrEmpty(currentInstallerPath) &&
                            string.Equals(destPath, currentInstallerPath, StringComparison.OrdinalIgnoreCase))
                        {
                            pendingInstallerTargetPath = destPath;
                            pendingInstallerTempPath = Path.Combine(
                                Path.GetDirectoryName(destPath) ?? win64Dir,
                                "BNL-Installer.next.exe");
                            entry.ExtractToFile(pendingInstallerTempPath, true);
                            continue;
                        }

                        entry.ExtractToFile(destPath, true);
                    }
                }

                bool scheduledInstallerSelfUpdate = false;
                if (!string.IsNullOrEmpty(pendingInstallerTempPath) &&
                    !string.IsNullOrEmpty(pendingInstallerTargetPath) &&
                    File.Exists(pendingInstallerTempPath))
                {
                    ScheduleInstallerSelfReplace(
                        pendingInstallerTempPath!,
                        pendingInstallerTargetPath!);
                    scheduledInstallerSelfUpdate = true;
                }

                string launchOptionStatus = "";
                if (_chkSteamLaunchOption.Checked)
                {
                    string launchOptions = "\"" + Path.Combine(gamePath, "Win64", "BlockNLoad.exe") + "\" %COMMAND%";
                    LaunchOptionUpdateResult launchOptionResult = SetSteamLaunchOptionsForBlockNLoad(launchOptions);
                    if (!launchOptionResult.FoundAny)
                    {
                        throw new Exception(
                            "The mod files were installed, but Steam launch options could not be updated automatically.\n\n" +
                            "Make sure Steam has been opened at least once for this user and Block N Load has a localconfig.vdf entry.");
                    }

                    launchOptionStatus = launchOptionResult.UpdatedCount > 0
                        ? "\n\nSteam launch options updated for " + launchOptionResult.UpdatedCount + " Steam user configuration(s)."
                        : "\n\nSteam launch options were already set correctly.";
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
                    "Launch Block N Load through Steam to play on the community server." +
                    launchOptionStatus +
                    (scheduledInstallerSelfUpdate ? "\n\nThe local installer will replace itself after this window closes." : "") +
                    "\n\n" +
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
                string currentVersion = _currentVersionOverride ?? string.Empty;
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

                TryPopulateOptionalComponentsFromLatestRelease();
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

        private static bool IsSteamRunning()
        {
            return System.Diagnostics.Process.GetProcessesByName("steam").Length > 0
                || System.Diagnostics.Process.GetProcessesByName("steamwebhelper").Length > 0;
        }

        private static string? FindSteamPath()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    var installPath = key?.GetValue("InstallPath") as string;
                    if (!string.IsNullOrWhiteSpace(installPath))
                        return installPath;
                }
            }
            catch { }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                {
                    var installPath = key?.GetValue("InstallPath") as string;
                    if (!string.IsNullOrWhiteSpace(installPath))
                        return installPath;
                }
            }
            catch { }

            return null;
        }

        private static LaunchOptionUpdateResult SetSteamLaunchOptionsForBlockNLoad(string launchOptions)
        {
            string? steamPath = FindSteamPath();
            if (string.IsNullOrWhiteSpace(steamPath))
                return new LaunchOptionUpdateResult();

            string userdataDir = Path.Combine(steamPath, "userdata");
            if (!Directory.Exists(userdataDir))
                return new LaunchOptionUpdateResult();

            var result = new LaunchOptionUpdateResult();
            foreach (string localConfigPath in Directory.EnumerateFiles(userdataDir, "localconfig.vdf", SearchOption.AllDirectories))
            {
                try
                {
                    var fileResult = TryUpdateSteamLocalConfig(localConfigPath, launchOptions);
                    result.FoundAny |= fileResult.FoundAny;
                    result.UpdatedCount += fileResult.UpdatedCount;
                }
                catch { }
            }

            return result;
        }

        private static LaunchOptionUpdateResult TryUpdateSteamLocalConfig(string localConfigPath, string launchOptions)
        {
            var result = new LaunchOptionUpdateResult();
            var lines = File.ReadAllLines(localConfigPath).ToList();
            int appLine = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains("\"299360\""))
                {
                    int j = i + 1;
                    while (j < lines.Count && string.IsNullOrWhiteSpace(lines[j]))
                        j++;

                    if (j < lines.Count && lines[j].Trim() == "{")
                    {
                        appLine = i;
                        break;
                    }
                }
            }

            if (appLine < 0)
                return result;

            result.FoundAny = true;

            int openBraceLine = appLine + 1;
            while (openBraceLine < lines.Count && lines[openBraceLine].Trim() != "{")
                openBraceLine++;

            if (openBraceLine >= lines.Count)
                return result;

            int depth = 0;
            int closeBraceLine = -1;
            for (int i = openBraceLine; i < lines.Count; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed == "{")
                    depth++;
                else if (trimmed == "}")
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeBraceLine = i;
                        break;
                    }
                }
            }

            if (closeBraceLine < 0)
                return result;

            string launchLine = "\t\t\t\t\t\"LaunchOptions\"\t\t\"" + EscapeVdfString(launchOptions) + "\"";

            for (int i = openBraceLine + 1; i < closeBraceLine; i++)
            {
                if (lines[i].Contains("\"LaunchOptions\""))
                {
                    if (string.Equals(lines[i].Trim(), launchLine.Trim(), StringComparison.Ordinal))
                        return result;

                    lines[i] = launchLine;
                    File.WriteAllLines(localConfigPath, lines);
                    result.UpdatedCount = 1;
                    return result;
                }
            }

            lines.Insert(closeBraceLine, launchLine);
            File.WriteAllLines(localConfigPath, lines);
            result.UpdatedCount = 1;
            return result;
        }

        private static string EscapeVdfString(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
        }

        private static void ScheduleInstallerSelfReplace(string replacementPath, string targetPath)
        {
            string scriptPath = Path.Combine(Path.GetTempPath(), "bnl-installer-self-update-" + Guid.NewGuid().ToString("N") + ".cmd");
            string script =
                "@echo off\r\n" +
                "setlocal\r\n" +
                ":waitloop\r\n" +
                "ping 127.0.0.1 -n 2 >nul\r\n" +
                "move /Y \"" + replacementPath + "\" \"" + targetPath + "\" >nul 2>nul\r\n" +
                "if errorlevel 1 goto waitloop\r\n" +
                "del \"%~f0\" >nul 2>nul\r\n";

            File.WriteAllText(scriptPath, script, Encoding.ASCII);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + scriptPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            });
        }

        private static ReleaseManifest BuildDefaultManifest()
        {
            return new ReleaseManifest
            {
                Version = "0.0.0",
                Components = new List<ReleaseComponent>
                {
                    new ReleaseComponent
                    {
                        Id = "bepinex-runtime",
                        Name = "BepInEx Runtime",
                        Version = "1.3.5",
                        Description = "Doorstop bootstrap and BepInEx core files.",
                        Required = true,
                        Default = true,
                        Paths = new List<string>
                        {
                            ".doorstop_version",
                            "changelog.txt",
                            "doorstop_config.ini",
                            "winhttp.dll",
                            "BepInEx/core/",
                            "BepInEx/config/BepInEx.cfg"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "launcher",
                        Name = "Community Launcher",
                        Version = "1.3.5",
                        Description = "Server patches, installer helper, and version metadata.",
                        Required = true,
                        Default = true,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.Launcher/BnlPlugins.Launcher.dll",
                            "BepInEx/plugins/Launcher/BNL-Installer.exe",
                            "BepInEx/plugins/Launcher/version.txt",
                            ManifestEntryPath
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "card-textures",
                        Name = "Card Texture Overrides",
                        Version = "1.3.5",
                        Description = "Bundled custom card images required by launcher-provided perk and shop overrides.",
                        Required = true,
                        Default = true,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/Launcher/CardTextures/"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "configuration-manager",
                        Name = "Configuration Manager",
                        Version = "18.4.1",
                        Description = "In-game settings UI.",
                        Required = false,
                        Default = true,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/ConfigurationManager/",
                            "BepInEx/config/com.bepis.bepinex.configurationmanager.cfg"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "fov",
                        Name = "FOV / ADS",
                        Version = "0.1.0",
                        Description = "Forced camera FOV, ADS sensitivity multiplier, and weapon model FOV.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.Fov/BnlPlugins.Fov.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "crosshair",
                        Name = "Crosshair",
                        Version = "0.1.0",
                        Description = "Crosshair color, size, spread, visibility, and forced-shape overrides.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.Crosshair/BnlPlugins.Crosshair.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "combatnumbers",
                        Name = "Combat Numbers",
                        Version = "0.1.1",
                        Description = "Damage, crit, healing, combine, and self-heal number controls that match the community launcher behavior.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.CombatNumbers/BnlPlugins.CombatNumbers.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "shieldtimer",
                        Name = "Shield Timer",
                        Version = "0.1.0",
                        Description = "Enemy shield buff bar with circle or numeric shield duration timer, matching the community launcher behavior.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.ShieldTimer/BnlPlugins.ShieldTimer.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "buildpreview",
                        Name = "Build Preview",
                        Version = "0.1.0",
                        Description = "Optimistic local block and device placement with rollback on server rejection. Recommended mainly for high ping.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.BuildPreview/BnlPlugins.BuildPreview.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "aimhealthbar",
                        Name = "Aim Healthbar",
                        Version = "0.1.0",
                        Description = "Show a unit healthbar while your crosshair is aimed directly at that unit.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.AimHealthbar/BnlPlugins.AimHealthbar.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "deathcamhp",
                        Name = "Death Cam HP",
                        Version = "0.1.0",
                        Description = "Show spectated target HP in the death-cam nickname row and keep friendly healthbars visible while dead.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.DeathCamHp/BnlPlugins.DeathCamHp.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "autoqueue",
                        Name = "Auto Queue",
                        Version = "0.1.0",
                        Description = "Automatically join casual queue from custom games and leave the custom game when a match is found.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.AutoQueue/BnlPlugins.AutoQueue.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "lowhpalert",
                        Name = "Low HP Alert",
                        Version = "0.1.0",
                        Description = "Highlight low-health friendlies with an alert color and optional off-screen direction indicator.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.LowHpAlert/BnlPlugins.LowHpAlert.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "autocrouch",
                        Name = "Auto Crouch",
                        Version = "0.1.0",
                        Description = "Disable the forced-crouch behaviour that triggers when the ceiling is too low to stand.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.AutoCrouch/BnlPlugins.AutoCrouch.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "teammatehp",
                        Name = "Teammate HP",
                        Version = "0.1.0",
                        Description = "Show each teammate's HP percentage next to their name in the team panel.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.TeammateHp/BnlPlugins.TeammateHp.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "impactvfx",
                        Name = "Impact VFX",
                        Version = "0.1.0",
                        Description = "Hide impact and explosion VFX, lava/water plane visuals, and falling block visuals.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.ImpactVfx/BnlPlugins.ImpactVfx.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "unitguiwsiscale",
                        Name = "Unit GUI / WSI Scale",
                        Version = "0.1.0",
                        Description = "Scale unit GUI elements and world-space indicators with separate toggles and multipliers.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.UnitGuiWsiScale/BnlPlugins.UnitGuiWsiScale.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "maprender",
                        Name = "Map Render",
                        Version = "0.1.0",
                        Description = "Override the map's environmental lighting preset.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.MapRender/BnlPlugins.MapRender.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "misc",
                        Name = "Misc",
                        Version = "0.1.0",
                        Description = "Skip intro, disable main-menu frame cap, and hide the objective beam.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.Misc/BnlPlugins.Misc.dll"
                        }
                    },
                    new ReleaseComponent
                    {
                        Id = "teamcolors",
                        Name = "Team Colors",
                        Version = "0.1.0",
                        Description = "Override friendly, enemy, and background team colors with presets or custom hex values.",
                        Required = false,
                        Default = false,
                        Paths = new List<string>
                        {
                            "BepInEx/plugins/BnlPlugins.TeamColors/BnlPlugins.TeamColors.dll"
                        }
                    }
                }
            };
        }

        private static ReleaseManifest? LoadManifestFromZip(string zipPath)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var entry = zip.Entries.FirstOrDefault(e =>
                        string.Equals(NormalizeZipRelativePath(e.FullName), ManifestEntryPath, StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                        return null;

                    using (var stream = entry.Open())
                    {
                        var serializer = new DataContractJsonSerializer(typeof(ReleaseManifest));
                        return serializer.ReadObject(stream) as ReleaseManifest;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeZipRelativePath(string entryPath)
        {
            string path = entryPath.Replace('\\', '/');
            if (path.StartsWith("Win64/", StringComparison.OrdinalIgnoreCase))
                path = path.Substring("Win64/".Length);
            return path;
        }

        private HashSet<string> BuildSelectedComponentSet(ReleaseManifest manifest)
        {
            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string gamePath = _txtPath.Text.Trim();
            foreach (var component in manifest.Components)
            {
                if (component.Required)
                {
                    selected.Add(component.Id);
                    continue;
                }

                switch (component.Id)
                {
                    case "card-textures":
                        if (_chkCardTextures.Checked)
                            selected.Add(component.Id);
                        break;
                    case "configuration-manager":
                        if (_chkCfgManager.Checked)
                            selected.Add(component.Id);
                        break;
                    case "bepinex-runtime":
                        if (_chkBepInEx.Checked)
                            selected.Add(component.Id);
                        break;
                    case "launcher":
                        if (_chkLauncher.Checked)
                            selected.Add(component.Id);
                        break;
                    default:
                        var installInfo = !string.IsNullOrWhiteSpace(gamePath)
                            ? GetComponentInstallInfo(gamePath, component)
                            : ComponentInstallInfo.NotInstalled;
                        if (installInfo.IsInstalled && !installInfo.HasUpdateAvailable)
                        {
                            selected.Add(component.Id);
                        }
                        else if (_optionalComponentChecks.TryGetValue(component.Id, out var extraCheck) && extraCheck.Checked)
                            selected.Add(component.Id);
                        break;
                }
            }

            return selected;
        }

        private void TryPopulateOptionalComponentsFromLocalZip()
        {
            try
            {
                var scriptDir = AppDomain.CurrentDomain.BaseDirectory;
                var localZip = Directory.GetFiles(scriptDir, "bnl-bepinex-plugins-v*.zip")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .FirstOrDefault();
                if (localZip == null)
                    return;

                var manifest = LoadManifestFromZip(localZip.FullName);
                if (manifest == null)
                    return;

                ApplyOptionalComponentsToUi(manifest, _txtPath.Text.Trim());
            }
            catch { }
        }

        private void TryPopulateOptionalComponentsFromLatestRelease()
        {
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "BNL-Installer");
                    string latestJson = client.DownloadString(
                        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
                    var assetUrl = FindAssetUrl(latestJson, ".zip");
                    if (string.IsNullOrEmpty(assetUrl))
                        return;

                    string tempZip = Path.Combine(Path.GetTempPath(), "bnl-manifest-" + Guid.NewGuid().ToString("N") + ".zip");
                    try
                    {
                        client.DownloadFile(assetUrl, tempZip);
                        var manifest = LoadManifestFromZip(tempZip);
                        if (manifest != null)
                            ApplyOptionalComponentsToUi(manifest, _txtPath.Text.Trim());
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(tempZip))
                                File.Delete(tempZip);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void ApplyOptionalComponentsToUi(ReleaseManifest manifest, string gamePath)
        {
            foreach (var control in _dynamicComponentControls)
                _componentsPanel.Controls.Remove(control);
            _dynamicComponentControls.Clear();
            _optionalComponentChecks.Clear();

            var extraComponents = manifest.Components
                .Where(c => !c.Required
                    && !string.Equals(c.Id, "card-textures", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.Id, "configuration-manager", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.Id, "bepinex-runtime", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.Id, "launcher", StringComparison.OrdinalIgnoreCase)
                    && ShouldShowOptionalComponent(gamePath, c))
                .ToList();

            int y = _chkSteamLaunchOption.Bottom + 12;
            foreach (var component in extraComponents)
            {
                var installInfo = !string.IsNullOrWhiteSpace(gamePath)
                    ? GetComponentInstallInfo(gamePath, component)
                    : ComponentInstallInfo.NotInstalled;

                string checkText = component.Name;
                if (installInfo.IsInstalled && installInfo.HasUpdateAvailable)
                    checkText += $" (installed {installInfo.InstalledVersion ?? "unknown"} -> {component.Version})";

                var check = new CheckBox
                {
                    Text = checkText,
                    Checked = installInfo.IsInstalled ? true : component.Default,
                    Enabled = true,
                    Size = new Size(560, 20),
                    Location = new Point(10, y)
                };
                var desc = new Label
                {
                    Text = BuildComponentDescription(component, installInfo),
                    Size = new Size(530, 34),
                    Location = new Point(28, y + 20)
                };

                _componentsPanel.Controls.Add(check);
                _componentsPanel.Controls.Add(desc);
                _dynamicComponentControls.Add(check);
                _dynamicComponentControls.Add(desc);
                _optionalComponentChecks[component.Id] = check;
                y += 58;
            }
        }

        private bool UiCoversManifestOptionalComponents(ReleaseManifest manifest)
        {
            string gamePath = _txtPath.Text.Trim();
            return manifest.Components
                .Where(c => !c.Required
                    && !string.Equals(c.Id, "card-textures", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.Id, "configuration-manager", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.Id, "bepinex-runtime", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.Id, "launcher", StringComparison.OrdinalIgnoreCase)
                    && ShouldShowOptionalComponent(gamePath, c))
                .All(c => _optionalComponentChecks.ContainsKey(c.Id));
        }

        private static bool PromptForAdditionalOptionalComponents(ReleaseManifest manifest, HashSet<string> selectedComponents, string gamePath)
        {
            var extraComponents = manifest.Components
                .Where(c => !c.Required
                    && !string.Equals(c.Id, "card-textures", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.Id, "configuration-manager", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.Id, "bepinex-runtime", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(c.Id, "launcher", StringComparison.OrdinalIgnoreCase)
                    && ShouldShowOptionalComponent(gamePath, c))
                .ToList();

            if (extraComponents.Count == 0)
                return true;

            using (var form = new Form())
            using (var panel = new FlowLayoutPanel())
            using (var okButton = new Button())
            using (var cancelButton = new Button())
            {
                form.Text = "Optional Components";
                form.ClientSize = new Size(540, 360);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.Font = new Font("Segoe UI", 9f);

                form.Controls.Add(new Label
                {
                    Text = "This update includes optional components. Choose what to install.",
                    Location = new Point(15, 12),
                    Size = new Size(500, 34)
                });

                panel.Location = new Point(15, 52);
                panel.Size = new Size(500, 230);
                panel.FlowDirection = FlowDirection.TopDown;
                panel.WrapContents = false;
                panel.AutoScroll = true;
                form.Controls.Add(panel);

                var checkboxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
                foreach (var component in extraComponents)
                {
                    var installInfo = GetComponentInstallInfo(gamePath, component);
                    var check = new CheckBox
                    {
                        Text = installInfo.IsInstalled && installInfo.HasUpdateAvailable
                            ? component.Name + " (installed " + (installInfo.InstalledVersion ?? "unknown") + " -> " + component.Version + ")"
                            : component.Name,
                        Checked = installInfo.IsInstalled ? true : component.Default,
                        Width = 470,
                        Margin = new Padding(3, 3, 3, 0)
                    };
                    panel.Controls.Add(check);
                    panel.Controls.Add(new Label
                    {
                        Text = BuildComponentDescription(component, installInfo),
                        Width = 470,
                        Height = 32,
                        Margin = new Padding(24, 0, 3, 8)
                    });
                    checkboxes[component.Id] = check;
                }

                okButton.Text = "Continue";
                okButton.Size = new Size(110, 30);
                okButton.Location = new Point(290, 300);
                okButton.DialogResult = DialogResult.OK;
                form.Controls.Add(okButton);

                cancelButton.Text = "Cancel";
                cancelButton.Size = new Size(110, 30);
                cancelButton.Location = new Point(405, 300);
                cancelButton.DialogResult = DialogResult.Cancel;
                form.Controls.Add(cancelButton);

                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                if (form.ShowDialog() != DialogResult.OK)
                    return false;

                foreach (var component in extraComponents)
                {
                    if (checkboxes.TryGetValue(component.Id, out var cb) && cb.Checked)
                        selectedComponents.Add(component.Id);
                }

                return true;
            }
        }

        private static bool IsPathSelectedByManifest(string relativePath, HashSet<string> selectedComponents, ReleaseManifest manifest)
        {
            string normalized = relativePath.Replace('\\', '/');
            foreach (var component in manifest.Components)
            {
                if (!selectedComponents.Contains(component.Id))
                    continue;

                foreach (string componentPath in component.Paths)
                {
                    string expected = componentPath.Replace('\\', '/').TrimStart('/');
                    if (expected.EndsWith("/"))
                    {
                        if (normalized.StartsWith(expected, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    else if (string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsComponentInstalled(string gamePath, ReleaseComponent component)
        {
            foreach (string componentPath in component.Paths)
            {
                string normalized = componentPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                string fullPath = Path.Combine(Path.Combine(gamePath, "Win64"), normalized);
                if (componentPath.EndsWith("/"))
                {
                    if (Directory.Exists(fullPath.TrimEnd(Path.DirectorySeparatorChar)))
                        return true;
                }
                else if (File.Exists(fullPath) || Directory.Exists(fullPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldShowOptionalComponent(string gamePath, ReleaseComponent component)
        {
            if (string.IsNullOrWhiteSpace(gamePath))
                return true;

            var info = GetComponentInstallInfo(gamePath, component);
            return !info.IsInstalled || info.HasUpdateAvailable;
        }

        private static string BuildComponentDescription(ReleaseComponent component, ComponentInstallInfo installInfo)
        {
            if (installInfo.IsInstalled && installInfo.HasUpdateAvailable)
                return (component.Description ?? "") + Environment.NewLine + "Update available: " + (installInfo.InstalledVersion ?? "unknown") + " -> " + component.Version;

            if (!string.IsNullOrWhiteSpace(component.Version))
                return (component.Description ?? "") + Environment.NewLine + "Version: " + component.Version;

            return component.Description ?? "";
        }

        private static ComponentInstallInfo GetComponentInstallInfo(string gamePath, ReleaseComponent component)
        {
            if (string.IsNullOrWhiteSpace(gamePath))
                return ComponentInstallInfo.NotInstalled;

            bool installed = IsComponentInstalled(gamePath, component);
            if (!installed)
                return ComponentInstallInfo.NotInstalled;

            string? installedVersion = TryGetInstalledComponentVersion(gamePath, component);
            bool hasUpdateAvailable = false;
            if (!string.IsNullOrWhiteSpace(component.Version))
            {
                if (string.IsNullOrWhiteSpace(installedVersion))
                    hasUpdateAvailable = true;
                else
                    hasUpdateAvailable = IsNewerVersion(component.Version, installedVersion!);
            }

            return new ComponentInstallInfo
            {
                IsInstalled = true,
                InstalledVersion = installedVersion,
                HasUpdateAvailable = hasUpdateAvailable
            };
        }

        private static string? TryGetInstalledComponentVersion(string gamePath, ReleaseComponent component)
        {
            foreach (string componentPath in component.Paths)
            {
                string normalized = componentPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
                string fullPath = Path.Combine(Path.Combine(gamePath, "Win64"), normalized);

                if (componentPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                {
                    try
                    {
                        var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(fullPath);
                        if (!string.IsNullOrWhiteSpace(fileVersion.ProductVersion))
                            return NormalizeVersionString(fileVersion.ProductVersion!);
                        if (!string.IsNullOrWhiteSpace(fileVersion.FileVersion))
                            return NormalizeVersionString(fileVersion.FileVersion!);
                    }
                    catch { }
                }

                if (normalized.EndsWith("version.txt", StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                {
                    try
                    {
                        return NormalizeVersionString(File.ReadAllText(fullPath).Trim());
                    }
                    catch { }
                }
            }

            return null;
        }

        private static string NormalizeVersionString(string value)
        {
            int plus = value.IndexOf('+');
            if (plus >= 0)
                value = value.Substring(0, plus);

            int space = value.IndexOf(' ');
            if (space >= 0)
                value = value.Substring(0, space);

            return value.Trim();
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

    [DataContract]
    public class ReleaseManifest
    {
        [DataMember(Name = "version")]
        public string Version { get; set; } = string.Empty;

        [DataMember(Name = "components")]
        public List<ReleaseComponent> Components { get; set; } = new List<ReleaseComponent>();
    }

    [DataContract]
    public class ReleaseComponent
    {
        [DataMember(Name = "id")]
        public string Id { get; set; } = string.Empty;

        [DataMember(Name = "name")]
        public string Name { get; set; } = string.Empty;

        [DataMember(Name = "version")]
        public string Version { get; set; } = string.Empty;

        [DataMember(Name = "description")]
        public string Description { get; set; } = string.Empty;

        [DataMember(Name = "required")]
        public bool Required { get; set; }

        [DataMember(Name = "default")]
        public bool Default { get; set; }

        [DataMember(Name = "paths")]
        public List<string> Paths { get; set; } = new List<string>();
    }

    public struct LaunchOptionUpdateResult
    {
        public bool FoundAny { get; set; }
        public int UpdatedCount { get; set; }
    }

    public struct ComponentInstallInfo
    {
        public static ComponentInstallInfo NotInstalled => new ComponentInstallInfo
        {
            IsInstalled = false,
            InstalledVersion = null,
            HasUpdateAvailable = false
        };

        public bool IsInstalled { get; set; }
        public string? InstalledVersion { get; set; }
        public bool HasUpdateAvailable { get; set; }
    }
}
