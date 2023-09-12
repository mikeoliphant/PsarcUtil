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
    [Flags]
    public enum EArrangement
    {
        Lead = 1,
        Rhythm = 2,
        Bass = 4,
        Vocals = 8
    }

    public class PsarcIndexEntry
    {
        public string SongKey { get; set; }
        public string SongName { get; set; }
        public string ArtistName { get; set; }
        public string AlbumName { get; set; }
        public string PsarcPath { get; set; }
        public EArrangement Arrangements { get; set; }
        public string LeadTuning { get; set; }
        public string RhythmTuning { get; set;}
        public string BassTuning { get; set; } 
    }

    public class PsarcIndex
    {
        Dictionary<string, PsarcIndexEntry> dict = new Dictionary<string, PsarcIndexEntry>();

        public List<PsarcIndexEntry> GetSongList()
        {
            return dict.Values.ToList();
        }

        public bool LoadFromJson(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                return false;
            }

            List<PsarcIndexEntry> songs;

            using (FileStream stream = File.OpenRead(jsonPath))
            {
                songs = JsonSerializer.Deserialize(stream, typeof(List<PsarcIndexEntry>)) as List<PsarcIndexEntry>;
            }
            
            foreach (var song in songs)
            {
                dict[song.SongKey] = song;
            }

            return true;
        }

        public void WriteJson(string jsonPath)
        {
            using (FileStream stream = File.Create(jsonPath))
            {
                JsonSerializer.Serialize(stream, GetSongList());
            }
        }

        public void AddPsarcFolder(string folderPath)
        {
            foreach (string path in Directory.GetFiles(folderPath, "*.psarc"))
            {
                AddPsarc(path);
            }
        }

        public void AddPsarc(string psarcPath)
        {
            // This file has song entries, but no audio
            if (Path.GetFileName(psarcPath) == "rs1compatibilitydlc_p.psarc")
            {
                return;
            }
                
            PsarcDecoder decoder = new PsarcDecoder(psarcPath);

            foreach (PsarcSongEntry song in decoder.AllSongs)
            {
                PsarcIndexEntry songEntry = new PsarcIndexEntry()
                {
                    SongKey = song.SongKey,
                    SongName = song.SongName,
                    ArtistName = song.ArtistName,
                    AlbumName = song.AlbumName,
                    PsarcPath = psarcPath,
                    LeadTuning = song.GetTuning(EArrangement.Lead),
                    RhythmTuning = song.GetTuning(EArrangement.Rhythm),
                    BassTuning = song.GetTuning(EArrangement.Bass),
                };
                
                if (dict.ContainsKey(songEntry.SongKey))
                {
                }
                else
                {
                    dict[songEntry.SongKey] = songEntry;
                }
            }
        }

        public PsarcDecoder GetPsarcDecoder(string songKey)
        {
            return new PsarcDecoder(dict[songKey].PsarcPath);
        }
    }

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
                    SongArrangement arrangement = psarcFile.InflateEntry<JsonObjectAsset<SongArrangement>>(toc).JsonObject;

                    AddArrangement(arrangement);
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

        public SngAsset GetSongAsset(string songKey, EArrangement arrangement)
        {
            string tocName = songKey.ToLower() + "_" + arrangement.ToString().ToLower() + ".sng";

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

        public List<Note> GetNotes(SngAsset song)
        {
            var allNotes = new List<Note>();
            // for each arrangement get the highest note count phrase (phrase.MaxDifficulty needs investigation)
            var phraseIterations = song.PhraseIterations.Select((x, i) => new { x, i });

            foreach (var phr in phraseIterations)
            {
                foreach (var arr in song.Arrangements.OrderByDescending(x => x.Difficulty))
                {
                    var phrNotes = arr.Notes.Where(x => x.PhraseIterationId == phr.i);
                    if (phrNotes.Any())
                    {
                        allNotes.AddRange(phrNotes);
                        break;
                    }
                }
            }

            return allNotes;
        }

        public VorbisReader GetVorbisReader(string songKey)
        {
            PsarcSongEntry songEntry = songDict[songKey];

            PsarcTOCEntry bankEntry = GetTOCEntry(songEntry.SongBank);

            if (bankEntry == null)
                return null;

            BkhdAsset bank = psarcFile.InflateEntry<BkhdAsset>(bankEntry);

            uint wemID = bank.GetWemId();

            PsarcTOCEntry wemEntry = GetTOCEntry(wemID + ".wem");

            if (wemEntry == null)
                return null;

            MemoryStream revorbStream = new MemoryStream();

            using (MemoryStream oggStream = new MemoryStream())
            {
                using (MemoryStream inflateStream = new MemoryStream())
                {
                    psarcFile.InflateEntry(wemEntry, inflateStream);

                    BinaryWriter oggWriter = new BinaryWriter(oggStream);

                    Wwise_RIFF_Vorbis ww = new Wwise_RIFF_Vorbis(inflateStream, Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Ww2ogg\\Codebooks\\packed_codebooks_aoTuV_603.bin"), false, false, ForcePacketFormat.NoForcePacketFormat);

                    ww.GenerateOgg(oggWriter);
                }

                oggStream.Seek(0, SeekOrigin.Begin);

                RevorbSharp.Convert(oggStream, revorbStream);

                // Have to re-open because RevorbSharp closes the output stream...
                revorbStream = new MemoryStream(revorbStream.GetBuffer());

                revorbStream.Seek(0, SeekOrigin.Begin);
            }

            return new VorbisReader(revorbStream, true);
        }

        void AddArrangement(SongArrangement arrangement)
        {
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

            if (arrangement.Attributes.ArrangementProperties != null)
            {
                if (arrangement.Attributes.ArrangementProperties.PathBass == 1)
                {
                    songEntry.BassArrangement = arrangement;
                }
                else if (arrangement.Attributes.ArrangementProperties.PathRhythm == 1)
                {
                    songEntry.RhythmArrangement = arrangement;
                }
                else if (arrangement.Attributes.ArrangementProperties.PathLead == 1)
                {
                    songEntry.LeadArrangement = arrangement;
                }
            }
            else
            {
                songEntry.VocalArrangement = arrangement;
            }
        }
    }

    public class PsarcSongEntry
    {
        public string SongKey { get; set; }
        public string SongName { get; set; }
        public string ArtistName { get; set; }
        public string AlbumName { get; set; }
        public string SongBank { get; set; }
        public SongArrangement RhythmArrangement { get; set; }
        public SongArrangement LeadArrangement { get; set; }
        public SongArrangement BassArrangement { get; set; }
        public SongArrangement VocalArrangement { get; set; }

        public SongArrangement GetArrangement(EArrangement arrangement)
        {
            switch (arrangement)
            {
                case EArrangement.Lead:
                    return LeadArrangement;
                case EArrangement.Rhythm:
                    return RhythmArrangement;
                case EArrangement.Bass:
                    return BassArrangement;
                case EArrangement.Vocals:
                    return VocalArrangement;
            }

            return null;
        }

        public string GetTuning(EArrangement arrangement)
        {
            SongArrangement songArrangement = GetArrangement(arrangement);

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
