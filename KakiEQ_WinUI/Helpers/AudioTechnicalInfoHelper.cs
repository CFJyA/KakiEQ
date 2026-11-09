using System;
using System.IO;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Helpers
{
    public static class AudioTechnicalInfoHelper
    {
        public static void EnsureTrackTechnicalInfo(Track? track)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
                return;

            bool hasTechSpecs = !string.IsNullOrEmpty(track.AudioFormat) &&
                                track.Bitrate > 0 &&
                                track.BitsPerSample > 0 &&
                                track.BaseDecibels.HasValue;

            bool hasHarmonicSpecs = track.Bpm > 0 && !string.IsNullOrEmpty(track.CamelotKey);

            // If already fully populated, avoid re-extracting
            if (hasTechSpecs && hasHarmonicSpecs)
            {
                return;
            }

            try
            {
                string ext = Path.GetExtension(track.FilePath).TrimStart('.').ToUpperInvariant();
                track.AudioFormat = string.IsNullOrEmpty(ext) ? "AUDIO" : ext;

                using var tfile = TagLib.File.Create(track.FilePath);

                int bitrate = tfile.Properties.AudioBitrate;
                int bits = tfile.Properties.BitsPerSample;
                var duration = tfile.Properties.Duration;

                // Fallback for Bitrate if TagLib reports 0 (e.g. certain AAC/M4A or irregular streams)
                if (bitrate <= 0)
                {
                    double durSeconds = duration.TotalSeconds > 0 ? duration.TotalSeconds : track.Duration.TotalSeconds;
                    if (durSeconds > 0)
                    {
                        try
                        {
                            long fileLen = new FileInfo(track.FilePath).Length;
                            bitrate = (int)Math.Round((fileLen * 8.0) / (durSeconds * 1000.0));
                        }
                        catch { }
                    }
                }

                // Fallback for Bits if TagLib reports 0 (common for lossy codecs like MP3, AAC, OGG)
                if (bits <= 0)
                {
                    bits = 16;
                }

                // Base Decibels: Check ReplayGain Track Gain -> Album Gain -> Track Peak -> default 0.0 dB
                double? baseDb = null;
                if (!double.IsNaN(tfile.Tag.ReplayGainTrackGain))
                {
                    baseDb = tfile.Tag.ReplayGainTrackGain;
                }
                else if (!double.IsNaN(tfile.Tag.ReplayGainAlbumGain))
                {
                    baseDb = tfile.Tag.ReplayGainAlbumGain;
                }
                else if (!double.IsNaN(tfile.Tag.ReplayGainTrackPeak) && tfile.Tag.ReplayGainTrackPeak > 0)
                {
                    baseDb = 20.0 * Math.Log10(tfile.Tag.ReplayGainTrackPeak);
                }
                else
                {
                    baseDb = 0.0;
                }

                track.Bitrate = bitrate;
                track.BitsPerSample = bits;
                track.BaseDecibels = baseDb;

                var (tagBpm, tagKey) = HarmonicAnalysisHelper.ExtractHarmonicMetadataFromTag(tfile);
                if (track.Bpm <= 0 && tagBpm > 0) track.Bpm = tagBpm;
                if (string.IsNullOrEmpty(track.CamelotKey) && !string.IsNullOrEmpty(tagKey)) track.CamelotKey = tagKey;
            }
            catch
            {
                // Safe fallback for locked or unparseable files
                if (string.IsNullOrEmpty(track.AudioFormat))
                {
                    string ext = Path.GetExtension(track.FilePath).TrimStart('.').ToUpperInvariant();
                    track.AudioFormat = string.IsNullOrEmpty(ext) ? "AUDIO" : ext;
                }
                if (track.BitsPerSample <= 0) track.BitsPerSample = 16;
                if (track.Bitrate <= 0) track.Bitrate = 320;
                if (!track.BaseDecibels.HasValue) track.BaseDecibels = 0.0;
            }

            // Fallback DSP Offline si el archivo no tenía etiquetas BPM o INITIALKEY
            if (track.Bpm <= 0 || string.IsNullOrEmpty(track.CamelotKey))
            {
                var (dspBpm, dspKey) = HarmonicAnalysisHelper.EnsureTrackHarmonicSync(track);
                if (track.Bpm <= 0 && dspBpm > 0) track.Bpm = dspBpm;
                if (string.IsNullOrEmpty(track.CamelotKey) && !string.IsNullOrEmpty(dspKey)) track.CamelotKey = dspKey;
            }
        }

        public static string FormatFormat(string? format)
        {
            return string.IsNullOrWhiteSpace(format) ? "AUDIO" : format.ToUpperInvariant();
        }

        public static string FormatBits(int bits)
        {
            int b = bits > 0 ? bits : 16;
            return $"{b}-bit";
        }

        public static string FormatBitrate(int bitrate)
        {
            return bitrate > 0 ? $"{bitrate} kbps" : "--- kbps";
        }

        public static string FormatDecibels(double? baseDb)
        {
            if (!baseDb.HasValue) return "0.0 dB";
            double val = baseDb.Value;
            return val > 0.05 ? $"+{val:0.0} dB" : $"{val:0.0} dB";
        }

        public static string FormatCamelotKey(string? camelotKey)
        {
            return CamelotColorConverter.NormalizeCamelotKey(camelotKey);
        }

        /// <summary>
        /// Formato objetivo: "FLAC · 24-bit · 1762 kbps · 0.0 dB · 8A"
        /// </summary>
        public static string FormatFullSpecification(Track? track)
        {
            if (track == null) return string.Empty;
            string fmt = FormatFormat(track.AudioFormat);
            string bits = FormatBits(track.BitsPerSample);
            string br = FormatBitrate(track.Bitrate);
            string db = FormatDecibels(track.BaseDecibels);
            string key = FormatCamelotKey(track.CamelotKey);
            return string.IsNullOrEmpty(key)
                ? $"{fmt} · {bits} · {br} · {db}"
                : $"{fmt} · {bits} · {br} · {db} · {key}";
        }
    }
}
