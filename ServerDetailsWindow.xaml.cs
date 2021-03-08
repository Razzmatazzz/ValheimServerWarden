using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
//using RazzLogging;
using RazzTools;

namespace ValheimServerWarden
{
    /// <summary>
    /// Interaction logic for ServerDetailsWindow.xaml
    /// </summary>
    public partial class ServerDetailsWindow : Window
    {
        //public event EventHandler<ServerEventArgs> Starting;
        //public event EventHandler<ServerEventArgs> Stopping;
        public event EventHandler<ServerEventArgs> EditedServer;
        public event EventHandler<ServerEventArgs> ShowLog;
        private ValheimServer _server;
        private string _steamPath;
        private string _externalIP;
        public ValheimServer Server
        {
            get
            {
                return this._server;
            }
        }
        public ServerDetailsWindow(ValheimServer server)
        {
            InitializeComponent();
            this._server = server;
            txtServerLog.Document.Blocks.Clear();
            foreach (var installmethod in Enum.GetValues(typeof(ValheimServer.ServerInstallMethod)))
            {
                cmbServerType.Items.Add(installmethod);
            }
            RefreshControls();
            attachServerEventHandlers();
            ServerToControls();
            _steamPath = null;
            try
            {
                string filePath = @"Program Files (x86)\Steam\steam.exe";
                DriveInfo[] drives = DriveInfo.GetDrives();
                foreach (DriveInfo drive in drives)
                {
                    string testpath = $@"{drive.Name}{filePath}";
                    if (File.Exists(testpath))
                    {
                        _steamPath = testpath;
                        RefreshControls();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error searching for Steam path");
                Debug.WriteLine(ex);
            }
            GetExternalIP();
            LoadLists();

            foreach (var entry in Server.LogEntries)
            {
                if (txtServerLog.Document.Blocks.Count > 0)
                {
                    txtServerLog.Document.Blocks.InsertBefore(txtServerLog.Document.Blocks.FirstBlock, (Paragraph)entry);
                }
                else
                {
                    txtServerLog.Document.Blocks.Add((Paragraph)entry);
                }
            }
            cmbPriority.Items.Add(ProcessPriorityClass.Normal);
            cmbPriority.Items.Add(ProcessPriorityClass.AboveNormal);
            cmbPriority.Items.Add(ProcessPriorityClass.High);
        }

        public void RefreshControls()
        {
            this.Dispatcher.Invoke(() =>
            {
                try
                {
                    Title = $"{this.Server.DisplayName} Details";
                    lblPlayerCount.Content = this.Server.PlayerCount;
                    dgPlayers.ItemsSource = this.Server.Players;
                    dgPlayers.Items.Refresh();
                    ValheimServer.ServerStatus status = this.Server.Status;
                    lblStatus.Content = status;
                    if (Server.StartTime.Equals(new DateTime()))
                    {
                        lblStartTime.Content = "N/A";
                    }
                    else
                    {
                        lblStartTime.Content = Server.StartTime.ToString();
                    }

                    /*btnStop.IsEnabled = false;
                    btnStart.IsEnabled = false;*/
                    btnStart.Visibility = Visibility.Collapsed;
                    btnStop.Visibility = Visibility.Collapsed;
                    btnWorking.Visibility = Visibility.Collapsed;
                    btnConnect.IsEnabled = false;
                    //btnStop.Content = FindResource("StopGrey");
                    //btnStart.Content = FindResource("StartGrey");
                    btnConnect.Content = FindResource("ConnectGrey");
                    btnuModInstall.IsEnabled = false;
                    btnuModUpdate.IsEnabled = false;
                    if (status == ValheimServer.ServerStatus.Running)
                    {
                        //btnStop.IsEnabled = true;
                        //btnStop.Content = FindResource("Stop");
                        btnStop.Visibility = Visibility.Visible;
                        if (_steamPath != null)
                        {
                            btnConnect.IsEnabled = true;
                            btnConnect.Content = FindResource("Connect");
                        }
                        menuSteamCmdUpdate.Visibility = Visibility.Collapsed;
                    }
                    else if (status == ValheimServer.ServerStatus.Stopped)
                    {
                        //btnStart.IsEnabled = true;
                        //btnStart.Content = FindResource("Start");
                        btnStart.Visibility = Visibility.Visible;
                        menuSteamCmdUpdate.Visibility = Visibility.Visible;
                        btnuModInstall.IsEnabled = true;
                        btnuModUpdate.IsEnabled = true;
                    } 
                    else
                    {
                        btnWorking.Visibility = Visibility.Visible;
                        if (status == ValheimServer.ServerStatus.Starting)
                        {
                            btnWorking.ToolTip = "Starting...";
                        }
                        else if (status == ValheimServer.ServerStatus.Stopping)
                        {
                            btnWorking.ToolTip = "Stopping...";
                        }
                        else if (status == ValheimServer.ServerStatus.Updating)
                        {
                            btnWorking.ToolTip = "Updating...";
                        }
                        menuSteamCmdUpdate.Visibility = Visibility.Collapsed;
                    }
                    //btnLog.IsEnabled = (File.Exists(Server.LogRawName));
                    btnLog.Visibility = (File.Exists(Server.LogRawName)) ? Visibility.Visible : Visibility.Collapsed;

                    if (Properties.Settings.Default.SteamCMDPath != null && Properties.Settings.Default.SteamCMDPath.Length > 0 && File.Exists(Properties.Settings.Default.SteamCMDPath) && Server.InstallMethod == ValheimServer.ServerInstallMethod.SteamCMD)
                    {
                        btnSteamCmd.Visibility = Visibility.Visible;
                        chkUpdateOnRestart.Visibility = Visibility.Visible;
                        gridAutoUpdate.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        btnSteamCmd.Visibility = Visibility.Collapsed;
                        chkUpdateOnRestart.Visibility = Visibility.Collapsed;
                        gridAutoUpdate.Visibility = Visibility.Collapsed;
                    }
                    if (uMod.AgentInstalled)
                    {
                        tabuMod.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        tabuMod.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Error refreshing controls: {ex.Message}", LogEntryType.Error);
                }
            });
        }
        private void RefreshControls(object sender, EventArgs e)
        {
            RefreshControls();
        }
        private void RefreshControls(object sender, ServerStoppedEventArgs e)
        {
            RefreshControls();
        }
        private void RefreshControls(object sender, PlayerEventArgs e)
        {
            RefreshControls();
        }
        private void RefreshControls(object sender, ServerErrorEventArgs e)
        {
            RefreshControls();
        }
        private void attachServerEventHandlers()
        {
            Server.LoggedMessage += ((object sender, LoggedMessageEventArgs e) => {
                this.Dispatcher.Invoke(() =>
                {
                    LogMessage(e.LogEntry);
                });
            });
            Server.Stopped += RefreshControls;
            Server.PlayerConnected += RefreshControls;
            Server.PlayerDisconnected += RefreshControls;
            Server.PlayerDied += RefreshControls;
            Server.FailedPassword += RefreshControls;
            Server.Starting += RefreshControls;
            Server.Stopping += RefreshControls;
            Server.Started += RefreshControls;
            Server.StartFailed += RefreshControls;
            Server.StopFailed += RefreshControls;
            Server.Updated += ((object sender, UpdatedEventArgs e) =>
            {
                RefreshControls();
                ServerToControls();
            });
        }

        private void ServerToControls()
        {
            try
            {
                txtName.Text = Server.Name;
                txtPort.Text = Server.Port.ToString();
                txtWorld.Text = Server.World;
                txtPassword.Text = Server.Password;
                txtSaveDir.Text = Server.SaveDir;
                chkPublic.IsChecked = Server.Public;
                if (Server.InstallPath != null)
                {
                    txtServerDir.Text = Server.InstallPath;
                }
                else if (Properties.Settings.Default.ServerFilePath != null)
                {
                    txtServerDir.Text = Properties.Settings.Default.ServerFilePath;
                }
                cmbServerType.SelectedIndex = (int)Server.InstallMethod;
                chkAutostart.IsChecked = Server.Autostart;
                chkRawLog.IsChecked = Server.RawLog;

                if (Server.RestartHours > 0)
                {
                    chkAutoRestart.IsChecked = true;
                    txtRestartInterval.Text = Server.RestartHours.ToString();
                    txtRestartInterval.IsEnabled = true;
                }
                else
                {
                    chkAutoRestart.IsChecked = false;
                    txtRestartInterval.IsEnabled = false;
                }
                chkUpdateOnRestart.IsChecked = Server.UpdateOnRestart;
                if (Server.UpdateCheckMinutes > 0)
                {
                    chkAutoUpdate.IsChecked = true;
                    txtUpdateCheckInterval.Text = Server.UpdateCheckMinutes.ToString();
                    txtUpdateCheckInterval.IsEnabled = true;
                }
                cmbPriority.SelectedItem = Server.ProcessPriority;
                chkuModUpdate.IsChecked = Server.AutoUpdateuMod;
            }
            catch (Exception ex)
            {
                LogMessage($"Error reading server settings: {ex.Message}", LogEntryType.Error);
            }
        }

        private void GetExternalIP()
        {
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                try
                {
                    _externalIP = new WebClient().DownloadString("http://icanhazip.com");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error getting external IP.");
                    Debug.WriteLine(ex);
                }
            }).Start();
        }

        private void LoadLists()
        {
            try
            {
                string savedir;
                if (Server.SaveDir.Length > 0)
                {
                    savedir = Server.SaveDir;
                }
                else
                {
                    savedir = ValheimServer.DefaultSaveDir;
                }
                txtAdminList.Text = "";
                txtBannedList.Text = "";
                txtPermittedList.Text = "";
                if (File.Exists(savedir+"\\adminlist.txt"))
                {
                    txtAdminList.Text = File.ReadAllText(savedir + "\\adminlist.txt");
                }
                if (File.Exists(savedir + "\\bannedlist.txt"))
                {
                    txtBannedList.Text = File.ReadAllText(savedir + "\\bannedlist.txt");
                }
                if (File.Exists(savedir + "\\permittedlist.txt"))
                {
                    txtPermittedList.Text = File.ReadAllText(savedir + "\\permittedlist.txt");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading admin/banned/permitted list: {ex.Message}");
            }
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            //btnStart.IsEnabled = false;
            //btnStart.Content = FindResource("StartGrey");
            //OnStarting(new ServerEventArgs(this.Server));
            Server.Start();
        }
        /*private void OnStarting(ServerEventArgs args)
        {
            EventHandler<ServerEventArgs> handler = Starting;
            if (null != handler) handler(this, args);
        }
        private void OnStopping(ServerEventArgs args)
        {
            EventHandler<ServerEventArgs> handler = Stopping;
            if (null != handler) handler(this, args);
        }*/
        private void OnShowLog(ServerEventArgs args)
        {
            EventHandler<ServerEventArgs> handler = ShowLog;
            if (null != handler) handler(this, args);
        }
        private void OnEditedServer(ServerEventArgs args)
        {
            EventHandler<ServerEventArgs> handler = EditedServer;
            if (null != handler) handler(this, args);
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            //btnStop.IsEnabled = false;
            //btnStop.Content = FindResource("StopGrey");
            //OnStopping(new ServerEventArgs(this.Server));
            Server.Stop();
        }

        private void btnLog_Click(object sender, RoutedEventArgs e)
        {
            OnShowLog(new ServerEventArgs(this.Server));
        }
        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            if (txtName.Text.Length > 0)
            {
                Server.Name = txtName.Text;
                Title = Server.DisplayName;
            } else
            {
                txtName.Text = Server.Name;
            }
            int port;
            if (int.TryParse(txtPort.Text, out port))
            {
                Server.Port = port;
            } else
            {
                txtPort.Text = Server.Port.ToString();
            }
            if (txtWorld.Text.Length > 0)
            {
                Server.World = txtWorld.Text;
            } else
            {
                txtWorld.Text = Server.World;
            }
            if (txtPassword.Text.Length == 0) {
                LogMessage("Warning: Servers must have passwords unless modded to remove that requirement.");
            }
            else if (txtPassword.Text.Length >= 5)
            {
                if (!Server.World.Contains(txtPassword.Text))
                {
                    Server.Password = txtPassword.Text;
                }
                else
                {
                    var mmb = new ModernMessageBox(this);
                    mmb.Show("Passwords must be at least 5 characters, and cannot be contained in your world name.", "Invalid Password", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPassword.Text = Server.Password;
                }
            }
            else
            {
                var mmb = new ModernMessageBox(this);
                mmb.Show("Passwords must be at least 5 characters, and cannot be contained in your world name.", "Invalid Password", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPassword.Text = Server.Password;
            }
            Server.Password = txtPassword.Text;
            Server.SaveDir = txtSaveDir.Text;
            Server.Public = chkPublic.IsChecked.GetValueOrDefault();
            Server.InstallPath = txtServerDir.Text;
            Server.InstallMethod = (ValheimServer.ServerInstallMethod)cmbServerType.SelectedIndex;

            Server.Autostart = chkAutostart.IsChecked.GetValueOrDefault();
            Server.RawLog = chkRawLog.IsChecked.GetValueOrDefault();
            Server.ProcessPriority = (ProcessPriorityClass)cmbPriority.SelectedItem;
            int restartHours = Server.RestartHours;
            if (chkAutoRestart.IsChecked.GetValueOrDefault())
            {
                int.TryParse(txtRestartInterval.Text, out restartHours);
            }
            else
            {
                restartHours = 0;
            }
            if (restartHours > -1)
            {
                Server.RestartHours = restartHours;
            } 
            if (restartHours == 0)
            {
                txtRestartInterval.Text = "";
                chkAutoRestart.IsChecked = false;
            }
            Server.UpdateOnRestart = chkUpdateOnRestart.IsChecked.GetValueOrDefault();
            int updateCheckMinutes = Server.UpdateCheckMinutes;
            if (chkAutoUpdate.IsChecked.GetValueOrDefault())
            {
                int.TryParse(txtUpdateCheckInterval.Text, out updateCheckMinutes);
            }
            else
            {
                updateCheckMinutes = 0;
            }
            if (updateCheckMinutes > -1)
            {
                Server.UpdateCheckMinutes = updateCheckMinutes;
            }
            if (updateCheckMinutes == 0)
            {
                txtUpdateCheckInterval.Text = "";
                chkAutoUpdate.IsChecked = false;
            }
            OnEditedServer(new ServerEventArgs(this.Server));
            RefreshControls();
            if (Server.Running)
            {
                var mmb = new ModernMessageBox(this);
                mmb.Show("You must restart the server for the server name, port, world, password, save folder, or public status to change.", "Server Restart", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            LoadLists();
        }

        private void btnSaveDir_Click(object sender, RoutedEventArgs e)
        {
            string saveDirPath = txtSaveDir.Text;
            System.Windows.Forms.FolderBrowserDialog openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFolderDialog.SelectedPath = saveDirPath;
            openFolderDialog.UseDescriptionForTitle = true;
            openFolderDialog.Description = "Select save folder";
            System.Windows.Forms.DialogResult result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string folderName = openFolderDialog.SelectedPath;
                if (folderName.Equals(saveDirPath))
                {
                    return;
                }
                /*if (!Directory.Exists($@"{folderName}\worlds"))
                {
                    var mmb = new ModernMessageBox(this);
                    mmb.Show("Please select the folder where your Valheim save files are located. This folder should contain a \"worlds\" folder.",
                                     "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                    return;
                }*/
                txtSaveDir.Text = folderName;
            }
            RefreshControls();
        }

        private void menuSaveDirReset_Click(object sender, RoutedEventArgs e)
        {
            txtSaveDir.Text = "";
        }

        private void dgPlayers_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            menuCopySteamId.IsEnabled = (dgPlayers.SelectedIndex != -1);
        }

        private void menuCopySteamId_Click(object sender, RoutedEventArgs e)
        {
            Player player = (Player)dgPlayers.SelectedItem;
            Clipboard.SetText(player.SteamID);
        }

        private void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_steamPath != null)
                {
                    //Process.Start(_steamPath, $"steam://connect/127.0.0.1:{this.Server.Port + 1}");
                    Process.Start(_steamPath, $"-applaunch 892970 +connect 127.0.0.1:{this.Server.Port} +password \"{this.Server.Password}\"");
                }
                else
                {
                    Debug.WriteLine("Steam path not found");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void menuConnectLink_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText($"steam://connect/{_externalIP}:{this.Server.Port + 1}");
        }

        private void chkAutoRestart_Checked(object sender, RoutedEventArgs e)
        {
            txtRestartInterval.IsEnabled = chkAutoRestart.IsChecked.GetValueOrDefault();
            if (chkAutoRestart.IsChecked.GetValueOrDefault() && txtRestartInterval.Text == "")
            {
                txtRestartInterval.Text = "4";
            }
        }

        private void btnDiscordWebhook_Click(object sender, RoutedEventArgs e)
        {
            var webhookWin = new DiscordWebhookWindow(this.Server);
            webhookWin.ShowDialog();
        }

        private void menuConnectCheckExternal_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("cmd", $"/C start https://southnode.net/form_get.php?ip={_externalIP}");
        }

        private void btnSteamCmd_Click(object sender, RoutedEventArgs e)
        {
            btnSteamCmd.ContextMenu.IsOpen = true;
        }

        private void menuSteamCmdUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (!Server.Running)
            {
                Server.Update();
            } 
            else
            {
                var mmb = new ModernMessageBox(this);
                mmb.Show("Please stop the server before updating.", "Stop Server to Update", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
            }
        }
        public void LogMessage(string msg)
        {
            LogMessage(msg, LogEntryType.Normal);
        }
        public void LogMessage(string msg, LogEntryType lt)
        {
            LogMessage(new LogEntry(msg, lt));
        }
        public void LogMessage(LogEntry entry)
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    if (txtServerLog.Document.Blocks.Count > 0)
                    {
                        txtServerLog.Document.Blocks.InsertBefore(txtServerLog.Document.Blocks.FirstBlock, (Paragraph)entry);
                    }
                    else
                    {
                        txtServerLog.Document.Blocks.Add((Paragraph)entry);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error logging message: {ex.Message}");
            }
        }
        private void Window_ContentRendered(object sender, EventArgs e)
        {
            /*tabsServer.SelectedIndex = 1;
            UpdateLayout();
            SizeToContent = SizeToContent.Manual;
            tabsServer.SelectedIndex = 0;*/
        }

