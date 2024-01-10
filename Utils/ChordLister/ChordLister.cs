using PsarcUtil;
using Rocksmith2014PsarcLib.Psarc.Asset;
using Rocksmith2014PsarcLib.Psarc.Models.Json;
using Rocksmith2014PsarcLib.Psarc.Models.Sng;

namespace ChordLister
{
    public class ChordLister
    {
        public ChordLister()
        {
        }

        public void ParseFolder(string path)
        {
            foreach (string psarcPath in Directory.GetFiles(path, "*.psarc"))
            {
                ParsePsarc(psarcPath);
            }
        }

        public void ParsePsarc(string psarcPath)
        {
            PsarcDecoder decoder = new PsarcDecoder(psarcPath);

            Dictionary<int, bool> chordNoteDict = new();

            foreach (PsarcSongEntry songEntry in decoder.AllSongs)
            {
                foreach (string arrangementName in songEntry.Arrangements.Keys)
                {
                    bool wroteSong = false;

                    try
                    {
                        SongArrangement arrangement = songEntry.Arrangements[arrangementName];

                        SngAsset asset = decoder.GetSongAsset(songEntry.SongKey, arrangementName);

                        if ((asset != null) && (asset.ChordNotes != null))
                        {
                            foreach (Note note in decoder.GetNotes(asset))
                            {
                                if ((note.ChordId != -1) && (note.ChordNotesId != -1))
                                {
                                    if (!chordNoteDict.ContainsKey(note.ChordNotesId))
                                    {
                                        Chord chord = asset.Chords[note.ChordId];
                                        ChordNotes chordNotes = asset.ChordNotes[note.ChordNotesId];

                                        if (!wroteSong)
                                        {
                                            Console.WriteLine(songEntry.ArtistName + "-" + songEntry.SongName + "-" + arrangementName);

                                            wroteSong = true;
                                        }

                                        Console.WriteLine(chord.Name + "[" + note.ChordId + "/" + note.ChordNotesId + "]" + ((note.NoteMask != 0) ? (" (" + (NoteMaskFlag)note.NoteMask + ")") : ""));

                                        for (int str = 0; str < 6; str++)
                                        {
                                            Console.WriteLine(str + ": " + (sbyte)chord.Frets[str] + ((chordNotes.BendData[str].UsedCount != 0) ? " BEND" : "") + (((sbyte)chordNotes.SlideTo[str] != -1) ? " SLIDE" : "") + 
                                                ((chordNotes.NoteMask[str] != 0) ? (" (" + ((NoteMaskFlag)chordNotes.NoteMask[str]).ToString() + ")") : "") + ((chordNotes.Vibrato[str] != 0) ? " VIB" : ""));
                                        }

                                        Console.WriteLine();

                                        chordNoteDict[note.ChordNotesId] = true;
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    if (wroteSong)
                        Console.WriteLine();
                }
            }
        }
    }
}

