using System;
using System.IO;
using PsarcUtil;

namespace PsarcExtract
{
    class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("\nUsage: PsarcExtract <PsarcPath> <ExtractPath>\n");

                return;
            }

            string psarcPath = args[0];

            try
            {
                if (!File.Exists(psarcPath))
                {
                    Console.Write("\nFile [" + psarcPath + "] does not exist\n");

                    return;
                }

                PsarcDecoder decoder = new PsarcDecoder(psarcPath);

                decoder.ExtractAll(args[1]);

                //foreach (PsarcSongEntry song in decoder.AllSongs)
                //{
                //    Console.WriteLine("Extracting song key: " + song.SongKey + " (" + song.ArtistName + " - " + song.SongName + ")");

                //    Directory.CreateDirectory(song.SongKey);

                //    using (Stream outputStream = File.Create(Path.Combine(song.SongKey, "Song.ogg")))
                //    {
                //        TextWriter consoleOut = Console.Out;

                //        // Suppress Ww2ogg logging
                //        Console.SetOut(TextWriter.Null);
                        
                //        decoder.WriteOgg(song.SongKey, outputStream);

                //        Console.SetOut(consoleOut);
                //    }
                //}
            }
            catch (Exception ex)
            {
                Console.WriteLine("\nError extracting file:\n\n" + ex.ToString() + "\n");
            }
        }
    }
}