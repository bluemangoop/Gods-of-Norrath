using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Net.Http;
using System.Threading;
using System.Reflection;
using System.Diagnostics;
using System.Web.Script.Serialization;
using YamlDotNet.Core.Tokens;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace EQEmu_Patcher
{
    /* General Utility Methods */
    class UtilityLibrary
    {
        //Download a file to current directory
        public static async Task<string> DownloadFile(CancellationTokenSource cts, string url, string outFile)
        {

            try
            {
                var client = new HttpClient();
                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    var outPath = outFile.Replace("/", "\\");
                    if (outFile.Contains("\\")) { //Make directory if needed.
                        string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\" + outFile.Substring(0, outFile.LastIndexOf("\\"));
                        Directory.CreateDirectory(dir);
                    }
                    outPath = System.IO.Path.GetDirectoryName(Application.ExecutablePath) + "\\" + outFile;

                    using (var w = File.Create(outPath)) {
                        await stream.CopyToAsync(w, 81920, cts.Token);
                    }
                }
            } catch(ArgumentNullException e)
            {
                return "ArgumentNullExpception: " + e.Message;
            } catch(HttpRequestException e)
            {
                return "HttpRequestException: " + e.Message;
            } catch (Exception e)
            {
                return "Exception: " + e.Message;
            }
            return "";
        }

        // Download will grab a remote URL's file and return the data as a byte array
        public static async Task<byte[]> Download(CancellationTokenSource cts, string url)
        {
            var client = new HttpClient();
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();
            using (var stream = await response.Content.ReadAsStreamAsync())
            {
                using (var w = new MemoryStream())
                {
                    await stream.CopyToAsync(w, 81920, cts.Token);
                    return w.ToArray();
                }
            }
        }

        public static string GetMD5(string filename)
        {
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(filename))
                {
                    var hash = md5.ComputeHash(stream);

                    StringBuilder sb = new StringBuilder();

                    for (int i = 0; i < hash.Length; i++)
                    {
                        sb.Append(hash[i].ToString("X2"));
                    }

                    return sb.ToString();
                }
            }
        }

        /// <summary>
        /// Name of the DLL to place alongside eqgame.exe (dinput8.dll proxy)
        /// No injection needed - Windows loads it automatically because
        /// eqgame.exe imports dinput8.dll and the app directory is checked first.
        /// </summary>
        private static readonly string DllName = "dinput8.dll";

        /// <summary>
        /// Start EverQuest with the dinput8.dll proxy for live db_str lookups.
        /// No injection needed - the DLL is loaded automatically by Windows
        /// when eqgame.exe starts because it's placed in the app directory.
        /// </summary>
        public static System.Diagnostics.Process StartEverquest()
        {
            string eqPath = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            string dllPath = eqPath + "\\" + DllName;

            // Check if the DLL exists
            if (!File.Exists(dllPath))
            {
                StatusLibrary.Log($"Warning: {DllName} not found at {dllPath}. Starting without live db_str proxy support.");
            }
            else
            {
                StatusLibrary.Log($"Starting eqgame.exe with {DllName} proxy (auto-loaded by Windows)...");
            }

            // Start eqgame.exe normally - Windows will load dinput8.dll from the app directory
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = eqPath + "\\eqgame.exe",
                Arguments = "patchme",
                WorkingDirectory = eqPath
            };

            return System.Diagnostics.Process.Start(startInfo);
        }


        //Pass the working directory (or later, you can pass another directory) and it returns a hash if the file is found
        public static string GetEverquestExecutableHash(string path)
        {
            var di = new System.IO.DirectoryInfo(path);
            var files = di.GetFiles("eqgame.exe");
            if (files == null || files.Length == 0)
            {
                return "";
            }
            return UtilityLibrary.GetMD5(files[0].FullName);
        }

        // Returns true only if the path is a relative and does not contain ..
        public static bool IsPathChild(string path)
        {
            // get the absolute path
            var absPath = Path.GetFullPath(path);
            var basePath = Path.GetDirectoryName(Application.ExecutablePath); 
            // check if absPath contains basePath
            if (!absPath.Contains(basePath))
            {
                return false;
            }
            if (path.Contains("..\\"))
            {
                return false;
            }
            return true;
        }

        #region Self-Update

        // GitHub repository for update checks
        private static readonly string GitHubApiUrl = "https://api.github.com/repos/bluemangoop/eqemupatcher-test/releases/latest";

        /// <summary>
        /// Result of checking for patcher updates
        /// </summary>
        public class UpdateCheckResult
        {
            public bool UpdateAvailable { get; set; }
            public string CurrentVersion { get; set; }
            public string LatestVersion { get; set; }
            public string DownloadUrl { get; set; }
            public string Error { get; set; }
        }

        /// <summary>
        /// Check GitHub releases for a newer version of the patcher
        /// </summary>
        public static async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationTokenSource cts)
        {
            var result = new UpdateCheckResult
            {
                CurrentVersion = Assembly.GetEntryAssembly().GetName().Version.ToString(),
                UpdateAvailable = false
            };

            try
            {
                using (var client = new HttpClient())
                {
                    // GitHub API requires a User-Agent header
                    client.DefaultRequestHeaders.Add("User-Agent", "EQEmuPatcher");
                    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

                    var response = await client.GetAsync(GitHubApiUrl, cts.Token);
                    if (!response.IsSuccessStatusCode)
                    {
                        result.Error = $"GitHub API returned {response.StatusCode}";
                        return result;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var serializer = new JavaScriptSerializer();
                    var release = serializer.Deserialize<Dictionary<string, object>>(json);

                    if (release == null || !release.ContainsKey("tag_name"))
                    {
                        result.Error = "Invalid release data from GitHub";
                        return result;
                    }

                    result.LatestVersion = release["tag_name"].ToString();

                    // Find the .exe asset
                    if (release.ContainsKey("assets"))
                    {
                        var assets = release["assets"] as System.Collections.ArrayList;
                        if (assets != null)
                        {
                            foreach (Dictionary<string, object> asset in assets)
                            {
                                if (asset.ContainsKey("name") && asset["name"].ToString().EndsWith(".exe"))
                                {
                                    result.DownloadUrl = asset["browser_download_url"].ToString();
                                    break;
                                }
                            }
                        }
                    }

                    // Compare versions
                    if (IsNewerVersion(result.CurrentVersion, result.LatestVersion))
                    {
                        result.UpdateAvailable = true;
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Compare version strings to determine if remote is newer
        /// </summary>
        private static bool IsNewerVersion(string current, string latest)
        {
            try
            {
                // Parse versions, handling formats like "1.0.6.123" or "v1.0.6.123"
                current = current.TrimStart('v', 'V');
                latest = latest.TrimStart('v', 'V');

                var currentParts = current.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();
                var latestParts = latest.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();

                // Pad arrays to same length
                int maxLen = Math.Max(currentParts.Length, latestParts.Length);
                Array.Resize(ref currentParts, maxLen);
                Array.Resize(ref latestParts, maxLen);

                for (int i = 0; i < maxLen; i++)
                {
                    if (latestParts[i] > currentParts[i]) return true;
                    if (latestParts[i] < currentParts[i]) return false;
                }

                return false; // Versions are equal
            }
            catch
            {
                return false; // If parsing fails, assume no update
            }
        }

        /// <summary>
        /// Download and apply the patcher update, then restart
        /// </summary>
        /// <returns>True if update was applied and app should exit, false otherwise</returns>
        public static async Task<bool> ApplyUpdateAsync(CancellationTokenSource cts, string downloadUrl, Action<string> logCallback)
        {
            string currentExe = Application.ExecutablePath;
            string newExe = currentExe + ".new";
            string oldExe = currentExe + ".old";

            try
            {
                logCallback?.Invoke("Downloading patcher update...");

                // Download new version
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "EQEmuPatcher");
                    
                    var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    response.EnsureSuccessStatusCode();

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = File.Create(newExe))
                    {
                        await stream.CopyToAsync(fileStream, 81920, cts.Token);
                    }
                }

                logCallback?.Invoke("Download complete. Applying update...");

                // Delete old backup if it exists
                if (File.Exists(oldExe))
                {
                    File.Delete(oldExe);
                }

                // Rename current exe to .old (Windows allows renaming running executables)
                File.Move(currentExe, oldExe);

                // Rename new exe to current name
                File.Move(newExe, currentExe);

                logCallback?.Invoke("Update applied! Restarting patcher...");

                // Start new process with --updated flag
                var startInfo = new ProcessStartInfo
                {
                    FileName = currentExe,
                    Arguments = "--updated",
                    WorkingDirectory = Path.GetDirectoryName(currentExe),
                    UseShellExecute = true
                };
                Process.Start(startInfo);

                return true; // Signal that app should exit
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Update failed: {ex.Message}");

                // Try to clean up
                try
                {
                    if (File.Exists(newExe))
                    {
                        File.Delete(newExe);
                    }
                    // If we renamed the exe but failed after, try to restore
                    if (!File.Exists(currentExe) && File.Exists(oldExe))
                    {
                        File.Move(oldExe, currentExe);
                    }
                }
                catch { }

                return false;
            }
        }

        #endregion
    }
}
