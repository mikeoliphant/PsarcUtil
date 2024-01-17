using System;
using System.IO;

namespace PsarcConverter
{
    class Program
    {
        public static void Main(string[] args)
        {
            var converter = new PsarcUtil.PsarcConverter(@"C:\Share\JamSongs", convertAudio: false);

            converter.ConvertPsarc(@"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\dlc\ZZ_Top_La_Grange_v1_p.psarc");

            //converter.ConvertPsarc(@"C:\Share\Rocksmith DLC\jimimanic_p.psarc");


            //converter.ConvertPsarc(@"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\dlc\Dire-Straits_Sultans-of-Swing_v443_p.psarc");

            converter.ConvertPsarc(@"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\songs.psarc");
            converter.ConvertFolder(@"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\dlc");
            converter.ConvertFolder(@"C:\Share\Rocksmith DLC");
        }
    }
}