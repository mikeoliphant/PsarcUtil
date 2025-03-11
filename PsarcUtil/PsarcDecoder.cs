using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Rocksmith2014PsarcLib.Psarc;
using Rocksmith2014PsarcLib.Psarc.Asset;
using Rocksmith2014PsarcLib.Psarc.Models.Json;
using Rocksmith2014PsarcLib.Psarc.Models.Sng;
using NVorbis;
using BnkExtractor.Ww2ogg;
using BnkExtractor.Revorb;

namespace PsarcUtil
{
    public class PsarcDecoder
    {
        PsarcFile psarcFile;

        Dictionary<string, PsarcSongEntry> songDict = new Dictionary<string, PsarcSongEntry>();
        Dictionary<string, PsarcTOCEntry> tocDict = new Dictionary<string, PsarcTOCEntry>();

        public IEnumerable<PsarcSongEntry> AllSongs
        {
            get { return songDict.Values; }
        }

        public PsarcDecoder(string psarcPath)
        {
            psarcFile = new PsarcFile(psarcPath);

            foreach (var toc in psarcFile.TOC.Entries)
            {
                string fileName = Path.GetFileName(toc.Path);

                tocDict[fileName] = toc;

                if (fileName.EndsWith(".json", StringComparison.InvariantCultureIgnoreCase))
                {
                    AddArrangement(toc);
                }
            }
        }

        public void ExtractAll(string destPath)
        {
            foreach (var toc in psarcFile.TOC.Entries)
            {
                string dir = Path.Combine(destPath, Path.GetDirectoryName(toc.Path));

                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (Stream stream = File.Create(Path.Combine(destPath, toc.Path)))
                {
                    psarcFile.InflateEntry(toc, stream);
                }
            }
        }

        public PsarcTOCEntry GetTOCEntry(string filename)
        {
            if (tocDict.ContainsKey(filename))
                return tocDict[filename];

            return null;
        }

        public PsarcSongEntry GetSongEntry(string songKey)
        {
            if (!songDict.ContainsKey(songKey))
                return null;

            return songDict[songKey];
        }

        public SngAsset GetSongAsset(string songKey, string arrangement)
        {
            string tocName = songKey.ToLower() + "_" + arrangement + ".sng";

            PsarcTOCEntry tocEntry = GetTOCEntry(tocName);

            if (tocEntry == null)
                return null;

            SngAsset song = new();

            using (MemoryStream memStream = new MemoryStream())
            {
                psarcFile.InflateEntry(tocEntry, memStream);

                song.ReadFrom(memStream);
            }

            return song;
        }

        public DdsAsset GetAlbumArtAsset(string songKey, int size)
        {
            string tocName = "album_" + songKey.ToLower() + "_" + size + ".dds";

            PsarcTOCEntry tocEntry = GetTOCEntry(tocName);

            if (tocEntry == null)
                return null;

            DdsAsset dds = new();

            using (MemoryStream memStream = new MemoryStream())
            {
                psarcFile.InflateEntry(tocEntry, memStream);

                dds.ReadFrom(memStream);
            }

            return dds;
        }

        public List<Note> GetNotes(SngAsset song)
        {
            var allNotes = new List<Note>();
            // for each arrangement get the highest note count phrase (phrase.MaxDifficulty needs investigation)
            var phraseIterations = song.PhraseIterations.Select((x, i) => new { x, i });

            foreach (var phr in phraseIterations)
            {
                foreach (var arr in song.Arrangements.OrderByDescending(x => x.Difficulty))
                {
                    var phrNotes = arr.Notes.Where(x => x.PhraseIterationId == phr.i).ToArray();

                    int lastChordID = -1;

                    if (phrNotes.Length > 0)
                    {
                        for (int i = 0; i < phrNotes.Length;i++)
                        {
                            int chordID = -1;
                            float duration = 0;

                            if (phrNotes[i].FingerPrintId[0] != -1)
                            {
                                chordID = arr.Fingerprints1[phrNotes[i].FingerPrintId[0]].ChordId;
                                duration = (arr.Fingerprints1[phrNotes[i].FingerPrintId[0]].EndTime - arr.Fingerprints1[phrNotes[i].FingerPrintId[0]].StartTime);
                            }

                            if (phrNotes[i].FingerPrintId[1] != -1)
                            {
                                chordID = arr.Fingerprints2[phrNotes[i].FingerPrintId[1]].ChordId;
                                duration = (arr.Fingerprints2[phrNotes[i].FingerPrintId[1]].EndTime - arr.Fingerprints2[phrNotes[i].FingerPrintId[1]].StartTime);
                            }

                            if (chordID != -1)
                            {
                                phrNotes[i].ChordId = chordID;

                                if ((i == 0) || (lastChordID != chordID))
                                {
                                    phrNotes[i].NoteMask |= (uint)NoteMaskFlag.CHORD;
                                    phrNotes[i].Sustain = duration;
                                }
                            }

                            lastChordID = chordID;
                        }

                        allNotes.AddRange(phrNotes);

                        break;
                    }
                }
            }

            return allNotes;
        }

