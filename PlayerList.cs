using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ValheimServerWarden
{
    public class PlayerList : List<Player>
    {
        public override string ToString()
        {
            if (this.Count == 0) return "None";
            List<string> playernames = new List<string>();
            foreach (Player player in this)
            {
                playernames.Add(player.Name);
            }
            return string.Join(", ", playernames);
        }
    }
}
