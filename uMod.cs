using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RazzTools
{
    class uMod
    {
        public static bool AgentInstalled
        {
            get
            {
                return File.Exists(AgentPath);
            }
        }
        public static string AgentPath
        {
            get
            {
                if (testing)
                {
                    return @"C:\Windows\System32\ping.exe";
                }
                return $@"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\.dotnet\tools\umod.exe";
            }
        }
        private static bool testing = false;
        private string _serverPath;
        private string _gameName;
        public event EventHandler<LoggedMessageEventArgs> LoggedMessage;
        public event EventHandler<ProcessEndedEventArgs> InstallEnded;
        public event EventHandler<ProcessEndedEventArgs> UpdateEnded;
        public event EventHandler<ProcessEndedEventArgs> PluginInstallEnded;
        public event EventHandler<ProcessEndedEventArgs> RemoveEnded;
        public string ServerPath {  get { return _serverPath; } }
        public string GameName { get { return _gameName; } }
        public bool Prerelease { get; set; }
        public uMod(string serverPath, string gameName)
        {
            if (serverPath.EndsWith(".exe"))
            {
                _serverPath = new FileInfo(serverPath).Directory.FullName;
            }
            else
            {
                _serverPath = serverPath;
            }
            _gameName = gameName;
            Prerelease = true;
        }
        private Process GetProcess()
        {
            return GetProcess(null);
        }
        private Process GetProcess(string arguments)
        {
            var process = new Process();
            process.StartInfo.FileName = AgentPath;
            if (arguments != null && arguments != "")
            {
                process.StartInfo.Arguments = arguments;
            }
            if (testing)
            {
                process.StartInfo.Arguments = "google.com";
            }
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardInput = true;
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += Process_OutputDataReceived;
            return process;
        }
        public void Install() { Install(null); }
        public void Install(string filter)
        {
            new Thread(() =>
            {
                try
                {
                    if (filter != null)
                    {
                        filter = $" --filter={filter}";
                    } else
                    {
                        filter = "";
                    }
                    var process = GetProcess($"install {GameName}{filter} --strict --no-input --working-directory=\"{ServerPath}\" -P");
                    process.Exited += InstallProcess_Exited;
                    process.Start();
                    process.BeginOutputReadLine();
                    process.WaitForExit();
                }
                catch (Exception ex)
                {
                    LogMessage($"Error installing server: {ex.Message}", LogEntryType.Error);
                }
            }).Start();
        }
        private void InstallProcess_Exited(object sender, EventArgs e)
        {
            var process = (Process)sender;
            OnInstallEnded(process.ExitCode);
            process.Dispose();
        }

        public void Update()
        {
            Update("game core apps extensions");
        }
        public void Update(string updateComponents)
        {
            new Thread(() =>
            {
                try
                {
                    var process = GetProcess($"update {updateComponents} --patch-available --strict --validate --prerelease --no-input --working-directory=\"{ServerPath}\"");
                    process.Exited += UpdateProcess_Exited;
                    process.Start();
                    process.BeginOutputReadLine();
                    process.WaitForExit();
                }
                catch (Exception ex)
                {
                    LogMessage($"Error updating server: {ex.Message}", LogEntryType.Error);
                }
            }).Start();
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Debug.WriteLine(e.Data);
            if (e.Data != null)
            {
                //LogMessage(e.Data);
            }
        }

        private void UpdateProcess_Exited(object sender, EventArgs e)
        {
            var process = (Process)sender;
            OnUpdateEnded(process.ExitCode);
            process.Dispose();
        }
        public void InstallPlugin(string pluginName)
        {
            new Thread(() =>
            {
                try
                {
                    var process = GetProcess($"require {pluginName} --strict --no-input --dir=\"{ServerPath}\"");
                    process.Exited += InstallPluginProcess_Exited;
                    process.Start();
                    process.BeginOutputReadLine();
                    process.WaitForExit();
                }
                catch (Exception ex)
                {
                    LogMessage($"Error installing plugin: {ex.Message}", LogEntryType.Error);
                }
            }).Start();
        }
        private void InstallPluginProcess_Exited(object sender, EventArgs e)
        {
            var process = (Process)sender;
            OnPluginInstallEnded(process.ExitCode);
            process.Dispose();
        }
        public void Remove(string pluginName)
        {
            new Thread(() =>
            {
                try
                {
                    var process = GetProcess($"remove {pluginName} --strict --no-input --dir=\"{ServerPath}\"");
                    process.Exited += RemovePluginProcess_Exited;
                    process.Start();
                    process.BeginOutputReadLine();
                    process.WaitForExit();
                }
                catch (Exception ex)
                {
                    LogMessage($"Error installing plugin: {ex.Message}", LogEntryType.Error);
                }
            }).Start();
        }

        private void RemovePluginProcess_Exited(object sender, EventArgs e)
        {
            var process = (Process)sender;
            OnRemoveEnded(process.ExitCode);
            process.Dispose();
        }

        private void LogMessage(string message)
        {
            LogMessage(message, LogEntryType.Normal);
        }
        private void LogMessage(string message, LogEntryType entrytype)
        {
            EventHandler<LoggedMessageEventArgs> handler = LoggedMessage;
            if (null != handler) handler(this, new LoggedMessageEventArgs(message, entrytype));
        }
        private void OnInstallEnded(int exitcode)
        {
            if (exitcode == 1)
            {
                LogMessage("Unable to update game, uMod core, or uMod apps.", LogEntryType.Warning);
            }
            else if (exitcode == 2)
            {
                LogMessage("No updated needed.");
            }
            EventHandler<ProcessEndedEventArgs> handler = InstallEnded;
            if (null != handler) handler(this, new ProcessEndedEventArgs(exitcode));
        }
        private void OnUpdateEnded(int exitcode)
        {
            EventHandler<ProcessEndedEventArgs> handler = UpdateEnded;
            if (null != handler) handler(this, new ProcessEndedEventArgs(exitcode));
        }
        private void OnPluginInstallEnded(int exitcode)
        {
            EventHandler<ProcessEndedEventArgs> handler = PluginInstallEnded;
            if (null != handler) handler(this, new ProcessEndedEventArgs(exitcode));
        }
        private void OnRemoveEnded(int exitcode)
        {
            EventHandler<ProcessEndedEventArgs> handler = RemoveEnded;
            if (null != handler) handler(this, new ProcessEndedEventArgs(exitcode));
        }

        public class ProcessEndedEventArgs : EventArgs
        {
            private int _exitCode;
            public int ExitCode { get { return _exitCode; } }
            public ProcessEndedEventArgs(int exitcode)
            {
                _exitCode = exitcode;
            }
        }


        //classes for parsing https://assets.umod.org/uMod.Manifest.json
        public class Rootobject
        {
            public Package[] Packages { get; set; }
            public Game[] Games { get; set; }
        }

        public class Package
        {
            public string Title { get; set; }
            public string Name { get; set; }
            public string RootFolder { get; set; }
            public string FileName { get; set; }
            public Resource[] Resources { get; set; }
        }

        public class Resource
        {
            public int Type { get; set; }
            public string Version { get; set; }
            public Artifact[] Artifacts { get; set; }
        }

        public class Artifact
        {
            public string Checksum { get; set; }
            public string Url { get; set; }
            public string Architecture { get; set; }
            public string Platform { get; set; }
        }

        public class Game
        {
            public string Name { get; set; }
            public string Aliases { get; set; }
            public string ServiceUrl { get; set; }
            public string PackageName { get; set; }
            public string Sdk { get; set; }
            public string PreprocessorSymbol { get; set; }
            public Scandata ScanData { get; set; }
            public Steam Steam { get; set; }
            public Launcher Launcher { get; set; }
        }

        public class Scandata
        {
            public Keyfile[] KeyFiles { get; set; }
        }

        public class Keyfile
        {
            public int Type { get; set; }
            public string Path { get; set; }
        }

        public class Steam
        {
            public int AppId { get; set; }
            public string DefaultBranch { get; set; }
            public string Login { get; set; }
            public Clienttarget ClientTarget { get; set; }
            public Branch[] Branches { get; set; }
        }

        public class Clienttarget
        {
            public string x32 { get; set; }
            public string x64 { get; set; }
        }

        public class Branch
        {
            public string Name { get; set; }
            public int BuildId { get; set; }
            public Lastupdate LastUpdate { get; set; }
            public int Password { get; set; }
        }

        public class Lastupdate
        {
            public string date { get; set; }
            public int timezone_type { get; set; }
            public string timezone { get; set; }
        }

        public class Launcher
        {
            public string Template { get; set; }
            public string Arguments { get; set; }
        }
    }
}
