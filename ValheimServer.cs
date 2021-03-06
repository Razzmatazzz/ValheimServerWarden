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
using RazzTools;

namespace ValheimServerWarden
{
    public class ValheimServer : IEditableObject, IComparable, IDisposable
    {
        public enum ServerStatus
        {
            Stopped,
            Running,
            Starting,
            Stopping,
            Updating
        }
        public enum ServerInstallMethod
        {
            Manual,
            Steam,
            SteamCMD
        }
        public static List<ValheimServer> Servers { get; } = new List<ValheimServer>();
        public static string DefaultSaveDir { get { return $@"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\AppData\LocalLow\IronGate\Valheim"; } }
        public static string ExecutableName { get { return "valheim_server.exe"; } }
        public static Dictionary<string, string> DiscordWebhookDefaultMessages { get; } = new Dictionary<string, string> {
            {"OnStarted", "Server {Server.Name} has started." },
            {"OnStartFailed", "Server {Server.Name} failed to start." },
            {"OnServerExited", "Server {Server.Name} has stopped." },
            {"OnFailedPassword", "User with SteamID {Player.SteamID} tried to join with an invalid password." },
            {"OnPlayerConnected", "{Player.Name} has entered the fray!" },
            {"OnPlayerDisconnected", "{Player.Name} has departed." },
            {"OnPlayerDied", "{Player.Name} met an untimely demise." },
            {"OnRandomServerEvent", "An {EventName} attack is underway!" }
        };
        public static string SteamID { get { return "896660"; } }
        struct ServerData
        {
            internal string name;
            internal int port;
            internal string world;
            internal string password;
            internal string savedir;
            internal bool pub;
            internal bool autostart;
            internal bool rawlog;
            internal int restartHours;
            internal bool updateOnRestart;
            internal int updateCheckMinutes;
            internal string discordWebhook;
            internal ProcessPriorityClass processPriority;
        }
        public event EventHandler<UpdatedEventArgs> Updated;
        public event EventHandler<FailedPasswordEventArgs> FailedPassword;
        public event EventHandler<PlayerEventArgs> PlayerConnected;
        public event EventHandler<PlayerEventArgs> PlayerDisconnected;
        public event EventHandler<PlayerEventArgs> PlayerDied;
        public event EventHandler<RandomServerEventArgs> RandomServerEvent;
        public event EventHandler<EventArgs> Starting;
        public event EventHandler<EventArgs> Started;
        public event EventHandler<ServerErrorEventArgs> StartFailed;
        public event EventHandler<ServerErrorEventArgs> StopFailed;
        public event EventHandler<EventArgs> ScheduledRestart;
        public event EventHandler<EventArgs> AutomaticUpdate;
        public event EventHandler<ServerStoppedEventArgs> Stopped;
        public event EventHandler<EventArgs> Stopping;
        public event EventHandler<ServerStoppedEventArgs> StoppedUnexpectedly;
        public event EventHandler<ServerErrorEventArgs> ErrorOccurred;
        public event EventHandler<UpdateCheckEventArgs> CheckedForUpdate;
        public event EventHandler<UpdateEndedEventArgs> UpdateEnded;
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
        public event EventHandler<LoggedMessageEventArgs> LoggedMessage;
        private bool testMode = false;
        private ServerData data;
        private Dictionary<string, string> _discordWebhookMesssages;
        private ServerData backupData;
        private Process process;
        private PlayerList players;
        private ServerStatus status;
        private DateTime startTime;
        private bool intentionalExit;
        private List<string> connectingSteamIds;
        private bool needsRestart;
        private bool needsUpdate;
        private bool scheduledRestart;
        private bool automaticUpdate;
        private System.Timers.Timer restartTimer;
        private System.Timers.Timer updateTimer;
        private List<LogEntry> logEntries;
        private bool inTxn = false;
        private int stopAttempts;
        private bool disposed = false;

