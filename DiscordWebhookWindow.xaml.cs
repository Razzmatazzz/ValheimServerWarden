using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
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
    /// Interaction logic for DiscordWebhookWindow.xaml
    /// </summary>
    public partial class DiscordWebhookWindow : Window
    {
        private ValheimServer _server;
        private List<TextBox> messageControls;
        private TextBox clickedTextBox;
        public DiscordWebhookWindow(ValheimServer server)
        {
            InitializeComponent();
            _server = server;
            txtWebhook.Text = _server.DiscordWebhook;
            /*OnPlayerConnected.Text = server.GetWebhookMessage(OnPlayerConnected.Name);
            OnPlayerDisconnected.Text = server.GetWebhookMessage(OnPlayerDisconnected.Name);
            OnPlayerDied.Text = server.GetWebhookMessage(OnPlayerDied.Name);
            OnFailedPassword.Text = server.GetWebhookMessage(OnFailedPassword.Name);
            OnRandomServerEvent.Text = server.GetWebhookMessage(OnRandomServerEvent.Name);*/

            ContextMenu contextMenu = new ContextMenu();
            MenuItem defaultMenu = new MenuItem();
            contextMenu.Items.Add(defaultMenu);
            defaultMenu.Header = "Reset to default";
            defaultMenu.Click += DefaultMenu_Click;

            messageControls = new List<TextBox>();
            messageControls.Add(OnPlayerConnected);
            messageControls.Add(OnPlayerDisconnected);
            messageControls.Add(OnPlayerDied);
            messageControls.Add(OnFailedPassword);
            messageControls.Add(OnRandomServerEvent);
            messageControls.Add(OnStarted);
            foreach (TextBox textBox in messageControls)
            {
                textBox.Text = server.GetWebhookMessage(textBox.Name);
                textBox.ContextMenu = contextMenu;
                textBox.ContextMenuOpening += TextBox_ContextMenuOpening;
            }
        }

        private void TextBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            clickedTextBox = (TextBox)sender;
        }

        private void DefaultMenu_Click(object sender, RoutedEventArgs e)
        {
            clickedTextBox.Text = _server.DefaultWebhookMessages[clickedTextBox.Name];
        }

        private void btnTest_Click(object sender, RoutedEventArgs e)
        {
            //_server.SendDiscordWebhook($"Ahoy from {_server.Name}");
            string wh = txtWebhook.Text;
            if (wh != null && wh != "")
            {
                using (DiscordWebhook webhook = new DiscordWebhook())
                {
                    webhook.WebHook = wh;
                    webhook.SendMessage($"Ahoy from {_server.Name}");
                }
            }
        }

        private void btnTestConnected_Click(object sender, RoutedEventArgs e)
        {
            _server.SendDiscordWebhook(OnPlayerConnected.Name, new Player("Bjorn", "123456789101112"));
        }

        private void btnTestDisconnected_Click(object sender, RoutedEventArgs e)
        {
            _server.SendDiscordWebhook(OnPlayerDisconnected.Name, new Player("Bjorn", "123456789101112"));
        }

        private void btnTestDied_Click(object sender, RoutedEventArgs e)
        {
            _server.SendDiscordWebhook(OnPlayerDied.Name, new Player("Bjorn", "123456789101112"));
        }

        private void btnFailedPassword_Click(object sender, RoutedEventArgs e)
        {
            _server.SendDiscordWebhook(OnFailedPassword.Name, new Player("Bjorn", "123456789101112"));
        }

        private void btnRandomServerEvent_Click(object sender, RoutedEventArgs e)
        {
            _server.SendDiscordWebhook(OnRandomServerEvent.Name, null, "army_bonemass");
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            _server.DiscordWebhook = txtWebhook.Text;
            foreach (TextBox textBox in messageControls)
            {
                if (textBox.Text != null && textBox.Text != this._server.DefaultWebhookMessages[textBox.Name])
                {
                    _server.DiscordWebhookMessages[textBox.Name] = textBox.Text;
                }
                else if (textBox.Text == this._server.DefaultWebhookMessages[textBox.Name])
                {
                    if (_server.DiscordWebhookMessages.ContainsKey(textBox.Name))
                    {
                        _server.DiscordWebhookMessages.Remove(textBox.Name);
                    }
                }
            }
        }

        private void btnTestServerStarted_Click(object sender, RoutedEventArgs e)
        {
            _server.SendDiscordWebhook(OnStarted.Name, null, null);
        }
    }
}