        private void btnServerDir_Click(object sender, RoutedEventArgs e)
        {
            var openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            if (txtServerDir.Text != "")
            {
                var serverpath = new FileInfo(txtServerDir.Text).Directory.FullName;
                if (Directory.Exists(serverpath))
                {
                    openFolderDialog.SelectedPath = serverpath;
                }
            }
            openFolderDialog.UseDescriptionForTitle = true;
            openFolderDialog.Description = "Server installation folder";
            var result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                var folderName = openFolderDialog.SelectedPath;
                /*if (folderName.Equals(serverpath))
                {
                    return;
                }*/
                if (!File.Exists($@"{folderName}\{ValheimServer.ExecutableName}") && cmbServerType.SelectedIndex == (int)ValheimServer.ServerInstallMethod.SteamCMD && File.Exists(Properties.Settings.Default.SteamCMDPath))
                {
                    var mmb = new ModernMessageBox(this);
                    var install = mmb.Show($"{ValheimServer.ExecutableName} was not found in this folder, do you want to install it via SteamCMD?",
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
                            LogMessage($"Error installing dedicated server: {ex.Message}", LogEntryType.Error);
                        }
                    }
                }
                folderName += $@"\{ValheimServer.ExecutableName}";
                txtServerDir.Text = folderName;
            }
            RefreshControls();
        }

        private void menuServerDirReset_Click(object sender, RoutedEventArgs e)
        {
            txtServerDir.Text = Properties.Settings.Default.ServerFilePath;
        }

        private void txtServerDir_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (!File.Exists(txtServerDir.Text) && cmbServerType.SelectedIndex == (int)ValheimServer.ServerInstallMethod.SteamCMD && File.Exists(Properties.Settings.Default.SteamCMDPath))
            {
                menuServerDirInstall.IsEnabled = true;
                menuServerDirInstall.Visibility = Visibility.Visible;
            }
            else
            {
                menuServerDirInstall.IsEnabled = false;
                menuServerDirInstall.Visibility = Visibility.Collapsed;
            }
        }

        private void menuLogClear_Click(object sender, RoutedEventArgs e)
        {
            txtServerLog.Document.Blocks.Clear();
            Server.LogEntries.Clear();
        }

        private void menuLogSelectAll_Click(object sender, RoutedEventArgs e)
        {
            txtServerLog.SelectAll();
        }

        private void menuLog_Opened(object sender, RoutedEventArgs e)
        {
            if (txtServerLog.Selection.IsEmpty)
            {
                menuLogCopy.Visibility = Visibility.Collapsed;
            }
            else
            {
                menuLogCopy.Visibility = Visibility.Visible;
            }
        }

        private void menuLogCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtServerLog.Selection.Text);
        }

        private void btnSaveAdminList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string savedir;
                if (Server.SaveDir.Length > 0)
                {
                    savedir = Server.SaveDir;
                }
                else
                {
                    savedir = ValheimServer.DefaultSaveDir;
                }
                File.WriteAllTextAsync(savedir + "\\adminlist.txt", txtAdminList.Text);
                LogMessage("Admin list updated.", LogEntryType.Success);
            }
            catch (Exception ex)
            {
                LogMessage($"Error updating admin list: {ex.Message}", LogEntryType.Error);
            }
        }

        private void btnSaveBannedList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string savedir;
                if (Server.SaveDir.Length > 0)
                {
                    savedir = Server.SaveDir;
                }
                else
                {
                    savedir = ValheimServer.DefaultSaveDir;
                }
                File.WriteAllTextAsync(savedir + "\\bannedlist.txt", txtBannedList.Text);
                LogMessage("Banned list updated.", LogEntryType.Success);
            }
            catch (Exception ex)
            {
                LogMessage($"Error updating banned list: {ex.Message}", LogEntryType.Error);
            }
        }

        private void btnSavePermittedList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string savedir;
                if (Server.SaveDir.Length > 0)
                {
                    savedir = Server.SaveDir;
                }
                else
                {
                    savedir = ValheimServer.DefaultSaveDir;
                }
                File.WriteAllTextAsync(savedir + "\\permittedlist.txt", txtPermittedList.Text);
                LogMessage("Permitted list updated.", LogEntryType.Success);
            }
            catch (Exception ex)
            {
                LogMessage($"Error updating permitted list: {ex.Message}", LogEntryType.Error);
            }
        }
        public void ThemeUpdated()
        {
            txtServerLog.Document.Blocks.Clear();
            foreach (var entry in Server.LogEntries)
            {
                if (txtServerLog.Document.Blocks.Count > 0)
                {
                    txtServerLog.Document.Blocks.InsertBefore(txtServerLog.Document.Blocks.FirstBlock, (Block)entry);
                }
                else
                {
                    txtServerLog.Document.Blocks.Add((Block)entry);
                }
            }
        }

        private void menuSteamCmdCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            Server.CheckForUpdate();
        }

        private void chkAutoUpdate_Checked(object sender, RoutedEventArgs e)
        {
            txtUpdateCheckInterval.IsEnabled = chkAutoUpdate.IsChecked.GetValueOrDefault();
            if (chkAutoUpdate.IsChecked.GetValueOrDefault() && txtUpdateCheckInterval.Text == "")
            {
                txtUpdateCheckInterval.Text = "20";
            }
        }

        private void btnuModUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (Server.Status == ValheimServer.ServerStatus.Stopped)
            {
                this.Dispatcher.Invoke(() =>
                {
                    btnuModUpdate.IsEnabled = false;
                });
                var umod = new uMod(Server.InstallPath, "valheim");
                umod.UpdateEnded += Umod_UpdateEnded;
                umod.LoggedMessage += (object sender, LoggedMessageEventArgs args) =>
                {
                    LogMessage("uMod: " + args.LogEntry.Message, args.LogEntry.Type);
                };
                umod.Update("core apps extensions");
            }
            else
            {
                var mmb = new ModernMessageBox(this);
                mmb.Show("Stop server before updating.", "Server Running", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Umod_UpdateEnded(object sender, uMod.ProcessEndedEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                btnuModUpdate.IsEnabled = true;
                if (e.ExitCode == 0)
                {
                    var mmb = new ModernMessageBox(this);
                    mmb.Show("uMod update compelete.", "uMod Updated", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (e.ExitCode == 2)
                {
                    var mmb = new ModernMessageBox(this);
                    mmb.Show($"uMod did not need to install any updates.", "No Update Needed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var mmb = new ModernMessageBox(this);
                    mmb.Show($"uMod terminated with code {e.ExitCode}; unable to update uMod core or uMod apps.", "uMod Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void btnuModInstall_Click(object sender, RoutedEventArgs e)
        {
            if (Server.Status == ValheimServer.ServerStatus.Stopped)
            {
                this.Dispatcher.Invoke(() =>
                {
                    btnuModInstall.IsEnabled = false;
                });
                var umod = new uMod(Server.InstallPath, "valheim");
                umod.InstallEnded += Umod_InstallEnded;
                umod.LoggedMessage += (object sender, LoggedMessageEventArgs args) =>
                {
                    LogMessage("uMod: "+args.LogEntry.Message, args.LogEntry.Type);
                };
                umod.Install("umod");
            }
            else
            {
                var mmb = new ModernMessageBox(this);
                mmb.Show("Stop server before updating.", "Server Running", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Umod_InstallEnded(object sender, uMod.ProcessEndedEventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                btnuModInstall.IsEnabled = true;
                if (e.ExitCode == 0)
                {
                    var mmb = new ModernMessageBox(this);
                    mmb.Show("uMod installation compelete.", "uMod Installed", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var mmb = new ModernMessageBox(this);
                    mmb.Show($"uMod terminated with code {e.ExitCode}, which indicates an error.", "uMod Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void chkuModUpdate_Checked(object sender, RoutedEventArgs e)
        {
            Server.AutoUpdateuMod = chkuModUpdate.IsChecked.GetValueOrDefault();
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
}