        public string Name
        {
            get
            {
                return this.data.name;
            }
            set
            {
                this.data.name = value;
            }
        }
        public int Port
        {
            get
            {
                return this.data.port;
            }
            set
            {
                this.data.port = value;
            }
        }
        public string World
        {
            get
            {
                return this.data.world;
            }
            set
            {
                this.data.world = value;
            }
        }
        public string Password
        {
            get
            {
                return this.data.password;
            }
            set
            {
                this.data.password = value;
            }
        }
        public string SaveDir
        {
            get
            {
                return this.data.savedir;
            }
            set
            {
                this.data.savedir = value;
            }
        }
        public bool Public
        {
            get
            {
                return this.data.pub;
            }
            set
            {
                this.data.pub = value;
            }
        }
        public bool Autostart
        {
            get
            {
                return this.data.autostart;
            }
            set
            {
                this.data.autostart = value;
            }
        }
        public bool RawLog
        {
            get
            {
                return this.data.rawlog;
            }
            set
            {
                this.data.rawlog = value;
            }
        }
        public int RestartHours
        {
            get
            {
                return this.data.restartHours;
            }
            set
            {
                this.data.restartHours = value;
                if (value > 0 && (this.Status == ServerStatus.Running || this.status == ServerStatus.Starting))
                {
                    restartTimer.Interval = this.GetMilisecondsUntilRestart();
                    restartTimer.Enabled = true;
                    restartTimer.Start();
                }
                else
                {
                    restartTimer.Enabled = false;
                }
            }
        }
        public int UpdateCheckMinutes
        {
            get
            {
                return this.data.updateCheckMinutes;
            }
            set
            {
                this.data.updateCheckMinutes = value;
                if (value > 0 && (this.Status == ServerStatus.Running || this.status == ServerStatus.Starting))
                {
                    updateTimer.Interval = this.GetMilisecondsUntilUpdateCheck();
                    updateTimer.Enabled = true;
                    updateTimer.Start();
                }
                else
                {
                    restartTimer.Enabled = false;
                }
            }
        }
        public bool UpdateOnRestart
        {
            get
            {
                return this.data.updateOnRestart;
            }
            set
            {
                this.data.updateOnRestart = value;
            }
        }
        public string DiscordWebhook
        {
            get
            {
                return this.data.discordWebhook;
            }
            set
            {
                this.data.discordWebhook = value;
            }
        }
        public Dictionary<string,string> DiscordWebhookMessages
        {
            get
            {
                return this._discordWebhookMesssages;
            }
            set
            {
                this._discordWebhookMesssages = value;
            }
        }
        public string InstallPath { get; set; }
        public ProcessPriorityClass ProcessPriority
        {
            get
            {
                return this.data.processPriority;
            }
            set
            {
                this.data.processPriority = value;
            }
        }
        public ServerInstallMethod InstallMethod { get; set; }
        [JsonIgnore]
        public bool Running
        {
            get
            {
                return (this.Status == ServerStatus.Running || this.Status == ServerStatus.Starting || this.Status == ServerStatus.Stopping);
            }
        }
        [JsonIgnore]
        public ServerStatus Status
        {
            get
            {
                return this.status;
            }
        }
        [JsonIgnore]
        public int PlayerCount
        {
            get
            {
                return this.Players.Count;
            }
        }
        [JsonIgnore]
        public PlayerList Players
        {
            get
            {
                return this.players;
            }
        }
        [JsonIgnore]
        public string PlayerList
        {
            get
            {
                return this.players.ToString();
            }
        }
        [JsonIgnore]
        public DateTime StartTime
        {
            get { return this.startTime; }
        }
        [JsonIgnore]
        public List<LogEntry> LogEntries
        {
            get
            {
                return logEntries;
            }
        }
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                try
                {
                    var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                    htmlDoc.LoadHtml(Name);
                    return htmlDoc.DocumentNode.InnerText.Trim();
                }
                catch (Exception ex)
                {
                    return Name;
                }
            }
        }
        public ValheimServer(string name, int port, string world, string password, bool pubserver, bool autostart, bool rawlog, int restarthours, bool updateonrestart, int updatecheckminutes, string discordwebhook, Dictionary<string,string> discordmessages, ServerInstallMethod install, string instpath, ProcessPriorityClass processpriority)
        {
            this.data.name = name;
            this.data.port = 2456;
            this.data.world = world;
            this.data.password = password;
            this.data.savedir = "";
            this.data.pub = pubserver;
            this.data.autostart = autostart;
            this.data.rawlog = rawlog;
            this.data.restartHours = restarthours;
            this.data.updateOnRestart = updateonrestart;
            this.data.discordWebhook = discordwebhook;
            this.data.processPriority = processpriority;
            this._discordWebhookMesssages = discordmessages;
            InstallMethod = install;
            InstallPath = instpath;

            this.process = new Process();
            this.process.StartInfo.EnvironmentVariables["SteamAppId"] = "892970";
            this.process.StartInfo.UseShellExecute = false;
            this.process.StartInfo.CreateNoWindow = true;
            this.process.StartInfo.RedirectStandardOutput = true;
            this.process.StartInfo.RedirectStandardError = true;
            this.process.StartInfo.RedirectStandardInput = true;
            this.process.EnableRaisingEvents = true;
            this.process.OutputDataReceived += Process_OutputDataReceived;
            this.process.Exited += Process_Exited;

            restartTimer = new System.Timers.Timer();
            restartTimer.AutoReset = false;
            restartTimer.Elapsed += RestartTimer_Elapsed;

            updateTimer = new System.Timers.Timer();
            updateTimer.AutoReset = false;
            updateTimer.Elapsed += UpdateTimer_Elapsed;

            this.players = new PlayerList();
            this.status = ServerStatus.Stopped;
            this.scheduledRestart = false;
            this.needsRestart = false;
            this.automaticUpdate = false;
            this.needsUpdate = false;
            this.logEntries = new List<LogEntry>();

            connectingSteamIds = new List<string>();
            stopAttempts = 0;
            Servers.Add(this);
        }

        private void UpdateTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            CheckedForUpdate += ValheimServer_ScheduledCheckedForUpdate;
            this.CheckForUpdate();
        }

        private void ValheimServer_ScheduledCheckedForUpdate(object sender, UpdateCheckEventArgs e)
        {
            CheckedForUpdate -= ValheimServer_ScheduledCheckedForUpdate;
            if (e.UpdateAvailable)
            {
                if (this.PlayerCount == 0)
                {
                    OnAutomaticUpdate(new EventArgs());
                    this.Stop();
                }
                else
                {
                    this.needsUpdate = true;
                }
            }
            else
            {
                UpdateCheckMinutes = UpdateCheckMinutes;
            }
        }

        private void RestartTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (this.PlayerCount == 0)
            {
                OnScheduledRestart(new EventArgs());
                this.Stop();
            } else
            {
                this.needsRestart = true;
            }
        }

        public ValheimServer() : this("My Server", 2456, "Dedicated", "Secret", false, false, false, 0, false, 0, null, new Dictionary<string,string>(), ServerInstallMethod.Manual, Properties.Settings.Default.ServerFilePath, ProcessPriorityClass.Normal)
        {
        }

        public ValheimServer(string name) : this(name, 2456, "Dedicated", "Secret", false, false, false, 0, false, 0, null, new Dictionary<string,string>(), ServerInstallMethod.Manual, Properties.Settings.Default.ServerFilePath, ProcessPriorityClass.Normal)
        {

        }
        public string LogRawName
        {
            get
            {
                return LogName.Replace(".log", "-raw.log");
            }
        }
        public string LogName
        {
            get
            {
                string logname = this.DisplayName.Replace(" ", "_");
                logname = Regex.Replace(logname, @"[<]", "[");
                logname = Regex.Replace(logname, @"[>]", "]");
                foreach (var c in Path.GetInvalidFileNameChars()) { logname = logname.Replace(c, '-'); }
                return $"{logname}-{this.Port}-{this.World}.log";
            }
        }
        public double GetMilisecondsUntilRestart()
        {
            DateTime restartTime = this.startTime.AddHours(this.RestartHours);
            //DateTime restartTime = this.startTime.AddMinutes(1);
            TimeSpan ts = restartTime - this.startTime;
            return ts.TotalMilliseconds;
        }
        public double GetMilisecondsUntilUpdateCheck()
        {
            DateTime updateCheckTime = this.startTime.AddMinutes(this.UpdateCheckMinutes);
            TimeSpan ts = updateCheckTime - this.startTime;
            return ts.TotalMilliseconds;
        }

        public string GetWebhookMessage(string EventName)
        {
            if (this.DiscordWebhookMessages.ContainsKey(EventName))
            {
                return this.DiscordWebhookMessages[EventName];
            }
            else if (DiscordWebhookDefaultMessages.ContainsKey(EventName))
            {
                return DiscordWebhookDefaultMessages[EventName];
            }
            return null;
        }

        public void SendDiscordWebhook(string EventName, Player player, string serverEventName)
        {
            string message = GetWebhookMessage(EventName);
            if (message == "" || message == null) return;
            message = message.Replace("{Server.Name}", this.DisplayName);
            message = message.Replace("{Server.PlayerCount}", this.PlayerCount.ToString());
            if (player != null)
            {
                message = message.Replace("{Player.Name}", player.Name);
                message = message.Replace("{Player}", player.Name);
                message = message.Replace("{Player.SteamID}", player.SteamID);
                message = message.Replace("{SteamID}", player.SteamID);
                message = message.Replace("{Player.Deaths}", player.Deaths.ToString());
                message = message.Replace("{Player.JoinTime}", player.JoinTime.ToString());
            }
            if (serverEventName != null)
            {
                message = message.Replace("{EventName}", serverEventName);
            }
            SendDiscordWebhook(message);
        }
        public void SendDiscordWebhook(string EventName, Player player)
        {
            SendDiscordWebhook(EventName, player, null);
        }
        public void SendDiscordWebhook(string message)
        {
            if (this.DiscordWebhook != null && this.DiscordWebhook != "")
            {
                using (DiscordWebhook webhook = new DiscordWebhook())
                {
                    //webhook.ProfilePicture = "https://static.giantbomb.com/uploads/original/4/42381/1196379-gas_mask_respirator.jpg";
                    //webhook.UserName = "Bot";
                    webhook.WebHook = this.DiscordWebhook;
                    webhook.SendMessage(message);
                }
            }
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            string msg = e.Data;
            if (msg == null) return;
            //Debug.WriteLine(msg);
            if (this.RawLog)
            {
                try
                {
                    StreamWriter writer = System.IO.File.AppendText(LogRawName);
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
                if (connectingSteamIds.Contains(match.Groups[1].ToString()))
                {
                    connectingSteamIds.Remove(match.Groups[1].ToString());
                }
                OnFailedPassword(new FailedPasswordEventArgs(match.Groups[1].ToString()));
                return;
            }

            //Monitor for initiation of new connection
            rx = new Regex(@"Got handshake from client (\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = rx.Match(msg);
            if (match.Success)
            {
                connectingSteamIds.Add(match.Groups[1].ToString());
                return;
            }

            //Monitor for new player connected and player deaths
            rx = new Regex(@"Got character ZDOID from (.+) : (-?\d+:-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = rx.Match(msg);
            if (match.Success)
            {
                if (match.Groups[2].ToString().Equals("0:0"))
                {
                    //player died
                    foreach (Player player in this.players)
                    {
                        if (player.Name.Equals(match.Groups[1].ToString()))
                        {
                            player.Deaths++;
                            OnPlayerDied(new PlayerEventArgs(player));
                            break;
                        }
                    }
                } 
                else if (connectingSteamIds.Count > 0)
                {
                    //player connected
                    var steamid = connectingSteamIds.First();
                    Player player = new Player(match.Groups[1].ToString(), steamid);
                    this.players.Add(player);
                    connectingSteamIds.Remove(steamid);
                    OnPlayerConnected(new PlayerEventArgs(player));
                }
                return;
            }

            //Monitor for player disconnected
            rx = new Regex(@"Closing socket (\d{2,})", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = rx.Match(msg);
            if (match.Success)
            {
                string steamid = match.Groups[1].ToString();
                //Player player = new Player(match.Groups[1].ToString(), this.connectingSteamID);
                foreach (Player player in this.players)
                {
                    if (steamid.Equals(player.SteamID))
                    {
                        this.players.Remove(player);
                        OnPlayerDisconnected(new PlayerEventArgs(player));
                        if (this.needsRestart && this.PlayerCount == 0)
                        {
                            OnScheduledRestart(new EventArgs());
                            this.Stop();
                        }
                        if (this.needsUpdate && this.PlayerCount == 0)
                        {
                            OnAutomaticUpdate(new EventArgs());
                            this.Stop();
                        }
                        break;
                    }
                }
                return;
            }

            //Monitor for update to number of players connected
            /*rx = new Regex(@"Connections (\d+) ZDOS:(?:\d+)  sent:(?:\d+) recv:(?:\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = rx.Match(msg);
            if (match.Success)
            {
                this.playerCount = Int16.Parse(match.Groups[1].ToString());
                OnPlayerCountUpdated(new ServerEventArgs(this));
            }*/

            //Monitor for server finishes starting
            if (this.Status == ServerStatus.Starting)
            {
                rx = new Regex(@"DungeonDB Start \d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                match = rx.Match(msg);
                if (match.Success)
                {
                    this.status = ServerStatus.Running;
                    OnStarted(new EventArgs());
                    //logMessage($"Server {this.Name}: started", LogType.Success);
                }
                return;
            }

            //Monitor for server fails to start
            // handled more robustly in the process exited event handler
            /*rx = new Regex(@"GameServer.Init\(\) failed", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = rx.Match(msg);
            if (match.Success)
            {
                this.status = ServerStatus.Stopping;
                OnStartFailed(new ServerEventArgs(this));
                logMessage($"Server {this.Name} failed to start. Maybe try a different port", LogType.Error);
                return;
            }*/

            //Monitor for random events
            rx = new Regex(@"Random event set:([a-zA-Z0-9_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = rx.Match(msg);
            if (match.Success)
            {
                OnRandomServerEvent(new RandomServerEventArgs(match.Groups[1].ToString()));
                return;
                //army_moder
            }
        }

        public void Start()
        {
            if (this.Running)
            {
                OnStartFailed(new ServerErrorEventArgs("Server cannot start; it is already running."));
                return;
            }
            foreach (var s in ValheimServer.Servers)
            {
                if (s.Running)
                {
                    IEnumerable<int> range = Enumerable.Range(s.Port, 2);
                    if (range.Contains(this.Port) || range.Contains(this.Port + 1))
                    {
                        OnStartFailed(new ServerErrorEventArgs($"Server cannot start; server {s.DisplayName} is already running on conflicting port {s.Port}."));
                        return;
                    }
                    if (s.SaveDir == this.SaveDir && s.World == this.World)
                    {
                        OnStartFailed(new ServerErrorEventArgs($"Server cannot start; server {s.DisplayName} is already running using world {s.World}."));
                        return;
                    }
                }
            }
            string saveDir = this.SaveDir;
            if (saveDir == null || saveDir.Length == 0)
            {
                saveDir = DefaultSaveDir;
            }
            string serverpath = InstallPath;//Properties.Settings.Default.ServerFilePath;
            if (!File.Exists(serverpath))
            {
                OnStartFailed(new ServerErrorEventArgs($"Server cannot start because {ValheimServer.ExecutableName} was not found at the server executable path ({serverpath}). Please update the server executable path."));
                return;
            }
            string arguments = $"-nographics -batchmode -name \"{this.Name}\" -port {this.Port} -world \"{this.World}\"";
            if (Password != null & Password.Length > 0)
            {
                arguments += $" -password \"{this.Password}\"";
            }
            if (!saveDir.Equals(DefaultSaveDir))
            {
                arguments += $" -savedir \"{this.SaveDir}\"";
            }
            if (Public)
            {
                arguments += $" -public 1";
            }
            if (testMode)
            {
                serverpath = "tracert.exe";
                arguments = "google.com";
            }
            this.intentionalExit = false;
            new Thread(() =>
            {
                if (this.RawLog)
                {
                    System.IO.File.WriteAllText(LogRawName,"");
                }
                this.startTime = DateTime.Now;
                if (this.RestartHours > 0)
                {
                    restartTimer.Interval = this.GetMilisecondsUntilRestart();
                    restartTimer.Enabled = true;
                    restartTimer.Start();
                } 
                else
                {
                    restartTimer.Enabled = false;
                }
                if (this.UpdateCheckMinutes > 0)
                {
                    updateTimer.Interval = this.GetMilisecondsUntilUpdateCheck();
                    updateTimer.Enabled = true;
                    updateTimer.Start();
                }
                else
                {
                    updateTimer.Enabled = false;
                }
                stopAttempts = 0;
                this.status = ServerStatus.Starting;
                OnStarting(new EventArgs());
                this.process.StartInfo.FileName = serverpath;
                this.process.StartInfo.Arguments = arguments;
                Thread.CurrentThread.IsBackground = true;
                //this.processRunning = true;
                this.process.Refresh();
                this.needsRestart = false;
                this.scheduledRestart = false;
                this.needsUpdate = false;
                this.automaticUpdate = false;
                this.process.Start();
                this.process.PriorityClass = this.ProcessPriority;
                this.process.BeginOutputReadLine();
                this.process.WaitForExit();
            }).Start();
        }

        public void Stop()
        {
            if (!this.Running)
            {
                //throw new Exception("This server is not running.");
                OnStopFailed(new ServerErrorEventArgs($"Server cannot stop since it is not running."));
                return;
            }
            new Thread(() =>
            {
                if (AttachConsole((uint)this.process.Id))
                {
                    this.status = ServerStatus.Stopping;
                    OnStopping(new EventArgs());
                    SetConsoleCtrlHandler(null, true);
                    GenerateConsoleCtrlEvent(ConsoleCtrlEvent.CTRL_C, 0);
                    FreeConsole();
                    this.process.WaitForExit(2000);
                    SetConsoleCtrlHandler(null, false);
                    this.intentionalExit = true;
                    this.restartTimer.Enabled = false;
                    this.updateTimer.Enabled = false;
                }
                else
                {
                    if (stopAttempts < 5)
                    {
                        stopAttempts++;
                        this.Stop();
                    }
                    else
                    {
                        OnStopFailed(new ServerErrorEventArgs($"Tried to attach console to stop server but failed 5 times; giving up."));
                        stopAttempts = 0;
                    }
                }
            }).Start();
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GenerateConsoleCtrlEvent(ConsoleCtrlEvent sigevent, int dwProcessGroupId);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);
        [DllImport("kernel32.dll")]
        static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);
        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool FreeConsole();
        delegate Boolean ConsoleCtrlDelegate(uint CtrlType);
        public enum ConsoleCtrlEvent
        {
            CTRL_C = 0,
            CTRL_BREAK = 1,
            CTRL_CLOSE = 2,
            CTRL_LOGOFF = 5,
            CTRL_SHUTDOWN = 6
        }

        private void Process_Exited(object sender, EventArgs e)
        {
            bool failedstart = (this.status == ServerStatus.Starting);
            bool unwantedexit = (this.status != ServerStatus.Stopping);
            this.status = ServerStatus.Stopped;
            this.process.CancelOutputRead();
            //this.processRunning = false;
            this.players.Clear();
            if (failedstart)
            {
                OnStartFailed(new ServerErrorEventArgs($"Server failed to start."));
            }
            else
            {
                OnServerStopped(new ServerStoppedEventArgs(this.process.ExitCode, this.intentionalExit));
                if (scheduledRestart)
                {
                    if (this.UpdateOnRestart && this.InstallMethod == ServerInstallMethod.SteamCMD)
                    {
                        this.CheckedForUpdate += ValheimServer_CheckedForUpdateAndRestart;
                        this.CheckForUpdate();
                    }
                    else
                    {
                        this.Start();
                    }
                } 
                else if (automaticUpdate)
                {
                    if (this.InstallMethod == ServerInstallMethod.SteamCMD)
                    {
                        this.UpdateEnded += ValheimServer_UpdateEndedAndStart;
                        this.Update();
                    }
                }
                else if (unwantedexit)
                {
                    OnServerStoppedUnexpectedly(new ServerStoppedEventArgs(this.process.ExitCode, this.intentionalExit));
                    if (this.Autostart)
                    {
                        this.Start();
                    }
                }
            }
        }

        private void ValheimServer_CheckedForUpdateAndRestart(object sender, UpdateCheckEventArgs e)
        {
            this.CheckedForUpdate -= ValheimServer_CheckedForUpdateAndRestart;
            if (e.UpdateAvailable)
            {
                this.UpdateEnded += ValheimServer_UpdateEndedAndStart;
                this.Update();
            }
            else
            {
                this.Start();
            }
        }

        private void ValheimServer_UpdateEndedAndStart(object sender, UpdateEndedEventArgs e)
        {
            this.UpdateEnded -= ValheimServer_UpdateEndedAndStart;
            this.Start();
        }

        void IEditableObject.BeginEdit()
        {
            if (!inTxn)
            {
                this.backupData = this.data;
                inTxn = true;
            }
        }

        void IEditableObject.CancelEdit()
        {
            if (inTxn)
            {
                this.data = backupData;
                inTxn = false;
            }
        }

        void IEditableObject.EndEdit()
        {
            if (inTxn)
            {
                var changedValues = new NameValueCollection();
                if (backupData.name != data.name)
                {
                    changedValues["Name"] = backupData.name;
                    //OnUpdated(new UpdatedEventArgs("Name"));
                }
                if (backupData.port!= data.port)
                {
                    changedValues["Port"] = backupData.port.ToString();
                    //OnUpdated(new UpdatedEventArgs("Port"));
                }
                if (backupData.world != data.world)
                {
                    changedValues["World"] = backupData.world;
                    //OnUpdated(new UpdatedEventArgs("World"));
                }
                if (backupData.password != data.password)
                {
                    changedValues["Password"] = backupData.password;
                    //OnUpdated(new UpdatedEventArgs("Password"));
                }
                if (backupData.savedir != data.savedir)
                {
                    changedValues["SaveDir"] = backupData.savedir;
                    //OnUpdated(new UpdatedEventArgs("SaveDir"));
                }
                if (backupData.pub != data.pub)
                {
                    changedValues["Public"] = backupData.pub.ToString();
                    //OnUpdated(new UpdatedEventArgs("Public"));
                }
                if (backupData.autostart != data.autostart)
                {
                    changedValues["Autostart"] = backupData.autostart.ToString();
                    //OnUpdated(new UpdatedEventArgs("Autostart"));
                }
                if (backupData.rawlog != data.rawlog)
                {
                    changedValues["RawLog"] = backupData.rawlog.ToString();
                    //OnUpdated(new UpdatedEventArgs("Log"));
                }
                if (backupData.restartHours != data.restartHours)
                {
                    changedValues["RestartHours"] = backupData.restartHours.ToString();
                    //OnUpdated(new UpdatedEventArgs("RestartHours"));
                }
                if (changedValues.Count > 0)
                {
                    OnUpdated(new UpdatedEventArgs(changedValues));
                }
                backupData = new ServerData();
                inTxn = false;
            }
        }

        int IComparable.CompareTo(object obj)
        {
            ValheimServer vs = (ValheimServer)obj;
            int nameCompare = String.Compare(this.DisplayName, vs.DisplayName);
            if (nameCompare == 0)
            {
                return String.Compare(this.Port.ToString(), vs.Port.ToString());
            }
            return nameCompare;
        }

        private void OnUpdated(UpdatedEventArgs args)
        {
            EventHandler<UpdatedEventArgs> handler = Updated;
            if (null != handler) handler(this, args);
        }
        private void OnFailedPassword(FailedPasswordEventArgs args)
        {
            try
            {
                SendDiscordWebhook(System.Reflection.MethodBase.GetCurrentMethod().Name, new Player("Unknown", args.SteamID));
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ServerErrorEventArgs($"Error sending Webhook for failed password attempt: {ex.Message}", ex));
            }
            addToLog($"Failed password attempt for steamid {args.SteamID}.");
            EventHandler<FailedPasswordEventArgs> handler = FailedPassword;
            if (null != handler) handler(this, args);
        }
        private void OnPlayerConnected(PlayerEventArgs args)
        {
            try
            {
                SendDiscordWebhook(System.Reflection.MethodBase.GetCurrentMethod().Name, args.Player);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ServerErrorEventArgs($"Error sending Webhook for player connected: {ex.Message}", ex));
            }
            addToLog($"Player {args.Player.Name} ({args.Player.SteamID}) connected");
            EventHandler<PlayerEventArgs> handler = PlayerConnected;
            if (null != handler) handler(this, args);
        }
        private void OnPlayerDisconnected(PlayerEventArgs args)
        {
            try
            {
                SendDiscordWebhook(System.Reflection.MethodBase.GetCurrentMethod().Name, args.Player);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ServerErrorEventArgs($"Error sending Webhook for player disconnected: {ex.Message}", ex));
            }
            addToLog($"Player {args.Player.Name} ({args.Player.SteamID}) disconnected");
            EventHandler<PlayerEventArgs> handler = PlayerDisconnected;
            if (null != handler) handler(this, args);
        }
        private void OnPlayerDied(PlayerEventArgs args)
        {
            try
            {
                SendDiscordWebhook(System.Reflection.MethodBase.GetCurrentMethod().Name, args.Player);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ServerErrorEventArgs($"Error sending Webhook for player death: {ex.Message}", ex));
            }
            addToLog($"Player {args.Player.Name} ({args.Player.SteamID}) died");
            EventHandler<PlayerEventArgs> handler = PlayerDied;
            if (null != handler) handler(this, args);
        }
        private void OnServerStopped(ServerStoppedEventArgs args)
        {
            try
            {
                SendDiscordWebhook(System.Reflection.MethodBase.GetCurrentMethod().Name, null, null);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ServerErrorEventArgs($"Error sending Webhook for server stop: {ex.Message}", ex));
            }
            if (args.ExitCode == 0 || args.ExitCode == -1073741510)
            {
                addToLog($"Server stopped.");
            }
            else
            {
                addToLog($"Server exited with code {args.ExitCode}.", LogEntryType.Error);
            }
            EventHandler<ServerStoppedEventArgs> handler = Stopped;
            if (null != handler) handler(this, args);
        }
        private void OnServerStoppedUnexpectedly(ServerStoppedEventArgs args)
        {
            addToLog($"Server stopped unexpectedly.", LogEntryType.Error);
            EventHandler<ServerStoppedEventArgs> handler = StoppedUnexpectedly;
            if (null != handler) handler(this, args);
        }
        private void OnRandomServerEvent(RandomServerEventArgs args)
        {
            try
            {
                SendDiscordWebhook(System.Reflection.MethodBase.GetCurrentMethod().Name, null, args.EventName);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ServerErrorEventArgs($"Error sending Webhook for random server eventt: {ex.Message}", ex));
            }
            addToLog($"Random event {args.EventName}.");
            EventHandler<RandomServerEventArgs> handler = RandomServerEvent;
            if (null != handler) handler(this, args);
        }
        private void OnLoggedMessage(LoggedMessageEventArgs args)
        {
            EventHandler<LoggedMessageEventArgs> handler = LoggedMessage;
            if (null != handler) handler(this, args);
        }
        private void OnStarting(EventArgs args)
        {
            addToLog("Server starting...");
            EventHandler<EventArgs> handler = Starting;
            if (null != handler) handler(this, args);
        }
        private void OnStopping(EventArgs args)
        {
            addToLog("Server stopping...");
            EventHandler<EventArgs> handler = Stopping;
            if (null != handler) handler(this, args);
        }
        private void OnStarted(EventArgs args)
        {
            try
            {
                SendDiscordWebhook(System.Reflection.MethodBase.GetCurrentMethod().Name, null, null);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ServerErrorEventArgs($"Error sending Webhook for server start: {ex.Message}", ex));
            }
            addToLog($"Server started.", LogEntryType.Success);
            EventHandler<EventArgs> handler = Started;
            if (null != handler) handler(this, args);
        }
        private void OnStartFailed(ServerErrorEventArgs args)
        {
            addToLog(args.Message, LogEntryType.Error);
            EventHandler<ServerErrorEventArgs> handler = StartFailed;
            if (null != handler) handler(this, args);
        }
        private void OnStopFailed(ServerErrorEventArgs args)
        {
            addToLog(args.Message, LogEntryType.Error);
            EventHandler<ServerErrorEventArgs> handler = StopFailed;
            if (null != handler) handler(this, args);
        }
        private void OnScheduledRestart(EventArgs args)
        {
            scheduledRestart = true;
            addToLog($"Initiating scheduled restart...");
            EventHandler<EventArgs> handler = ScheduledRestart;
            if (null != handler) handler(this, args);
        }
        private void OnAutomaticUpdate(EventArgs args)
        {
            this.automaticUpdate = true;
            addToLog($"Initiating automatic update...");
            EventHandler<EventArgs> handler = AutomaticUpdate;
            if (null != handler) handler(this, args);
        }
        private void OnErrorOccurred(ServerErrorEventArgs args)
        {
            addToLog(args.Message, LogEntryType.Error);
            EventHandler<ServerErrorEventArgs> handler = ErrorOccurred;
            if (null != handler) handler(this, args);
        }
        private void OnCheckedForUpdate(UpdateCheckEventArgs args)
        {
            if (args.Success)
            {
                if (args.UpdateAvailable)
                {
                    addToLog($"Server update available.", LogEntryType.Success);
                }
                else
                {
                    addToLog($"No server update available.");
                }
            }
            else
            {
                addToLog(args.Message, LogEntryType.Error);
            }
            EventHandler<UpdateCheckEventArgs> handler = CheckedForUpdate;
            if (null != handler) handler(this, args);
        }
        private void OnUpdateEnded(UpdateEndedEventArgs args)
        {
            if (args.Updated)
            {
                addToLog("Update complete.", LogEntryType.Success);
            }
            else if (args.Result == UpdateEndedEventArgs.UpdateResults.AlreadyUpToDate)
            {
                addToLog("Server already up to date.");
            }
            else
            {
                addToLog(args.Message, LogEntryType.Error);
            }
            EventHandler<UpdateEndedEventArgs> handler = UpdateEnded;
            if (null != handler) handler(this, args);
        }
        /*private void OutputReceived(DataReceivedEventArgs args)
        {
            EventHandler<DataReceivedEventArgs> handler = OutputDataReceived;
            if (null != handler) handler(this, args);
        }
        private void ErrorReceived(DataReceivedEventArgs args)
        {
            EventHandler<DataReceivedEventArgs> handler = ErrorDataReceived;
            if (null != handler) handler(this, args);
        }*/
        private void addToLog(string message, LogEntryType t)
        {
            var entry = new LogEntry(message, t);
            LogEntries.Add(entry);
            OnLoggedMessage(new LoggedMessageEventArgs(entry));
        }
        private void addToLog(string message)
        {
            addToLog(message, LogEntryType.Normal);
        }
        public void CheckForUpdate()
        {
            try
            {
                new Thread(() =>
                {
                    addToLog($"Checking for server update...");
                    if (!File.Exists(Properties.Settings.Default.SteamCMDPath))
                    {
                        OnCheckedForUpdate(new UpdateCheckEventArgs($"SteamCMD was not found at {Properties.Settings.Default.SteamCMDPath}."));
                        return;
                    }
                    var process = new Process();
                    process.StartInfo.FileName = Properties.Settings.Default.SteamCMDPath;
                    process.StartInfo.Arguments = $"+login anonymous +app_info_update 1 +app_info_print {ValheimServer.SteamID} +quit";
                    process.StartInfo.CreateNoWindow = true;
                    process.EnableRaisingEvents = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    //process.Exited += SteamCmdProcess_Exited;

                    process.Exited += ((object sender, EventArgs e) =>
                    {
                        var process = (Process)sender;
                        var output = process.StandardOutput.ReadToEnd();
                        output = output.Substring(output.IndexOf("\"depots\""));
                        output = output.Substring(output.IndexOf("\"branches\""));
                        output = output.Substring(output.IndexOf("\"public\""));
                        output = output.Substring(0, output.IndexOf("}"));
                        Regex rx = new Regex("\"buildid\"\\s+\"(\\d+)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                        Match match = rx.Match(output);
                        if (match.Success)
                        {
                            var remoteBuild = Convert.ToInt32(match.Groups[1].ToString());
                            if (remoteBuild == 0)
                            {
                                OnCheckedForUpdate(new UpdateCheckEventArgs($"Remote buildid {match.Groups[1]} is not a valid number."));
                                return;
                            }
                            var manifestpath = new FileInfo(this.InstallPath).DirectoryName + $@"\steamapps\appmanifest_{ValheimServer.SteamID}.acf";
                            if (File.Exists(manifestpath))
                            {
                                var manifest = File.ReadAllText(manifestpath);
                                match = rx.Match(manifest);
                                if (match.Success)
                                {
                                    var localBuild = Convert.ToInt32(match.Groups[1].ToString());
                                    if (localBuild == 0)
                                    {
                                        OnCheckedForUpdate(new UpdateCheckEventArgs($"Local buildid {match.Groups[1]} is not a valid number."));
                                        return;
                                    }
                                    OnCheckedForUpdate(new UpdateCheckEventArgs(true, remoteBuild > localBuild));
                                }
                                else
                                {
                                    OnCheckedForUpdate(new UpdateCheckEventArgs($"Could not find local buildid for update check."));
                                }
                            }
                            else
                            {
                                OnCheckedForUpdate(new UpdateCheckEventArgs($"Could not find {manifestpath} for update check."));
                            }
                        }
                        else
                        {
                            OnCheckedForUpdate(new UpdateCheckEventArgs($"Could find not remote buildid for update check."));
                        }
                    });
                    process.Start();
                    //process.WaitForExit();
                }).Start();
            }
            catch (Exception ex)
            {
                OnCheckedForUpdate(new UpdateCheckEventArgs($"Error checking for new version: {ex.Message}"));
            }
        }
        public void Update()
        {
            if (!this.Running)
            {
                foreach (var s in ValheimServer.Servers)
                {
                    if (s != this && s.Status != ServerStatus.Stopped && s.InstallPath == this.InstallPath)
                    {
                        OnUpdateEnded(new UpdateEndedEventArgs($"Could not update server; server {s.DisplayName} uses the same installation and is running."));
                        return;
                    }
                }
                addToLog($"Updating server...");
                if (!File.Exists(Properties.Settings.Default.SteamCMDPath))
                {
                    OnUpdateEnded(new UpdateEndedEventArgs($"SteamCMD was not found at {Properties.Settings.Default.SteamCMDPath}."));
                    return;
                }
                var process = new Process();
                process.StartInfo.FileName = Properties.Settings.Default.SteamCMDPath;
                process.StartInfo.Arguments = $"+login anonymous +force_install_dir \"{(new FileInfo(this.InstallPath).Directory.FullName)}\" +app_update {ValheimServer.SteamID} +quit";
                process.StartInfo.CreateNoWindow = true;
                process.EnableRaisingEvents = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.Exited += ((object sender, EventArgs e) =>
                {
                    var process = (Process)sender;
                    var output = process.StandardOutput.ReadToEnd();
                    //Debug.Write(output);
                    Regex rx = new Regex($"App '{SteamID}' already up to date.", RegexOptions.Compiled);
                    Match match = rx.Match(output);
                    if (match.Success)
                    {
                        OnUpdateEnded(new UpdateEndedEventArgs(UpdateEndedEventArgs.UpdateResults.AlreadyUpToDate));
                        return;
                    }
                    rx = new Regex($"App '{SteamID}' fully installed.", RegexOptions.Compiled);
                    match = rx.Match(output);
                    if (match.Success)
                    {
                        OnUpdateEnded(new UpdateEndedEventArgs(UpdateEndedEventArgs.UpdateResults.Updated));
                        return;
                    }
                    OnUpdateEnded(new UpdateEndedEventArgs("Update probably failed. Unrecognized output from SteamCMD."));
                });
                process.Start();
                //process.WaitForExit();
            }
            else
            {
                OnUpdateEnded(new UpdateEndedEventArgs("Please stop the server before updating."));
            }
        }
        public static void TerminateAll(Process[] servers)
        {
            new Thread(() =>
            {
                foreach (Process proc in servers)
                {
                    if (AttachConsole((uint)proc.Id))
                    {
                        SetConsoleCtrlHandler(null, true);
                        GenerateConsoleCtrlEvent(ConsoleCtrlEvent.CTRL_C, 0);
                        FreeConsole();
                        proc.WaitForExit(2000);
                        SetConsoleCtrlHandler(null, false);
                    }
                }
            }).Start();
        }
        ~ValheimServer()
        {
            Dispose(false);
        }
        public void Dispose()
        {
            Dispose(true);
            // This object will be cleaned up by the Dispose method.
            // Therefore, you should call GC.SupressFinalize to
            // take this object off the finalization queue 
            // and prevent finalization code for this object
            // from executing a second time.
            GC.SuppressFinalize(this);
        }
        public void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (this.Status != ServerStatus.Stopped)
                {
                    throw new Exception("Server must be stopped before it can be disposed");
                }
                // If disposing equals true, dispose all managed 
                // and unmanaged resources.
                if (disposing)
                {
                    // Dispose managed resources.
                    //component.Dispose();
                }
                if (Servers.Contains(this))
                {
                    Servers.Remove(this);
                }
                // Call the appropriate methods to clean up 
                // unmanaged resources here.
                //resource.Cleanup()
            }
            disposed = true;
        }
    }

        public class UpdatedEventArgs : EventArgs
    {
        private readonly NameValueCollection _fieldNames;

        public UpdatedEventArgs(NameValueCollection fieldNames)
        {
            _fieldNames = fieldNames;
        }

        public NameValueCollection FieldName
        {
            get { return _fieldNames; }
        }
    }

    public class FailedPasswordEventArgs : EventArgs
    {
        private readonly string _steamid;

        public FailedPasswordEventArgs(string steamid)
        {
            _steamid = steamid;
        }
        public string SteamID
        {
            get { return _steamid; }
        }
    }
    public class PlayerEventArgs : EventArgs
    {
        private readonly Player _player;

        public PlayerEventArgs(Player player)
        {
            _player = player;
        }
        public Player Player
        {
            get { return _player; }
        }
    }
    public class ServerStoppedEventArgs
    {
        private readonly int _exitcode;
        private readonly bool _intentionalexit;

        public ServerStoppedEventArgs(int exitcode, bool intentionalexit)
        {
            _exitcode = exitcode;
            _intentionalexit = intentionalexit;
        }
        public int ExitCode
        {
            get { return _exitcode; }
        }
        public bool IntentionalExit
        {
            get { return _intentionalexit; }
        }
    }
    public class RandomServerEventArgs
    {
        private readonly string _eventname;

        public RandomServerEventArgs(string eventname)
        {
            _eventname = eventname;
        }
        public string EventName
        {
            get { return _eventname; }
        }
    }
    public class ServerErrorEventArgs : EventArgs
    {
        private readonly Exception _exception;
        private readonly string _message;
        public ServerErrorEventArgs(Exception ex)
        {
            _exception = ex;
            _message = ex.Message;
        }
        public ServerErrorEventArgs(string message)
        {
            _exception = new Exception(message);
            _message = message;
        }
        public ServerErrorEventArgs(string message, Exception ex)
        {
            _exception = ex;
            _message = message;
        }
        public string Message
        {
            get { return _message; }
        }
        public Exception Exception
        {
            get
            {
                return _exception;
            }
        }
    }
    public class UpdateCheckEventArgs : EventArgs
    {
        private readonly bool _updateAvailable;
        private readonly bool _success;
        private readonly string _message;
        public UpdateCheckEventArgs(bool success, bool updateAvailable)
        {
            _success = success;
            _updateAvailable = updateAvailable;
            if (success)
            {
                _message = "Success";
            }
            else
            {
                _message = "Failed";
            }
        }
        public UpdateCheckEventArgs(string errorMessage) : this(false,false)
        {
            _message = errorMessage;
        }
        public bool UpdateAvailable
        {
            get { return _updateAvailable; }
        }
        public bool Success
        {
            get { return _success; }
        }
        public string Message
        {
            get { return _message; }
        }
    }
    public class UpdateEndedEventArgs : EventArgs
    {
        public enum UpdateResults
        {
            Updated,
            AlreadyUpToDate,
            Failed
        }
        private readonly UpdateResults _result;
        private readonly string _message;
        public UpdateEndedEventArgs(UpdateResults result)
        {
            _result = result;
            _message = "";
        }
        public UpdateEndedEventArgs(string failMessage) : this(UpdateResults.Failed)
        {
            _message = failMessage;
        }
        public bool Updated
        {
            get { return _result == UpdateResults.Updated; }
        }
        public UpdateResults Result
        {
            get { return _result; }
        }
        public string Message
        {
            get { return _message; }
        }
    }
}
