namespace ChordLister
{
    class Program
    {
        public static void Main(string[] args)
        {
            ChordLister lister = new ChordLister();


            lister.ParsePsarc(@"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\songs.psarc");
            lister.ParseFolder(@"C:\Program Files (x86)\Steam\steamapps\common\Rocksmith2014\dlc");
            lister.ParseFolder(@"C:\Share\Rocksmith DLC");
        }
    }
}