        public VorbisReader GetVorbisReader(string songKey)
        {
            MemoryStream revorbStream = new MemoryStream();

            WriteOgg(songKey, revorbStream);

            // Have to re-open because RevorbSharp closes the output stream...
            revorbStream = new MemoryStream(revorbStream.GetBuffer());

            revorbStream.Seek(0, SeekOrigin.Begin);

            return new VorbisReader(revorbStream, true);
        }

        static string CodebookPath = Path.Combine("Ww2ogg", "Codebooks", "packed_codebooks_aoTuV_603.bin");

        public void WriteOgg(string songKey, Stream outputStream, PsarcTOCEntry? bankEntry = null)
        {
            if (bankEntry == null) {
                PsarcSongEntry songEntry = songDict[songKey];

                bankEntry = GetTOCEntry(songEntry.SongBank);

                if (bankEntry == null)
                    throw new InvalidOperationException("Song key [" + songKey + "] has no song bank entry");
            }

            BkhdAsset bank = psarcFile.InflateEntry<BkhdAsset>(bankEntry);

            uint wemID = bank.GetWemId();

            PsarcTOCEntry wemEntry = GetTOCEntry(wemID + ".wem");

            if (wemEntry == null)
                throw new InvalidOperationException("Song key [" + songKey + "] has no wem file");

            using (MemoryStream oggStream = new MemoryStream())
            {
                using (MemoryStream inflateStream = new MemoryStream())
                {
                    psarcFile.InflateEntry(wemEntry, inflateStream);

                    BinaryWriter oggWriter = new BinaryWriter(oggStream);

                    Wwise_RIFF_Vorbis ww = new Wwise_RIFF_Vorbis(inflateStream, Path.Combine(Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]),
                        CodebookPath), false, false, ForcePacketFormat.NoForcePacketFormat);

                    ww.GenerateOgg(oggWriter);
                }

                oggStream.Seek(0, SeekOrigin.Begin);

