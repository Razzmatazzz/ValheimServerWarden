using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValheimServerWarden
{
    public class Player
    {
        public string Name { get; set; }
        public string SteamID { get; set; }
        public DateTime JoinTime { get; set; }
        public Player(string name, string steamid)
        {
            this.Name = name;
            this.SteamID = steamid;
            this.JoinTime = DateTime.Now;
        }
        public override string ToString()
        {
            return this.Name;
        }
    }
}
