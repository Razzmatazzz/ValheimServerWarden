using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;

namespace ValheimServerWarden
{
    public class DiscordWebhook : IDisposable
    {
        private readonly WebClient dWebClient;
        private static NameValueCollection discordValues = new NameValueCollection();
        public string WebHook { get; set; }
        public string UserName { get; set; }
        public string ProfilePicture { get; set; }

        public DiscordWebhook()
        {
            dWebClient = new WebClient();
        }


        public void SendMessage(string msgSend)
        {
            //discordValues.Add("username", UserName);
            //discordValues.Add("avatar_url", ProfilePicture);
            discordValues.Remove("content");
            discordValues.Add("content", msgSend);

            dWebClient.UploadValues(WebHook, discordValues);
        }

        public void Dispose()
        {
            dWebClient.Dispose();
        }
    }
}
