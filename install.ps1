# BNL Community Launcher - Installer
# Automatically detects your Block N Load folder and installs the mod.
# Run this script (right-click → Run with PowerShell).
param(
    [string]$GamePath = ""
)

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "BNL Community Launcher - Installer"

# GitHub release info
$RepoOwner = "devprbtt"
$RepoName = "bnl-bepinex-plugins"

# ── Auto-detect Block N Load ──────────────────────────────────────────

function Find-BnlFolder {
    $steamPaths = @(
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
        "HKCU:\SOFTWARE\Valve\Steam"
    )
    
    $libraryFolders = @()
    foreach ($sp in $steamPaths) {
        try {
            $steamPath = (Get-ItemProperty -Path $sp -Name "InstallPath" -ErrorAction Stop).InstallPath
            $libraryFolders += $steamPath
            # Also read libraryfolders.vdf for additional libraries
            $vdfPath = Join-Path $steamPath "steamapps\libraryfolders.vdf"
            if (Test-Path $vdfPath) {
                $vdf = Get-Content $vdfPath -Raw
                $matches = [regex]::Matches($vdf, '"\d+"\s+"([^"]+)"')
                foreach ($m in $matches) {
                    $libPath = $m.Groups[1].Value -replace '\\\\', '\'
                    $libraryFolders += $libPath
                }
            }
        }
        catch { }
    }
    
    # Also check common default locations
    $libraryFolders += "C:\Program Files (x86)\Steam"
    $libraryFolders += "C:\Program Files\Steam"
    $libraryFolders += "D:\Steam"
    $libraryFolders += "E:\Steam"
    
    $libraryFolders = $libraryFolders | Select-Object -Unique
    
    foreach ($lib in $libraryFolders) {
        $bnlPath = Join-Path $lib "steamapps\common\BlockNLoad"
        if (Test-Path (Join-Path $bnlPath "Win64\BlockNLoad.exe")) {
            return (Resolve-Path $bnlPath).Path
        }
    }
    
    return $null
}

# ── Windows Forms GUI ─────────────────────────────────────────────────

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form = New-Object System.Windows.Forms.Form
$form.Text = "BNL Community Launcher - Installer"
$form.Size = New-Object System.Drawing.Size(520, 480)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.Font = New-Object System.Drawing.Font("Segoe UI", 9)

# Title label
$titleLabel = New-Object System.Windows.Forms.Label
$titleLabel.Text = "Block N Load Community Launcher"
$titleLabel.Font = New-Object System.Drawing.Font("Segoe UI", 14, [System.Drawing.FontStyle]::Bold)
$titleLabel.Size = New-Object System.Drawing.Size(480, 30)
$titleLabel.Location = New-Object System.Drawing.Point(15, 15)

# Description
$descLabel = New-Object System.Windows.Forms.Label
$descLabel.Text = "Installs BepInEx + community launcher plugin.`nNo game files are modified - safe for Steam."
$descLabel.Size = New-Object System.Drawing.Size(480, 35)
$descLabel.Location = New-Object System.Drawing.Point(15, 50)

# Game folder group
$groupGame = New-Object System.Windows.Forms.GroupBox
$groupGame.Text = "Game Folder"
$groupGame.Size = New-Object System.Drawing.Size(480, 55)
$groupGame.Location = New-Object System.Drawing.Point(15, 95)

$txtPath = New-Object System.Windows.Forms.TextBox
$txtPath.Size = New-Object System.Drawing.Size(385, 20)
$txtPath.Location = New-Object System.Drawing.Point(10, 22)
$txtPath.Text = if ($GamePath) { $GamePath } else { "Detecting..." }

$btnBrowse = New-Object System.Windows.Forms.Button
$btnBrowse.Text = "Browse..."
$btnBrowse.Size = New-Object System.Drawing.Size(80, 23)
$btnBrowse.Location = New-Object System.Drawing.Point(400, 21)

$groupGame.Controls.Add($txtPath)
$groupGame.Controls.Add($btnBrowse)

# Plugins group
$groupPlugins = New-Object System.Windows.Forms.GroupBox
$groupPlugins.Text = "Components to Install"
$groupPlugins.Size = New-Object System.Drawing.Size(480, 175)
$groupPlugins.Location = New-Object System.Drawing.Point(15, 160)

