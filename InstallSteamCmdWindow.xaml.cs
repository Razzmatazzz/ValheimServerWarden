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
using System.Windows.Shapes;
using System.IO;
using System.Net;
using System.Diagnostics;

namespace ValheimServerWarden
{
    /// <summary>
    /// Interaction logic for InstallSteamCmdWindow.xaml
    /// </summary>
    public partial class InstallSteamCmdWindow : Window
    {
        public string SteamCmdPath
        {
            get
            {
                return txtSteamCMDPath.Text;
            }
            set
            {
                txtSteamCMDPath.Text = value;
            }
        }
        public string ServerPath
        {
            get
            {
                return txtServerPath.Text;
            }
            set
            {
                txtServerPath.Text = value;
            }
        }
        public InstallSteamCmdWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (File.Exists(txtSteamCMDPath.Text))
            {
                btnInstallServer.IsEnabled = true;
                btnInstallSteamCMD.IsEnabled = false;
            }
            else
            {
                btnInstallServer.IsEnabled = false;
                btnInstallSteamCMD.IsEnabled = true;
            }
        }

        private void txtSteamCMDPath_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFolderDialog.SelectedPath = new FileInfo(txtSteamCMDPath.Text).DirectoryName;
            openFolderDialog.UseDescriptionForTitle = true;
            openFolderDialog.Description = "SteamCMD installation folder";
            System.Windows.Forms.DialogResult result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                var fullpath = openFolderDialog.SelectedPath + "\\steamcmd.exe";
                if (File.Exists(fullpath))
                {
                    txtSteamCMDPath.Text = fullpath;
                    btnInstallServer.IsEnabled = true;
                    btnInstallSteamCMD.IsEnabled = false;
                }
                else
                {
                    btnInstallServer.IsEnabled = false;
                    btnInstallSteamCMD.IsEnabled = true;
                }
            }
            openFolderDialog.Dispose();
        }

        private void btnInstallSteamCMD_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(txtSteamCMDPath.Text))
            {
                using (WebClient webClient = new WebClient())
                {
                    webClient.DownloadFile("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", "steamcmd.zip");
                }
                if (!Directory.Exists(new FileInfo(txtSteamCMDPath.Text).Directory.FullName))
                {
                    Directory.CreateDirectory(new FileInfo(txtSteamCMDPath.Text).Directory.FullName);
                }
                System.IO.Compression.ZipFile.ExtractToDirectory("steamcmd.zip", new FileInfo(txtSteamCMDPath.Text).Directory.FullName);
                File.Delete("steamcmd.zip");
                if (File.Exists(txtSteamCMDPath.Text))
                {
                    btnInstallServer.IsEnabled = true;
                    btnInstallSteamCMD.IsEnabled = false;
                }
            }
        }

        private void txtServerPath_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog openFolderDialog = new System.Windows.Forms.FolderBrowserDialog();
            openFolderDialog.SelectedPath = new FileInfo(txtServerPath.Text).DirectoryName;
            openFolderDialog.UseDescriptionForTitle = true;
            openFolderDialog.Description = "Folder where Valheim dedicated server will be installed";
            System.Windows.Forms.DialogResult result = openFolderDialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                txtServerPath.Text = openFolderDialog.SelectedPath;
            }
            openFolderDialog.Dispose();
        }

        private void btnInstallServer_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists($@"{txtServerPath.Text}\{ValheimServer.ExecutableName}"))
            {
                btnInstallServer.IsEnabled = false;
                txtServerPath.MouseLeftButtonUp -= txtServerPath_MouseLeftButtonUp;
                txtSteamCMDPath.MouseLeftButtonUp -= txtSteamCMDPath_MouseLeftButtonUp;
                var process = new Process();
                process.StartInfo.FileName = txtSteamCMDPath.Text;
                process.StartInfo.Arguments = $"+login anonymous +force_install_dir \"{txtServerPath.Text}\" +app_update {ValheimServer.SteamID} +validate +quit";
                process.EnableRaisingEvents = true;
                process.Exited += SteamCmdProcess_Exited;
                process.Start();
                process.WaitForExit();
            }
            else
            {
                Properties.Settings.Default.ServerInstallType = (int)ValheimServer.ServerInstallMethod.SteamCMD;
                Properties.Settings.Default.ServerFilePath = txtServerPath.Text + $@"\{ValheimServer.ExecutableName}";
                Properties.Settings.Default.SteamCMDPath = txtSteamCMDPath.Text;
                Properties.Settings.Default.Save();
                DialogResult = true;
                Close();
            }
        }

        private void SteamCmdProcess_Exited(object sender, EventArgs e)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (File.Exists($@"{txtServerPath.Text}\{ValheimServer.ExecutableName}"))
                {
                    Properties.Settings.Default.ServerInstallType = (int)ValheimServer.ServerInstallMethod.SteamCMD;
                    Properties.Settings.Default.ServerFilePath = $@"{txtServerPath.Text}\{ValheimServer.ExecutableName}";
                    Properties.Settings.Default.SteamCMDPath = txtSteamCMDPath.Text;
                    Properties.Settings.Default.Save();
                    DialogResult = true;
                    Close();
                }
                else
                {
                    btnInstallServer.IsEnabled = true;
                    txtServerPath.MouseLeftButtonUp += txtServerPath_MouseLeftButtonUp;
                    txtSteamCMDPath.MouseLeftButtonUp += txtSteamCMDPath_MouseLeftButtonUp;
                }
            });
        }
    }
}
