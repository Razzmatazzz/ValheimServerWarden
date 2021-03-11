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
    /// Interaction logic for DiscordWebhookEventNamesWindow.xaml
    /// </summary>
    public partial class DiscordWebhookEventNamesWindow : Window
    {
        private ValheimServer Server { get; set; }
        private Dictionary<string, string> DefaultNames {get; set;}
        private Dictionary<string, string> CustomNames { get; set; }
        private List<TextBox> nameTextBoxes;
        private List<string> removeKeys;
        public DiscordWebhookEventNamesWindow(ValheimServer server)
        {
            InitializeComponent();
            Server = server;
            nameTextBoxes = new();
            removeKeys = new();
            DefaultNames = ValheimServer.DiscordWebhookDefaultAttackNames;
            CustomNames = Server.DiscordServerEventNames;
            var combinedNames = new SortedDictionary<string,string>();
            foreach (string key in CustomNames.Keys)
            {
                combinedNames.Add(key, CustomNames[key]);
            }
            foreach (string key in DefaultNames.Keys)
            {
                if (!combinedNames.ContainsKey(key))
                {
                    combinedNames.Add(key, DefaultNames[key]);
                }                
            }
            foreach (string key in combinedNames.Keys)
            {
                addEventRow(key, combinedNames[key]);
            }
        }

        private void addEventRow(string key, string name)
        {
            if (key == null) key = "";
            if (Name == null) Name = "";
            var txtName = new TextBox();
            txtName.Margin = new Thickness(5);
            txtName.Text = name;
            txtName.SetValue(Grid.RowProperty, gridNames.RowDefinitions.Count);
            txtName.SetValue(Grid.ColumnProperty, 1);
            nameTextBoxes.Add(txtName);

            var txtKey = new TextBox();
            txtKey.Margin = new Thickness(5);
            txtKey.Text = key;
            txtKey.Tag = key;
            txtKey.IsReadOnly = DefaultNames.ContainsKey(key);
            txtKey.SetValue(Grid.RowProperty, gridNames.RowDefinitions.Count);
            txtKey.SetValue(Grid.ColumnProperty, 0);
            txtName.Tag = txtKey;

            gridNames.Children.Add(txtKey);
            gridNames.Children.Add(txtName);
            if (!DefaultNames.ContainsKey(key))
            {
                var btnRemove = new Button();
                var image = new Image();
                image.Source = (BitmapImage)FindResource("Remove");
                btnRemove.Content = image;
                btnRemove.ToolTip = "Remove custom event name";
                btnRemove.Margin = new Thickness(5);
                btnRemove.Tag = txtName;
                btnRemove.SetValue(Grid.RowProperty, gridNames.RowDefinitions.Count);
                btnRemove.SetValue(Grid.ColumnProperty, 2);
                btnRemove.Click += (sender, args) =>
                {
                    var button = (Button)sender;
                    var txtName = (TextBox)button.Tag;
                    var txtKey = (TextBox)txtName.Tag;
                    nameTextBoxes.Remove(txtName);
                    gridNames.Children.Remove(txtKey);
                    gridNames.Children.Remove(txtName);
                    gridNames.Children.Remove(button);
                    if (Server.DiscordServerEventNames.ContainsKey(txtKey.Tag.ToString()))
                    {
                        if (!removeKeys.Contains(txtKey.Tag.ToString()))
                        {
                            removeKeys.Add(txtKey.Tag.ToString());
                        }
                    }
                };
                gridNames.Children.Add(btnRemove);
            }

            var rowDef = new RowDefinition();
            rowDef.Height = GridLength.Auto;
            gridNames.RowDefinitions.Add(rowDef);
        }
        private void addEventRow()
        {
            addEventRow(null, null);
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            foreach (var removekey in removeKeys)
            {
                if (Server.DiscordServerEventNames.ContainsKey(removekey))
                {
                    Server.DiscordServerEventNames.Remove(removekey);
                }
            }
            foreach (var txtName in nameTextBoxes)
            {
                var key = ((TextBox)txtName.Tag).Text;
                var oldkey = ((TextBox)txtName.Tag).Tag.ToString();
                var name = txtName.Text;
                if (!ValheimServer.DiscordWebhookDefaultAttackNames.ContainsKey(key) || (ValheimServer.DiscordWebhookDefaultAttackNames.ContainsKey(key) && ValheimServer.DiscordWebhookDefaultAttackNames[key] != name))
                {
                    if (key == "") continue;
                    if (Server.DiscordServerEventNames.ContainsKey(key))
                    {
                        Server.DiscordServerEventNames[key] = name;
                    }
                    else
                    {
                        Server.DiscordServerEventNames.Add(key, name);
                    }
                    if (key != oldkey && Server.DiscordServerEventNames.ContainsKey(oldkey))
                    {
                        Server.DiscordServerEventNames.Remove(oldkey);
                    }
                }
                else if (ValheimServer.DiscordWebhookDefaultAttackNames.ContainsKey(key) && Server.DiscordServerEventNames.ContainsKey(key))
                {
                    Server.DiscordServerEventNames.Remove(key);
                }
            }
            Close();
        }
        private void btnAddEvent_Click(object sender, RoutedEventArgs e)
        {
            addEventRow();
        }
    }
}
