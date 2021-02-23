using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.ServiceProcess;
using System.Runtime.InteropServices;
using System.IO;
using System.Globalization;
using System.Net;

namespace ValheimServerWarden
{
    public class SteamCMD
    {
        private Process process;
        public event DataReceivedEventHandler OutputDataReceived
        {
            add
            {
                this.process.OutputDataReceived += value;
            }
            remove
            {
                this.process.OutputDataReceived -= value;
            }
        }
        public event DataReceivedEventHandler ErrorDataReceived
        {
            add
            {
                this.process.ErrorDataReceived += value;
            }
            remove
            {
                this.process.ErrorDataReceived -= value;
            }
        }
        private string LogName
        {
            get
            {
                return "steamcmd.log";
            }
        }
        public string Username { get; set; }
        public bool Log { get; set; }
        public SteamCMD(string executablePath) : this(executablePath, false) { }
        public SteamCMD(string executablePath, bool log)
        {
            Log = log;
            Username = "anonymous";
            this.process = new Process();
            this.process.StartInfo.FileName = executablePath;
            this.process.StartInfo.UseShellExecute = false;
            this.process.StartInfo.CreateNoWindow = true;
            this.process.StartInfo.RedirectStandardOutput = true;
            this.process.StartInfo.RedirectStandardError = true;
            this.process.StartInfo.RedirectStandardInput = true;
            this.process.EnableRaisingEvents = true;
            this.process.OutputDataReceived += Process_OutputDataReceived;
            this.process.Exited += Process_Exited;
        }
        public SteamCMD() : this(@"steamcmd\steamcmd.exe", false) {
            if (!File.Exists(process.StartInfo.FileName))
            {
                using (WebClient webClient = new WebClient())
                {
                    webClient.DownloadFile("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", "steamcmd.zip");
                }
                if (!Directory.Exists(new FileInfo(process.StartInfo.FileName).Directory.FullName))
                {
                    Directory.CreateDirectory(new FileInfo(process.StartInfo.FileName).Directory.FullName);
                }
                System.IO.Compression.ZipFile.ExtractToDirectory("steamcmd.zip", "steamcmd/");
                File.Delete("steamcmd.zip");
            }
        }
        public SteamCMD(bool log) : this(@"steamcmd\steamcmd.exe", log) { }
        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            string msg = e.Data;
            if (msg == null) return;
            //Debug.WriteLine(msg);
            if (this.Log)
            {
                try
                {
                    StreamWriter writer = System.IO.File.AppendText(LogName);
                    writer.WriteLine(msg);
                    writer.Close();
                }
                catch (Exception ex)
                {
                    //not being able to clear the log is not a major problem
                }
            }
            //Monitor for incorrect password attempts
            Regex rx = new Regex(@"Peer (\d+) has wrong password", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Match match = rx.Match(msg);
            if (match.Success)
            {
                //match.Groups[1].ToString()
                return;
            }
        }

        public void Login()
        {
            new Thread(() =>
            {
                if (this.Log)
                {
                    System.IO.File.WriteAllText(LogName, "");
                }
                Thread.CurrentThread.IsBackground = true;
                this.process.Refresh();
                this.process.Start();
                this.process.BeginOutputReadLine();
                this.process.StandardInput.WriteLine($"login {Username}");
                this.process.WaitForExit();
            }).Start();
        }

        public void Execute(string command)
        {
            this.process.StandardInput.WriteLine(command);
        }

        public void Quit()
        {
            //add code to exit
            Execute("quit");
        }
        public void Install(string installpath, string appid)
        {
            Execute($"force_install_dir {installpath}");
            Execute($"app_update {appid}");
        }
        public string Install(string appid)
        {
            string path = new FileInfo(process.StartInfo.FileName).DirectoryName + "\\" + appid + "\\";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            Install(path, appid);
            return path;
        }
        private void Process_Exited(object sender, EventArgs e)
        {
            this.process.CancelOutputRead();
        }
    }
}
