using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        private ServerLogWindow _logWindow;
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
        }

        public void RefreshControls()
        {
            this.Dispatcher.Invoke(() =>
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
                }else
                {
                    lblStartTime.Content = Server.StartTime.ToString();
                }

                btnStop.IsEnabled = false;
                btnStart.IsEnabled = false;
                btnStop.Content = FindResource("StopGrey");
                btnStart.Content = FindResource("StartGrey");
                if (status == ValheimServer.ServerStatus.Running)
                {
                    btnStop.IsEnabled = true;
                    btnStop.Content = FindResource("Stop");
                }
                if (status == ValheimServer.ServerStatus.Stopped)
                {
                    btnStart.IsEnabled = true;
                    btnStart.Content = FindResource("Start");
                }
                btnLog.IsEnabled = (File.Exists(Server.GetLogName()));
                btnLog.Visibility = (File.Exists(Server.GetLogName())) ? Visibility.Visible : Visibility.Hidden;
            });
        }

        private void Server_Updated(object sender, UpdatedEventArgs e)
        {
            RefreshControls();
            ServerToControls();
        }

        private void ServerToControls()
        {
            txtName.Text = Server.Name;
            txtPort.Text = Server.Port.ToString();
            txtWorld.Text = Server.World;
            txtPassword.Text = Server.Password;
            txtSaveDir.Text = Server.SaveDir;
            chkAutostart.IsChecked = Server.Autostart;
            chkLog.IsChecked = Server.Log;
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
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
            OnStopping(new ServerEventArgs(this.Server));
        }

        private void btnLog_Click(object sender, RoutedEventArgs e)
        {
            OnShowLog(new ServerEventArgs(this.Server));
        }

        private void _logWindow_Closed(object sender, EventArgs e)
        {
            _logWindow = null;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (_logWindow != null)
            {
                _logWindow.Close();
            }
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
            if (txtPassword.Text.Length == 0 || txtPassword.Text.Length >= 5)
            {
                Server.Password = txtPassword.Text;
            }
            else
            {
                txtPassword.Text = Server.Password;
            }
            Server.Password = txtPassword.Text;
            Server.SaveDir = txtSaveDir.Text;
            Server.Autostart = chkAutostart.IsChecked.HasValue ? chkAutostart.IsChecked.Value : false;
            Server.Log = chkLog.IsChecked.HasValue ? chkLog.IsChecked.Value : false;
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
                    MessageBox.Show("Please select the folder where your Valheim save files are located. This folder should contain a \"worlds\" folder.",
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
    }
}