                RevorbSharp.Convert(oggStream, outputStream);
            }
        }

        void AddArrangement(PsarcTOCEntry toc)
        {
            SongArrangement arrangement = psarcFile.InflateEntry<JsonObjectAsset<SongArrangement>>(toc).JsonObject;

            PsarcSongEntry songEntry = null;

            if (!songDict.ContainsKey(arrangement.Attributes.SongKey))
            {
                songEntry = new PsarcSongEntry()
                {
                    SongKey = arrangement.Attributes.SongKey
                };

                songDict[arrangement.Attributes.SongKey] = songEntry;
            }
            else
            {
                songEntry = songDict[arrangement.Attributes.SongKey];
            }

            if (string.IsNullOrEmpty(songEntry.SongName))
            {
                songEntry.SongName = arrangement.Attributes.SongName;
                songEntry.ArtistName = arrangement.Attributes.ArtistName;
                songEntry.AlbumName = arrangement.Attributes.AlbumName;
                songEntry.SongBank = arrangement.Attributes.SongBank;
            }

            string arrangementName = Path.GetFileNameWithoutExtension(toc.Path).Replace(arrangement.Attributes.SongKey.ToLower() + "_", "");

            songEntry.Arrangements[arrangementName] = arrangement;
        }
    }

    public class PsarcSongEntry
    {
        public string SongKey { get; set; }
        public string SongName { get; set; }
        public string ArtistName { get; set; }
        public string AlbumName { get; set; }
        public string SongBank { get; set; }
        public Dictionary<string, SongArrangement> Arrangements { get; private set; } = new Dictionary<string, SongArrangement>();

        public SongArrangement GetArrangement(string arrangement)
        {
            return Arrangements[arrangement];
        }

        public string GetTuning(SongArrangement songArrangement)
        {
            if (songArrangement == null)
                return null;

            if (IsOffsetFromStandard(songArrangement))
            {
                string key = GetOffsetNote(songArrangement.Attributes.Tuning.String1);

                if (key == null)
                    return "Custom";

                if (songArrangement.Attributes.Tuning.String0 == songArrangement.Attributes.Tuning.String1)
                {
                    return key;
                }
                else // Drop tuning
                {                    
                    string drop = GetDropNote(songArrangement.Attributes.Tuning.String0);

                    if (drop == null)
                        return "Custom";

                    if (key == "E")
                        return "Drop " + drop;

                    return key + " Drop " + drop;
                }
            }

            return "Custom";
        }

        public static bool IsOffsetFromStandard(SongArrangement arrangement)
        {
            return ((arrangement.Attributes.Tuning.String1 == arrangement.Attributes.Tuning.String2) &&
                (arrangement.Attributes.Tuning.String1 == arrangement.Attributes.Tuning.String3) &&
                (arrangement.Attributes.Tuning.String1 == arrangement.Attributes.Tuning.String4) &&
                (arrangement.Attributes.Tuning.String1 == arrangement.Attributes.Tuning.String5));
        }

        public static string GetOffsetNote(int offset)
        {
            switch (offset)
            {
                case 0:
                    return "E";
                case 1:
                    return "F";
                case 2:
                    return "F#";
                case -1:
                    return "Eb";
                case -2:
                    return "D";
                case -3:
                    return "C#";
                case -4:
                    return "C";
                case -5:
                    return "B";
            }

            return null;
        }

        public static string GetDropNote(int offset)
        {
            switch (offset)
            {
                case -1:
                    return "Eb";
                case -2:
                    return "D";
                case -3:
                    return "Db";
                case -4:
                    return "C";
                case -5:
                    return "B";
            }

            return null;
        }
    }

    //
    // Wwise soundbank ".bnk" asset
    //
    // From https://github.com/Murph9/tabplayerV2
    //
    public class BkhdAsset : PsarcAsset
    {
        private UInt32 _bkhd_length;
        private UInt32 _bkhd_version;
        private UInt32 _bkhd_id;

        private UInt32 _didx_length;
        private DIDX[] _didx;
        struct DIDX
        {
            public UInt32 wem_id;
            public UInt32 offset;
            public UInt32 length;
        }

        private Int32 _data_length;

        public override void ReadFrom(MemoryStream stream)
        {
            base.ReadFrom(stream);

            using (var reader = new BinaryReader(stream))
            {
                var bkhdLabel = reader.ReadChars(4);
                _bkhd_length = reader.ReadUInt32();
                _bkhd_version = reader.ReadUInt32();
                _bkhd_id = reader.ReadUInt32();
                var cur = _bkhd_length - 8;
                while (cur > 0)
                {
                    reader.ReadInt32();
                    cur -= 4;
                }

                var didxLabel = reader.ReadChars(4);
                _didx_length = reader.ReadUInt32();
                cur = _didx_length;
                var dList = new List<DIDX>();
                while (cur > 0)
                {
                    var d = new DIDX()
                    {
                        wem_id = reader.ReadUInt32(),
                        offset = reader.ReadUInt32(),
                        length = reader.ReadUInt32(),
                    };
                    dList.Add(d);

                    cur -= 12;
                }
                _didx = dList.ToArray();

                var dataLabel = reader.ReadChars(4);
                _data_length = reader.ReadInt32();
            }
        }

        public UInt32 GetWemId()
        {
            return _didx[0].wem_id;
        }
    }

    [Flags]
    public enum NoteMaskFlag : uint
    {
        // https://github.com/rscustom/rocksmith-custom-song-toolkit/blob/master/RocksmithToolkitLib/Sng/Constants.cs
        UNDEFINED = 0x00,
        MISSING = 0x01,
        CHORD = 0x02,
        OPEN = 0x04,
        FRETHANDMUTE = 0x08,
        TREMOLO = 0x10,
        HARMONIC = 0x20,
        PALMMUTE = 0x40,
        SLAP = 0x80,
        PLUCK = 0x0100,
        POP = 0x0100,
        HAMMERON = 0x0200,
        PULLOFF = 0x0400,
        SLIDE = 0x0800,
        BEND = 0x1000,
        SUSTAIN = 0x2000,
        TAP = 0x4000,
        PINCHHARMONIC = 0x8000,
        VIBRATO = 0x010000,
        MUTE = 0x020000,
        IGNORE = 0x040000,   // ignore=1
        LEFTHAND = 0x00080000,
        RIGHTHAND = 0x00100000,
        HIGHDENSITY = 0x200000,
        SLIDEUNPITCHEDTO = 0x400000,
        SINGLE = 0x00800000, // single note
        CHORDNOTES = 0x01000000, // has chordnotes exported
        DOUBLESTOP = 0x02000000,
        ACCENT = 0x04000000,
        PARENT = 0x08000000, // linkNext=1
        CHILD = 0x10000000, // note after linkNext=1
        ARPEGGIO = 0x20000000,
        MISSING2 = 0x40000000,
        STRUM = 0x80000000, // handShape defined at chord time
    }
}
