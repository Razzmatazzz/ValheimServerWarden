using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
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
    /// Interaction logic for ServerLogWindow.xaml
    /// </summary>
    public partial class ServerLogWindow : Window
    {
        private ValheimServer _server;
        private FileSystemWatcher logWatcher;
        private DateTime lastRefresh;
        private Timer refreshTimer;
        private int refreshInterval;
        private Timer retryTimer;
        private int retryInterval;
        private double prevExtentHeight;
        public ValheimServer Server
        {
            get
            {
                return this._server;
            }
        }
        public ServerLogWindow(ValheimServer server)
        {
            InitializeComponent();
            _server = server;
            this.Title = $"{server.LogRawName}";
            prevExtentHeight = 0;
            RefreshLogText();

            refreshInterval = 1000;
            refreshTimer = new();
            refreshTimer.AutoReset = false;
            refreshTimer.Elapsed += RefreshTimer_Elapsed;

            retryInterval = 4000;
            retryTimer = new();
            retryTimer.AutoReset = false;
            retryTimer.Elapsed += RetryTimer_Elapsed;

            logWatcher = new FileSystemWatcher();
            // Watch for changes in LastWrite times.
            logWatcher.NotifyFilter = NotifyFilters.LastWrite;
            logWatcher.Path = Environment.CurrentDirectory;

            // Only watch .db files.
            logWatcher.Filter = $"{server.LogRawName}";

            logWatcher.Changed += LogWatcher_Changed;
            logWatcher.EnableRaisingEvents = true;
        }

        private void RefreshTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            RefreshLogText();
        }

        private void RetryTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Debug.WriteLine("timer elapsed");
            if (lastRefresh.AddMilliseconds(retryInterval) >= DateTime.Now)
            {
                Debug.WriteLine($"timer elapsed >= {retryInterval}ms ago");
                RefreshLogText();
            }
        }

        private void LogWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            refreshTimer.Interval = refreshInterval;
            refreshTimer.Enabled = true;
        }

        public void RefreshLogText()
        {
            if (_server == null)
            {
                this.Close();
                return;
            }
            if (File.Exists(_server.LogRawName))
            {
                this.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        txtLog.Document.Blocks.Clear();
                        Run run = new Run(File.ReadAllText(_server.LogRawName));
                        Paragraph paragraph = new Paragraph(run);
                        paragraph.Margin = new Thickness(0);
                        txtLog.Document.Blocks.Add(paragraph);
                        lastRefresh = DateTime.Now;
                        //Debug.WriteLine(txtLog.VerticalOffset+" "+txtLog.ExtentHeight+" "+txtLog.ViewportHeight);
                        if (prevExtentHeight < txtLog.ViewportHeight && txtLog.ExtentHeight > txtLog.ViewportHeight)
                        {
                            txtLog.ScrollToEnd();
                        }
                        else if (txtLog.VerticalOffset+txtLog.ViewportHeight == prevExtentHeight)
                        {
                            txtLog.ScrollToEnd();
                        }
                        else if (txtLog.VerticalOffset+txtLog.ViewportHeight == txtLog.ExtentHeight)
                        {
                            txtLog.ScrollToEnd();
                        }
                        prevExtentHeight = txtLog.ExtentHeight;
                    }
                    catch (IOException)
                    {
                        retryTimer.Interval = retryInterval;
                        retryTimer.Enabled = true;
                        retryTimer.Start();
                    }
                });
            }
        }

        private void menuRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshLogText();
        }

        private void menuStop_Click(object sender, RoutedEventArgs e)
        {
            logWatcher.EnableRaisingEvents = false;
            menuStop.Visibility = Visibility.Collapsed;
            menuRefresh.Visibility = Visibility.Visible;
            menuStart.Visibility = Visibility.Visible;
        }

        private void menuStart_Click(object sender, RoutedEventArgs e)
        {
            logWatcher.EnableRaisingEvents = true;
            menuStop.Visibility = Visibility.Visible;
            menuRefresh.Visibility = Visibility.Collapsed;
            menuStart.Visibility = Visibility.Collapsed;
        }

        private void menuLogSelectAll_Click(object sender, RoutedEventArgs e)
        {
            txtLog.SelectAll();
        }

        private void menuLogCopy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(txtLog.Selection.Text);
        }

        private void txtLog_ContextMenuOpening(object sender, ContextMenuEventArgs e)
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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            txtLog.ScrollToEnd();
            prevExtentHeight = txtLog.ExtentHeight;
        }
    }
}
