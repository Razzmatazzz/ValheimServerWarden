using System;
using System.Collections.Generic;
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

namespace ValheimServerWarden
{
    public class ValheimServer : IEditableObject, IComparable
    {
        public enum ServerStatus
        {
            Stopped,
            Running,
            Starting,
            Stopping
        }
        struct ServerData
        {
            internal string name;
            internal int port;
            internal string world;
            internal string password;
            internal string savedir;
            internal bool autostart;
            internal bool log;
        }
        public event EventHandler<UpdatedEventArgs> Updated;
        public event EventHandler<FailedPasswordEventArgs> FailedPassword;
        public event EventHandler<PlayerEventArgs> PlayerConnected;
        public event EventHandler<PlayerEventArgs> PlayerDisconnected;
        public event EventHandler<PlayerEventArgs> PlayerDied;
        public event EventHandler<RandomServerEventArgs> RandomServerEvent;
        public event EventHandler<ServerEventArgs> Starting;
        public event EventHandler<ServerEventArgs> Started;
        public event EventHandler<ServerEventArgs> StartFailed;
        public event EventHandler<ServerExitedEventArgs> Exited;
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
        public event EventHandler<ServerLogMessageEventArgs> LogMessage;
        private string defaultSaveDir = $@"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\AppData\LocalLow\IronGate\Valheim";
        private string serverExe = "valheim_server.exe";
        private bool testMode = false;
        private ServerData data;
        private ServerData backupData;
        private Process process;
        private PlayerList players;
        private ServerStatus status;
        private DateTime startTime;
        private bool intentionalExit;
        private string connectingSteamID;
        private bool inTxn = false;

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
        public bool Log
        {
            get
            {
                return this.data.log;
            }
            set
            {
                this.data.log = value;
            }
        }
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
                //return this.playerCount;
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
        public ValheimServer(string name, int port, string world, string password, bool autostart, bool log)
        {
            this.data.name = name;
            this.data.port = 2456;
            this.data.world = world;
            this.data.password = password;
            this.data.savedir = "";
            this.data.autostart = autostart;
            this.data.log = log;
            //this.processRunning = false;

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

            this.players = new PlayerList();
            this.status = ServerStatus.Stopped;
        }

        public ValheimServer() : this("My Server", 2456, "Dedicated", "secret", false, false)
        {
        }

