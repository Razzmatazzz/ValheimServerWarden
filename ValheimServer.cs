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
using System.Net;

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
            {"OnStarted", "Server {Server.Name} has started on {Server.IP}:{Server.Port} ({Server.Version})." },
            {"OnStartFailed", "Server {Server.Name} failed to start." },
            {"OnStopped", "Server {Server.Name} has stopped." },
            {"OnFailedPassword", "User with SteamID {Player.SteamID} tried to join with an invalid password." },
            {"OnPlayerConnected", "{Player.Name} has entered the fray!" },
            {"OnPlayerDisconnected", "{Player.Name} has departed." },
            {"OnPlayerDied", "{Player.Name} met an untimely demise." },
            {"OnRandomServerEvent", "{EventName} are attacking!" },
            {"OnUpdateEnded", "Server update complete." }
        };
        public static Dictionary<string, string> DiscordWebhookDefaultAttackNames { get; } = new Dictionary<string, string>
        {
            { "army_eikthyr", "Eikthyr's Kin" },
            { "army_theelder", "The Elder's Minions" },
            { "army_bonemass", "Swamp Monsters" },
            { "army_moder", "Moder's Minions" },
            { "army_goblin", "Fulings" },
            { "foresttrolls", "Forest Trolls" },
            { "skeletons", "Skeletons" },
            { "blobs", "Blobs" },
            { "wolves", "Wolves" },
            { "surtlings", "Surtlings" }
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
            internal bool crossplay;
            internal bool autostart;
            internal bool rawlog;
            internal int restartHours;
            internal bool updateOnRestart;
            internal int updateCheckMinutes;
            internal string discordWebhook;
            internal ProcessPriorityClass processPriority;
            internal bool autoUpdateuMod;
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
        public event EventHandler<UpdateCheckEventArgs> CheckingForUpdate;
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
        private static string externalIP;

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
        public bool Crossplay
        {
            get
            {
                return this.data.crossplay;
            }
            set
            {
                this.data.crossplay = value;
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
                if (value > 0 && (this.Status == ServerStatus.Running || this.Status == ServerStatus.Starting))
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
                if (value > 0 && (this.Status == ServerStatus.Running || this.Status == ServerStatus.Starting))
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
        public Dictionary<string, string> DiscordServerEventNames { get; set; }
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
        public bool AutoUpdateuMod
        {
            get
            {
                return this.data.autoUpdateuMod;
            }
            set
            {
                this.data.autoUpdateuMod = value;
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
                    addToLog($"Error getting server display name: {ex.Message}", LogEntryType.Error);
                    return Name;
                }
            }
        }
        [JsonIgnore]
        public string LogRawName
        {
            get
            {
                return LogName.Replace(".log", "-raw.log");
            }
        }
        [JsonIgnore]
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
        [JsonIgnore]
        public double MemoryUsed
        {
            get
            {
                if (this.Status == ServerStatus.Running)
                {
                    try
                    {
                        process.Refresh();
                        return Math.Round(process.WorkingSet64 / (1024.0 * 1024.0));
                    }
                    catch (Exception ex)
                    {
                        addToLog($"Error getting server memory usage: {ex.Message}", LogEntryType.Error);
                        return 0;
                    }
                }
                return 0;
            }
        }
        //[JsonIgnore]
        public string Version { get; set; }
        [JsonIgnore]
        public static string ExternalIP
        {
            get
            {
                return externalIP;
            }
        }
        static ValheimServer()
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            UpdateExternalIP();
        }
        private static void UpdateExternalIP()
        {
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                try
                {
                    externalIP = new WebClient().DownloadString("http://icanhazip.com").Trim();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error getting external IP.");
                    Debug.WriteLine(ex);
                }
            }).Start();
        }
        public ValheimServer(string name, int port, string world, string password, bool pubserver, bool crossplay, bool autostart, bool rawlog, int restarthours, bool updateonrestart, int updatecheckminutes, string discordwebhook, Dictionary<string,string> discordmessages, Dictionary<string, string> discordservereventnames, ServerInstallMethod install, string instpath, ProcessPriorityClass processpriority, bool umodupdating)
        {
            this.data.name = name;
            this.data.port = 2456;
            this.data.world = world;
            this.data.password = password;
            this.data.savedir = "";
            this.data.pub = pubserver;
            this.data.crossplay = crossplay;
            this.data.autostart = autostart;
            this.data.rawlog = rawlog;
            this.data.restartHours = restarthours;
            this.data.updateOnRestart = updateonrestart;
            this.data.discordWebhook = discordwebhook;
            this.data.processPriority = processpriority;
            this._discordWebhookMesssages = discordmessages;
            this.DiscordServerEventNames = discordservereventnames;
            this.data.autoUpdateuMod = umodupdating;
            InstallMethod = install;
            InstallPath = instpath;
            Version = "Unknown";

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
            this.CheckForUpdate(false);
        }

        private void ValheimServer_ScheduledCheckedForUpdate(object sender, UpdateCheckEventArgs e)
        {
            CheckedForUpdate -= ValheimServer_ScheduledCheckedForUpdate;
            if (e.UpdateAvailable)
            {
                if (this.PlayerCount == 0)
                {
                    OnAutomaticUpdate(new EventArgs());
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
            } else
            {
                this.needsRestart = true;
            }
        }

        public ValheimServer() : this("My Server", 2456, "Dedicated", "Secret", false, false, false, false, 0, false, 0, null, new Dictionary<string, string>(), new Dictionary<string, string>(), ServerInstallMethod.Manual, Properties.Settings.Default.ServerFilePath, ProcessPriorityClass.Normal, false)
        {
        }

        public ValheimServer(string name) : this(name, 2456, "Dedicated", "Secret", false, false, false, false, 0, false, 0, null, new Dictionary<string,string>(), new Dictionary<string, string>(), ServerInstallMethod.Manual, Properties.Settings.Default.ServerFilePath, ProcessPriorityClass.Normal, false)
        {

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
            try
            {
                if (this.DiscordWebhookMessages.ContainsKey(EventName))
                {
                    return this.DiscordWebhookMessages[EventName];
                }
                else if (DiscordWebhookDefaultMessages.ContainsKey(EventName))
                {
                    return DiscordWebhookDefaultMessages[EventName];
                }
            } 
            catch (Exception ex)
            {
                addToLog($"Error getting webhook message for {EventName}: {ex.Message}", LogEntryType.Error);
            }
            return null;
        }
        public string GetWebhookServerEventName(string serverEventName) {
            try
            {
                if (this.DiscordServerEventNames.ContainsKey(serverEventName))
                {
                    return this.DiscordServerEventNames[serverEventName];
                }
                else if (DiscordWebhookDefaultAttackNames.ContainsKey(serverEventName))
                {
                    return DiscordWebhookDefaultAttackNames[serverEventName];
                }
                return serverEventName;
            }
            catch (Exception ex)
            {
                addToLog($"Error getting webhook server event name for {serverEventName}: {ex.Message}", LogEntryType.Error);
            }
            return null;
        }
        public void SendDiscordWebhook(string EventName, Player player, string serverEventName)
        {
            try
            {
                string message = GetWebhookMessage(EventName);
                if (message == "" || message == null) return;
                message = message.Replace("{Server.Name}", this.DisplayName);
                message = message.Replace("{Server.PlayerCount}", this.PlayerCount.ToString());
                message = message.Replace("{Server.Version}", this.Version);
                message = message.Replace("{Server.IP}", ValheimServer.ExternalIP);
                message = message.Replace("{Server.Port}", this.Port.ToString());
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
                    message = message.Replace("{EventName}", GetWebhookServerEventName(serverEventName));
                }
                SendDiscordWebhook(message);
            }
            catch (Exception ex) {
                addToLog($"Error sending webhook for {EventName}: {ex.Message}", LogEntryType.Error);
            }
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
                    try
                    {
                        //webhook.ProfilePicture = "https://static.giantbomb.com/uploads/original/4/42381/1196379-gas_mask_respirator.jpg";
                        //webhook.UserName = "Bot";
                        webhook.WebHook = this.DiscordWebhook;
                        webhook.SendMessage(message);
                    }
                    catch (Exception ex)
                    {
                        addToLog($"Error sending Discord webhook: {ex.Message}", LogEntryType.Error);
                    }
                }
            }
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            try
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
                    catch (Exception)
                    {
                        //not being able to clear the log is not a major problem
                    }
                }

                Regex rx;
                Match match;

                //Monitor for incorrect password attempts
                rx = new Regex(@"Peer (\d+) has wrong password", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                match = rx.Match(msg);
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
                    var playername = FixPlayerNameEncoding(match.Groups[1].Value);
                    if (match.Groups[2].ToString().Equals("0:0"))
                    {
                        //player died
                        foreach (Player player in this.players)
                        {
                            if (player.Name.Equals(playername))
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
                        Player player = new Player(playername, steamid);
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
                    var playerfound = false;
                    foreach (Player player in this.players)
                    {
                        if (steamid.Equals(player.SteamID))
                        {
                            this.players.Remove(player);
                            OnPlayerDisconnected(new PlayerEventArgs(player));
                            playerfound = true;
                            if (this.PlayerCount == 0)
                            {
                                if (this.needsRestart)
                                {
                                    OnScheduledRestart(new EventArgs());
                                }
                                else if (this.needsUpdate)
                                {
                                    OnAutomaticUpdate(new EventArgs());
                                }
                            }
                            break;
                        }
                    }
                    if (!playerfound)
                    {
                        if (connectingSteamIds.Contains(match.Groups[1].ToString()))
                        {
                            connectingSteamIds.Remove(match.Groups[1].ToString());
                        }
                    }
                    return;
                }

                //Monitor for random events
                rx = new Regex(@"Random event set:([a-zA-Z0-9_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                match = rx.Match(msg);
                if (match.Success)
                {
                    OnRandomServerEvent(new RandomServerEventArgs(match.Groups[1].ToString()));
                    return;
                    //army_moder
                }

                if (this.Status == ServerStatus.Starting)
                {
                    //Monitor for server version
                    rx = new Regex(@"Valheim version:(\d+\.\d+\.\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    match = rx.Match(msg);
                    if (match.Success)
                    {
                        Version = match.Groups[1].ToString();
                        //logMessage($"Server {this.Name}: started", LogType.Success);
                        return;
                    }

                    //Monitor for server finishes starting
                    //Last since it should only happen once per server restart, so more efficient overall to check others first
                    rx = new Regex(@"DungeonDB Start \d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                    match = rx.Match(msg);
                    if (match.Success)
                    {
                        OnStarted(new EventArgs());
                        //logMessage($"Server {this.Name}: started", LogType.Success);
                        return;
                    }
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

                //Monitor for update to number of players connected
                /*rx = new Regex(@"Connections (\d+) ZDOS:(?:\d+)  sent:(?:\d+) recv:(?:\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                match = rx.Match(msg);
                if (match.Success)
                {
                    this.playerCount = Int16.Parse(match.Groups[1].ToString());
                    OnPlayerCountUpdated(new ServerEventArgs(this));
                }*/
            }
            catch (Exception ex)
            {
                addToLog($"Error processing server output: {ex.Message}", LogEntryType.Error);
            }
        }
        public void Start()
        {
            try
            {
                if (this.Status != ValheimServer.ServerStatus.Stopped)
                {
                    var oldstatus = this.Status;
                    OnStartFailed(new ServerErrorEventArgs("Server cannot start; it is already running."));
                    this.status = oldstatus;
                    return;
                }
                OnStarting(new EventArgs());
                if (uMod.AgentInstalled && AutoUpdateuMod)
                {
                    var umod = new uMod(this.InstallPath, "valheim");
                    umod.UpdateEnded += StartuMod_UpdateEnded;
                    umod.LoggedMessage += ((sender, args) => {
                        //addToLog("uMod: "+args.LogEntry.Message, args.LogEntry.Type);
                    });
                    umod.Update("core apps extensions");
                }
                else
                {
                    StartServer();
                }
            }
            catch (Exception ex)
            {
                addToLog($"Error initiating server start: {ex.Message}", LogEntryType.Error);
            }
        }

        private void StartuMod_UpdateEnded(object sender, uMod.ProcessEndedEventArgs e)
        {
            if (e.ExitCode == 0)
            {
                addToLog("uMod update compelete.");
            }
            else if (e.ExitCode == 2)
            {
                addToLog($"No uMod updates needed.");
            }
            else
            {
                addToLog($"uMod terminated with code {e.ExitCode}; unable to update uMod core or uMod apps.");
            }
            StartServer();
        }

        private void StartServer()
        {
            try
            {
                if (this.Status != ServerStatus.Starting)
                {
                    OnStartFailed(new ServerErrorEventArgs($"Server cannot start; it is {this.Status}."));
                    return;
                }
                foreach (var s in ValheimServer.Servers)
                {
                    if (s.Running && s != this)
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
                string arguments = $"-nographics -batchmode -name \"{this.Name}\" -port {this.Port} -world \"{this.World}\" -public {Convert.ToInt32(Public)}";
                if (this.Crossplay)
                {
                    arguments += " -crossplay";
                }
                if (Password != null & Password.Length > 0)
                {
                    arguments += $" -password \"{this.Password}\"";
                }
                if (!saveDir.Equals(DefaultSaveDir))
                {
                    arguments += $" -savedir \"{this.SaveDir}\"";
                }
                this.intentionalExit = false;
                new Thread(() =>
                {
                    try
                    {
                        if (this.RawLog)
                        {
                            System.IO.File.WriteAllText(LogRawName, "");
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
                        this.process.StartInfo.FileName = serverpath;
                        this.process.StartInfo.Arguments = arguments;
                        this.process.Refresh();
                        this.needsRestart = false;
                        this.scheduledRestart = false;
                        this.needsUpdate = false;
                        this.automaticUpdate = false;
                        this.process.Start();
                        this.process.PriorityClass = this.ProcessPriority;
                        this.process.BeginOutputReadLine();
                        this.process.WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        addToLog($"Error waiting for server process exit: {ex.Message}", LogEntryType.Error);
                    }
                }).Start();
            }
            catch (Exception ex)
            {
                addToLog($"Error starting server: {ex.Message}", LogEntryType.Error);
            }
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
                try
                {
                    if (AttachConsole((uint)this.process.Id))
                    {
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
                }
                catch (Exception ex)
                {
                    addToLog($"Error stopping server: {ex.Message}", LogEntryType.Error);
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
            this.process.CancelOutputRead();
            //this.processRunning = false;
            this.players.Clear();
            if (failedstart)
            {
                OnStartFailed(new ServerErrorEventArgs($"Server failed to start."));
            }
            else
            {
                OnStopped(new ServerStoppedEventArgs(this.process.ExitCode, this.intentionalExit));
                if (scheduledRestart)
                {
                    if (this.UpdateOnRestart && this.InstallMethod == ServerInstallMethod.SteamCMD)
                    {
                        this.CheckedForUpdate += ValheimServer_CheckedForUpdateAndRestart;
                        this.CheckForUpdate(false);
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
                    /*if (this.Autostart)
                    {
                        this.Start();
                    }*/
                    this.Start();
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
            try
            {
                addToLog($"Failed password attempt for steamid {args.SteamID}.");
                EventHandler<FailedPasswordEventArgs> handler = FailedPassword;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing FailedPassword event handlers: {ex.Message}", LogEntryType.Error);
            }
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
            try
            {
                addToLog($"Player {args.Player.Name} ({args.Player.SteamID}) connected");
                EventHandler<PlayerEventArgs> handler = PlayerConnected;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing PlayerConnected event handlers: {ex.Message}", LogEntryType.Error);
            }
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
            try
            {
                addToLog($"Player {args.Player.Name} ({args.Player.SteamID}) disconnected");
                EventHandler<PlayerEventArgs> handler = PlayerDisconnected;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing PlayerDisconnected event handlers: {ex.Message}", LogEntryType.Error);
            }
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
            try
            {
                addToLog($"Player {args.Player.Name} ({args.Player.SteamID}) died");
                EventHandler<PlayerEventArgs> handler = PlayerDied;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing PlayerDied event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnStopped(ServerStoppedEventArgs args)
        {
            this.status = ServerStatus.Stopped;
            try
            {
                SendDiscordWebhook(System.Reflection.MethodBase.GetCurrentMethod().Name, null, null);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ServerErrorEventArgs($"Error sending Webhook for server stop: {ex.Message}", ex));
            }
            try
            {
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
            catch (Exception ex)
            {
                addToLog($"Error firing Stopped event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnServerStoppedUnexpectedly(ServerStoppedEventArgs args)
        {
            try
            {
                addToLog($"Server stopped unexpectedly.", LogEntryType.Error);
                EventHandler<ServerStoppedEventArgs> handler = StoppedUnexpectedly;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing ServerStoppedUnexpectedly event handlers: {ex.Message}", LogEntryType.Error);
            }
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
            try
            {
                addToLog($"Random event {args.EventName}.");
                EventHandler<RandomServerEventArgs> handler = RandomServerEvent;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing RandomServerEvent event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnLoggedMessage(LoggedMessageEventArgs args)
        {
            try
            {
                EventHandler<LoggedMessageEventArgs> handler = LoggedMessage;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing LoggedMessage event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnStarting(EventArgs args)
        {
            try
            {
                this.status = ServerStatus.Starting;
                addToLog("Server starting...");
                EventHandler<EventArgs> handler = Starting;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing Starting event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnStopping(EventArgs args)
        {
            try
            {
                this.status = ServerStatus.Stopping;
                addToLog("Server stopping...");
                EventHandler<EventArgs> handler = Stopping;
                if (null != handler) handler(this, args);
                UpdateExternalIP();
            }
            catch (Exception ex)
            {
                addToLog($"Error firing Stopping event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnStarted(EventArgs args)
        {
            this.status = ServerStatus.Running;
            try
            {
                SendDiscordWebhook(System.Reflection.MethodBase.GetCurrentMethod().Name, null, null);
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new ServerErrorEventArgs($"Error sending Webhook for server start: {ex.Message}", ex));
            }
            try
            {
                var v = Version == "Unknown" ? "" : " (" + Version + ")";
                addToLog($"Server started{v}.", LogEntryType.Success);
                EventHandler<EventArgs> handler = Started;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing Started event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnStartFailed(ServerErrorEventArgs args)
        {
            try
            {
                this.status = ServerStatus.Stopped;
                addToLog(args.Message, LogEntryType.Error);
                EventHandler<ServerErrorEventArgs> handler = StartFailed;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing StartFailed event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnStopFailed(ServerErrorEventArgs args)
        {
            try
            {
                this.status = ServerStatus.Running;
                addToLog(args.Message, LogEntryType.Error);
                EventHandler<ServerErrorEventArgs> handler = StopFailed;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing StopFailed event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnScheduledRestart(EventArgs args)
        {
            try
            {
                scheduledRestart = true;
                addToLog($"Initiating scheduled restart...");
                EventHandler<EventArgs> handler = ScheduledRestart;
                if (null != handler) handler(this, args);
                this.Stop();
            }
            catch (Exception ex)
            {
                addToLog($"Error firing ScheduledRestart event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnAutomaticUpdate(EventArgs args)
        {
            try
            {
                this.automaticUpdate = true;
                addToLog($"Initiating automatic update...");
                EventHandler<EventArgs> handler = AutomaticUpdate;
                if (null != handler) handler(this, args);
                this.Stop();
            }
            catch (Exception ex)
            {
                addToLog($"Error firing AutomaticUpdate event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnErrorOccurred(ServerErrorEventArgs args)
        {
            try
            {
                addToLog(args.Message, LogEntryType.Error);
                EventHandler<ServerErrorEventArgs> handler = ErrorOccurred;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing ErrorOccurred event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnCheckingForUpdate(UpdateCheckEventArgs args)
        {
            try
            {
                if (args.Noisy)
                {
                    addToLog($"Checking for server update...");
                }
                EventHandler<UpdateCheckEventArgs> handler = CheckingForUpdate;
                if (null != handler) handler(this, args);
            }
            catch (Exception ex)
            {
                addToLog($"Error firing CheckingForUpdate event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnCheckedForUpdate(UpdateCheckEventArgs args)
        {
            try
            {
                if (args.Success)
                {
                    if (args.UpdateAvailable)
                    {
                        if (InstallMethod == ServerInstallMethod.SteamCMD)
                        {
                            addToLog($"Server update available.", LogEntryType.Success);
                        }
                    }
                    else if (args.Noisy)
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
            catch (Exception ex)
            {
                addToLog($"Error firing CheckedForUpdate event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void OnUpdateEnded(UpdateEndedEventArgs args)
        {
            try
            {
                status = ServerStatus.Stopped;
                if (args.Updated)
                {
                    try
                    {
                        SendDiscordWebhook(System.Reflection.MethodBase.GetCurrentMethod().Name, null, null);
                    }
                    catch (Exception ex)
                    {
                        OnErrorOccurred(new ServerErrorEventArgs($"Error sending Webhook for server updated: {ex.Message}", ex));
                    }
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
            catch (Exception ex)
            {
                addToLog($"Error firing UpdateEnded event handlers: {ex.Message}", LogEntryType.Error);
            }
        }
        private void addToLog(string message, LogEntryType t)
        {
            try
            {
                var entry = new LogEntry(message, t);
                LogEntries.Add(entry);
                OnLoggedMessage(new LoggedMessageEventArgs(entry));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding message to log: {ex.Message}");
            }
        }
        private void addToLog(string message)
        {
            addToLog(message, LogEntryType.Normal);
        }
        public void CheckForUpdate(bool noisy)
        {
            new Thread(() =>
            {
                try
                {
                    //addToLog($"Checking for server update...");
                    if (!File.Exists(Properties.Settings.Default.SteamCMDPath))
                    {
                        OnCheckedForUpdate(new UpdateCheckEventArgs($"SteamCMD was not found at {Properties.Settings.Default.SteamCMDPath}."));
                        return;
                    }
                    OnCheckingForUpdate(new UpdateCheckEventArgs(false, false, noisy));
                    var steamcmddir = (new FileInfo(Properties.Settings.Default.SteamCMDPath)).Directory.FullName;
                    if (File.Exists($@"{steamcmddir}\appcache\appinfo.vdf")) File.Delete($@"{steamcmddir}\appcache\appinfo.vdf");
                    //if (File.Exists($@"{steamcmddir}\appcache\packageinfo.vdf")) File.Delete($@"{steamcmddir}\appcache\packageinfo.vdf");
                    var process = new Process();
                    process.StartInfo.FileName = Properties.Settings.Default.SteamCMDPath;
                    process.StartInfo.Arguments = $"+login anonymous +app_info_update 1 +app_info_print {ValheimServer.SteamID} +quit";
                    process.StartInfo.WorkingDirectory = new FileInfo(this.InstallPath).Directory.FullName;
                    process.StartInfo.CreateNoWindow = true;
                    process.EnableRaisingEvents = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    //process.Exited += SteamCmdProcess_Exited;

                    process.Exited += ((object sender, EventArgs e) =>
                    {
                        try
                        {
                            var process = (Process)sender;
                            var output = process.StandardOutput.ReadToEnd();
                            int depotsIndex = output.IndexOf("\"depots\"");
                            if (depotsIndex == -1)
                            {
                                OnCheckedForUpdate(new UpdateCheckEventArgs($"Could not find 'depots' node for update check."));
                                return;
                            }
                            output = output.Substring(depotsIndex);
                            int branchesIndex = output.IndexOf("\"branches\"");
                            if (branchesIndex == -1)
                            {
                                OnCheckedForUpdate(new UpdateCheckEventArgs($"Could not find 'branches' node for update check."));
                                return;
                            }
                            output = output.Substring(branchesIndex);
                            int publicIndex = output.IndexOf("\"public\"");
                            if (publicIndex == -1)
                            {
                                OnCheckedForUpdate(new UpdateCheckEventArgs($"Could not find 'public' node for update check."));
                                return;
                            }
                            output = output.Substring(publicIndex);
                            int closingbraceIndex = output.IndexOf("}");
                            if (closingbraceIndex == -1)
                            {
                                OnCheckedForUpdate(new UpdateCheckEventArgs($"Could not find closing bracket of 'public' node for update check."));
                                return;
                            }
                            output = output.Substring(0, closingbraceIndex);
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
                                var appManifestFound = File.Exists(manifestpath);
                                if (!appManifestFound)
                                {
                                    var steamcmdpath = new FileInfo(Properties.Settings.Default.SteamCMDPath).DirectoryName;
                                    if (this.InstallPath.StartsWith($@"{steamcmdpath}\steamapps\common"))
                                    {
                                        if (File.Exists($@"{steamcmdpath}\steamapps\appmanifest_{ValheimServer.SteamID}.acf"))
                                        {
                                            manifestpath = $@"{steamcmdpath}\steamapps\appmanifest_{ValheimServer.SteamID}.acf";
                                            appManifestFound = true;
                                        }
                                    }
                                }
                                if (appManifestFound)
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
                                        OnCheckedForUpdate(new UpdateCheckEventArgs(true, remoteBuild > localBuild, noisy));
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
                        }
                        catch (Exception ex)
                        {
                            addToLog($"Error parsing update check output: {ex.Message}", LogEntryType.Error);
                        }
                    });
                    process.Start();
                    //process.WaitForExit();
                }
                catch (Exception ex)
                {
                    OnCheckedForUpdate(new UpdateCheckEventArgs($"Error checking for new version: {ex.Message}"));
                }
            }).Start();
        }
        public void Update()
        {
            try
            {
                if (!this.Running)
                {
                    status = ServerStatus.Updating;
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
                        try
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
                        }
                        catch (Exception ex)
                        {
                            OnUpdateEnded(new UpdateEndedEventArgs($"Error during update: {ex.Message}"));
                        }
                    });
                    process.Start();
                }
                else
                {
                    OnUpdateEnded(new UpdateEndedEventArgs("Please stop the server before updating."));
                }
            }
            catch (Exception ex)
            {
                OnUpdateEnded(new UpdateEndedEventArgs($"Error starting update: {ex.Message}"));
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
        public static string FixPlayerNameEncoding(string n)
        {
            //var encoding = Encoding.GetEncoding(437);
            var encoding = Encoding.GetEncoding(1252);
            var bytes = encoding.GetBytes(n);
            return Encoding.UTF8.GetString(bytes);
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
        private readonly bool _noisy;
        public UpdateCheckEventArgs(bool success, bool updateAvailable, bool noisy)
        {
            _success = success;
            _updateAvailable = updateAvailable;
            _noisy = noisy;
            if (success)
            {
                _message = "Success";
            }
            else
            {
                _message = "Failed";
            }
        }
        public UpdateCheckEventArgs(string errorMessage) : this(false,false,false)
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
        public bool Noisy
        {
            get { return _noisy; }
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
