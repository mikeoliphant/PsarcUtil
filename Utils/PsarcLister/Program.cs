using System;
using System.IO;
using PsarcUtil;

namespace PsarcLister
{
    class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("\nUsage: PsarcLister <PsarcPath>\n");

                return;
            }

            string psarcPath = args[0];

            try
            {
                PsarcDecoder decoder = new PsarcDecoder(psarcPath);

                foreach (PsarcSongEntry song in decoder.AllSongs)
                {
                    Console.WriteLine("Song key: " + song.SongKey + " (" + song.ArtistName + " - " + song.SongName + ")");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\nError listing file:\n\n" + ex.ToString() + "\n");
            }
        }
    }
}