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
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _server = server;

            txtWebhook.Text = _server.DiscordWebhook;

            var contextMenu = new ContextMenu();
            var defaultMenu = new MenuItem();
            contextMenu.Items.Add(defaultMenu);
            defaultMenu.Header = "Reset to default";
            defaultMenu.Click += DefaultMenu_Click;

            messageControls = new();
            messageControls.Add(txtOnPlayerConnected);
            messageControls.Add(txtOnPlayerDisconnected);
            messageControls.Add(txtOnPlayerDied);
            messageControls.Add(txtOnFailedPassword);
            messageControls.Add(txtOnRandomServerEvent);
            messageControls.Add(txtOnStarted);
            messageControls.Add(txtOnStartFailed);
            messageControls.Add(txtOnServerExited);
            foreach (var textBox in messageControls)
            {
                textBox.Text = server.GetWebhookMessage(textBox.Tag.ToString());
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
            clickedTextBox.Text = ValheimServer.DiscordWebhookDefaultMessages[clickedTextBox.Tag.ToString()];
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            _server.DiscordWebhook = txtWebhook.Text;
            foreach (var textBox in messageControls)
            {
                var webHookName = textBox.Tag.ToString();
                if (textBox.Text != null && ValheimServer.DiscordWebhookDefaultMessages.ContainsKey(webHookName) && textBox.Text != ValheimServer.DiscordWebhookDefaultMessages[webHookName])
                {
                    _server.DiscordWebhookMessages[webHookName] = textBox.Text;
                }
                else if (ValheimServer.DiscordWebhookDefaultMessages.ContainsKey(webHookName) && textBox.Text == ValheimServer.DiscordWebhookDefaultMessages[webHookName])
                {
                    if (_server.DiscordWebhookMessages.ContainsKey(webHookName))
                    {
                        _server.DiscordWebhookMessages.Remove(webHookName);
                    }
                }
            }
            DialogResult = true;
            Close();
        }
        private void btnTestWebhook_Click(object sender, RoutedEventArgs e)
        {
            var oldUrl = _server.DiscordWebhook;
            _server.DiscordWebhook = txtWebhook.Text;
            _server.SendDiscordWebhook(((Button)sender).Tag.ToString(), new Player("Bjorn", "123456789101112"), "army_bonemass");
            _server.DiscordWebhook = oldUrl;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
