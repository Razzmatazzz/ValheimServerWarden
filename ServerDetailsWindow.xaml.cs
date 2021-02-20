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

namespace ValheimServerWarden
{
    /// <summary>
    /// Interaction logic for ServerDetailsWindow.xaml
    /// </summary>
    public partial class ServerDetailsWindow : Window
    {
        public event EventHandler<ServerEventArgs> Starting;
        public event EventHandler<ServerEventArgs> Stopping;
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
            RefreshControls();
            this.Server.Exited += ((object sender, ServerExitedEventArgs e) =>
            {
                RefreshControls();
            });
            this.Server.PlayerConnected += ((object sender, PlayerEventArgs e) =>
            {
                RefreshControls();
            });
            this.Server.PlayerDisconnected += ((object sender, PlayerEventArgs e) =>
            {
                RefreshControls();
            });
            this.Server.PlayerDied += ((object sender, PlayerEventArgs e) =>
            {
                RefreshControls();
            });
            this.Server.Starting += ((object sender, ServerEventArgs e) =>
            {
                RefreshControls();
            });
            this.Server.Started += ((object sender, ServerEventArgs e) =>
            {
                RefreshControls();
            });
            this.Server.StartFailed += ((object sender, ServerEventArgs e) =>
            {
                RefreshControls();
            });
            Server.Updated += Server_Updated;
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
        }

        public void RefreshControls()
        {
            this.Dispatcher.Invoke(() =>
            {
                try
                {
                    Title = $"{this.Server.Name} Details";
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

                    btnStop.IsEnabled = false;
                    btnStart.IsEnabled = false;
                    btnConnect.IsEnabled = false;
                    btnStop.Content = FindResource("StopGrey");
                    btnStart.Content = FindResource("StartGrey");
                    btnConnect.Content = FindResource("ConnectGrey");
                    if (status == ValheimServer.ServerStatus.Running)
                    {
                        btnStop.IsEnabled = true;
                        btnStop.Content = FindResource("Stop");
                        if (_steamPath != null)
                        {
                            btnConnect.IsEnabled = true;
                            btnConnect.Content = FindResource("Connect");
                        }
                    }
                    if (status == ValheimServer.ServerStatus.Stopped)
                    {
                        btnStart.IsEnabled = true;
                        btnStart.Content = FindResource("Start");
                    }
                    btnLog.IsEnabled = (File.Exists(Server.GetLogName()));
                    btnLog.Visibility = (File.Exists(Server.GetLogName())) ? Visibility.Visible : Visibility.Hidden;

                    if (Server.RestartHours > 0)
                    {
                        chkAutoRestart.IsChecked = true;
                        txtRestartInterval.Text = Server.RestartHours.ToString();
                        txtRestartInterval.IsEnabled = true;
                    } else
                    {
                        chkAutoRestart.IsChecked = false;
                        txtRestartInterval.IsEnabled = false;
                    }
                }
                catch (Exception ex)
                {
                    
                }
            });
        }

        private void Server_Updated(object sender, UpdatedEventArgs e)
        {
            RefreshControls();
            ServerToControls();
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
                chkAutostart.IsChecked = Server.Autostart;
                chkLog.IsChecked = Server.Log;
            }
            catch (Exception ex)
            {

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

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            btnStart.IsEnabled = false;
            btnStart.Content = FindResource("StartGrey");
            OnStarting(new ServerEventArgs(this.Server));
        }
        private void OnStarting(ServerEventArgs args)
        {
            EventHandler<ServerEventArgs> handler = Starting;
            if (null != handler) handler(this, args);
        }
        private void OnStopping(ServerEventArgs args)
        {
            EventHandler<ServerEventArgs> handler = Stopping;
            if (null != handler) handler(this, args);
        }
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
            btnStop.IsEnabled = false;
            btnStop.Content = FindResource("StopGrey");
            OnStopping(new ServerEventArgs(this.Server));
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
            if (txtPassword.Text.Length >= 5)
            {
                if (!Server.World.Contains(txtPassword.Text))
                {
                    Server.Password = txtPassword.Text;
                }
                else
                {
                    var mmb = new ModernMessageBox(this);
                    mmb.Show("Passwords are required, must be at least 5 characters, and cannot be contained in your world name.", "Invalid Password", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtPassword.Text = Server.Password;
                }
            }
            else
            {
                var mmb = new ModernMessageBox(this);
                mmb.Show("Passwords are required, must be at least 5 characters, and cannot be contained in your world name.", "Invalid Password", MessageBoxButton.OK, MessageBoxImage.Warning);
                txtPassword.Text = Server.Password;
            }
            Server.Password = txtPassword.Text;
            Server.SaveDir = txtSaveDir.Text;
            Server.Public = chkPublic.IsChecked.GetValueOrDefault();
            Server.Autostart = chkAutostart.IsChecked.GetValueOrDefault();
            Server.Log = chkLog.IsChecked.GetValueOrDefault();
            int restartHours = Server.RestartHours;
            if (chkAutoRestart.IsChecked.GetValueOrDefault())
            {
                int.TryParse(txtRestartInterval.Text, out restartHours);
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
            OnEditedServer(new ServerEventArgs(this.Server));
            RefreshControls();
        }

        private void btnSaveDir_Click(object sender, RoutedEventArgs e)
        {
            string saveDirPath = txtSaveDir.Text;
            System.Windows.Forms.FolderBrowserDialog openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFolderDialog.SelectedPath = saveDirPath;
            openFolderDialog.Description = "Select your save directory";
            System.Windows.Forms.DialogResult result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                string folderName = openFolderDialog.SelectedPath;
                if (folderName.Equals(saveDirPath))
                {
                    return;
                }
                if (!Directory.Exists($@"{folderName}\worlds"))
                {
                    var mmb = new ModernMessageBox(this);
                    mmb.Show("Please select the folder where your Valheim save files are located. This folder should contain a \"worlds\" folder.",
                                     "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);
                    return;
                }
                txtSaveDir.Text = folderName;
            }
            RefreshControls();
        }

        private void menuSaveDirReset_Click(object sender, RoutedEventArgs e)
        {
            txtSaveDir.Text = "";
        }

        private void dgPlayers_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column.Header.Equals("JoinTime"))
            {
                e.Column.Header = "Joined";
            } else if (e.Column.Header.Equals("SteamID"))
            {
                e.Cancel = true;
            }
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

        private void chkAutoRestart_Click(object sender, RoutedEventArgs e)
        {
            
        }

        private void chkAutoRestart_Checked(object sender, RoutedEventArgs e)
        {
            txtRestartInterval.IsEnabled = chkAutoRestart.IsChecked.GetValueOrDefault();
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
    }
}
