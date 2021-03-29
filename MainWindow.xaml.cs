using System;
using System.Collections.Generic;
using System.Collections.Specialized;
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
using RazzTools;
using GitHub;
using System.Data;

namespace ValheimServerWarden
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //private List<ValheimServer> servers;
        private bool suppressLog = false;
        private bool editing = false;
        private System.Windows.Forms.NotifyIcon notifyIcon;
        private WindowState storedWindowState;
        private DateTime lastUpdateCheck;
        private List<ServerDetailsWindow> serverDetailWindows;
        private List<ServerLogWindow> serverLogWindows;
        private List<LogEntry> logEntries;
        private FileSystemWatcher shutdownWatcher;
        private string ServerJsonPath
        {
            get
            {
                return "valheim_servers.json";
            }
        }
        private string LogPath
        {
            get
            {
                return "vswlog.txt";
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
            if (Properties.Settings.Default.MainWindowWidth > 0)
            {
                Width = Properties.Settings.Default.MainWindowWidth;
            }
            if (Properties.Settings.Default.MainWindowHeight > 0)
            {
                Height = Properties.Settings.Default.MainWindowHeight;
            }
            LogEntry.NormalColor = ((SolidColorBrush)this.Foreground).Color;
            logEntries = new List<LogEntry>();
            serverDetailWindows = new List<ServerDetailsWindow>();
            serverLogWindows = new List<ServerLogWindow>();
            if (Properties.Settings.Default.AppTheme.Equals("Dark"))
            {
                radThemeDark.IsChecked = true;
            }
            else
            {
                radThemeLight.IsChecked = true;
                radThemeLight_Checked(null, null);
            }

            if (Properties.Settings.Default.WriteAppLog)
            {
                System.IO.File.WriteAllText(LogPath, "");
            }
            txtLog.Document.Blocks.Clear();
            logMessage($"Version {typeof(MainWindow).Assembly.GetName().Version}");
            CheckServerPath();
            txtServerPath.Text = Properties.Settings.Default.ServerFilePath;
            txtSteamCmdPath.Text = Properties.Settings.Default.SteamCMDPath;
            foreach (var i in Enum.GetValues(typeof(ValheimServer.ServerInstallMethod)))
            {
                cmbServerType.Items.Add(Enum.GetName(typeof(ValheimServer.ServerInstallMethod), i));
            }
            cmbServerType.SelectedIndex = Properties.Settings.Default.ServerInstallType;
            chkAutoCheckUpdate.IsChecked = Properties.Settings.Default.AutoCheckUpdate;
            chkLog.IsChecked = Properties.Settings.Default.WriteAppLog;
            chkRunningServerCheck.IsChecked = Properties.Settings.Default.RunningServerCheck;
            chkStopOnClose.IsChecked = Properties.Settings.Default.StopOnClose;
            if (Properties.Settings.Default.AutoCheckUpdate)
            {
                checkForUpdate();
            }
            Console.CancelKeyPress += Console_CancelKeyPress;
            //dgServers.ContextMenuOpening += dgServers_ContextMenuOpening;
            notifyIcon = new System.Windows.Forms.NotifyIcon();
            notifyIcon.BalloonTipText = "VSW has been minimized. Click the tray icon to restore.";
            notifyIcon.BalloonTipClicked += NotifyIcon_Click;
            notifyIcon.Text = "Valheim Server Warden";
            this.notifyIcon.Icon = ValheimServerWarden.Properties.Resources.vsw2;
            notifyIcon.MouseClick += NotifyIcon_MouseClick;
            System.Windows.Forms.ContextMenuStrip cm = new System.Windows.Forms.ContextMenuStrip();
            System.Windows.Forms.ToolStripMenuItem menuQuit = new System.Windows.Forms.ToolStripMenuItem();
            menuQuit.Text = "Quit";
            menuQuit.Click += NotifyMenuQuit_Click;
            cm.Items.Add(menuQuit);
            notifyIcon.ContextMenuStrip = cm;
            storedWindowState = WindowState.Normal;

            if (Properties.Settings.Default.RunningServerCheck)
            {
                checkForRunningServers();
            }

            if (File.Exists(this.ServerJsonPath))
            {
                try
                {
                    ValheimServer[] savedServers = JsonSerializer.Deserialize<ValheimServer[]>(File.ReadAllText(this.ServerJsonPath));
                    foreach (ValheimServer s in savedServers)
                    {
                        attachServerEventListeners(s);
                        //servers.Add(s);
                        if (s.Autostart)
                        {
                            s.Start();
                        }
                    }
                }
                catch (Exception ex)
                {
                    logMessage($"Error reading saved servers: {ex.Message}", LogEntryType.Error);
                }
            }
            dgServers.ItemsSource = ValheimServer.Servers;//servers;
            RefreshDataGrid();

            if (File.Exists("shutdown.now")) File.Delete("shutdown.now");
            shutdownWatcher = new();
            shutdownWatcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName;
            shutdownWatcher.Filter = "shutdown.now";
            shutdownWatcher.Path = System.AppDomain.CurrentDomain.BaseDirectory;
            shutdownWatcher.EnableRaisingEvents = true;
            shutdownWatcher.Created += ShutdownWatcher_Created;
            shutdownWatcher.Renamed += ShutdownWatcher_Created;
        }

        private void ShutdownWatcher_Created(object sender, FileSystemEventArgs e)
        {
            try
            {
                logMessage("Shutdown file detected, initiating shutdown of server(s).");
                ShutdownAndQuit();
            }
            catch (Exception ex)
            {
                logMessage($"Error while stopping servers for shutdown: {ex.Message}");
            }
        }

        private void ShutdownAndQuit()
        {
            try
            {
                foreach (var server in ValheimServer.Servers)
                {
                    if (server.Status != ValheimServer.ServerStatus.Stopped && server.Status != ValheimServer.ServerStatus.Stopping)
                    {
                        server.Stopped += Server_StoppedShutdownCheck;
                        server.Stop();
                    } else if (server.Status == ValheimServer.ServerStatus.Stopping)
                    {
                        server.Stopped += Server_StoppedShutdownCheck;
                    }
                }
            }
            catch (Exception ex)
            {
                logMessage($"Error while stopping servers for shutdown: {ex.Message}");
            }
        }

        private void Server_StoppedShutdownCheck(object sender, ServerStoppedEventArgs e)
        {
            var allStopped = true;
            foreach (var s in ValheimServer.Servers)
            {
                if (s.Status != ValheimServer.ServerStatus.Stopped)
                {
                    allStopped = false;
                    break;
                }
            }
            this.Dispatcher.Invoke(() =>
            {
                if (allStopped) Close();
            });
        }

        private void NotifyMenuQuit_Click(object sender, EventArgs e)
        {
            foreach (var server in ValheimServer.Servers)
            {
                if (server.Running)
                {
                    logMessage($"Stop all running servers before exiting.", LogEntryType.Error);
                    Show();
                    WindowState = storedWindowState;
                    return;
                }
            }
            Close();
        }

        private void NotifyIcon_Click(object sender, EventArgs e)
        {
            Show();
            Activate();
            WindowState = storedWindowState;
        }
        private void NotifyIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                Show();
                Activate();
                WindowState = storedWindowState;
            }
            else
            {
                //context menu?
            }
        }

        private void dgServers_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            try
            {
                ((System.Windows.Media.Animation.Storyboard)FindResource("WorkingStoryboard")).Begin();
                serversMenuStart.Visibility = Visibility.Collapsed;
                serversMenuStop.Visibility = Visibility.Collapsed;
                serversMenuWorking.Visibility = Visibility.Collapsed;
                serversMenuRemove.Visibility = Visibility.Collapsed;
                serversMenuDetails.Visibility = Visibility.Collapsed;
                serversMenuLog.Visibility = Visibility.Collapsed;
                serversmenuSep1.Visibility = Visibility.Collapsed;
                serversmenuSep2.Visibility = Visibility.Collapsed;
                serversmenuSep3.Visibility = Visibility.Collapsed;
                if (dgServers.SelectedIndex == -1)
                {
                    /*serversMenuStart.IsEnabled = false;
                    serversMenuStart.Icon = FindResource("StartGrey");
                    serversMenuStop.IsEnabled = false;
                    serversMenuStop.Icon = FindResource("StopGrey");
                    serversMenuRemove.IsEnabled = false;
                    serversMenuRemove.Icon = FindResource("RemoveGrey");
                    serversMenuDetails.IsEnabled = false;
                    serversMenuLog.IsEnabled = false;*/
                    return;
                }
                ValheimServer server = ((ValheimServer)dgServers.SelectedItem);
                //serversMenuDetails.IsEnabled = true;
                //serversMenuLog.IsEnabled = (File.Exists(server.LogRawName));
                serversMenuDetails.Visibility = Visibility.Visible;
                if (File.Exists(server.LogRawName))
                {
                    serversMenuLog.Visibility = Visibility.Visible;
                }
                else
                {
                    serversMenuLog.Visibility = Visibility.Collapsed;
                }
                serversmenuSep1.Visibility = Visibility.Visible;
                serversmenuSep2.Visibility = Visibility.Visible;
                serversmenuSep3.Visibility = Visibility.Visible;
                if (server.Status == ValheimServer.ServerStatus.Running)
                {
                    /*serversMenuStart.IsEnabled = false;
                    serversMenuStart.Icon = FindResource("StartGrey");
                    serversMenuStop.IsEnabled = true;
                    serversMenuStop.Icon = FindResource("Stop");
                    serversMenuRemove.IsEnabled = false;
                    serversMenuRemove.Icon = FindResource("RemoveGrey");*/
                    serversMenuStart.Visibility = Visibility.Collapsed;
                    serversMenuStop.Visibility = Visibility.Visible;
                    serversMenuRemove.Visibility = Visibility.Collapsed;
                    serversmenuSep2.Visibility = Visibility.Collapsed;
                }
                else if (server.Status == ValheimServer.ServerStatus.Stopped)
                {
                    /*serversMenuStart.IsEnabled = true;
                    serversMenuStart.Icon = FindResource("Start");
                    serversMenuStop.IsEnabled = false;
                    serversMenuStop.Icon = FindResource("StopGrey");
                    serversMenuRemove.IsEnabled = true;
                    serversMenuRemove.Icon = FindResource("Remove");*/
                    serversMenuStart.Visibility = Visibility.Visible;
                    serversMenuStop.Visibility = Visibility.Collapsed;
                    serversMenuRemove.Visibility = Visibility.Visible;
                    serversmenuSep2.Visibility = Visibility.Visible;
                    serversmenuSep3.Visibility = Visibility.Visible;
                }
                else
                {
                    serversMenuStart.Visibility = Visibility.Collapsed;
                    serversMenuStop.Visibility = Visibility.Collapsed;
                    serversMenuWorking.Visibility = Visibility.Visible;
                    if (server.Status == ValheimServer.ServerStatus.Starting)
                    {
                        serversMenuWorking.Header = "Starting...";
                    }
                    else if (server.Status == ValheimServer.ServerStatus.Stopping)
                    {
                        serversMenuWorking.Header = "Stopping...";
                    }
                    else if (server.Status == ValheimServer.ServerStatus.Updating)
                    {
                        serversMenuWorking.Header = "Updating...";
                    }
                    serversMenuRemove.Visibility = Visibility.Collapsed;
                    serversmenuSep2.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                logMessage($"Error opening context menu: {ex.Message}", LogEntryType.Error);
            }
        }

        private void CheckServerPath()
        {
            try
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
                    string steampath = @"Program Files (x86)\Steam\steam.exe";
                    string fullSteamPath = "";
                    string filePath = $@"Program Files (x86)\Steam\steamapps\common\Valheim dedicated server\{ValheimServer.ExecutableName}";
                    bool serverfound = false;
                    DriveInfo[] drives = DriveInfo.GetDrives();
                    foreach (DriveInfo drive in drives)
                    {
                        if (File.Exists($@"{drive.Name}{steampath}"))
                        {
                            fullSteamPath = $@"{drive.Name}{steampath}";
                        }
                        string testpath = $@"{drive.Name}{filePath}";
                        if (File.Exists(testpath))
                        {
                            logMessage($"Dedicated server path found at {drive.Name}{filePath}");
                            Properties.Settings.Default.ServerFilePath = drive.Name + filePath;
                            Properties.Settings.Default.Save();
                            serverfound = true;
                            break;
                        }
                    }
                    if (!serverfound)
                    {
                        if (fullSteamPath != null)
                        {
                            var mmb = new ModernMessageBox(this);
                            mmb.SetButtonText(new NameValueCollection() { { "Yes", "Steam" }, { "No", "SteamCMD" }, { "Cancel", "Manual" } });
                            var confirmResult = mmb.Show("VSW couldn't find the Valheim dedicated server installed, but it found Steam. Do you want to install the dedicated server via Steam, SteamCMD, or manually select your installation location?",
                                         "Install dedicated server?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                            if (confirmResult == MessageBoxResult.Yes)
                            {
                                mmb = new ModernMessageBox(this);
                                mmb.Show("Once the dedicated server finishes installing, restart this app and it will hopefully detect the dedicated server location.",
                                         "Restart Required", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);
                                Process.Start(fullSteamPath, $"-applaunch {ValheimServer.SteamID}");
                                logMessage("Please restart this app once the Valheim dedicated server finishes installing.");
                                Properties.Settings.Default.ServerInstallType = (int)ValheimServer.ServerInstallMethod.Steam;
                                Properties.Settings.Default.Save();
                                return;
                            }
                            else if (confirmResult == MessageBoxResult.No)
                            {
                                var steamCmdWindow = new InstallSteamCmdWindow();
                                steamCmdWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                                if (steamCmdWindow.ShowDialog().GetValueOrDefault())
                                {
                                    logMessage("Valheim dedicated server installed via SteamCMD.");
                                    return;
                                }
                            }
                        }
                        else
                        {
                            var mmb = new ModernMessageBox(this);
                            mmb.SetButtonText(new NameValueCollection() { { "OK", "SteamCMD" }, { "Cancel", "Manual" } });
                            var confirmResult = mmb.Show("VSW couldn't find the Valheim dedicated server installation. Do you want to install the dedicated server via SteamCMD or manually select your installation location?",
                                         "Install dedicated server?", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
                            if (confirmResult == MessageBoxResult.OK)
                            {
                                var steamCmdWindow = new InstallSteamCmdWindow();
                                steamCmdWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                                if (steamCmdWindow.ShowDialog().GetValueOrDefault())
                                {
                                    logMessage("Valheim dedicated server installed via SteamCMD.");
                                    return;
                                }
                            }
                        }
                    }
                    logMessage("Valid path for dedicated server not found. Please set manually in settings.");
                    Properties.Settings.Default.ServerInstallType = (int)ValheimServer.ServerInstallMethod.Manual;
                    Properties.Settings.Default.Save();
                }
            }
            catch (Exception ex)
            {
                logMessage($"Error checking for dedicated server path: {ex.Message}");
            }
        }
        private void btnServerPath_Click(object sender, RoutedEventArgs e)
        {
            var openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            if (txtServerPath.Text != "")
            {
                var serverpath = new FileInfo(txtServerPath.Text).Directory.FullName;
                if (Directory.Exists(serverpath))
                {
                    openFolderDialog.SelectedPath = serverpath;
                }
            }
            openFolderDialog.UseDescriptionForTitle = true;
            openFolderDialog.Description = "Default server installation folder";
            var result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                var folderName = openFolderDialog.SelectedPath;
                /*if (folderName+ "\\valheim_server.exe" == txtServerPath.Text)
                {
                    return;
                }*/
                if (!File.Exists($@"{folderName}\{ValheimServer.ExecutableName}") && cmbServerType.SelectedIndex == (int)ValheimServer.ServerInstallMethod.SteamCMD && File.Exists(Properties.Settings.Default.SteamCMDPath))
                {
                    var mmb = new ModernMessageBox(this);
                    var install = mmb.Show($"{ValheimServer.ExecutableName} was not found in {folderName}, do you want to install it via SteamCMD?",
                                     "Install Valheim dedicated server?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                    if (install == MessageBoxResult.Yes)
                    {
                        try
                        {
                            var process = new Process();
                            process.StartInfo.FileName = Properties.Settings.Default.SteamCMDPath;
                            process.StartInfo.Arguments = $"+login anonymous +force_install_dir \"{folderName}\" +app_update {ValheimServer.SteamID} +quit";
                            //process.EnableRaisingEvents = true;
                            //process.Exited += SteamCmdProcess_Exited;
                            process.Start();
                            process.WaitForExit();
                        }
                        catch (Exception ex)
                        {
                            logMessage($"Error installing dedicated server: {ex.Message}", LogEntryType.Error);
                        }
                    }
                }
                folderName += $@"\{ValheimServer.ExecutableName}";
                txtServerPath.Text = folderName;
                Properties.Settings.Default.ServerFilePath = folderName;
                Properties.Settings.Default.Save();
            }



            /*System.Windows.Forms.OpenFileDialog openFileDialog = new System.Windows.Forms.OpenFileDialog();
            if (txtServerPath.Text.Length > 0)
            {
                string filepath = txtServerPath.Text;
                openFileDialog.CheckFileExists = true;
                if (File.Exists(filepath))
                {
                    openFileDialog.InitialDirectory = (new FileInfo(filepath)).DirectoryName;
                }
            }
            openFileDialog.Filter = "Server executable|valheim_server.exe";
            openFileDialog.Title = "Select where valheim_server.exe is installed";
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
                    var mmb = new ModernMessageBox(this);
                    mmb.Show("Please select the location of valheim_server.exe.",
                                     "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                    return;
                }
                txtServerPath.Text = fileName;
                Properties.Settings.Default.ServerFilePath = fileName;
                Properties.Settings.Default.Save();
            }*/
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
            foreach (ValheimServer server in ValheimServer.Servers)
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
                File.WriteAllTextAsync(this.ServerJsonPath, JsonSerializer.Serialize(ValheimServer.Servers));
            }
            catch (Exception ex)
            {
                logMessage($"Error writing servers to json: {ex.Message}", LogEntryType.Error);
            }
        }
        private void attachServerEventListeners(ValheimServer server)
        {
            try
            {
                //server.OutputDataReceived += Server_OutputDataReceived;
                //server.ErrorDataReceived += Server_OutputDataReceived;
                server.LoggedMessage += ((object sender, LoggedMessageEventArgs e) => {
                    this.Dispatcher.Invoke(() =>
                    {
                        var server = (ValheimServer)sender;
                        logMessage(server.DisplayName+": "+e.LogEntry.Message, e.LogEntry.Type);
                    });
                });
                server.Stopped += Server_Stopped;
                server.Started += Server_Started;
                server.Starting += Server_StartingStopping;
                server.StartFailed += Server_StartFailed;
                server.Stopping += Server_StartingStopping;
                server.StopFailed += Server_StopFailed;
                server.PlayerConnected += Server_PlayerConnected;
                server.PlayerDisconnected += Server_PlayerDisconnected;
            }
            catch (Exception ex)
            {
                logMessage($"Error attaching event listeners: {ex.Message}", LogEntryType.Error);
            }
        }
        private void Server_StartingStopping(object sender, EventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                dgServers.CancelEdit();
                dgServers.IsReadOnly = true;
                RefreshDataGrid();
            });
        }
        private void Server_StopFailed(object sender, ServerErrorEventArgs e)
        {
            try
            {
                var server = (ValheimServer)sender;
                this.Dispatcher.Invoke(() =>
                {
                    dgServers.IsReadOnly = false;
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to server stop failed event: {ex.Message}", LogEntryType.Error);
            }
        }

        private void Server_StartFailed(object sender, ServerErrorEventArgs e)
        {
            try
            {
                var server = (ValheimServer)sender;
                this.Dispatcher.Invoke(() =>
                {
                    dgServers.IsReadOnly = false;
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to server start failed event: {ex.Message}", LogEntryType.Error);
            }
        }

        private void Server_Started(object sender, EventArgs e)
        {
            try
            {
                var server = (ValheimServer)sender;
                this.Dispatcher.Invoke(() =>
                {
                    dgServers.IsReadOnly = false;
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to server started event: {ex.Message}", LogEntryType.Error);
            }
        }
        private void Server_PlayerDisconnected(object sender, PlayerEventArgs e)
        {
            try
            {
                var server = (ValheimServer)sender;
                this.Dispatcher.Invoke(() =>
                {
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to PlayerDisconnected event: {ex.Message}",LogEntryType.Error);
            }
        }

        private void Server_PlayerConnected(object sender, PlayerEventArgs e)
        {
            try
            {
                var server = (ValheimServer)sender;
                this.Dispatcher.Invoke(() =>
                {
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to PlayerConnected event: {ex.Message}", LogEntryType.Error);
            }
        }
        private void serversMenuAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ValheimServer s = new ValheimServer();
                s.InstallMethod = (ValheimServer.ServerInstallMethod)Properties.Settings.Default.ServerInstallType;
                attachServerEventListeners(s);
                //servers.Add(s);
                RefreshDataGrid();
                dgServers.SelectedItem = s;
                dgServers.BeginEdit();
            }
            catch (Exception ex)
            {
                logMessage($"Error adding new server: {ex.Message}", LogEntryType.Error);
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
                foreach (ValheimServer server in ValheimServer.Servers)
                {
                    if (server.Running)
                    {
                        e.Cancel = true;
                        if (Properties.Settings.Default.StopOnClose)
                        {
                            logMessage($"Stopping all servers for app exit.");
                            WindowState = WindowState.Minimized;
                            ShutdownAndQuit();
                        } else
                        {
                            logMessage($"Server {server.DisplayName} is still running. Please stop all servers before exiting.", LogEntryType.Error);
                        }
                    }
                    else
                    {
                        Properties.Settings.Default.MainWindowWidth = Width;
                        Properties.Settings.Default.MainWindowHeight = Height;
                        Properties.Settings.Default.Save();
                    }
                }
                if (!e.Cancel) SaveServers();
            }
            catch (Exception ex) {
                logMessage($"Error while closing: {ex.Message}", LogEntryType.Error);
            }
        }

        private void dgServers_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            try
            {
                ValheimServer server = (ValheimServer)e.Row.Item;
                /*if (e.Column.Header.Equals("Name"))
                {
                    string newName = ((TextBox)e.EditingElement).Text;
                    foreach (ValheimServer s in servers)
                    {
                        if (server != s && s.Name.Equals(newName))
                        {
                            logMessage($"A server named {newName} already exists. Please choose another name.", LogEntryType.Error);
                            e.Cancel = true;
                            return;
                        }
                    }
                }
                else*/ if (e.Column.Header.Equals("Password"))
                {
                    string newPass = ((TextBox)e.EditingElement).Text;
                    if (newPass.Length == 0)
                    {
                        logMessage("Warning: Servers must have passwords unless modded to remove that requirement.");
                    }
                    else if (newPass.Length < 5)
                    {
                        logMessage("Passwords must be at least 5 characters long.", LogEntryType.Error);
                        ((TextBox)e.EditingElement).Text = server.Password;
                        e.Cancel = true;
                        return;
                    }
                    else if (server.World.Contains(newPass)) {
                        logMessage("Your password cannot be contained in your World name.", LogEntryType.Error);
                        ((TextBox)e.EditingElement).Text = server.Password;
                        e.Cancel = true;
                        return;
                    }
                }
                //force the edit to end so the dataGrid can get refreshed again. if there's only one item in the datagrid, it never leaves editing.
                tabsMain.Focus();
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to CellEditEnding event: {ex.Message}", LogEntryType.Error);
            }
        }

        private void dgServers_RowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
        {
            try
            {
                editing = false;
            }
            catch (Exception ex)
            {
                logMessage($"Error responding to RowEditEnding event: {ex.Message}", LogEntryType.Error);
            }
        }

        private void serversMenuStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ValheimServer server = ((ValheimServer)dgServers.SelectedItem);
                server.Start();
            }
            catch (Exception ex)
            {
                logMessage($"Error starting server from context menu: {ex.Message}",LogEntryType.Error);
            }
        }

        private void serversMenuStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ValheimServer server = ((ValheimServer)dgServers.SelectedItem);
                server.Stop();
            }
            catch (Exception ex)
            {
                logMessage($"Error stopping server: {ex.Message}", LogEntryType.Error);
            }
        }

        private void Server_Stopped(object sender, ServerStoppedEventArgs e)
        {
            try
            {
                var server = (ValheimServer)sender;
                this.Dispatcher.Invoke(() =>
                {
                    dgServers.IsReadOnly = false;
                    RefreshDataGrid();
                });
            }
            catch (Exception ex)
            {
                logMessage($"Error handling server exited event: {ex.Message}", LogEntryType.Error);
            }
        }

        public void logMessage(string msg)
        {
            logMessage(msg, LogEntryType.Normal);
        }

        public void logMessage(string msg, LogEntryType lt)
        {
            logMessage(new LogEntry(msg, lt));
        }

        public void logMessage(LogEntry entry)
        {
            logEntries.Add(entry);
            this.Dispatcher.Invoke(() =>
            {
                if (!suppressLog)
                {
                    if (txtLog.Document.Blocks.Count > 0)
                    {
                        txtLog.Document.Blocks.InsertBefore(txtLog.Document.Blocks.FirstBlock, (Block)entry);
                    }
                    else
                    {
                        txtLog.Document.Blocks.Add((Block)entry);
                    }
                    if (entry.Message.Contains('\n'))
                    {
                        lblLastMessage.Content = entry.Message.Split('\n')[0];
                    }
                    else
                    {
                        lblLastMessage.Content = entry.Message;
                    }
                    lblLastMessage.Foreground = new SolidColorBrush(entry.Color);
                    if (entry.Type == LogEntryType.Normal)
                    {
                        lblLastMessage.FontWeight = FontWeights.Normal;
                    }
                    else
                    {
                        lblLastMessage.FontWeight = FontWeights.Bold;
                    }
                }
            });
            if (Properties.Settings.Default.WriteAppLog)
            {
                StreamWriter writer = System.IO.File.AppendText(LogPath);
                writer.WriteLine(entry.TimeStamp+": " +entry.Message);
                writer.Close();
            }
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
                    }
                }
                catch (Exception ex)
                {
                    logMessage($"Error refreshing server grid: {ex.Message}", LogEntryType.Error);
                }
            });
            
        }

        private void serversMenuRemove_Click(object sender, RoutedEventArgs e)
        {
            ValheimServer server = (ValheimServer)dgServers.SelectedItem;
            var mmb = new ModernMessageBox(this);
            var confirmResult = mmb.Show($"Are you sure you want to remove {server.DisplayName}?",
                                     "Remove Server", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (confirmResult == MessageBoxResult.Yes)
            {

                //servers.Remove(server);
                server.Dispose();
                RefreshDataGrid();
            }
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
            LogEntry.NormalColor = ((SolidColorBrush)this.Foreground).Color;
            if (logEntries.Count > 0)
            {
                var lastEntry = logEntries[logEntries.Count - 1];
                lblLastMessage.Foreground = new SolidColorBrush(lastEntry.Color);
            }
            txtLog.Document.Blocks.Clear();
            foreach (var entry in logEntries)
            {
                if (txtLog.Document.Blocks.Count > 0)
                {
                    txtLog.Document.Blocks.InsertBefore(txtLog.Document.Blocks.FirstBlock, (Block)entry);
                }
                else
                {
                    txtLog.Document.Blocks.Add((Block)entry);
                }
            }
            foreach (var detailWin in serverDetailWindows)
            {
                detailWin.ThemeUpdated();
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
                    client.Headers.Add("User-Agent", "ValheimServerWarden");
                    string source = client.DownloadString("https://api.github.com/repos/Razzmatazzz/ValheimServerWarden/releases/latest");
                    client.Dispose();
                    var release = JsonSerializer.Deserialize<GitHubRelease>(source);
                    Version remoteVersion = new Version(release.tag_name);
                    Version localVersion = typeof(MainWindow).Assembly.GetName().Version;
                    this.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            if (localVersion.CompareTo(remoteVersion) == -1)
                            {
                                var mmb = new ModernMessageBox(this);
                                var confirmResult = mmb.Show("There is a new version available. Would you like to visit the download page?",
                                         "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                                if (confirmResult == MessageBoxResult.Yes)
                                {
                                    Process.Start("cmd", $"/C start {release.html_url}");
                                }
                            }
                            else
                            {
                                //logMessage("No new version found.");
                            }
                        }
                        catch (Exception ex)
                        {
                            logMessage($"Error navigating to new version web page: {ex.Message}", LogEntryType.Error);
                        }
                    });
                }
                catch (Exception ex)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        logMessage($"Error checking for new version: {ex.Message}", LogEntryType.Error);
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
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                this.serverDetailWindows.Add(window);
                window.Closed += ((object sender, EventArgs e) =>
                {
                    this.serverDetailWindows.Remove(window);
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
                logMessage($"Error showing server details: {ex.Message}", LogEntryType.Error);
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
                        //win.LoadLogText();
                        return;
                    }
                }
                ServerLogWindow window = new ServerLogWindow(server);
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                this.serverLogWindows.Add(window);
                window.Closed += ((object sender, EventArgs e) =>
                {
                    this.serverLogWindows.Remove(window);
                });
                window.Show();
            }
            catch (Exception ex)
            {
                logMessage($"Error showing {server.DisplayName} server log: {ex.Message}", LogEntryType.Error);
            }
        }

        private void serversMenuLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ValheimServer server = ((ValheimServer)dgServers.SelectedItem);
                if (server == null) return;
                if (File.Exists(server.LogRawName))
                {
                    ShowServerLog(server);
                }
            }
            catch (Exception ex)
            {
                logMessage($"Error showing server log: {ex.Message}", LogEntryType.Error);
            }
        }
        private void checkForRunningServers()
        {
            Process[] servers = Process.GetProcessesByName("valheim_server");
            if (servers.Length > 0)
            {
                var mmb = new ModernMessageBox(this);
                MessageBoxResult confirmResult = mmb.Show($"There are already {servers.Length} Valheim dedicated servers running. Do you want to shut them down to manage them here?",
                                     "Servers Running", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                if (confirmResult == MessageBoxResult.Yes)
                {
                    ValheimServer.TerminateAll(servers);
                    return;
                }
            }
        }

        private void btnSteamCmdPath_Click(object sender, RoutedEventArgs e)
        {
            var openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            if (txtSteamCmdPath.Text != "")
            {
                var filepath = new FileInfo(txtSteamCmdPath.Text).Directory.FullName;
                if (Directory.Exists(filepath))
                {
                    openFolderDialog.SelectedPath = filepath;
                }
            }
            openFolderDialog.UseDescriptionForTitle = true;
            openFolderDialog.Description = "Select SteamCMD installation folder";
            var result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                var folderName = openFolderDialog.SelectedPath;
                /*if (folderName.Equals(txtSteamCmdPath.Text))
                {
                    return;
                }*/
                if (!File.Exists($@"{folderName}\steamcmd.exe"))
                {
                    var mmb = new ModernMessageBox(this);
                    var install = mmb.Show($"steamcmd.exe was not found in {folderName}, do you want to install it?",
                                     "Install SteamCMD?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                    if (install == MessageBoxResult.Yes)
                    {
                        try
                        {
                            using (WebClient webClient = new WebClient())
                            {
                                webClient.DownloadFile("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", "steamcmd.zip");
                            }
                            System.IO.Compression.ZipFile.ExtractToDirectory("steamcmd.zip", folderName);
                            File.Delete("steamcmd.zip");
                        }
                        catch (Exception ex)
                        {
                            logMessage($"Error installing SteamCMD: {ex.Message}", LogEntryType.Error);
                        }
                    } else
                    {
                        return;
                    }
                }
                folderName += "\\steamcmd.exe";
                txtSteamCmdPath.Text = folderName;
                Properties.Settings.Default.SteamCMDPath = folderName;
                Properties.Settings.Default.Save();
            }
        }

        private void cmbServerType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded) return;
            if (cmbServerType.SelectedItem.ToString() == "SteamCMD" && !File.Exists(txtSteamCmdPath.Text))
            {
                var mmb = new ModernMessageBox(this);
                mmb.Show("You must install SteamCMD before you can update any servers via SteamCMD.", "SteamCMD Not Installed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Properties.Settings.Default.ServerInstallType = cmbServerType.SelectedIndex;
            Properties.Settings.Default.Save();
        }

        private void menuLog_Opened(object sender, RoutedEventArgs e)
        {
            if (txtLog.Selection.IsEmpty)
            {
                menuLogCopy.Visibility = Visibility.Collapsed;
            }
            else
            {
                menuLogCopy.Visibility = Visibility.Visible;
            }
        }

        private void menuLogSelectAll_Click(object sender, RoutedEventArgs e)
        {
            txtLog.SelectAll();
        }

        private void menuLogCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtLog.Selection.Text);
        }

        private void menuLogClear_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Document.Blocks.Clear();
            logEntries.Clear();
        }

        private void chkLog_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                bool newValue = chkLog.IsChecked.GetValueOrDefault();
                if (newValue & !Properties.Settings.Default.WriteAppLog)
                {
                    System.IO.File.WriteAllText(LogPath, DateTime.Now.ToString() + ": Version " + typeof(MainWindow).Assembly.GetName().Version + "\r\n");
                }
                Properties.Settings.Default.WriteAppLog = newValue;
                Properties.Settings.Default.Save();
            } catch (Exception ex)
            {
                logMessage($"Error changing log option: {ex.Message}");
            }
        }

        private void btnReportBug_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("cmd", "/C start https://github.com/Razzmatazzz/ValheimServerWarden/issues");
        }

        private void chkRunningServerCheck_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.RunningServerCheck = chkRunningServerCheck.IsChecked.GetValueOrDefault();
            Properties.Settings.Default.Save();
        }

        private void chkStopOnClose_Checked(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.StopOnClose = chkStopOnClose.IsChecked.GetValueOrDefault();
            Properties.Settings.Default.Save();
        }
    }
}