# Mandatory: Card Textures
$chkCardTextures = New-Object System.Windows.Forms.CheckBox
$chkCardTextures.Text = "Card Textures (custom perk images) - REQUIRED"
$chkCardTextures.Checked = $true
$chkCardTextures.Enabled = $false
$chkCardTextures.Size = New-Object System.Drawing.Size(460, 20)
$chkCardTextures.Location = New-Object System.Drawing.Point(10, 22)

# Mandatory: BepInEx core
$chkBepInEx = New-Object System.Windows.Forms.CheckBox
$chkBepInEx.Text = "BepInEx (mod loader) - REQUIRED"
$chkBepInEx.Checked = $true
$chkBepInEx.Enabled = $false
$chkBepInEx.Size = New-Object System.Drawing.Size(460, 20)
$chkBepInEx.Location = New-Object System.Drawing.Point(10, 47)

# Optional: Launcher plugin
$chkLauncher = New-Object System.Windows.Forms.CheckBox
$chkLauncher.Text = "Community Launcher (server connect + EAC bypass)"
$chkLauncher.Checked = $true
$chkLauncher.Size = New-Object System.Drawing.Size(460, 20)
$chkLauncher.Location = New-Object System.Drawing.Point(10, 72)

# Optional: Longshot
$chkLongshot = New-Object System.Windows.Forms.CheckBox
$chkLongshot.Text = "Longshot (recoil-free sniper mod)"
$chkLongshot.Checked = $false
$chkLongshot.Size = New-Object System.Drawing.Size(460, 20)
$chkLongshot.Location = New-Object System.Drawing.Point(10, 97)

# Optional: Configuration Manager
$chkCfgManager = New-Object System.Windows.Forms.CheckBox
$chkCfgManager.Text = "Configuration Manager (in-game settings menu, press F1)"
$chkCfgManager.Checked = $true
$chkCfgManager.Size = New-Object System.Drawing.Size(460, 20)
$chkCfgManager.Location = New-Object System.Drawing.Point(10, 122)

$groupPlugins.Controls.Add($chkCardTextures)
$groupPlugins.Controls.Add($chkBepInEx)
$groupPlugins.Controls.Add($chkLauncher)
$groupPlugins.Controls.Add($chkLongshot)
$groupPlugins.Controls.Add($chkCfgManager)

# Progress
$lblStatus = New-Object System.Windows.Forms.Label
$lblStatus.Text = ""
$lblStatus.Size = New-Object System.Drawing.Size(480, 40)
$lblStatus.Location = New-Object System.Drawing.Point(15, 320)

$progressBar = New-Object System.Windows.Forms.ProgressBar
$progressBar.Style = "Marquee"
$progressBar.Visible = $false
$progressBar.Size = New-Object System.Drawing.Size(480, 20)
$progressBar.Location = New-Object System.Drawing.Point(15, 395)

# Install button
$btnInstall = New-Object System.Windows.Forms.Button
$btnInstall.Text = "Install"
$btnInstall.Size = New-Object System.Drawing.Size(100, 30)
$btnInstall.Location = New-Object System.Drawing.Point(200, 425)
$btnInstall.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)

# ── Events ────────────────────────────────────────────────────────────

$btnBrowse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Select your Block N Load folder (contains Win64\BlockNLoad.exe)"
    if ($dialog.ShowDialog() -eq "OK") {
        $txtPath.Text = $dialog.SelectedPath
    }
})

