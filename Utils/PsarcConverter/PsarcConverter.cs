using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using PsarcUtil;
using SongFormat;
using Rocksmith2014PsarcLib.Psarc.Asset;
using Rocksmith2014PsarcLib.Psarc.Models.Json;
using Rocksmith2014PsarcLib.Psarc.Models.Sng;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Asn1.Crmf;
using System.Linq;

namespace PsarcConverter
{
    public class PsarcConverter
    {
        string destPath;
        bool convertAudio = false;

        JsonSerializerOptions serializerOptions = new JsonSerializerOptions()
        {
            WriteIndented = true,            
        };

        public PsarcConverter(string destPath, bool convertAudio)
        {
            this.destPath = destPath;
            this.convertAudio = convertAudio;
        }

        public void ConvertFolder(string path)
        {
            foreach (string psarcPath in Directory.GetFiles(path, "*.psarc"))
            {
                // This file has song entries, but no audio
                if (Path.GetFileName(psarcPath) == "rs1compatibilitydlc_p.psarc")
                {
                    continue;
                }

                ConvertPsarc(psarcPath);
            }
        }

        public void ConvertPsarc(string psarcPath)
        {
            PsarcDecoder decoder = new PsarcDecoder(psarcPath);

            if (!Directory.Exists(destPath))
            {
                Directory.CreateDirectory(destPath);
            }

            foreach (PsarcSongEntry songEntry in decoder.AllSongs)
            {
                SongData songData = new SongData()
                {
                    SongName = songEntry.SongName,
                    ArtistName = songEntry.ArtistName,
                    AlbumName = songEntry.AlbumName,                    
                };

                string artistDir = Path.Combine(destPath, GetSafeFilename(songData.ArtistName));

                if (!Directory.Exists(artistDir))
                {
                    Directory.CreateDirectory(artistDir);
                }

                string songDir = Path.Combine(artistDir, GetSafeFilename(songData.SongName));

                if (!Directory.Exists(songDir))
                {
                    Directory.CreateDirectory(songDir);
                }

                SongStructure songStructure = new SongStructure();

                foreach (string arrangementName in songEntry.Arrangements.Keys)
                {
                    try
                    {
                        SongArrangement arrangement = songEntry.Arrangements[arrangementName];

                        SngAsset asset = decoder.GetSongAsset(songEntry.SongKey, arrangementName);

                        if (asset != null)
                        {
                            if ((asset.BPMs != null) && (asset.BPMs.Length > songStructure.Beats.Count))
                            {
                                songStructure.Beats.Clear();

                                foreach (Bpm bpm in asset.BPMs)
                                {
                                    songStructure.Beats.Add(new SongBeat
                                    {
                                        TimeOffset = bpm.Time,
                                        IsMeasure = (bpm.Mask > 0),
                                    });
                                }
                            }

                            songData.InstrumentParts.Add(CreateInstrumentPart(songDir, arrangementName, arrangement, asset, decoder));

                            songData.A440CentsOffset = arrangement.Attributes.CentOffset;
                        }
                    }
                    catch { }
                }

                using (FileStream stream = File.Create(Path.Combine(songDir, "song.json")))
                {
                    JsonSerializer.Serialize(stream, songData, serializerOptions);
                }

                using (FileStream stream = File.Create(Path.Combine(songDir, "arrangement.json")))
                {
                    JsonSerializer.Serialize(stream, songStructure, serializerOptions);
                }

                if (convertAudio)
                {
                    using (Stream outputStream = File.Create(Path.Combine(songDir, "song.ogg")))
                    {
                        TextWriter consoleOut = Console.Out;

                        // Suppress Ww2ogg logging
                        Console.SetOut(TextWriter.Null);

                        decoder.WriteOgg(songEntry.SongKey, outputStream);

                        Console.SetOut(consoleOut);
                    }
                }
            }
        }

        string GetSafeFilename(string path)
        {
            return Regex.Replace(path, "[^a-zA-Z0-9]", String.Empty).Trim();
        }

