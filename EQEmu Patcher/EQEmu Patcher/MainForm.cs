using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using Microsoft.WindowsAPICodePack.Taskbar;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;

namespace EQEmu_Patcher
{

    public partial class MainForm : Form
    {

        public static string serverName; // server title name
        public static string version; //version of file
        string fileName; //base name of executable
        bool isPatching = false;
        bool isPatchCancelled = false;
        bool isPendingPatch = false; // This is used to indicate that someone pressed "Patch" before we did some background update checks
        bool isLoading;
        bool isAutoPatch = false;
        bool isAutoPlay = false;
        bool isCheckingForUpdates = false;
        CancellationTokenSource cts;
        System.Diagnostics.Process process;

        //Note that for supported versions, the 3 letter suffix is needed on the filelist_###.yml file.
        public static List<VersionTypes> supportedClients = new List<VersionTypes> { //Supported clients for patcher
            //VersionTypes.Unknown, //unk
            //VersionTypes.Titanium, //tit
            //VersionTypes.Underfoot, //und
            //VersionTypes.Secrets_Of_Feydwer, //sof
            //VersionTypes.Seeds_Of_Destruction, //sod
            VersionTypes.Rain_Of_Fear, //rof
            VersionTypes.Rain_Of_Fear_2 //rof
            //VersionTypes.Broken_Mirror, //bro
        };

        private Dictionary<VersionTypes, ClientVersion> clientVersions = new Dictionary<VersionTypes, ClientVersion>();

        VersionTypes currentVersion;

       // TaskbarItemInfo tii = new TaskbarItemInfo();
        public MainForm()
        {
            InitializeComponent();
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            isLoading = true;
            
            // Set the form icon to match the application icon
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            version = Assembly.GetEntryAssembly().GetName().Version.ToString();
            Console.WriteLine($"Initializing {version}");
            Console.WriteLine($"Current Directory: {Directory.GetCurrentDirectory()}");
            cts = new CancellationTokenSource();

            serverName = Assembly.GetExecutingAssembly().GetCustomAttribute<ServerName>().Value;
#if (DEBUG)
            serverName = "EQEMU Patcher";
#endif
            if (serverName == "") {
                MessageBox.Show("This patcher was built incorrectly. Please contact the distributor of this and inform them the server name is not provided or screenshot this message.");
                this.Close();
                return;
            }

            fileName = Assembly.GetExecutingAssembly().GetCustomAttribute<FileName>().Value;
#if (DEBUG)
            fileName = "eqemupatcher";
#endif
            if (fileName == "")
            {
                MessageBox.Show("This patcher was built incorrectly. Please contact the distributor of this and inform them the file name is not provided or screenshot this message.");
                this.Close();
                return;
            }

            // filelistUrl and patcherUrl no longer needed - we download directly from Spire

            txtList.Visible = false;
            splashLogo.Visible = true;
            if (this.Width < 432) {
                this.Width = 432;
            }
            if (this.Height < 550)
            {
                this.Height = 550;
            }
            buildClientVersions();
            IniLibrary.Load();
            detectClientVersion();
            isAutoPlay = (IniLibrary.instance.AutoPlay.ToLower() == "true");
            isAutoPatch = (IniLibrary.instance.AutoPatch.ToLower() == "true");
            chkAutoPlay.Checked = isAutoPlay;
            chkAutoPatch.Checked = isAutoPatch;
            try
            {
                if (File.Exists(Application.ExecutablePath + ".old"))
                {
                    File.Delete(Application.ExecutablePath + ".old");
                }

            } catch (Exception exDelete)
            {
                Console.WriteLine($"Failed to delete .old file: {exDelete.Message}");
            }

            if (IniLibrary.instance.ClientVersion == VersionTypes.Unknown)
            {
                detectClientVersion();
                if (currentVersion == VersionTypes.Unknown)
                {
                    this.Close();
                }
                IniLibrary.instance.ClientVersion = currentVersion;
                IniLibrary.Save();
            }
            string suffix = "unk";
            if (currentVersion == VersionTypes.Titanium) suffix = "tit";
            if (currentVersion == VersionTypes.Underfoot) suffix = "und";
            if (currentVersion == VersionTypes.Seeds_Of_Destruction) suffix = "sod";
            if (currentVersion == VersionTypes.Broken_Mirror) suffix = "bro";
            if (currentVersion == VersionTypes.Secrets_Of_Feydwer) suffix = "sof";
            if (currentVersion == VersionTypes.Rain_Of_Fear || currentVersion == VersionTypes.Rain_Of_Fear_2) suffix = "rof";

            bool isSupported = false;
            foreach (var ver in supportedClients)
            {
                if (ver != currentVersion) continue;
                isSupported = true;
                break;
            }
            if (!isSupported) {
                MessageBox.Show("The server " + serverName + " does not work with this copy of Everquest (" + currentVersion.ToString().Replace("_", " ") + ")", serverName);
                this.Close();
                return;
            }

            this.Text = serverName + " (Client: " + currentVersion.ToString().Replace("_", " ") + ")";
            progressBar.Minimum = 0;
            progressBar.Maximum = 10000;
            progressBar.Value = 0;
            StatusLibrary.SubscribeProgress(new StatusLibrary.ProgressHandler((int value) => {
                Invoke((MethodInvoker)delegate {
                    progressBar.Value = value;
                    if (Environment.OSVersion.Version.Major < 6) {
                        return;
                    }
                    var taskbar = TaskbarManager.Instance;
                    taskbar.SetProgressValue(value, 10000);
                    taskbar.SetProgressState((value == 10000) ? TaskbarProgressBarState.NoProgress : TaskbarProgressBarState.Normal);
                });
            }));

            StatusLibrary.SubscribeLogAdd(new StatusLibrary.LogAddHandler((string message) => {
                Invoke((MethodInvoker)delegate {
                    if (!txtList.Visible)
                    {
                        txtList.Visible = true;
                        splashLogo.Visible = false;
                    }
                    txtList.AppendText(message + "\r\n");
                });
            }));

            StatusLibrary.SubscribePatchState(new StatusLibrary.PatchStateHandler((bool isPatchGoing) => {
                Invoke((MethodInvoker)delegate {

                    btnCheck.BackColor = SystemColors.Control;
                    if (isPatchGoing)
                    {
                        btnCheck.Text = "Cancel";
                        return;
                    }

                    btnCheck.Text = "Patch";
                });
            }));

            // No filelist needed - we download directly from Spire each time
            isLoading = false;
            
            var path = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\eqemupatcher.png";
            if (File.Exists(path))
            {
                splashLogo.Load(path);
            }

            // Check if we just updated
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1] == "--updated")
            {
                StatusLibrary.Log("Patcher updated successfully!");
            }

