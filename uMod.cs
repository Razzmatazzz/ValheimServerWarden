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
        public static bool Installed
        {
            get
            {
                return File.Exists(ExecutablePath);
            }
        }
        public static string ExecutablePath
        {
            get
            {
                return $@"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\.dotnet\tools\umod.exe";
            }
        }
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
            _serverPath = serverPath;
            _gameName = gameName;
            Prerelease = true;
        }
        public void Install()
        {
            new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.IsBackground = true;
                    var process = new Process();
                    process.StartInfo.FileName = ExecutablePath;
                    process.StartInfo.Arguments = $"install {GameName} --force --strict --no-input";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.WorkingDirectory = ServerPath;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.RedirectStandardInput = true;
                    process.EnableRaisingEvents = true;
                    process.OutputDataReceived += Process_OutputDataReceived;
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
                    Thread.CurrentThread.IsBackground = true;
                    var process = new Process();
                    process.StartInfo.FileName = ExecutablePath;
                    process.StartInfo.Arguments = $"update {updateComponents} --patch-available --strict --validate --prerelease --no-input";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.WorkingDirectory = ServerPath;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.RedirectStandardInput = true;
                    process.EnableRaisingEvents = true;
                    process.OutputDataReceived += Process_OutputDataReceived;
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
                    Thread.CurrentThread.IsBackground = true;
                    var process = new Process();
                    process.StartInfo.FileName = ExecutablePath;
                    process.StartInfo.Arguments = $"require {pluginName} --strict --no-input";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.WorkingDirectory = ServerPath;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.RedirectStandardInput = true;
                    process.EnableRaisingEvents = true;
                    process.OutputDataReceived += Process_OutputDataReceived;
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
                    Thread.CurrentThread.IsBackground = true;
                    var process = new Process();
                    process.StartInfo.FileName = ExecutablePath;
                    process.StartInfo.Arguments = $"remove {pluginName} --strict --no-input";
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.WorkingDirectory = ServerPath;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.RedirectStandardInput = true;
                    process.EnableRaisingEvents = true;
                    process.OutputDataReceived += Process_OutputDataReceived;
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
    }
}