        SongInstrumentPart CreateInstrumentPart(string songDir, string partName, SongArrangement arrangement, SngAsset songAsset, PsarcDecoder decoder)
        {
            SongInstrumentPart part = new SongInstrumentPart()
            {
                InstrumentName = partName,
            };

            if (arrangement.Attributes.ArrangementProperties == null)
            {
                part.InstrumentType = ESongInstrumentType.Vocals;
            }
            else if (arrangement.Attributes.ArrangementProperties.PathLead == 1)
            {
                part.InstrumentType = ESongInstrumentType.LeadGuitar;
            }
            else if (arrangement.Attributes.ArrangementProperties.PathRhythm == 1)
            {
                part.InstrumentType = ESongInstrumentType.RhythmGuitar;
            }
            else if(arrangement.Attributes.ArrangementProperties.PathBass == 1)
            {
                part.InstrumentType = ESongInstrumentType.BassGuitar;
            }


            if (arrangement.Attributes.Tuning != null)
            {
                part.Tuning = new StringTuning()
                {
                    StringSemitoneOffsets = new List<int> { arrangement.Attributes.Tuning.String0, arrangement.Attributes.Tuning.String1, arrangement.Attributes.Tuning.String2, arrangement.Attributes.Tuning.String3, arrangement.Attributes.Tuning.String4 }
                };
            }

            SongInstrumentNotes notes = new SongInstrumentNotes();

            foreach (Chord chord in songAsset.Chords)
            {
                SongChord songChord = new SongChord()
                {
                    Name = chord.Name,
                    Fingers = new List<int>(chord.Fingers.Select(f => (int)((sbyte)f))),
                    Frets = new List<int>(chord.Frets.Select(f => (int)((sbyte)f)))
                };

                notes.Chords.Add(songChord);
            }

            foreach (Note note in decoder.GetNotes(songAsset))
            {
                SongNote songNote = new SongNote()
                {
                    TimeOffset = note.Time,
                    TimeLength = note.Sustain,
                    Fret = (sbyte)note.FretId,
                    String = (sbyte)note.StringIndex,
                    Techniques = ConvertTechniques((NoteMaskFlag)note.NoteMask),
                    HandFret = (sbyte)note.AnchorFretId,
                    SlideFret = (sbyte)note.SlideTo,
                    ChordID = (sbyte)note.ChordId
                };

                if (songNote.SlideFret <= 0)
                {
                    songNote.SlideFret = (sbyte)note.SlideUnpitchTo;
                }

                notes.Notes.Add(songNote);
            }

            using (FileStream stream = File.Create(Path.Combine(songDir, partName + ".json")))
            {
                JsonSerializer.Serialize(stream, notes);
            }

            return part;
        }

        static ESongNoteTechnique ConvertTechniques(NoteMaskFlag noteMask)
        {
            ESongNoteTechnique technique = new ESongNoteTechnique();

            if (noteMask.HasFlag(NoteMaskFlag.HAMMERON))
                technique |= ESongNoteTechnique.HammerOn;

            if (noteMask.HasFlag(NoteMaskFlag.PULLOFF))
                technique |= ESongNoteTechnique.PullOff;

            if (noteMask.HasFlag(NoteMaskFlag.ACCENT))
                technique |= ESongNoteTechnique.Accent;

            if (noteMask.HasFlag(NoteMaskFlag.PALMMUTE))
                technique |= ESongNoteTechnique.PalmMute;

            if (noteMask.HasFlag(NoteMaskFlag.FRETHANDMUTE))
                technique |= ESongNoteTechnique.FretHandMute;

            if (noteMask.HasFlag(NoteMaskFlag.SLIDE) || noteMask.HasFlag(NoteMaskFlag.SLIDEUNPITCHEDTO))
                technique |= ESongNoteTechnique.Slide;

            if (noteMask.HasFlag(NoteMaskFlag.TREMOLO))
                technique |= ESongNoteTechnique.Tremolo;

            if (noteMask.HasFlag(NoteMaskFlag.VIBRATO))
                technique |= ESongNoteTechnique.Vibrato;

            if (noteMask.HasFlag(NoteMaskFlag.HARMONIC))
                technique |= ESongNoteTechnique.Harmonic;

            if (noteMask.HasFlag(NoteMaskFlag.PINCHHARMONIC))
                technique |= ESongNoteTechnique.PinchHarmonic;

            if (noteMask.HasFlag(NoteMaskFlag.TAP))
                technique |= ESongNoteTechnique.Tap;

            if (noteMask.HasFlag(NoteMaskFlag.SLAP))
                technique |= ESongNoteTechnique.Slap;

            if (noteMask.HasFlag(NoteMaskFlag.POP))
                technique |= ESongNoteTechnique.Pop;

            if (noteMask.HasFlag(NoteMaskFlag.CHORD))
                technique |= ESongNoteTechnique.Chord;

            return technique;
        }
    }
}
