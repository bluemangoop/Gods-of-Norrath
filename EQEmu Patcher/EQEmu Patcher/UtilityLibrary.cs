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

        // DLL Injection imports
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out uint lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectoryA(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(IntPtr hThread);

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        // Process access flags
        private const uint PROCESS_CREATE_THREAD = 0x0002;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_SUSPEND_RESUME = 0x0800;

        // Memory allocation flags
        private const uint MEM_COMMIT = 0x00001000;
        private const uint MEM_RESERVE = 0x00002000;
        private const uint PAGE_READWRITE = 0x04;

        // Creation flags
        private const uint CREATE_SUSPENDED = 0x00000004;

        // Wait constants
        private const uint INFINITE = 0xFFFFFFFF;

        /// <summary>
        /// Name of the DLL to inject into eqgame.exe
        /// </summary>
        private static readonly string DllToInject = "db_str_proxy.dll";

        /// <summary>
        /// Start EverQuest with DLL injection for live db_str lookups.
        /// Launches eqgame.exe normally, waits for it to initialize,
        /// then injects the proxy DLL with retry logic.
        /// </summary>
        public static System.Diagnostics.Process StartEverquest()
        {
            string eqPath = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            string dllPath = eqPath + "\\" + DllToInject;

            // Check if the DLL exists
            if (!File.Exists(dllPath))
            {
                StatusLibrary.Log($"Warning: {DllToInject} not found at {dllPath}. Starting without injection.");
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = eqPath + "\\eqgame.exe",
                    Arguments = "patchme",
                    WorkingDirectory = eqPath
                };
                return System.Diagnostics.Process.Start(startInfo);
            }

            StatusLibrary.Log($"Starting eqgame.exe normally, then injecting {DllToInject}...");

            // Start eqgame.exe normally first
            var eqStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = eqPath + "\\eqgame.exe",
                Arguments = "patchme",
                WorkingDirectory = eqPath,
                UseShellExecute = false
            };

            var process = System.Diagnostics.Process.Start(eqStartInfo);
            if (process == null)
            {
                StatusLibrary.Log("Failed to start eqgame.exe");
                return null;
            }

            // Wait a moment for the process to initialize before injecting
            System.Threading.Thread.Sleep(2000);

            // Inject the DLL with retry logic (up to 3 attempts)
            bool injected = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    StatusLibrary.Log($"Injection attempt {attempt}/3...");
                    InjectDll(process, dllPath);
                    StatusLibrary.Log($"Successfully injected {DllToInject}");
                    injected = true;
                    break;
                }
                catch (Exception ex)
                {
                    StatusLibrary.Log($"Injection attempt {attempt} failed: {ex.Message}");
                    if (attempt < 3)
                    {
                        int delay = attempt * 2000; // 2s, 4s
                        StatusLibrary.Log($"Retrying in {delay / 1000} seconds...");
                        System.Threading.Thread.Sleep(delay);
                    }
                }
            }

            if (!injected)
            {
                StatusLibrary.Log("All injection attempts failed. Game will start without live db_str proxy support.");
            }

            return process;
        }

        /// <summary>
        /// Inject a DLL into a running process using the classic CreateRemoteThread technique.
        /// Uses SetDllDirectoryA first (like MacroQuest does) to ensure the DLL can find its dependencies.
        /// </summary>
        private static void InjectDll(System.Diagnostics.Process process, string dllPath)
        {
            IntPtr hProcess = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                false, process.Id);

            if (hProcess == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception($"OpenProcess failed (PID: {process.Id}, Error: {error}). Try running the patcher as Administrator.");
            }

            try
            {
                // Get the directory containing the DLL (for SetDllDirectoryA)
                string dllDir = System.IO.Path.GetDirectoryName(dllPath);

                // Allocate memory in the remote process (1024 bytes is enough for both paths)
                IntPtr remoteMemory = VirtualAllocEx(hProcess, IntPtr.Zero, 1024, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

                if (remoteMemory == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"VirtualAllocEx failed (Error: {error})");
                }

                // Step 1: Call SetDllDirectoryA in the remote process to set the DLL search path
                // This ensures the DLL can find its dependencies (like MacroQuest does)
                IntPtr kernel32Base = GetModuleHandle("kernel32.dll");
                if (kernel32Base == IntPtr.Zero)
                {
                    throw new Exception("GetModuleHandle(kernel32.dll) failed");
                }

                IntPtr setDllDirectoryAddr = GetProcAddress(kernel32Base, "SetDllDirectoryA");
                if (setDllDirectoryAddr == IntPtr.Zero)
                {
                    throw new Exception("GetProcAddress(SetDllDirectoryA) failed");
                }

                // Write the DLL directory path to remote memory
                byte[] dllDirBytes = Encoding.ASCII.GetBytes(dllDir + "\0");
                uint bytesWritten;
                bool success = WriteProcessMemory(hProcess, remoteMemory, dllDirBytes, (uint)dllDirBytes.Length, out bytesWritten);

                if (!success || bytesWritten != dllDirBytes.Length)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"WriteProcessMemory (for SetDllDirectoryA) failed (Error: {error})");
                }

                // Create remote thread to call SetDllDirectoryA(dllDir)
                IntPtr remoteThread = CreateRemoteThread(
                    hProcess,
                    IntPtr.Zero,
                    0,
                    setDllDirectoryAddr,
                    remoteMemory,
                    0,
                    IntPtr.Zero);

                if (remoteThread == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"CreateRemoteThread (SetDllDirectoryA) failed (Error: {error})");
                }

                // Wait for SetDllDirectoryA to complete
                uint waitResult = WaitForSingleObject(remoteThread, 10000);
                if (waitResult == 0xFFFFFFFF) // WAIT_FAILED
                {
                    int error = Marshal.GetLastWin32Error();
                    CloseHandle(remoteThread);
                    throw new Exception($"WaitForSingleObject (SetDllDirectoryA) failed (Error: {error})");
                }
                CloseHandle(remoteThread);

                // Step 2: Write the DLL path to the allocated memory (reuse the same buffer)
                byte[] dllPathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
                success = WriteProcessMemory(hProcess, remoteMemory, dllPathBytes, (uint)dllPathBytes.Length, out bytesWritten);

                if (!success || bytesWritten != dllPathBytes.Length)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"WriteProcessMemory (for LoadLibraryW) failed (Error: {error})");
                }

                // Step 3: Call LoadLibraryW in the remote process
                IntPtr loadLibraryAddr = GetProcAddress(kernel32Base, "LoadLibraryW");
                if (loadLibraryAddr == IntPtr.Zero)
                {
                    throw new Exception("GetProcAddress(LoadLibraryW) failed");
                }

                remoteThread = CreateRemoteThread(
                    hProcess,
                    IntPtr.Zero,
                    0,
                    loadLibraryAddr,
                    remoteMemory,
                    0,
                    IntPtr.Zero);

                if (remoteThread == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Exception($"CreateRemoteThread (LoadLibraryW) failed (Error: {error})");
                }

                // Wait for LoadLibraryW to complete
                waitResult = WaitForSingleObject(remoteThread, 30000); // 30 second timeout
                if (waitResult == 0xFFFFFFFF) // WAIT_FAILED
                {
                    int error = Marshal.GetLastWin32Error();
                    CloseHandle(remoteThread);
                    throw new Exception($"WaitForSingleObject (LoadLibraryW) failed (Error: {error})");
                }
                CloseHandle(remoteThread);

                // Free the allocated memory
                VirtualFreeEx(hProcess, remoteMemory, 0, MEM_RELEASE);
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        // Memory free flags
        private const uint MEM_RELEASE = 0x00008000;


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