$btnInstall.Add_Click({
    $gamePath = $txtPath.Text.Trim()
    if (-not (Test-Path (Join-Path $gamePath "Win64\BlockNLoad.exe"))) {
        [System.Windows.Forms.MessageBox]::Show(
            "Could not find BlockNLoad.exe in:`n$gamePath\Win64\`n`nPlease select the correct Block N Load folder.",
            "Game Not Found", "OK", "Error")
        return
    }
    
    $btnInstall.Enabled = $false
    $progressBar.Visible = $true
    $form.Refresh()
    
    try {
        # Find the release zip — check local folder first, then GitHub
        $scriptDir = Split-Path -Parent (Get-Command $PSCommandPath -ErrorAction SilentlyContinue).Path
        if (-not $scriptDir) { $scriptDir = Get-Location }
        
        $localZip = Get-ChildItem $scriptDir -Filter "bnl-bepinex-plugins-v*.zip" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        
        if ($localZip) {
            $tempZip = $localZip.FullName
            $lblStatus.Text = "Using local: $($localZip.Name)"
            $form.Refresh()
        } else {
            # Download from GitHub
            $lblStatus.Text = "Downloading latest release..."
            $form.Refresh()
            
            $releaseUrl = "https://api.github.com/repos/$RepoOwner/$RepoName/releases/latest"
            $webClient = New-Object System.Net.WebClient
            $webClient.Headers.Add("User-Agent", "BNL-Installer")
            
            try {
                $releaseJson = $webClient.DownloadString($releaseUrl) | ConvertFrom-Json
            }
            catch {
                throw "Could not reach GitHub. Make sure you're online, or place the release zip next to this script.`n`nDownload it from: https://github.com/$RepoOwner/$RepoName/releases"
            }
            
            $zipAsset = $releaseJson.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1
            if (-not $zipAsset) {
                throw "No release zip found on GitHub.`n`nVisit: https://github.com/$RepoOwner/$RepoName/releases"
            }
            
            $tempZip = Join-Path $env:TEMP $zipAsset.name
            $lblStatus.Text = "Downloading $($zipAsset.name)..."
            $form.Refresh()
            $webClient.DownloadFile($zipAsset.browser_download_url, $tempZip)
        }
        
        # Extract
        $lblStatus.Text = "Installing to $gamePath ..."
        $form.Refresh()
        
        $win64Dir = Join-Path $gamePath "Win64"
        $pluginsDir = Join-Path $win64Dir "BepInEx\plugins"
        $cardDir = Join-Path $pluginsDir "Launcher\CardTextures"
        
        # Create directories
        $dirs = @(
            (Join-Path $win64Dir "BepInEx\core"),
            (Join-Path $win64Dir "BepInEx\config"),
            $pluginsDir,
            $cardDir
        )
        foreach ($d in $dirs) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
        
        # Extract from zip
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [System.IO.Compression.ZipFile]::OpenRead($tempZip)
        
        foreach ($entry in $zip.Entries) {
            $relativePath = $entry.FullName
            if ($relativePath.StartsWith("Win64\", [StringComparison]::OrdinalIgnoreCase)) {
                $relativePath = $relativePath.Substring("Win64\".Length)
            }
            
            # Skip if path is empty (folder entry)
            if ([string]::IsNullOrEmpty($relativePath) -or $relativePath.EndsWith("\") -or $relativePath.EndsWith("/")) {
                continue
            }
            
            $destPath = Join-Path $win64Dir $relativePath
            
            # Check plugin filtering
            $skip = $false
            
            # Longshot optional
            if ($relativePath -like "BepInEx\plugins\BnlPlugins.Longshot*" -and -not $chkLongshot.Checked) {
                $skip = $true
            }
            
            # Configuration Manager optional
            if ($relativePath -like "BepInEx\plugins\ConfigurationManager*" -and -not $chkCfgManager.Checked) {
                $skip = $true
            }
            
            if ($skip) { continue }
            
            # Ensure directory exists
            $destDir = Split-Path $destPath -Parent
            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }
            
            # Extract file
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destPath, $true)
        }
        $zip.Dispose()
        
        # Cleanup temp (only if we downloaded it)
        if ($tempZip -like "$env:TEMP*") {
            Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
        }
        
        $progressBar.Visible = $false
        $lblStatus.Text = ""
        
        # Done!
        $result = [System.Windows.Forms.MessageBox]::Show(
            "BNL Community Launcher has been installed!`n`n" +
            "Launch Block N Load through Steam to play on the community server.`n`n" +
            "Open the CardTextures folder now?",
            "Installation Complete", "YesNo", "Information")
        
        if ($result -eq "Yes") {
            Start-Process $cardDir
        }
        
        $form.Close()
    }
    catch {
        $progressBar.Visible = $false
        $btnInstall.Enabled = $true
        $lblStatus.Text = "Error: $($_.Exception.Message)"
        [System.Windows.Forms.MessageBox]::Show(
            "Installation failed:`n$($_.Exception.Message)",
            "Error", "OK", "Error")
    }
})

# ── Initialize ────────────────────────────────────────────────────────

# Auto-detect game folder
if (-not $GamePath) {
    $detected = Find-BnlFolder
    if ($detected) {
        $txtPath.Text = $detected
    } else {
        $txtPath.Text = ""
        $lblStatus.Text = "Could not auto-detect game folder. Please click Browse."
    }
}

# Add controls to form
$form.Controls.Add($titleLabel)
$form.Controls.Add($descLabel)
$form.Controls.Add($groupGame)
$form.Controls.Add($groupPlugins)
$form.Controls.Add($lblStatus)
$form.Controls.Add($progressBar)
$form.Controls.Add($btnInstall)

# Show dialog
$form.ShowDialog() | Out-Null
