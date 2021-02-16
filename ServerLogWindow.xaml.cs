using System;
using System.Collections.Generic;
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
    /// Interaction logic for ServerLogWindow.xaml
    /// </summary>
    public partial class ServerLogWindow : Window
    {
        ValheimServer _server;
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
            this.Title = $"{server.GetLogName()} (does not update live)";
            LoadLogText();
        }

        public void LoadLogText()
        {
            try
            {
                if (File.Exists(_server.GetLogName()))
                {
                    txtLog.Document.Blocks.Clear();
                    Run run = new Run(File.ReadAllText(_server.GetLogName()));
                    Paragraph paragraph = new Paragraph(run);
                    paragraph.Margin = new Thickness(0);
                    txtLog.Document.Blocks.Add(paragraph);
                }
            }
            catch (IOException ex)
            {
                if (ex.Message.Contains("being used by another process"))
                {
                    System.Threading.Thread.Sleep(500);
                    LoadLogText();
                }
            }
        }
    }
}
