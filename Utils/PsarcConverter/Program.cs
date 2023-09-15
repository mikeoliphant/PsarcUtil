using System;
using System.IO;

namespace PsarcConverter
{
    class Program
    {
        public static void Main(string[] args)
        {
            PsarcConverter converter = new PsarcConverter(@"C:\Share\JamSongs", convertAudio: false);

            //converter.ConvertPsarc(@"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\dlc\Air_La-Femme-DArgent-Reedit_v1_p.psarc");
            //converter.ConvertPsarc(@"C:\Share\Rocksmith DLC\badr21st_p.psarc");

            converter.ConvertPsarc(@"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\songs.psarc");
            converter.ConvertFolder(@"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\dlc");
            converter.ConvertFolder(@"C:\Share\Rocksmith DLC");
        }
    }
}