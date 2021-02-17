using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Diagnostics;
using System.Threading;
using ModernWpf;
using System.Net;
using System.Text.RegularExpressions;

namespace ValheimServerWarden
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private List<ValheimServer> servers;
        private bool suppressLog = false;
        private bool editing = false;
        private Color defaultTextColor;
        private System.Windows.Forms.NotifyIcon notifyIcon;
        private WindowState storedWindowState;
        private DateTime lastUpdateCheck;
        private List<ServerDetailsWindow> serverDetailWindows;
        private List<ServerLogWindow> serverLogWindows;
        private bool skipCellEditEnding;
        private string ServerJsonPath
        {
            get
            {
                return "valheim_servers.json";
            }
        }
        public MainWindow()
        {
            InitializeComponent();
            if (Properties.Settings.Default.UpgradeRequired)
            {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpgradeRequired = false;
                Properties.Settings.Default.Save();
            }
            defaultTextColor = ((SolidColorBrush)this.Foreground).Color;
            if (Properties.Settings.Default.AppTheme.Equals("Dark"))
            {
                radThemeDark.IsChecked = true;
            }
            else
            {
                radThemeLight.IsChecked = true;
                radThemeLight_Checked(null, null);
            }
            txtLog.IsReadOnly = true;
            txtLog.Document.Blocks.Clear();
            logMessage($"Version {typeof(MainWindow).Assembly.GetName().Version}");
            CheckServerPath();
            txtServerPath.Text = Properties.Settings.Default.ServerFilePath;
            chkAutoCheckUpdate.IsChecked = Properties.Settings.Default.AutoCheckUpdate;
            if (Properties.Settings.Default.AutoCheckUpdate)
            {
                checkForUpdate();
            }
            Console.CancelKeyPress += Console_CancelKeyPress;
            dgServers.ContextMenuOpening += DgServers_ContextMenuOpening;
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.BalloonTipText = "VSW has been minimized. Click the tray icon to restore.";
            notifyIcon.BalloonTipClicked += NotifyIcon_Click;
            notifyIcon.Text = "Valheim Server Warden";
            this.notifyIcon.Icon = ValheimServerWarden.Properties.Resources.vsw2;
            notifyIcon.Click += NotifyIcon_Click;
            storedWindowState = WindowState.Normal;

            servers = new List<ValheimServer>();
            if (File.Exists(this.ServerJsonPath))
            {
                ValheimServer[] savedServers = JsonSerializer.Deserialize<ValheimServer[]>(File.ReadAllText(this.ServerJsonPath));
                foreach (ValheimServer s in savedServers)
                {
                    attachServerEventListeners(s);
                    servers.Add(s);
                    if (s.Autostart)
                    {
                        StartServer(s);
                    }
                }
            }
            dgServers.ItemsSource = servers;
            RefreshDataGrid();
            serverDetailWindows = new List<ServerDetailsWindow>();
            serverLogWindows = new List<ServerLogWindow>();
            skipCellEditEnding = false;
        }

        private void NotifyIcon_Click(object sender, EventArgs e)
        {
            Show();
            WindowState = storedWindowState;
        }

        private void DgServers_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try
            {
                if (dgServers.SelectedIndex == -1)
                {
                    serversMenuStart.IsEnabled = false;
                    serversMenuStart.Icon = FindResource("StartGrey");
                    serversMenuStop.IsEnabled = false;
                    serversMenuStop.Icon = FindResource("StopGrey");
                    serversMenuRemove.IsEnabled = false;
                    serversMenuRemove.Icon = FindResource("RemoveGrey");
                    serversMenuDetails.IsEnabled = false;
                    serversMenuLog.IsEnabled = false;
                    return;
                }
                ValheimServer server = ((ValheimServer)dgServers.SelectedItem);
                serversMenuDetails.IsEnabled = true;
                serversMenuLog.IsEnabled = (File.Exists(server.GetLogName()));
                if (server.Running)
                {
                    serversMenuStart.IsEnabled = false;
                    serversMenuStart.Icon = FindResource("StartGrey");
                    serversMenuStop.IsEnabled = true;
                    serversMenuStop.Icon = FindResource("Stop");
                    serversMenuRemove.IsEnabled = false;
                    serversMenuRemove.Icon = FindResource("RemoveGrey");
                }
                else
                {
                    serversMenuStart.IsEnabled = true;
                    serversMenuStart.Icon = FindResource("Start");
                    serversMenuStop.IsEnabled = false;
                    serversMenuStop.Icon = FindResource("StopGrey");
                    serversMenuRemove.IsEnabled = true;
                    serversMenuRemove.Icon = FindResource("Remove");
                }
            }
            catch (Exception ex)
            {
                logMessage($"Error opening context menu: {ex.Message}", LogType.Error);
            }
        }

        private void CheckServerPath()
        {
            string path = Properties.Settings.Default.ServerFilePath;
            bool searchNeeded = true;
            if (path.Length > 0 && File.Exists(path))
            {
                searchNeeded = false;
            }
            if (searchNeeded)
            {
                logMessage("Valid path for Valheim dedicated server not set.");
                string filePath = @"Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\valheim_server.exe";
                DriveInfo[] drives = DriveInfo.GetDrives();
                foreach (DriveInfo drive in drives)
                {
                    string testpath = $@"{drive.Name}{filePath}";
                    if (File.Exists(testpath))
                    {
                        logMessage($"Dedicated server path found at {drive.Name}{filePath}");
                        Properties.Settings.Default.ServerFilePath = drive.Name+filePath;
                        Properties.Settings.Default.Save();
                        return;
                    }
                }
                logMessage("Valid path for dedicated server not found. Please set manually in preferences.");
            }
        }
        private void btnServerPath_Click(object sender, RoutedEventArgs e)
        {
            string filepath = txtServerPath.Text;
            System.Windows.Forms.OpenFileDialog openFileDialog = new System.Windows.Forms.OpenFileDialog();
            openFileDialog.CheckFileExists = true;
            if (File.Exists(filepath))
            {
                openFileDialog.InitialDirectory = (new FileInfo(filepath)).DirectoryName;
            }
            openFileDialog.Filter = "Server executable|valheim_server.exe";
            openFileDialog.Title = "Select where valheim_server.exe is installed.";
            System.Windows.Forms.DialogResult result = openFileDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string fileName = openFileDialog.FileName;
                if (fileName.Equals(txtServerPath.Text))
                {
                    return;
                }
                if (!File.Exists(fileName))
                {
                    MessageBox.Show("Please select the location of valheim_server.exe.",
                                     "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                    return;
                }
                txtServerPath.Text = fileName;
                Properties.Settings.Default.ServerFilePath = fileName;
                Properties.Settings.Default.Save();
            }
        }

        private void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Debug.WriteLine("Console_CancelKeyPress");
            if (e.SpecialKey == ConsoleSpecialKey.ControlC)
                Debug.WriteLine("Control c event!");
                e.Cancel = true;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            SaveServers();
            notifyIcon.Dispose();
            notifyIcon = null;
            foreach (ValheimServer server in servers)
            {
                if (server.Status == ValheimServer.ServerStatus.Running || server.Status == ValheimServer.ServerStatus.Starting)
                {
                    server.Stop();
                }
            }
            for (int i=0; i < this.serverDetailWindows.Count; i++)
            {
                this.serverDetailWindows[i].Close();
            }
            for (int i = 0; i < this.serverLogWindows.Count; i++)
            {
                this.serverLogWindows[i].Close();
            }
        }

        private void SaveServers()
        {
            try
            {
                File.WriteAllTextAsync(this.ServerJsonPath, JsonSerializer.Serialize(servers));
            }
            catch (Exception ex)
            {
                logMessage($"Error writing servers to json: {ex.Message}", LogType.Error);
            }
        }
        private void attachServerEventListeners(ValheimServer server)
        {
            try
            {
                //server.OutputDataReceived += Server_OutputDataReceived;
                //server.ErrorDataReceived += Server_OutputDataReceived;
                server.LogMessage += Server_LogMessage;
                server.Exited += Server_Exited;
                //server.UpdatedPlayers += Server_UpdatedPlayers;
                server.Started += Server_Started;
                server.FailedPassword += Server_FailedPassword;
                server.PlayerConnected += Server_PlayerConnected;
                server.PlayerDisconnected += Server_PlayerDisconnected;
                server.PlayerDied += Server_PlayerDied;
                server.RandomServerEvent += Server_RandomServerEvent;
            }
            catch (Exception ex)
            {
                logMessage($"Error attaching event listeners: {ex.Message}", LogType.Error);
            }
        }

        private void Server_Started(object sender, ServerEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    logMessage($"Server {e.Server.Name} started.", LogType.Success);
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to server started event: {ex.Message}", LogType.Error);
            }
        }

        private void Server_LogMessage(object sender, ServerLogMessageEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    //logMessage($"Server {e.Server.Name}: {e.Message}", e.LogType);
                    logMessage(e.Message, e.LogType);
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to ServerLogMessage event: {ex.Message}", LogType.Error);
            }
        }

        private void Server_RandomServerEvent(object sender, RandomServerEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    logMessage($"Server {e.Server.Name}: random event {e.EventName}.");
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to random server event: {ex.Message}", LogType.Error);
            }
        }

        private void Server_PlayerDisconnected(object sender, PlayerEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    logMessage($"Server {e.Server.Name}: player {e.Player.Name} ({e.Player.SteamID}) disconnected");
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to PlayerDisconnected event: {ex.Message}",LogType.Error);
            }
        }

        private void Server_PlayerConnected(object sender, PlayerEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    logMessage($"Server {e.Server.Name}: player {e.Player.Name} ({e.Player.SteamID}) connected");
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to PlayerConnected event: {ex.Message}", LogType.Error);
            }
        }

        private void Server_PlayerDied(object sender, PlayerEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    logMessage($"Server {e.Server.Name}: player {e.Player.Name} ({e.Player.SteamID}) died");
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to PlayerDied event: {ex.Message}", LogType.Error);
            }
        }

        private void Server_FailedPassword(object sender, FailedPasswordEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    logMessage($"Server {e.Server.Name}: failed password attempt for steamid {e.SteamID}.");
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to FailedPassword event: {ex.Message}", LogType.Error);
            }
        }

        private void Server_UpdatedPlayers(object sender, ServerEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    logMessage($"Server {e.Server.Name}: updated player count to {e.Server.Players}");
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to UpdatedPlayers event: {ex.Message}", LogType.Error);
            }
        }

        private void serversMenuAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ValheimServer s = new ValheimServer();
                attachServerEventListeners(s);
                servers.Add(s);
                RefreshDataGrid();
                dgServers.SelectedItem = s;
                dgServers.BeginEdit();
            }
            catch (Exception ex)
            {
                logMessage($"Error adding new server: {ex.Message}", LogType.Error);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // setting cancel to true will cancel the close request
            // so the application is not closed
            /*e.Cancel = true;

            this.Hide();

            base.OnClosing(e);*/
            try
            {
                foreach (ValheimServer server in servers)
                {
                    if (server.Running)
                    {
                        logMessage($"Server {server.Name} is still running. Please stop all servers before exiting.", LogType.Error);
                        e.Cancel = true;
                    }
                }
                if (!e.Cancel) SaveServers();
            }
            catch (Exception ex) {
                logMessage($"Error while closing: {ex.Message}", LogType.Error);
            }
        }

        private void dgServers_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (skipCellEditEnding) return;
            try
            {
                if (e.Column.Header.Equals("Name"))
                {
                    ValheimServer server = (ValheimServer)e.Row.Item;
                    string newName = ((TextBox)e.EditingElement).Text;
                    foreach (ValheimServer s in servers)
                    {
                        if (server != s && s.Name.Equals(newName))
                        {
                            logMessage($"A server named {newName} already exists. Please choose another name.", LogType.Error);
                            e.Cancel = true;
                            return;
                        }
                    }
                }
                //force the edit to end so the dataGrid can get refreshed again. if there's only one item in the datagrid, it never leaves editing.
                tabsMain.Focus();
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to CellEditEnding event: {ex.Message}", LogType.Error);
            }
        }

        private void dgServers_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            try
            {
                editing = false;
                //RefreshDataGrid();
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to RowEditEnding event: {ex.Message}", LogType.Error);
            }
        }

        private void StartServer(ValheimServer server)
        {
            try
            {
                if (server == null) return;
                if (server.Running)
                {
                    logMessage($"Server {server.Name} is alread running.", LogType.Error);
                    return;
                }
                foreach (ValheimServer s in servers)
                {
                    if (s.Running)
                    {
                        IEnumerable<int> range = Enumerable.Range(s.Port, 3);
                        if (range.Contains(server.Port) || range.Contains(server.Port + 1) || range.Contains(server.Port + 2))
                        {
                            logMessage($"Server {s.Name} is already running on conflicting port {s.Port}.", LogType.Error);
                            return;
                        }
                        if (s.World.Equals(server.World))
                        {
                            logMessage($"Server {s.Name} is already running using world {s.World}.", LogType.Error);
                            return;
                        }
                    }
                }
                server.Start();
                RefreshDataGrid();
                logMessage($"Server {server.Name} starting...", LogType.Normal);
            }
            catch (Exception ex)
            {
                logMessage($"Error starting server: {ex.Message}", LogType.Error);
            }
        }

        private void serversMenuStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ValheimServer server = ((ValheimServer)dgServers.SelectedItem);
                StartServer(server);
            }
            catch (Exception ex)
            {
                logMessage($"Error starting server from context menu: {ex.Message}",LogType.Error);
            }
        }

        private void serversMenuStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ValheimServer server = ((ValheimServer)dgServers.SelectedItem);
                StopServer(server);
            }
            catch (Exception ex)
            {
                logMessage($"Error stopping server: {ex.Message}", LogType.Error);
            }
        }

        private void StopServer(ValheimServer server)
        {
            try
            {
                if (server == null) return;
                if (!server.Running)
                {
                    return;
                }
                server.Stop();
                RefreshDataGrid();
            }
            catch (Exception ex)
            {
                logMessage($"Error stopping server: {ex.Message}", LogType.Error);
            }
        }

        private void Server_Exited(object sender, ServerExitedEventArgs e)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    if (e.ExitCode == 0 || e.ExitCode == -1073741510)
                    {
                        logMessage($"Server {e.Server.Name} stopped.");
                    }
                    else
                    {
                        logMessage($"Server {e.Server.Name}: exited with code {e.ExitCode}.", LogType.Error);
                    }
                    RefreshDataGrid();
                    if (e.Server.Autostart && e.ExitCode != 0 && e.ExitCode != -1073741510 && !e.IntentionalExit && !e.Server.Running)
                    {
                        e.Server.Start();
                    }
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error handling server exited event: {ex.Message}", LogType.Error);
            }
        }

        private void Server_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            try
            {
                Debug.WriteLine(e.Data);
                this.Dispatcher.Invoke(() =>
                {
                    logMessage(e.Data);
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error receiving output from server: {ex.Message}", LogType.Error);
            }
        }

        public void logMessage(string msg)
        {
            logMessage(msg, defaultTextColor);
        }

        public void logMessage(string msg, LogType lt)
        {
            Color color = defaultTextColor;//Colors.White;
            if (lt == LogType.Success)
            {
                color = Color.FromRgb(50, 200, 50);
            }
            else if (lt == LogType.Error)
            {
                color = Color.FromRgb(200, 50, 50);
            }
            logMessage(msg, color);
        }

        public void logMessage(string msg, Color color)
        {
            if (!suppressLog)
            {
                Run run = new Run(DateTime.Now.ToString() + ": " + msg);
                run.Foreground = new SolidColorBrush(color);
                Paragraph paragraph = new Paragraph(run);
                paragraph.Margin = new Thickness(0);
                txtLog.Document.Blocks.Add(paragraph);
                if (msg.Contains('\n'))
                {
                    lblLastMessage.Content = msg.Split('\n')[0];
                }
                else
                {
                    lblLastMessage.Content = msg;
                }
                lblLastMessage.Foreground = new SolidColorBrush(color);
                if (color.Equals(defaultTextColor))
                {
                    lblLastMessage.FontWeight = FontWeights.Normal;
                }
                else
                {
                    lblLastMessage.FontWeight = FontWeights.Bold;
                }
            }
            /*if (Properties.Settings.Default.CreateLogFile)
            {
                StreamWriter writer = System.IO.File.AppendText("log.txt");
                writer.WriteLine(DateTime.Now.ToString() + ": " + msg);
                writer.Close();
            }*/
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                {
                    Hide();
                    if (notifyIcon != null)
                    {
                        if (Properties.Settings.Default.ShowMinimizeMessage)
                        {
                            notifyIcon.ShowBalloonTip(2000);
                            Properties.Settings.Default.ShowMinimizeMessage = false;
                            Properties.Settings.Default.Save();
                        }
                    }
                }
            }
            else
            {
                storedWindowState = WindowState;
            }
        }


        private void dgServers_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column.Header.Equals("SaveDir") || e.Column.Header.Equals("Players") || e.Column.Header.Equals("Running") || e.Column.Header.Equals("StartTime") || e.Column.Header.Equals("PlayerList"))
            {
                e.Cancel = true;
            }
            else if (e.Column.Header.Equals("PlayerCount"))
            {
                e.Column.Header = "# Players";
            }
        }

        private void menuEditPreferences_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                tabsMain.SelectedIndex = 1;
            }
            catch (Exception ex)
            {
                logMessage($"Error opening preferences: {ex.Message}", LogType.Error);
            }
        }

        private void dgServers_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            editing = true;
        }

        private void RefreshDataGrid()
        {
            this.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (!editing)
                    {
                        dgServers.Items.Refresh();
                        foreach (DataGridColumn column in dgServers.Columns)
                        {
                            column.Width = DataGridLength.Auto;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logMessage($"Error refreshing server grid: {ex.Message}", LogType.Error);
                }
            });
            
        }

        private void serversMenuRemove_Click(object sender, RoutedEventArgs e)
        {
            ValheimServer server = (ValheimServer)dgServers.SelectedItem;
            var confirmResult = MessageBox.Show($"Are you sure you want to remove {server.Name}?",
                                     "Remove Server", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (confirmResult == MessageBoxResult.Yes)
            {

                servers.Remove(server);
                RefreshDataGrid();
            }
        }

        private void menuFileExit_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void radThemeDark_Checked(object sender, RoutedEventArgs e)
        {
            if (!Properties.Settings.Default.AppTheme.Equals("Dark"))
            {
                Properties.Settings.Default.AppTheme = "Dark";
                Properties.Settings.Default.Save();
            }
            ChangeTheme(ApplicationTheme.Dark);
        }

        private void radThemeLight_Checked(object sender, RoutedEventArgs e)
        {
            if (!Properties.Settings.Default.AppTheme.Equals("Light"))
            {
                Properties.Settings.Default.AppTheme = "Light";
                Properties.Settings.Default.Save();
            }
            ChangeTheme(ApplicationTheme.Light);
        }

        private void ChangeTheme(ApplicationTheme theme)
        {
            ThemeManager.Current.ApplicationTheme = theme;
            //ThemeManager.Current.AccentColor = Colors.Orange;
            Color oldColor = defaultTextColor;
            defaultTextColor = ((SolidColorBrush)this.Foreground).Color;
            if (((SolidColorBrush)lblLastMessage.Foreground).Color.Equals(oldColor))
            {
                lblLastMessage.Foreground = new SolidColorBrush(defaultTextColor);
            }
        }

        private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            CheckTrayIcon();
        }

        void CheckTrayIcon()
        {
            ShowTrayIcon(!IsVisible);
        }
        void ShowTrayIcon(bool show)
        {
            if (notifyIcon != null)
            {
                notifyIcon.Visible = show;
            }
        }

        private void chkAutoCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            bool newValue = chkAutoCheckUpdate.IsChecked.HasValue ? chkAutoCheckUpdate.IsChecked.Value : false;
            Properties.Settings.Default.AutoCheckUpdate = newValue;
            Properties.Settings.Default.Save();
        }

        private void checkForUpdate()
        {
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;

                try
                {
                    WebClient client = new WebClient();
                    string source = client.DownloadString("https://github.com/Razzmatazzz/ValheimServerWarden/releases/latest");
                    string title = Regex.Match(source, @"\<title\b[^>]*\>\s*(?<Title>[\s\S]*?)\</title\>", RegexOptions.IgnoreCase).Groups["Title"].Value;
                    string remoteVer = Regex.Match(source, @"Valheim Server Warden (?<Version>([\d.]+)?)", RegexOptions.IgnoreCase).Groups["Version"].Value;

                    Version remoteVersion = new Version(remoteVer);
                    Version localVersion = typeof(MainWindow).Assembly.GetName().Version;

                    this.Dispatcher.Invoke(() =>
                    {
                        //do stuff in here with the interface
                        if (localVersion.CompareTo(remoteVersion) == -1)
                        {
                            var confirmResult = MessageBox.Show("There is a new version available. Would you like to open the download page?",
                                     "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                            if (confirmResult == MessageBoxResult.Yes)
                            {
                                Process.Start("https://github.com/Razzmatazzz/ValheimServerWarden/releases/latest");
                                System.Environment.Exit(1);
                            }
                        }
                        else
                        {
                            //logMessage("No new version found.");
                        }
                    });
                }
                catch (Exception ex)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        logMessage($"Error checking for new version: {ex.Message}", LogType.Error);
                    });
                }
            }).Start();
            lastUpdateCheck = DateTime.Now;
        }

        private void btnUpdateCheck_Click(object sender, RoutedEventArgs e)
        {
            if (lastUpdateCheck.AddMinutes(10) < DateTime.Now)
            {
                checkForUpdate();
            }
            else
            {
                TimeSpan span = (lastUpdateCheck.AddMinutes(10) - DateTime.Now);
                logMessage($"Please wait {span.Minutes} minutes, {span.Seconds} seconds before checking for update.");
            }
        }

        private void serversMenuDetails_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ValheimServer server = ((ValheimServer)dgServers.SelectedItem);
                if (server == null) return;
                foreach (ServerDetailsWindow win in this.serverDetailWindows)
                {
                    if (win.Server == server)
                    {
                        win.WindowState = WindowState.Normal;
                        return;
                    }
                }
                ServerDetailsWindow window = new ServerDetailsWindow(server);
                this.serverDetailWindows.Add(window);
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.Closed += ((object sender, EventArgs e) =>
                {
                    this.serverDetailWindows.Remove(window);
                });
                window.Starting += ((object sender, ServerEventArgs e) =>
                {
                    this.StartServer(e.Server);
                });
                window.Stopping += ((object sender, ServerEventArgs e) =>
                {
                    this.StopServer(e.Server);
                });
                window.ShowLog += ((object sender, ServerEventArgs e) =>
                {
                    ShowServerLog(e.Server);
                });
                window.EditedServer += ((object sender, ServerEventArgs e) =>
                {
                    RefreshDataGrid();
                });
                window.Show();
            }
            catch (Exception ex)
            {
                logMessage($"Error showing server details: {ex.Message}", LogType.Error);
            }
        }

        private void ShowServerLog(ValheimServer server)
        {
            try
            {
                foreach (ServerLogWindow win in this.serverLogWindows)
                {
                    if (win.Server == server)
                    {
                        win.WindowState = WindowState.Normal;
                        win.LoadLogText();
                        return;
                    }
                }
                ServerLogWindow window = new ServerLogWindow(server);
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                this.serverLogWindows.Add(window);
                window.Closed += ((object sender, EventArgs e) =>
                {
                    this.serverLogWindows.Remove(window);
                });
                window.Show();
            }
            catch (Exception ex)
            {
                logMessage($"Error showing {server.Name} server log: {ex.Message}", LogType.Error);
            }
        }

        private void serversMenuLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ValheimServer server = ((ValheimServer)dgServers.SelectedItem);
                if (server == null) return;
                if (File.Exists(server.GetLogName()))
                {
                    ShowServerLog(server);
                }
            }
            catch (Exception ex)
            {
                logMessage($"Error showing server log: {ex.Message}", LogType.Error);
            }
        }
    }
    public enum LogType
    {
        Normal,
        Success,
        Error
    }
    public class LogMessageEventArgs : EventArgs
    {
        private readonly string _message;
        private readonly LogType _logtype;
        public LogMessageEventArgs(string message, LogType logtype)
        {
            _message = message;
            _logtype = logtype;
        }
        public LogMessageEventArgs(string message) : this(message, LogType.Normal) { }

        public string Message
        {
            get { return _message; }
        }
        public LogType LogType
        {
            get { return _logtype; }
        }
    }
}