            // Check for patcher updates from GitHub releases
            cts = new CancellationTokenSource();
            await CheckForPatcherUpdateAsync();
        }

        /// <summary>
        /// Check for updates to the patcher itself
        /// </summary>
        private async Task CheckForPatcherUpdateAsync()
        {
            if (isCheckingForUpdates) return;
            isCheckingForUpdates = true;

            try
            {
                var updateResult = await UtilityLibrary.CheckForUpdateAsync(cts);

                if (updateResult.Error != null)
                {
                    Console.WriteLine($"Update check error: {updateResult.Error}");
                    // Don't show error to user - just silently continue
                    return;
                }

                if (updateResult.UpdateAvailable && !string.IsNullOrEmpty(updateResult.DownloadUrl))
                {
                    var result = MessageBox.Show(
                        $"A new version of the patcher is available!\n\n" +
                        $"Current version: {updateResult.CurrentVersion}\n" +
                        $"New version: {updateResult.LatestVersion}\n\n" +
                        $"Would you like to update now?",
                        "Patcher Update Available",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (result == DialogResult.Yes)
                    {
                        bool shouldExit = await UtilityLibrary.ApplyUpdateAsync(
                            cts, 
                            updateResult.DownloadUrl, 
                            (msg) => StatusLibrary.Log(msg));

                        if (shouldExit)
                        {
                            this.Close();
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update check exception: {ex.Message}");
            }
            finally
            {
                isCheckingForUpdates = false;
            }
        }

        private void detectClientVersion()
        {
            /*
            try
            {
                var hash = UtilityLibrary.GetEverquestExecutableHash(AppDomain.CurrentDomain.BaseDirectory);
                if (hash == "")
                {
                    MessageBox.Show("Please run this patcher in your Everquest directory.");
                    this.Close();
                    return;
                }
                switch (hash)
                {
                    case "240C80800112ADA825C146D7349CE85B":
                    case "A057A23F030BAA1C4910323B131407105ACAD14D": //This is a custom ROF2 from a torrent download
                    case "178C9C8FDDDF8F78B6B9142D025FE059": // Custom THJ
                    case "36968E793EBFDB3A1A1C55C7FF1D7C1A": // Retribution
                    case "6574AC667D4C522D21A47F4D00920CC2": // LAA
                    case "389709EC0E456C3DAE881A61218AAB3F": // This is a 4gb patched eqgame
                    case "AE4E4C995DF8842DAE3127E88E724033": // gangsta of RoT 4gb patched eqgame
                    case "3B44C6CD42313CB80C323647BCB296EF": // https://github.com/xackery/eqemupatcher/issues/15
                    case "513FDC2B5CC63898D7962F0985D5C207": // aslr checksum removed
                    case "26DC13388395A20B73E1B5A08415B0F8": // Legacy of Norrath Custom RoF2 Client https://github.com/xackery/eqemupatcher/issues/16
                        currentVersion = VersionTypes.Rain_Of_Fear_2;
                        splashLogo.Image = Properties.Resources.rof;
                        break;
                    default:
                        currentVersion = VersionTypes.Unknown;
                        break;
                }
                if (currentVersion == VersionTypes.Unknown)
                {
                    if (MessageBox.Show("Unable to recognize the Everquest client in this directory, open a web page to report to devs?", "Visit", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
                    {
                        System.Diagnostics.Process.Start("https://github.com/Xackery/eqemupatcher/issues/new?title=A+New+EQClient+Found&body=Hi+I+Found+A+New+Client!+Hash:+" + hash);
                    }
                    StatusLibrary.Log($"Unable to recognize the Everquest client in this directory, send to developers: {hash}");
                }
                else
                {
                    //StatusLibrary.Log($"You seem to have put me in a {clientVersions[currentVersion].FullName} client directory");
                }

                //MessageBox.Show(""+currentVersion);
                //StatusLibrary.Log($"If you wish to help out, press the scan button on the bottom left and wait for it to complete, then copy paste this data as an Issue on github!");
            }
            catch (UnauthorizedAccessException err)
            {
                MessageBox.Show("You need to run this program with Administrative Privileges" + err.Message);
                return;
            }
            */
            currentVersion = VersionTypes.Rain_Of_Fear_2;
            splashLogo.Image = Properties.Resources.rof;
        }

        //Build out all client version's dictionary
        private void buildClientVersions()
        {
            clientVersions.Clear();
            clientVersions.Add(VersionTypes.Titanium, new ClientVersion("Titanium", "titanium"));
            clientVersions.Add(VersionTypes.Secrets_Of_Feydwer, new ClientVersion("Secrets Of Feydwer", "sof"));
            clientVersions.Add(VersionTypes.Seeds_Of_Destruction, new ClientVersion("Seeds of Destruction", "sod"));
            clientVersions.Add(VersionTypes.Rain_Of_Fear, new ClientVersion("Rain of Fear", "rof"));
            clientVersions.Add(VersionTypes.Rain_Of_Fear_2, new ClientVersion("Rain of Fear 2", "rof2"));
            clientVersions.Add(VersionTypes.Underfoot, new ClientVersion("Underfoot", "underfoot"));
            clientVersions.Add(VersionTypes.Broken_Mirror, new ClientVersion("Broken Mirror", "brokenmirror"));
        }


        private void btnStart_Click(object sender, EventArgs e)
        {
            PlayGame();
        }

        private void PlayGame()
        {
            try
            {
                StatusLibrary.Log("Checking THJ UI...");
               
                string eqPath = Path.GetDirectoryName(Application.ExecutablePath);
                var di = new DirectoryInfo(eqPath);
                var files = di.GetFiles("UI*_thj.ini");

                foreach (var file in files)
                {
                    if (file.Length > 10240)
                    {
                        continue;
                    }
                    StatusLibrary.Log("Found corrupted UI file: " + file.Name);
                    string bakFile = file.FullName + ".bak";
                    if (File.Exists(bakFile))
                    {
                        if (MessageBox.Show($"UI file {file.Name} appears to be corrupted. Would you like to restore the backup?", "Restore Backup", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            File.Copy(bakFile, file.FullName, true);
                        }
                        continue;
                    }
                    if (MessageBox.Show($"UI file {file.Name} appears to be corrupted. Would you like to restore the default UI?", "Restore Default", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        File.WriteAllText(file.FullName, DefaultUI.String());
                    }
                }


                // now just back up all the UI files
                foreach (var file in files)
                {
                    if (file.Length < 10240)
                    {
                        continue;
                    }
                    string bakFile = file.FullName + ".bak";                   
                    // force overwrite ther existing file
                    if (File.Exists(bakFile))
                    {
                        File.Delete(bakFile);
                    }
                    File.Copy(file.FullName, bakFile);
                }
                StatusLibrary.Log("THJ UI check complete.");
            }
            catch (Exception err) 
            {
                MessageBox.Show("An error occured while trying to check UI files: " + err.Message);
            }

            try
            {
                process = UtilityLibrary.StartEverquest();
                if (process != null)
                {
                    // Minimize to system tray and inject DLL
                    StatusLibrary.Log("Minimizing patcher to system tray...");
                    this.WindowState = FormWindowState.Minimized;
                    this.ShowInTaskbar = false;

                    // Start injection in background thread
                    string dllPath = Path.GetDirectoryName(Application.ExecutablePath) + "\\godsofnorrath.dll";
                    Task.Run(() =>
                    {
                        bool injected = UtilityLibrary.WaitAndInject("eqgame", dllPath);
                        if (injected)
                        {
                            StatusLibrary.Log("DLL injected successfully! Closing patcher...");
                            // Small delay to let the log message be seen
                            System.Threading.Thread.Sleep(1000);
                        }
                        else
                        {
                            StatusLibrary.Log("Failed to inject DLL. The game may not have live db_str support.");
                            System.Threading.Thread.Sleep(3000);
                        }

                        // Close the patcher on the UI thread
                        this.Invoke((MethodInvoker)delegate {
                            this.Close();
                        });
                    });
                }
                else MessageBox.Show("The process failed to start");
            }
            catch (Exception err)
            {
                MessageBox.Show("An error occured while trying to start everquest: " + err.Message);
            }
        }


        private void btnCheck_Click(object sender, EventArgs e)
        {
            if (isLoading && !isPendingPatch)
            {
                isPendingPatch = true;
                pendingPatchTimer.Enabled = true;
                StatusLibrary.Log("Checking for updates...");
                btnCheck.Text = "Cancel";
                return;
            }

            if (isPatching)
            {
                isPatchCancelled = true;
                cts.Cancel();
            }
            Console.WriteLine("patch button called");
            StartPatch();
        }

        public static async Task<string> DownloadFile(CancellationTokenSource cts, string url, string path)
        {
            path = path.Replace("/", "\\");
            if (path.Contains("\\")) { //Make directory if needed.
                string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\" + path.Substring(0, path.LastIndexOf("\\"));
                Directory.CreateDirectory(dir);
            }
            return await UtilityLibrary.DownloadFile(cts, url, path);
        }

        public static async Task<byte[]> Download(CancellationTokenSource cts, string url)
        {
            return await UtilityLibrary.Download(cts, url);
        }

        private void StartPatch()
        {
            if (isPatching)
            {
                Console.WriteLine("premature patch call");
                return;
            }
            cts = new CancellationTokenSource();
            isPatchCancelled = false;
            txtList.Text = "";
            StatusLibrary.SetPatchState(true);
            isPatching = true;
            Task.Run(async () =>
            {
                try
                {
                    await AsyncPatch();
                } catch (Exception e)
                {
                    StatusLibrary.Log($"Exception during patch: {e.Message}");
                }
                StatusLibrary.SetPatchState(false);
                isPatching = false;
                isPatchCancelled = false;
                cts.Cancel();
            });
        }

        // Static file URLs for client files served via nginx
        // Key = local file path (relative to EQ folder), Value = URL path on the file server
        private static readonly Dictionary<string, string> SpireExports = new Dictionary<string, string>
        {
            { "spells_us.txt", "spells_us.txt" },
            { "Resources\\SkillCaps.txt", "Resources/SkillCaps.txt" },
            { "Resources\\BaseData.txt", "Resources/BaseData.txt" },
            { "dbstr_us.txt", "dbstr_us.txt" },
            { "godsofnorrath.dll", "godsofnorrath.dll" }

        };

        // Base URL for static file server - change this to your server
        private static readonly string SpireBaseUrl = "http://108.181.218.166/patch/rof/";

        // GitHub manifest URL for static file patching
        private static readonly string ManifestUrl = "https://raw.githubusercontent.com/atroche/eqemupatcher/refs/heads/master/manifest.json";

        // Whitelist of static files to patch from manifest (only these will be downloaded)
        private static readonly HashSet<string> ManifestFilesWhitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Resources/GlobalLoad.txt",
            "eqhost.txt"
        };

        // Storyline files to delete during patching
        private static readonly HashSet<string> StorylineFilesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "storyline/storySerpents.txt",
            "storyline/storyRevelation.txt",
            "storyline/storyPlea.txt",
            "storyline/storyMessenger.txt",
            "storyline/storyMagic.txt",
            "storyline/storyLanys.txt",
            "storyline/storyGate.txt",
            "storyline/storyDarkhollow.txt",
            "storyline/storyChosen.txt",
            "storyline/storyAbysmal.txt",
            "storyline/storyWhispers.txt",
            "storyline/storyVisions.txt",
            "storyline/storyUnturned.txt",
            "storyline/storyTroubled.txt",
            "storyline/storyTreachery.txt",
            "storyline/storyStone.txt",
            "storyline/storyShadow.txt",
            "storyline/storySeeking.txt",
            "storyline/storySecrets.txt",
            "storyline/storySearch.txt",
            "storyline/storySea.txt",
            "storyline/storyScholar.txt",
            "storyline/storyRumor.txt",
            "storyline/storyRiches.txt",
            "storyline/storyReunion.txt",
            "storyline/storyPursuit.txt",
            "storyline/storyProphecy.txt",
            "storyline/storyPromise.txt",
            "storyline/storyPath.txt",
            "storyline/storyPassage.txt",
            "storyline/storyPantheon.txt",
            "storyline/storyNest.txt",
            "storyline/storyMountains.txt",
            "storyline/storyMithaniel.txt",
            "storyline/storyMines.txt",
            "storyline/storyMinds.txt",
            "storyline/storyMadman.txt",
            "storyline/storyJourney.txt",
            "storyline/storyGrobb.txt",
            "storyline/storyFires.txt",
            "storyline/storyFelwithe.txt",
            "storyline/storyExile.txt",
            "storyline/storyDawn.txt",
            "storyline/storyDaughter.txt",
            "storyline/storyCurrents.txt",
            "storyline/storyCourse.txt",
            "storyline/storyBuild.txt",
            "storyline/storyAmulet.txt",
            "storyline/storyAgreement.txt",
            "storyline/storyNektulos.txt"
        };

        private async Task AsyncPatch()
        {
            Stopwatch start = Stopwatch.StartNew();
            StatusLibrary.Log($"Patching with patcher version {version}...");
            StatusLibrary.SetProgress(0);

            double totalBytes = 0;
            int totalFilesDownloaded = 0;

            // ============================================
            // PHASE 1: Download Spire export files (dynamic from database)
            // ============================================
            StatusLibrary.Log("Downloading server data files from Spire...");
            int spireFileCount = 0;

            foreach (var spireFile in SpireExports)
            {
                if (isPatchCancelled)
                {
                    StatusLibrary.Log("Patching cancelled.");
                    return;
                }

                string spireFilePath = spireFile.Key;
                string endpoint = spireFile.Value;
                string url = SpireBaseUrl + endpoint;
                string localPath = Path.GetDirectoryName(Application.ExecutablePath) + "\\" + spireFilePath;
                string displayName = Path.GetFileName(spireFilePath);

                try
                {
                    // Create directory if needed (for Resources subfolder)
                    string dir = Path.GetDirectoryName(localPath);
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    StatusLibrary.Log($"Downloading {displayName}...");
                    var data = await Download(cts, url);
                    if (data != null && data.Length > 0)
                    {
                        File.WriteAllBytes(localPath, data);
                        StatusLibrary.Log($"  {displayName} ({generateSize(data.Length)})");
                        spireFileCount++;
                        totalBytes += data.Length;
                        totalFilesDownloaded++;
                    }
                    else
                    {
                        StatusLibrary.Log($"  Warning: {displayName} was empty or failed");
                    }
                }
                catch (Exception ex)
                {
                    StatusLibrary.Log($"  Failed to download {displayName}: {ex.Message}");
                }

                // Progress: 0-30% for Spire files
                StatusLibrary.SetProgress((int)(((spireFileCount) / (double)SpireExports.Count) * 3000));
            }

            // ============================================
            // PHASE 2: Download manifest and patch static files from GitHub
            // ============================================
            StatusLibrary.Log("");
            StatusLibrary.Log("Checking for static file updates from GitHub...");
            
            try
            {
                // Download manifest
                var manifestData = await Download(cts, ManifestUrl);
                if (manifestData == null || manifestData.Length == 0)
                {
                    StatusLibrary.Log("  Warning: Could not download manifest");
                }
                else
                {
                    string manifestJson = System.Text.Encoding.UTF8.GetString(manifestData);
                    var serializer = new JavaScriptSerializer();
                    var manifest = serializer.Deserialize<PatchManifest>(manifestJson);

                    if (manifest?.files == null || manifest.files.Count == 0)
                    {
                        StatusLibrary.Log("  Warning: Manifest contains no files");
                    }
                    else
                    {
                        string basePath = Path.GetDirectoryName(Application.ExecutablePath);
                        string filesUrlPrefix = manifest.filesUrlPrefix;
                        if (!filesUrlPrefix.EndsWith("/"))
                        {
                            filesUrlPrefix += "/";
                        }

                        int manifestFileCount = 0;
                        int manifestFilesChecked = 0;
                        int manifestFilesTotal = manifest.files.Count;
                        List<KeyValuePair<string, string>> filesToDownload = new List<KeyValuePair<string, string>>();

                        // First pass: check which files need updating (only whitelisted files)
                        foreach (var fileEntry in manifest.files)
                        {
                            if (isPatchCancelled)
                            {
                                StatusLibrary.Log("Patching cancelled.");
                                return;
                            }

                            // Skip files not in whitelist
                            if (!ManifestFilesWhitelist.Contains(fileEntry.Key))
                            {
                                manifestFilesChecked++;
                                continue;
                            }

                            string relativePath = fileEntry.Key.Replace("/", "\\");
                            string expectedHash = fileEntry.Value;
                            string localPath = Path.Combine(basePath, relativePath);

                            // Security check: ensure path is within EQ directory
                            if (!UtilityLibrary.IsPathChild(relativePath))
                            {
                                continue;
                            }

                            bool needsDownload = false;

                            if (!File.Exists(localPath))
                            {
                                needsDownload = true;
                            }
                            else
                            {
                                try
                                {
                                    string localHash = XXHash64.ComputeFileHash(localPath);
                                    if (!string.Equals(localHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                                    {
                                        needsDownload = true;
                                    }
                                }
                                catch
                                {
                                    needsDownload = true;
                                }
                            }

                            if (needsDownload)
                            {
                                filesToDownload.Add(fileEntry);
                            }

                            manifestFilesChecked++;
                            // Progress: 30-40% for checking files
                            StatusLibrary.SetProgress(3000 + (int)((manifestFilesChecked / (double)manifestFilesTotal) * 1000));
                        }

                        if (filesToDownload.Count == 0)
                        {
                            StatusLibrary.Log("  All static files are up to date.");
                        }
                        else
                        {
                            StatusLibrary.Log($"  {filesToDownload.Count} file(s) need updating...");

                            int downloadedCount = 0;
                            foreach (var fileEntry in filesToDownload)
                            {
                                if (isPatchCancelled)
                                {
                                    StatusLibrary.Log("Patching cancelled.");
                                    return;
                                }

                                string relativePath = fileEntry.Key;
                                string downloadUrl = filesUrlPrefix + relativePath.Replace("\\", "/");
                                string localRelativePath = relativePath.Replace("/", "\\");
                                string displayName = Path.GetFileName(relativePath);

                                try
                                {
                                    StatusLibrary.Log($"  Downloading {displayName}...");
                                    string result = await DownloadFile(cts, downloadUrl, localRelativePath);
                                    if (string.IsNullOrEmpty(result))
                                    {
                                        // Get file size for reporting
                                        string localPath = Path.Combine(basePath, localRelativePath);
                                        if (File.Exists(localPath))
                                        {
                                            var fileInfo = new FileInfo(localPath);
                                            totalBytes += fileInfo.Length;
                                        }
                                        manifestFileCount++;
                                        totalFilesDownloaded++;
                                        StatusLibrary.Log($"    {displayName} updated");
                                    }
                                    else
                                    {
                                        StatusLibrary.Log($"    Failed: {result}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    StatusLibrary.Log($"    Failed to download {displayName}: {ex.Message}");
                                }

                                downloadedCount++;
                                // Progress: 40-100% for downloading files
                                StatusLibrary.SetProgress(4000 + (int)((downloadedCount / (double)filesToDownload.Count) * 6000));
                            }

                            StatusLibrary.Log($"  Updated {manifestFileCount} static file(s).");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusLibrary.Log($"  Error checking manifest: {ex.Message}");
            }

            // ============================================
            // PHASE 3: Delete storyline files
            // ============================================
            StatusLibrary.Log("");
            StatusLibrary.Log("Cleaning up storyline files...");
            string eqPath = Path.GetDirectoryName(Application.ExecutablePath);
            int deletedCount = 0;

            foreach (var storyFile in StorylineFilesToDelete)
            {
                if (isPatchCancelled)
                {
                    StatusLibrary.Log("Patching cancelled.");
                    return;
                }

                string relativePath = storyFile.Replace("/", "\\");

                // Security check: ensure path is within EQ directory
                if (!UtilityLibrary.IsPathChild(relativePath))
                {
                    continue;
                }

                string localPath = Path.Combine(eqPath, relativePath);
                try
                {
                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                        StatusLibrary.Log($"  Deleted {storyFile}");
                        deletedCount++;
                    }
                }
                catch (Exception ex)
                {
                    StatusLibrary.Log($"  Failed to delete {storyFile}: {ex.Message}");
                }
            }

            if (deletedCount == 0)
            {
                StatusLibrary.Log("  No storyline files to clean up.");
            }
            else
            {
                StatusLibrary.Log($"  Deleted {deletedCount} storyline file(s).");
            }

            StatusLibrary.SetProgress(10000);
            string elapsed = start.Elapsed.ToString("ss\\.ff");
            StatusLibrary.Log("");
            StatusLibrary.Log($"Complete! Downloaded {totalFilesDownloaded} files ({generateSize(totalBytes)}) in {elapsed} seconds.");
            StatusLibrary.Log("Press Play to begin.");
            return;
        }

        private void chkAutoPlay_CheckedChanged(object sender, EventArgs e)
        {
            if (isLoading) return;
            isAutoPlay = chkAutoPlay.Checked;
            IniLibrary.instance.AutoPlay = (isAutoPlay) ? "true" : "false";
            if (isAutoPlay) StatusLibrary.Log("To disable autoplay: edit eqemupatcher.yml or wait until next patch.");

            IniLibrary.Save();
        }

        private void chkAutoPatch_CheckedChanged(object sender, EventArgs e)
        {
            if (isLoading) return;
            isAutoPatch = chkAutoPatch.Checked;
            IniLibrary.instance.AutoPatch = (isAutoPatch) ? "true" : "false";
            IniLibrary.Save();
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
        }

        private string generateSize(double size) {
            if (size < 1024) {
                return $"{Math.Round(size, 2)} bytes";
            }

            size /= 1024;
            if (size < 1024)
            {
                return $"{Math.Round(size, 2)} KB";
            }

            size /= 1024;
            if (size < 1024)
            {
                return $"{Math.Round(size, 2)} MB";
            }

            size /= 1024;
            if (size < 1024)
            {
                return $"{Math.Round(size, 2)} GB";
            }

            return $"{Math.Round(size, 2)} TB";
        }

        private void pendingPatchTimer_Tick(object sender, EventArgs e)
        {
            if (isLoading) return;
            pendingPatchTimer.Enabled = false;
            isPendingPatch = false;
            btnCheck_Click(sender, e);
        }
    }

    public class FileList
    {
        public string version { get; set; }

        public List<FileEntry> deletes { get; set; }
        public string downloadprefix { get; set; }
        public List<FileEntry> downloads { get; set; }
        public List<FileEntry> unpacks { get; set; }

    }

    public class FileEntry
    {
        public string name { get; set;  }
        public string md5 { get; set; }
        public string date { get; set; }
        public string zip { get; set; }
        public int size { get; set; }
    }

    /// <summary>
    /// Manifest structure matching manifest.json from GitHub
    /// </summary>
    public class PatchManifest
    {
        public string shortName { get; set; }
        public string longName { get; set; }
        public string customFilesUrl { get; set; }
        public string filesUrlPrefix { get; set; }
        public string version { get; set; }
        public string website { get; set; }
        public string description { get; set; }
        public List<string> hosts { get; set; }
        public List<string> required { get; set; }
        public Dictionary<string, string> files { get; set; }
    }
}