        public ValheimServer(string name) : this(name, 2456, "Dedicated", "Secret", false, false)
        {

        }
        public string GetLogName()
        {
            string logname = this.Name.Replace(" ", "_");
            //Regex rgx = new Regex("[^a-zA-Z0-9_-]");
            //logname = rgx.Replace(logname, "");
            //return $"{logname}-{this.startTime.ToString("yyyy-MM-dd_HH-mm-ss")}.log";
            return $"{logname}-{this.Port}-{this.World}.log";
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            string msg = e.Data;
            if (msg == null) return;
            //Debug.WriteLine(msg);
            if (this.Log)
            {
                try
                {
                    StreamWriter writer = System.IO.File.AppendText(this.GetLogName());
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
                this.connectingSteamID = null;
                OnFailedPassword(new FailedPasswordEventArgs(this, match.Groups[1].ToString()));
            }

            //Monitor for initiation of new connection
            rx = new Regex(@"Got handshake from client (\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = rx.Match(msg);
            if (match.Success)
            {
                this.connectingSteamID = match.Groups[1].ToString();
            }

            //Monitor for new player connected
            rx = new Regex(@"Got character ZDOID from (.+) : (-?\d+:-?\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = rx.Match(msg);
            if (match.Success)
            {
                if (this.connectingSteamID != null)
                {
                    Player player = new Player(match.Groups[1].ToString(), this.connectingSteamID);
                    this.players.Add(player);
                    this.connectingSteamID = null;
                    OnPlayerConnected(new PlayerEventArgs(this, player));
                }
                else if (match.Groups[2].ToString().Equals("0:0"))
                {
                    foreach (Player player in this.players)
                    {
                        if (player.Name.Equals(match.Groups[1].ToString()))
                        {
                            player.Deaths++;
                            OnPlayerDied(new PlayerEventArgs(this, player));
                            break;
                        }
                    }
                }
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
                        OnPlayerDisconnected(new PlayerEventArgs(this, player));
                        break;
                    }
                }
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
                    OnStarted(new ServerEventArgs(this));
                    //logMessage($"Server {this.Name}: started", LogType.Success);
                }
            }

            //Monitor for server fails to start
            rx = new Regex(@"GameServer.Init\(\) failed", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = rx.Match(msg);
            if (match.Success)
            {
                this.status = ServerStatus.Stopping;
                OnStartFailed(new ServerEventArgs(this));
                logMessage($"Server {this.Name} failed to start. Maybe try a different port", LogType.Error);
            }

            //Monitor for random events
            rx = new Regex(@"Random event set:([a-zA-Z0-9_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            match = rx.Match(msg);
            if (match.Success)
            {
                OnRandomServerEvent(new RandomServerEventArgs(this, match.Groups[1].ToString()));
            }
        }

        public void Start()
        {
            if (this.Running)
            {
                logMessage($"Server {this.Name} cannot start since it is already running.", LogType.Error);
                return;
            }
            string saveDir = this.SaveDir;
            if (saveDir == null || saveDir.Length == 0)
            {
                saveDir = defaultSaveDir;
            }
            string serverpath = Properties.Settings.Default.ServerFilePath;
            if (!File.Exists(serverpath))
            {
                logMessage($"Server {this.Name} cannot start because the server executable does not exist at ({Properties.Settings.Default.ServerFilePath}) does not contain {this.serverExe}. Please update the server executable path in the app settings.", LogType.Error);
                return;
            }
            string arguments = $"-nographics -batchmode -name \"{this.Name}\" -port {this.Port} -world \"{this.World}\" -password \"{this.Password}\"";
            if (!saveDir.Equals(defaultSaveDir))
            {
                arguments += $" -savedir \"{this.SaveDir}\"";
            }
            if (testMode)
            {
                serverpath = "tracert.exe";
                arguments = "google.com";
            }
            this.intentionalExit = false;
            new Thread(() =>
            {
                if (this.Log)
                {
                    System.IO.File.WriteAllText(this.GetLogName(),"");
                }
                this.startTime = DateTime.Now;
                this.status = ServerStatus.Starting;
                OnStarting(new ServerEventArgs(this));
                this.process.StartInfo.FileName = serverpath;
                this.process.StartInfo.Arguments = arguments;
                Thread.CurrentThread.IsBackground = true;
                //this.processRunning = true;
                this.process.Refresh();
                this.process.Start();
                this.process.BeginOutputReadLine();
                this.process.WaitForExit();
            }).Start();
        }

        public void Stop()
        {
            if (!this.Running)
            {
                //throw new Exception("This server is not running.");
                logMessage($"Server {this.Name} cannot stop since it is not running.", LogType.Error);
                return;
            }
            new Thread(() =>
            {
                if (AttachConsole((uint)this.process.Id))
                {
                    this.status = ServerStatus.Stopping;
                    SetConsoleCtrlHandler(null, true);
                    GenerateConsoleCtrlEvent(ConsoleCtrlEvent.CTRL_C, 0);
                    FreeConsole();
                    this.process.WaitForExit(2000);
                    SetConsoleCtrlHandler(null, false);
                    this.intentionalExit = true;
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
            this.status = ServerStatus.Stopped;
            this.process.CancelOutputRead();
            //this.processRunning = false;
            this.players.Clear();
            OnServerExited(new ServerExitedEventArgs(this, this.process.ExitCode, this.intentionalExit));
        }
        private void logMessage(string message, LogType logtype)
        {
            OnLogMessage(new ServerLogMessageEventArgs(this, message, logtype));
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
                if (backupData.name != data.name)
                {
                    OnUpdated(new UpdatedEventArgs("Name"));
                }
                if (backupData.port!= data.port)
                {
                    OnUpdated(new UpdatedEventArgs("Port"));
                }
                if (backupData.world != data.world)
                {
                    OnUpdated(new UpdatedEventArgs("World"));
                }
                if (backupData.password != data.password)
                {
                    OnUpdated(new UpdatedEventArgs("Password"));
                }
                if (backupData.savedir != data.savedir)
                {
                    OnUpdated(new UpdatedEventArgs("SaveDir"));
                }
                if (backupData.autostart != data.autostart)
                {
                    OnUpdated(new UpdatedEventArgs("Autostart"));
                }
                if (backupData.log != data.log)
                {
                    OnUpdated(new UpdatedEventArgs("Log"));
                }
                backupData = new ServerData();
                inTxn = false;
            }
        }

        int IComparable.CompareTo(object obj)
        {
            ValheimServer vs = (ValheimServer)obj;
            int nameCompare = String.Compare(this.Name, vs.Name);
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
            EventHandler<FailedPasswordEventArgs> handler = FailedPassword;
            if (null != handler) handler(this, args);
        }
        private void OnPlayerConnected(PlayerEventArgs args)
        {
            EventHandler<PlayerEventArgs> handler = PlayerConnected;
            if (null != handler) handler(this, args);
        }
        private void OnPlayerDisconnected(PlayerEventArgs args)
        {
            EventHandler<PlayerEventArgs> handler = PlayerDisconnected;
            if (null != handler) handler(this, args);
        }
        private void OnPlayerDied(PlayerEventArgs args)
        {
            EventHandler<PlayerEventArgs> handler = PlayerDied;
            if (null != handler) handler(this, args);
        }
        private void OnServerExited(ServerExitedEventArgs args)
        {
            EventHandler<ServerExitedEventArgs> handler = Exited;
            if (null != handler) handler(this, args);
        }
        private void OnRandomServerEvent(RandomServerEventArgs args)
        {
            EventHandler<RandomServerEventArgs> handler = RandomServerEvent;
            if (null != handler) handler(this, args);
        }
        private void OnLogMessage(ServerLogMessageEventArgs args)
        {
            EventHandler<ServerLogMessageEventArgs> handler = LogMessage;
            if (null != handler) handler(this, args);
        }
        private void OnStarting(ServerEventArgs args)
        {
            EventHandler<ServerEventArgs> handler = Starting;
            if (null != handler) handler(this, args);
        }
        private void OnStarted(ServerEventArgs args)
        {
            EventHandler<ServerEventArgs> handler = Started;
            if (null != handler) handler(this, args);
        }
        private void OnStartFailed(ServerEventArgs args)
        {
            EventHandler<ServerEventArgs> handler = StartFailed;
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
    }

        public class UpdatedEventArgs : EventArgs
    {
        private readonly string _fieldName;

        public UpdatedEventArgs(string fieldName)
        {
            _fieldName = fieldName;
        }

        public string FieldName
        {
            get { return _fieldName; }
        }
    }

    public class ServerEventArgs : EventArgs
    {
        private readonly ValheimServer _server;

        public ServerEventArgs(ValheimServer server)
        {
            _server = server;
        }

        public ValheimServer Server
        {
            get { return _server; }
        }
    }

    public class FailedPasswordEventArgs : ServerEventArgs
    {
        private readonly string _steamid;

        public FailedPasswordEventArgs(ValheimServer server, string steamid) : base(server)
        {
            _steamid = steamid;
        }
        public string SteamID
        {
            get { return _steamid; }
        }
    }
    public class PlayerEventArgs : ServerEventArgs
    {
        private readonly Player _player;

        public PlayerEventArgs(ValheimServer server, Player player) : base(server)
        {
            _player = player;
        }
        public Player Player
        {
            get { return _player; }
        }
    }
    public class ServerExitedEventArgs : ServerEventArgs
    {
        private readonly int _exitcode;
        private readonly bool _intentionalexit;

        public ServerExitedEventArgs(ValheimServer server, int exitcode, bool intentionalexit) : base(server)
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
    public class RandomServerEventArgs : ServerEventArgs
    {
        private readonly string _eventname;

        public RandomServerEventArgs(ValheimServer server, string eventname) : base(server)
        {
            _eventname = eventname;
        }
        public string EventName
        {
            get { return _eventname; }
        }
    }
    public class ServerLogMessageEventArgs : LogMessageEventArgs
    {
        private readonly ValheimServer _server;
        public ServerLogMessageEventArgs(ValheimServer server, string message, LogType logtype) : base(message, logtype)
        {
            _server = server;
        }
        public ServerLogMessageEventArgs(ValheimServer server, string message) : this(server, message, LogType.Normal) { }

        public ValheimServer Server
        {
            get { return _server; }
        }
    }
}
