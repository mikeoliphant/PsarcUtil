using System;
using System.Collections.Generic;
using NVorbis;
using Rocksmith2014PsarcLib.Psarc.Asset;
using Rocksmith2014PsarcLib.Psarc.Models.Json;
using Rocksmith2014PsarcLib.Psarc.Models.Sng;

namespace PsarcUtil
{
    public class SongPlayer
    {
        public double PlaybackSampleRate { get; private set; } = 48000;
        public double CurrentSecond { get; private set; } = 0;
        public List<Note> Notes { get; private set; } = null;
        public Bpm[] Beats { get; private set; } = null;

        VorbisReader vorbisReader;
        WdlResampler resampler;
        double tuningOffsetSemitones = 0;
        double actualPlaybackSampleRate = 48000;

        public SongPlayer()
        {
            resampler = new WdlResampler();

            resampler.SetMode(true, 2, false);
            resampler.SetFilterParms();
            resampler.SetFeedMode(false); // output driven
        }

        public void SetPlaybackSampleRate(double playbackRate)
        {
            this.PlaybackSampleRate = playbackRate;
        }
        
        public void SetSong(PsarcDecoder decoder, string songKey, string instrument)
        {
            PsarcSongEntry songEntry = decoder.GetSongEntry(songKey);

            if (songEntry == null)
            {
                throw new InvalidOperationException("No song entry for: " + songKey);
            }

            SongArrangement arrangement = songEntry.BassArrangement;

            if (arrangement == null)
            {
                throw new InvalidOperationException("Song has no arrangement for " + instrument);
            }

            SngAsset sng = decoder.GetSongAsset(songKey, instrument);

            if (sng == null)
            {
                throw new InvalidOperationException("Song has no arrangement for " + instrument);
            }

            Notes = decoder.GetNotes(sng);

            Beats = sng.BPMs;

            int numStrings = 4;

            if (arrangement.Attributes.Tuning != null)
            {
                // Check for fixed offset from standard tuning
                if ((arrangement.Attributes.Tuning.String1 == arrangement.Attributes.Tuning.String2) &&
                    (arrangement.Attributes.Tuning.String1 == arrangement.Attributes.Tuning.String3) &&
                    (arrangement.Attributes.Tuning.String1 == arrangement.Attributes.Tuning.String4) &&
                    (arrangement.Attributes.Tuning.String1 == arrangement.Attributes.Tuning.String5))
                {
                    tuningOffsetSemitones = arrangement.Attributes.Tuning.String1;
                }
            }

            tuningOffsetSemitones += (double)arrangement.Attributes.CentOffset / 100.0;

            if (tuningOffsetSemitones == 0)
                actualPlaybackSampleRate = PlaybackSampleRate;
            else
                actualPlaybackSampleRate = PlaybackSampleRate * Math.Pow(2, (double)tuningOffsetSemitones / 12.0);

            vorbisReader = decoder.GetVorbisReader(songKey);

            if (vorbisReader == null)
            {
                throw new InvalidOperationException("Song has no audio file");
            }

            resampler.SetRates(vorbisReader.SampleRate, actualPlaybackSampleRate);
        }

        public void SeekTime(float secs)
        {
            vorbisReader.TimePosition = TimeSpan.FromSeconds(secs);
        }

        public int ReadSamples(float[] buffer)
        {
            int read = 0;

            if (actualPlaybackSampleRate == vorbisReader.SampleRate)
            {
                read = vorbisReader.ReadSamples(buffer);
            }
            else
            {
                float[] inBuffer;
                int inBufferOffset;
                int framesRequested = buffer.Length / 2;

                int inNeeded = resampler.ResamplePrepare(framesRequested, 2, out inBuffer, out inBufferOffset);
                int inAvailable = vorbisReader.ReadSamples(inBuffer, inBufferOffset, inNeeded * 2) / 2;
                read = resampler.ResampleOut(buffer, 0, inAvailable, framesRequested, 2) * 2;
            }

            CurrentSecond = vorbisReader.TimePosition.TotalSeconds;

            return read;
        }
    }
}
