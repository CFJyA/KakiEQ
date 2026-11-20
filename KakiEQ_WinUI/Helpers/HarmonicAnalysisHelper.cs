using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using ManagedBass;
using KakiEQ_WinUI.Models;
using KakiEQ_WinUI.Services.Data;

namespace KakiEQ_WinUI.Helpers
{
    /// <summary>
    /// Motor de extracción de etiquetas (BPM / INITIALKEY), mapeo de Rueda Camelot,
    /// análisis DSP asíncrono (Cromagrama + detección de BPM por envolvente de energía con BASS)
    /// y algoritmo de ordenamiento armónico para DJing.
    /// </summary>
    public static class HarmonicAnalysisHelper
    {
        // Mapeo exhaustivo de notaciones musicales tradicionales, internacionales y OpenKey a Camelot (1A–12B)
        private static readonly Dictionary<string, string> KeyToCamelotMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Directos Camelot
            ["1A"] = "1A", ["01A"] = "1A", ["1B"] = "1B", ["01B"] = "1B",
            ["2A"] = "2A", ["02A"] = "2A", ["2B"] = "2B", ["02B"] = "2B",
            ["3A"] = "3A", ["03A"] = "3A", ["3B"] = "3B", ["03B"] = "3B",
            ["4A"] = "4A", ["04A"] = "4A", ["4B"] = "4B", ["04B"] = "4B",
            ["5A"] = "5A", ["05A"] = "5A", ["5B"] = "5B", ["05B"] = "5B",
            ["6A"] = "6A", ["06A"] = "6A", ["6B"] = "6B", ["06B"] = "6B",
            ["7A"] = "7A", ["07A"] = "7A", ["7B"] = "7B", ["07B"] = "7B",
            ["8A"] = "8A", ["08A"] = "8A", ["8B"] = "8B", ["08B"] = "8B",
            ["9A"] = "9A", ["09A"] = "9A", ["9B"] = "9B", ["09B"] = "9B",
            ["10A"] = "10A", ["10B"] = "10B",
            ["11A"] = "11A", ["11B"] = "11B",
            ["12A"] = "12A", ["12B"] = "12B",

            // --- MENORES (Columna A) ---
            // 1A: Ab minor / G# minor
            ["ABM"] = "1A", ["ABMIN"] = "1A", ["AB MINOR"] = "1A", ["A-FLAT MINOR"] = "1A", ["ABMINOR"] = "1A",
            ["G#M"] = "1A", ["G#MIN"] = "1A", ["G# MINOR"] = "1A", ["G-SHARP MINOR"] = "1A", ["G#MINOR"] = "1A",
            ["SOL#M"] = "1A", ["LABM"] = "1A", ["6M"] = "1A",

            // 2A: Eb minor / D# minor
            ["EBM"] = "2A", ["EBMIN"] = "2A", ["EB MINOR"] = "2A", ["E-FLAT MINOR"] = "2A", ["EBMINOR"] = "2A",
            ["D#M"] = "2A", ["D#MIN"] = "2A", ["D# MINOR"] = "2A", ["D-SHARP MINOR"] = "2A", ["D#MINOR"] = "2A",
            ["RE#M"] = "2A", ["MIBM"] = "2A", ["7M"] = "2A",

            // 3A: Bb minor / A# minor
            ["BBM"] = "3A", ["BBMIN"] = "3A", ["BB MINOR"] = "3A", ["B-FLAT MINOR"] = "3A", ["BBMINOR"] = "3A",
            ["A#M"] = "3A", ["A#MIN"] = "3A", ["A# MINOR"] = "3A", ["A-SHARP MINOR"] = "3A", ["A#MINOR"] = "3A",
            ["LA#M"] = "3A", ["SIBM"] = "3A", ["8M"] = "3A",

            // 4A: F minor
            ["FM"] = "4A", ["FMIN"] = "4A", ["F MINOR"] = "4A", ["FMINOR"] = "4A", ["F-MINOR"] = "4A",
            ["FA MENOR"] = "4A", ["FAM"] = "4A", ["9M"] = "4A",

            // 5A: C minor
            ["CM"] = "5A", ["CMIN"] = "5A", ["C MINOR"] = "5A", ["CMINOR"] = "5A", ["C-MINOR"] = "5A",
            ["DO MENOR"] = "5A", ["DOM"] = "5A", ["10M"] = "5A",

            // 6A: G minor
            ["GM"] = "6A", ["GMIN"] = "6A", ["G MINOR"] = "6A", ["GMINOR"] = "6A", ["G-MINOR"] = "6A",
            ["SOL MENOR"] = "6A", ["SOLM"] = "6A", ["11M"] = "6A",

            // 7A: D minor
            ["DM"] = "7A", ["DMIN"] = "7A", ["D MINOR"] = "7A", ["DMINOR"] = "7A", ["D-MINOR"] = "7A",
            ["RE MENOR"] = "7A", ["REM"] = "7A", ["12M"] = "7A",

            // 8A: A minor
            ["AM"] = "8A", ["AMIN"] = "8A", ["A MINOR"] = "8A", ["AMINOR"] = "8A", ["A-MINOR"] = "8A",
            ["LA MENOR"] = "8A", ["LAM"] = "8A", ["1M"] = "8A",

            // 9A: E minor
            ["EM"] = "9A", ["EMIN"] = "9A", ["E MINOR"] = "9A", ["EMINOR"] = "9A", ["E-MINOR"] = "9A",
            ["MI MENOR"] = "9A", ["MIM"] = "9A", ["2M"] = "9A",

            // 10A: B minor
            ["BM"] = "10A", ["BMIN"] = "10A", ["B MINOR"] = "10A", ["BMINOR"] = "10A", ["B-MINOR"] = "10A",
            ["SI MENOR"] = "10A", ["SIM"] = "10A", ["3M"] = "10A",

            // 11A: F# minor / Gb minor
            ["F#M"] = "11A", ["F#MIN"] = "11A", ["F# MINOR"] = "11A", ["F-SHARP MINOR"] = "11A", ["F#MINOR"] = "11A",
            ["GBM"] = "11A", ["GBMIN"] = "11A", ["GB MINOR"] = "11A", ["G-FLAT MINOR"] = "11A", ["GBMINOR"] = "11A",
            ["FA#M"] = "11A", ["FA# MENOR"] = "11A", ["4M"] = "11A",

            // 12A: C# minor / Db minor
            ["C#M"] = "12A", ["C#MIN"] = "12A", ["C# MINOR"] = "12A", ["C-SHARP MINOR"] = "12A", ["C#MINOR"] = "12A",
            ["DBM"] = "12A", ["DBMIN"] = "12A", ["DB MINOR"] = "12A", ["D-FLAT MINOR"] = "12A", ["DBMINOR"] = "12A",
            ["DO#M"] = "12A", ["DO# MENOR"] = "12A", ["5M"] = "12A",

            // --- MAYORES (Columna B) ---
            // 1B: B Major / Cb Major
            ["B"] = "1B", ["BMAJ"] = "1B", ["B MAJOR"] = "1B", ["BMAJOR"] = "1B", ["B-MAJOR"] = "1B",
            ["CB"] = "1B", ["CB MAJOR"] = "1B", ["SI MAYOR"] = "1B", ["6D"] = "1B",

            // 2B: F# Major / Gb Major
            ["F#"] = "2B", ["F#MAJ"] = "2B", ["F# MAJOR"] = "2B", ["F-SHARP MAJOR"] = "2B", ["F#MAJOR"] = "2B",
            ["GB"] = "2B", ["GBMAJ"] = "2B", ["GB MAJOR"] = "2B", ["G-FLAT MAJOR"] = "2B", ["GBMAJOR"] = "2B",
            ["FA# MAYOR"] = "2B", ["SOLB MAYOR"] = "2B", ["7D"] = "2B",

            // 3B: Db Major / C# Major
            ["DB"] = "3B", ["DBMAJ"] = "3B", ["DB MAJOR"] = "3B", ["D-FLAT MAJOR"] = "3B", ["DBMAJOR"] = "3B",
            ["C#"] = "3B", ["C#MAJ"] = "3B", ["C# MAJOR"] = "3B", ["C-SHARP MAJOR"] = "3B", ["C#MAJOR"] = "3B",
            ["REB MAYOR"] = "3B", ["DO# MAYOR"] = "3B", ["8D"] = "3B",

            // 4B: Ab Major / G# Major
            ["AB"] = "4B", ["ABMAJ"] = "4B", ["AB MAJOR"] = "4B", ["A-FLAT MAJOR"] = "4B", ["ABMAJOR"] = "4B",
            ["G#"] = "4B", ["G#MAJ"] = "4B", ["G# MAJOR"] = "4B", ["G-SHARP MAJOR"] = "4B", ["G#MAJOR"] = "4B",
            ["LAB MAYOR"] = "4B", ["SOL# MAYOR"] = "4B", ["9D"] = "4B",

            // 5B: Eb Major / D# Major
            ["EB"] = "5B", ["EBMAJ"] = "5B", ["EB MAJOR"] = "5B", ["E-FLAT MAJOR"] = "5B", ["EBMAJOR"] = "5B",
            ["D#"] = "5B", ["D#MAJ"] = "5B", ["D# MAJOR"] = "5B", ["D-SHARP MAJOR"] = "5B", ["D#MAJOR"] = "5B",
            ["MIB MAYOR"] = "5B", ["RE# MAYOR"] = "5B", ["10D"] = "5B",

            // 6B: Bb Major / A# Major
            ["BB"] = "6B", ["BBMAJ"] = "6B", ["BB MAJOR"] = "6B", ["B-FLAT MAJOR"] = "6B", ["BBMAJOR"] = "6B",
            ["A#"] = "6B", ["A#MAJ"] = "6B", ["A# MAJOR"] = "6B", ["A-SHARP MAJOR"] = "6B", ["A#MAJOR"] = "6B",
            ["SIB MAYOR"] = "6B", ["LA# MAYOR"] = "6B", ["11D"] = "6B",

            // 7B: F Major
            ["F"] = "7B", ["FMAJ"] = "7B", ["F MAJOR"] = "7B", ["FMAJOR"] = "7B", ["F-MAJOR"] = "7B",
            ["FA MAYOR"] = "7B", ["12D"] = "7B",

            // 8B: C Major
            ["C"] = "8B", ["CMAJ"] = "8B", ["C MAJOR"] = "8B", ["CMAJOR"] = "8B", ["C-MAJOR"] = "8B",
            ["DO MAYOR"] = "8B", ["1D"] = "8B",

            // 9B: G Major
            ["G"] = "9B", ["GMAJ"] = "9B", ["G MAJOR"] = "9B", ["GMAJOR"] = "9B", ["G-MAJOR"] = "9B",
            ["SOL MAYOR"] = "9B", ["2D"] = "9B",

            // 10B: D Major
            ["D"] = "10B", ["DMAJ"] = "10B", ["D MAJOR"] = "10B", ["DMAJOR"] = "10B", ["D-MAJOR"] = "10B",
            ["RE MAYOR"] = "10B", ["3D"] = "10B",

            // 11B: A Major
            ["A"] = "11B", ["AMAJ"] = "11B", ["A MAJOR"] = "11B", ["AMAJOR"] = "11B", ["A-MAJOR"] = "11B",
            ["LA MAYOR"] = "11B", ["4D"] = "11B",

            // 12B: E Major
            ["E"] = "12B", ["EMAJ"] = "12B", ["E MAJOR"] = "12B", ["EMAJOR"] = "12B", ["E-MAJOR"] = "12B",
            ["MI MAYOR"] = "12B", ["5D"] = "12B"
        };

        // Mapeo por índice cromático (0=C, 1=C#, 2=D, 3=D#, 4=E, 5=F, 6=F#, 7=G, 8=G#, 9=A, 10=A#, 11=B)
        private static readonly string[] PitchClassToCamelotMajor =
        {
            "8B",  // C
            "3B",  // C# / Db
            "10B", // D
            "5B",  // D# / Eb
            "12B", // E
            "7B",  // F
            "2B",  // F# / Gb
            "9B",  // G
            "4B",  // G# / Ab
            "11B", // A
            "6B",  // A# / Bb
            "1B"   // B
        };

        private static readonly string[] PitchClassToCamelotMinor =
        {
            "5A",  // Cm
            "12A", // C#m / Dbm
            "7A",  // Dm
            "2A",  // D#m / Ebm
            "9A",  // Em
            "4A",  // Fm
            "11A", // F#m / Gbm
            "6A",  // Gm
            "1A",  // G#m / Abm
            "8A",  // Am
            "3A",  // A#m / Bbm
            "10A"  // Bm
        };

        // Perfiles tonales CBMS de Temperley (2007) para Cromagrama Armónico de Alta Precisión (HPCP-36)
        private static readonly double[] TemperleyMajorProfile =
        {
            5.0, 2.0, 3.5, 2.0, 4.5, 4.0, 2.0, 4.5, 2.0, 3.5, 1.5, 4.0
        };

        private static readonly double[] TemperleyMinorProfile =
        {
            5.0, 2.0, 3.5, 4.5, 2.0, 4.0, 2.0, 4.5, 3.5, 2.0, 1.5, 4.0
        };

        private sealed class FftPlan
        {
            public readonly int Size;
            public readonly int[] BitRev;
            public readonly double[] Hann;
            public readonly float[] CosTable;
            public readonly float[] SinTable;

            public FftPlan(int size)
            {
                Size = size;
                BitRev = new int[size];
                int bits = (int)Math.Log2(size);
                for (int i = 0; i < size; i++)
                {
                    int r = 0, x = i;
                    for (int b = 0; b < bits; b++)
                    {
                        r = (r << 1) | (x & 1);
                        x >>= 1;
                    }
                    BitRev[i] = r;
                }

                Hann = new double[size];
                for (int i = 0; i < size; i++)
                {
                    Hann[i] = 0.5 * (1.0 - Math.Cos((2.0 * Math.PI * i) / (size - 1)));
                }

                CosTable = new float[size / 2];
                SinTable = new float[size / 2];
                for (int k = 0; k < size / 2; k++)
                {
                    double ang = -2.0 * Math.PI * k / size;
                    CosTable[k] = (float)Math.Cos(ang);
                    SinTable[k] = (float)Math.Sin(ang);
                }
            }

            public void Forward(float[] real, float[] imag)
            {
                int n = Size;
                for (int i = 0; i < n; i++)
                {
                    int j = BitRev[i];
                    if (i < j)
                    {
                        (real[i], real[j]) = (real[j], real[i]);
                        (imag[i], imag[j]) = (imag[j], imag[i]);
                    }
                }

                for (int len = 2; len <= n; len <<= 1)
                {
                    int half = len >> 1;
                    int step = n / len;
                    for (int i = 0; i < n; i += len)
                    {
                        int tblIdx = 0;
                        for (int k = 0; k < half; k++)
                        {
                            float wRe = CosTable[tblIdx];
                            float wIm = SinTable[tblIdx];
                            tblIdx += step;

                            int even = i + k;
                            int odd = even + half;
                            float oRe = real[odd];
                            float oIm = imag[odd];
                            float tRe = wRe * oRe - wIm * oIm;
                            float tIm = wRe * oIm + wIm * oRe;
                            real[odd] = real[even] - tRe;
                            imag[odd] = imag[even] - tIm;
                            real[even] += tRe;
                            imag[even] += tIm;
                        }
                    }
                }
            }
        }

        private static readonly FftPlan Plan1024 = new(1024);
        private static readonly FftPlan Plan4096 = new(4096);
        private static readonly object _bassInitLock = new();

        private static readonly ConcurrentQueue<Track> AnalysisQueue = new();
        private static readonly ConcurrentDictionary<string, byte> QueuedPaths = new(StringComparer.OrdinalIgnoreCase);
        private static int _isWorkerRunning = 0;
        private static DatabaseService? _activeDbService;
        private static DispatcherQueue? _uiDispatcher;

        /// <summary>
        /// Convierte cualquier notación de clave musical (ej. "Am", "A minor", "8A", "C Major")
        /// al formato estándar de la Rueda Camelot (1A–12A o 1B–12B).
        /// </summary>
        public static string MapKeyToCamelot(string? rawKey)
        {
            if (string.IsNullOrWhiteSpace(rawKey))
                return string.Empty;

            string cleaned = rawKey.Trim()
                .Replace('♯', '#')
                .Replace('♭', 'b')
                .Replace("min.", "m", StringComparison.OrdinalIgnoreCase)
                .Replace("maj.", "", StringComparison.OrdinalIgnoreCase);

            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            if (KeyToCamelotMap.TryGetValue(cleaned, out var exact))
                return exact;

            // Si viene compuesto como "8A - Am" o "Am (8A)"
            var camelotRegex = Regex.Match(cleaned, @"\b(0?[1-9]|1[0-2])[AB]\b", RegexOptions.IgnoreCase);
            if (camelotRegex.Success)
            {
                string code = camelotRegex.Value.ToUpperInvariant();
                if (code.Length == 3 && code[0] == '0')
                    code = code.Substring(1);
                return code;
            }

            return string.Empty;
        }

        /// <summary>
        /// Extrae BPM y Clave Camelot directamente desde las etiquetas locales (ID3v2, Vorbis/Xiph, Apple MP4, APE).
        /// </summary>
        public static (double bpm, string camelotKey) ExtractHarmonicMetadataFromTag(TagLib.File tfile)
        {
            if (tfile?.Tag == null)
                return (0, string.Empty);

            double bpm = 0;
            string rawKey = string.Empty;

            try
            {
                if (tfile.Tag.BeatsPerMinute > 0)
                {
                    bpm = tfile.Tag.BeatsPerMinute;
                }

                if (!string.IsNullOrWhiteSpace(tfile.Tag.InitialKey))
                {
                    rawKey = tfile.Tag.InitialKey;
                }

                // 1. Revisar ID3v2 (TBPM, TKEY, TXXX)
                if (tfile.GetTag(TagLib.TagTypes.Id3v2, false) is TagLib.Id3v2.Tag id3v2)
                {
                    if (bpm <= 0)
                    {
                        var tbpm = TagLib.Id3v2.TextInformationFrame.Get(id3v2, "TBPM", false);
                        if (tbpm != null && tbpm.Text.Length > 0)
                            bpm = ParseBpmString(tbpm.Text[0]);

                        if (bpm <= 0)
                        {
                            foreach (string desc in new[] { "BPM", "TEMPO", "fBPM", "FBPM" })
                            {
                                var txxx = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, desc, false);
                                if (txxx != null && txxx.Text.Length > 0)
                                {
                                    bpm = ParseBpmString(txxx.Text[0]);
                                    if (bpm > 0) break;
                                }
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(rawKey))
                    {
                        var tkey = TagLib.Id3v2.TextInformationFrame.Get(id3v2, "TKEY", false);
                        if (tkey != null && tkey.Text.Length > 0)
                            rawKey = tkey.Text[0];

                        if (string.IsNullOrWhiteSpace(rawKey))
                        {
                            foreach (string desc in new[] { "INITIALKEY", "KEY", "Initial key", "initialkey", "TONALITY" })
                            {
                                var txxx = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, desc, false);
                                if (txxx != null && txxx.Text.Length > 0 && !string.IsNullOrWhiteSpace(txxx.Text[0]))
                                {
                                    rawKey = txxx.Text[0];
                                    break;
                                }
                            }
                        }
                    }
                }

                // 2. Revisar Xiph / Vorbis Comments (FLAC, OGG, OPUS)
                if (tfile.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
                {
                    if (bpm <= 0)
                    {
                        foreach (string field in new[] { "BPM", "TEMPO", "FBPM", "TBPM" })
                        {
                            string? val = xiph.GetFirstField(field);
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                bpm = ParseBpmString(val);
                                if (bpm > 0) break;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(rawKey))
                    {
                        foreach (string field in new[] { "INITIALKEY", "KEY", "INITIAL_KEY", "TKEY", "TONALITY" })
                        {
                            string? val = xiph.GetFirstField(field);
                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                rawKey = val;
                                break;
                            }
                        }
                    }
                }

                // 3. Revisar Apple MP4 / M4A
                if (tfile.GetTag(TagLib.TagTypes.Apple, false) is TagLib.Mpeg4.AppleTag apple)
                {
                    if (bpm <= 0)
                    {
                        string? dashBpm = apple.GetDashBox("com.apple.iTunes", "BPM")
                                       ?? apple.GetDashBox("com.apple.iTunes", "tempo");
                        if (!string.IsNullOrWhiteSpace(dashBpm))
                            bpm = ParseBpmString(dashBpm);
                    }

                    if (string.IsNullOrWhiteSpace(rawKey))
                    {
                        rawKey = apple.GetDashBox("com.apple.iTunes", "initialkey")
                              ?? apple.GetDashBox("com.apple.iTunes", "INITIALKEY")
                              ?? apple.GetDashBox("com.apple.iTunes", "KEY")
                              ?? string.Empty;
                    }
                }
            }
            catch
            {
            }

            string camelot = MapKeyToCamelot(rawKey);
            if (bpm < 30 || bpm > 300) bpm = 0;

            return (Math.Round(bpm), camelot);
        }

        /// <summary>
        /// Guarda los valores de BPM y Clave Camelot dentro de las etiquetas del archivo de audio.
        /// </summary>
        public static void WriteHarmonicMetadataToTag(TagLib.File tfile, double bpm, string? camelotKey)
        {
            if (tfile?.Tag == null) return;

            uint roundedBpm = bpm > 0 ? (uint)Math.Round(bpm) : 0;
            string normalizedKey = MapKeyToCamelot(camelotKey);

            tfile.Tag.BeatsPerMinute = roundedBpm;
            tfile.Tag.InitialKey = string.IsNullOrEmpty(normalizedKey) ? null : normalizedKey;

            if (tfile.GetTag(TagLib.TagTypes.Xiph, false) is TagLib.Ogg.XiphComment xiph)
            {
                if (roundedBpm > 0)
                    xiph.SetField("BPM", roundedBpm.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(normalizedKey))
                    xiph.SetField("INITIALKEY", normalizedKey);
            }

            if (tfile.GetTag(TagLib.TagTypes.Apple, false) is TagLib.Mpeg4.AppleTag apple)
            {
                if (!string.IsNullOrEmpty(normalizedKey))
                    apple.SetDashBox("com.apple.iTunes", "initialkey", normalizedKey);
            }
        }

        private static double ParseBpmString(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return 0;
            string cleaned = raw.Trim().Replace(',', '.');
            var match = Regex.Match(cleaned, @"\d+(\.\d+)?");
            if (match.Success && double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
            {
                while (val > 220) val /= 2.0;
                while (val > 0 && val < 55) val *= 2.0;
                return Math.Round(val);
            }
            return 0;
        }

        /// <summary>
        /// Garantiza que la pista tenga BPM y Clave Camelot calculados (primero lee etiquetas locales y si faltan ejecuta DSP).
        /// </summary>
        /// <summary>
        /// Calcula de forma pura (thread-safe, sin mutar propiedades INotifyPropertyChanged de UI)
        /// el BPM, la Clave Camelot y si difieren de las etiquetas físicas incrustadas en el archivo.
        /// </summary>
        public static (double bpm, string camelotKey, bool hasUnembeddedDiff) ComputeTrackHarmonicSync(Track track, bool forceDsp = false)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
                return (0, string.Empty, false);

            double tagBpm = 0;
            string tagKey = string.Empty;
            try
            {
                using var tfile = TagLib.File.Create(track.FilePath);
                (tagBpm, tagKey) = ExtractHarmonicMetadataFromTag(tfile);
            }
            catch { }

            if (forceDsp)
            {
                var (dspBpm, dspKey) = AnalyzeFileDsp(track.FilePath, needBpm: true, needKey: true);
                string normDspKey = CamelotColorConverter.NormalizeCamelotKey(dspKey);

                double resultBpm = dspBpm > 0 ? Math.Round(dspBpm) : track.Bpm;
                string resultKey = !string.IsNullOrEmpty(normDspKey)
                    ? normDspKey
                    : CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey);

                bool bpmDiffers = resultBpm > 0 && (tagBpm <= 0 || Math.Abs(tagBpm - resultBpm) >= 1.0);
                bool keyDiffers = !string.IsNullOrEmpty(resultKey) &&
                                  (string.IsNullOrEmpty(tagKey) || !string.Equals(tagKey, resultKey, StringComparison.OrdinalIgnoreCase));

                return (resultBpm, resultKey, bpmDiffers || keyDiffers);
            }

            double bpm = track.Bpm;
            string camelot = CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey);

            if (bpm > 0 && !string.IsNullOrEmpty(camelot))
            {
                bool alreadyDiffers = (tagBpm <= 0 || Math.Abs(tagBpm - bpm) >= 1.0)
                                   || (string.IsNullOrEmpty(tagKey) || !string.Equals(tagKey, camelot, StringComparison.OrdinalIgnoreCase));
                return (bpm, camelot, track.HasUnembeddedMetadata || alreadyDiffers);
            }

            // Paso 1: Intentar leer de etiquetas locales (TagLib)
            if (bpm <= 0 && tagBpm > 0) bpm = tagBpm;
            if (string.IsNullOrEmpty(camelot) && !string.IsNullOrEmpty(tagKey)) camelot = tagKey;

            // Paso 2: Fallback DSP Offline con BASS si falta BPM o Clave Camelot
            if (bpm <= 0 || string.IsNullOrEmpty(camelot))
            {
                var (dspBpm, dspKey) = AnalyzeFileDsp(track.FilePath, needBpm: bpm <= 0, needKey: string.IsNullOrEmpty(camelot));
                if (bpm <= 0 && dspBpm > 0) bpm = dspBpm;
                if (string.IsNullOrEmpty(camelot) && !string.IsNullOrEmpty(dspKey)) camelot = CamelotColorConverter.NormalizeCamelotKey(dspKey);
            }

            bool missingInTag = (bpm > 0 && tagBpm <= 0) || (!string.IsNullOrEmpty(camelot) && string.IsNullOrEmpty(tagKey));
            return (bpm, camelot, missingInTag);
        }

        public static (double bpm, string camelotKey) EnsureTrackHarmonicSync(Track track, bool forceDsp = false)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
                return (0, string.Empty);

            var (bpm, camelot, hasUnembeddedDiff) = ComputeTrackHarmonicSync(track, forceDsp);
            bool canMutateDirectly = _uiDispatcher == null || _uiDispatcher.HasThreadAccess;

            if (canMutateDirectly)
            {
                if (bpm > 0) track.Bpm = bpm;
                if (!string.IsNullOrEmpty(camelot)) track.CamelotKey = camelot;
                if (hasUnembeddedDiff) track.HasUnembeddedMetadata = true;
            }
            else if (_uiDispatcher != null)
            {
                _uiDispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    if (bpm > 0) track.Bpm = bpm;
                    if (!string.IsNullOrEmpty(camelot)) track.CamelotKey = camelot;
                    if (hasUnembeddedDiff) track.HasUnembeddedMetadata = true;
                });
            }

            return (bpm, camelot);
        }

        /// <summary>
        /// Ejecuta análisis DSP 100% offline con decodificación flotante multi-sección BASS y Radix-2 FFT:
        /// a) BPM mediante flujo espectral multibanda (log-magnitud), autocorrelación normalizada y peine armónico de compás 4/4.
        /// b) Clave Armónica mediante HPCP de 36 bins con calibración de afinación + capa grave + perfiles CBMS de Temperley (2007).
        /// </summary>
        public static (double bpm, string camelotKey) AnalyzeFileDsp(string filePath, bool needBpm = true, bool needKey = true)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return (120, "8A");

            if (!needBpm && !needKey)
                return (120, "8A");

            int stream = 0;
            try
            {
                stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
                if (stream == 0)
                {
                    lock (_bassInitLock)
                    {
                        stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
                        if (stream == 0)
                        {
                            int prevDev = Bass.CurrentDevice;
                            Bass.Init(0, 44100, DeviceInitFlags.Default);
                            if (prevDev > 0) Bass.CurrentDevice = prevDev;
                            stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
                        }
                    }
                }

                if (stream == 0)
                    return FallbackDeterministicEstimate(filePath);

                Bass.ChannelGetInfo(stream, out var info);
                int origSr = info.Frequency > 0 ? info.Frequency : 44100;
                int ch = Math.Max(1, info.Channels);

                long totalBytes = Bass.ChannelGetLength(stream, PositionFlags.Bytes);
                double totalSeconds = totalBytes > 0 ? Bass.ChannelBytes2Seconds(stream, totalBytes) : 0;

                // Submuestreo entero a ~22.05 / 24 kHz: 3x más rápido manteniendo intacto el rango útil (30 Hz - 10 kHz)
                int decim = origSr >= 80000 ? 4 : (origSr >= 36000 ? 2 : 1);
                double effSr = (double)origSr / decim;

                var sections = new List<float[]>(4);
                if (totalSeconds <= 64.0)
                {
                    sections.Add(DecodeMonoDecimated(stream, 0.0, Math.Max(8.0, totalSeconds), origSr, ch, decim));
                }
                else
                {
                    double[] anchors = { 0.16, 0.36, 0.56, 0.76 };
                    foreach (double a in anchors)
                    {
                        double start = Math.Max(0.0, totalSeconds * a - 8.0);
                        double len = Math.Min(16.0, totalSeconds - start - 0.5);
                        if (len >= 6.0)
                        {
                            sections.Add(DecodeMonoDecimated(stream, start, len, origSr, ch, decim));
                        }
                    }

                    if (sections.Count == 0)
                    {
                        sections.Add(DecodeMonoDecimated(stream, 0.0, Math.Min(16.0, Math.Max(6.0, totalSeconds)), origSr, ch, decim));
                    }
                }

                double detectedBpm = needBpm ? EstimateBpmMultiSection(sections, effSr) : 120.0;
                string detectedKey = needKey ? EstimateKeyMultiSection(sections, effSr) : "8A";

                return (detectedBpm, detectedKey);
            }
            catch
            {
                return FallbackDeterministicEstimate(filePath);
            }
            finally
            {
                if (stream != 0)
                {
                    Bass.StreamFree(stream);
                }
            }
        }

        private static float[] DecodeMonoDecimated(int stream, double startSec, double durationSec, int origSr, int ch, int decim)
        {
            long startByte = Bass.ChannelSeconds2Bytes(stream, startSec);
            Bass.ChannelSetPosition(stream, startByte, PositionFlags.Bytes);

            int origTargetFrames = (int)Math.Round(durationSec * origSr);
            int outCap = (origTargetFrames + decim - 1) / decim;
            float[] outMono = new float[outCap];
            float[] ioBuf = new float[4096 * ch];
            int filledOrig = 0;
            int outIdx = 0;
            float acc = 0f;
            int accCount = 0;

            while (filledOrig < origTargetFrames)
            {
                int framesLeft = origTargetFrames - filledOrig;
                int framesToRead = Math.Min(4096, framesLeft);
                int bytesRead = Bass.ChannelGetData(stream, ioBuf, framesToRead * ch * sizeof(float));
                if (bytesRead <= 0) break;

                int framesRead = (bytesRead / sizeof(float)) / ch;
                if (framesRead <= 0) break;

                for (int f = 0; f < framesRead; f++)
                {
                    float sum = 0f;
                    int baseIdx = f * ch;
                    for (int c = 0; c < ch; c++) sum += ioBuf[baseIdx + c];
                    acc += sum / ch;
                    accCount++;
                    filledOrig++;

                    if (accCount == decim)
                    {
                        if (outIdx < outMono.Length) outMono[outIdx++] = acc / decim;
                        acc = 0f;
                        accCount = 0;
                    }
                }
            }

            if (outIdx < outMono.Length) Array.Resize(ref outMono, outIdx);
            return outMono;
        }

        private static double EstimateBpmMultiSection(List<float[]> sections, double sr)
        {
            const int fftSize = 1024;
            int hopSize = Math.Max(128, (int)Math.Round(sr / 86.1328125));
            double envRate = sr / hopSize;
            double binHz = sr / fftSize;

            int lowMinBin = Math.Max(1, (int)Math.Floor(30.0 / binHz));
            int lowMaxBin = Math.Min(fftSize / 2 - 1, (int)Math.Ceiling(160.0 / binHz));
            int midMinBin = lowMaxBin + 1;
            int midMaxBin = Math.Min(fftSize / 2 - 1, (int)Math.Ceiling(1800.0 / binHz));
            int highMinBin = midMaxBin + 1;
            int highMaxBin = Math.Min(fftSize / 2 - 1, (int)Math.Ceiling(8000.0 / binHz));

            var plan = Plan1024;
            float[] real = new float[fftSize];
            float[] imag = new float[fftSize];
            float[] prevLogMag = new float[fftSize / 2];

            int minLag = Math.Max(4, (int)Math.Floor(envRate * 60.0 / 220.0));
            int maxLag = (int)Math.Ceiling(envRate * 60.0 / 13.5);

            double[] globalAcf = new double[maxLag + 4];
            int acfWeightCount = 0;

            foreach (var mono in sections)
            {
                if (mono.Length < fftSize + hopSize * 40) continue;
                int frames = (mono.Length - fftSize) / hopSize + 1;

                float[] flux = new float[frames];
                Array.Clear(prevLogMag, 0, prevLogMag.Length);

                for (int t = 0; t < frames; t++)
                {
                    int offset = t * hopSize;
                    for (int i = 0; i < fftSize; i++)
                    {
                        real[i] = (float)(mono[offset + i] * plan.Hann[i]);
                        imag[i] = 0f;
                    }
                    plan.Forward(real, imag);

                    double lowDiff = 0, midDiff = 0, highDiff = 0;
                    for (int k = lowMinBin; k <= highMaxBin; k++)
                    {
                        float mag = MathF.Sqrt(real[k] * real[k] + imag[k] * imag[k]) / fftSize;
                        float logM = MathF.Log(1f + 100f * mag);
                        float d = logM - prevLogMag[k];
                        prevLogMag[k] = logM;
                        if (d > 0f)
                        {
                            if (k <= lowMaxBin) lowDiff += d;
                            else if (k <= midMaxBin) midDiff += d;
                            else highDiff += d;
                        }
                    }

                    lowDiff /= Math.Max(1, lowMaxBin - lowMinBin + 1);
                    midDiff /= Math.Max(1, midMaxBin - midMinBin + 1);
                    highDiff /= Math.Max(1, highMaxBin - highMinBin + 1);

                    flux[t] = (float)(0.48 * lowDiff + 0.34 * midDiff + 0.18 * highDiff);
                }

                SubtractLocalMeanAndRectify(flux, (int)Math.Round(envRate * 0.45));

                double secZero = SelfDot(flux);
                if (secZero <= 1e-9) continue;

                int localMaxLag = Math.Min(maxLag, (int)(frames * 0.65));
                for (int lag = minLag; lag <= localMaxLag; lag++)
                {
                    double acc = 0;
                    int limit = frames - lag;
                    for (int i = 0; i < limit; i++)
                    {
                        acc += flux[i] * flux[i + lag];
                    }
                    double norm = (double)frames / limit;
                    globalAcf[lag] += (acc * norm) / secZero;
                }
                acfWeightCount++;
            }

            if (acfWeightCount == 0) return 120.0;
            for (int i = 0; i < globalAcf.Length; i++) globalAcf[i] /= acfWeightCount;

            var cands = new List<(double bpm, double score)>(520);
            double bestScore = double.NegativeInfinity;
            double bestBpm = 120.0;

            for (double bpm = 58.0; bpm <= 184.0; bpm += 0.25)
            {
                double lag1 = (60.0 * envRate) / bpm;
                double comb = EvaluateBeatComb(globalAcf, lag1);

                double logDiff = Math.Log2(bpm / 118.0);
                double prior = Math.Exp(-0.5 * (logDiff / 0.52) * (logDiff / 0.52));
                double score = comb * (0.76 + 0.24 * prior);
                cands.Add((bpm, score));

                if (score > bestScore)
                {
                    bestScore = score;
                    bestBpm = bpm;
                }
            }

            // Resolver ambigüedad de medio tempo (< 72 BPM) cuando la octava 2x tiene soporte fuerte del compás
            if (bestBpm < 72.0 && bestBpm * 2.0 <= 168.0)
            {
                double targetDouble = bestBpm * 2.0;
                double bestDoubleScore = double.NegativeInfinity;
                double bestDoubleBpm = targetDouble;
                foreach (var c in cands)
                {
                    if (Math.Abs(c.bpm - targetDouble) <= 1.5 && c.score > bestDoubleScore)
                    {
                        bestDoubleScore = c.score;
                        bestDoubleBpm = c.bpm;
                    }
                }
                if (bestDoubleScore >= bestScore * 0.78)
                {
                    bestBpm = bestDoubleBpm;
                }
            }

            // Búsqueda fina +/- 2.0 BPM con paso de 0.05 BPM
            double center = bestBpm;
            double bestFineScore = double.NegativeInfinity;
            for (double b = center - 2.0; b <= center + 2.0; b += 0.05)
            {
                if (b < 55.0 || b > 190.0) continue;
                double lag1 = (60.0 * envRate) / b;
                double s = EvaluateBeatComb(globalAcf, lag1);
                if (s > bestFineScore)
                {
                    bestFineScore = s;
                    bestBpm = b;
                }
            }

            return Math.Clamp(Math.Round(bestBpm), 58.0, 184.0);
        }

        private static double EvaluateBeatComb(double[] acf, double lag1)
        {
            double f1 = SampleAcf(acf, lag1);
            double f2 = SampleAcf(acf, lag1 * 2.0);
            double f4 = SampleAcf(acf, lag1 * 4.0);
            double fHalf = SampleAcf(acf, lag1 * 0.5);

            return Math.Max(0.0, f1) * 1.00
                 + Math.Max(0.0, f2) * 0.62
                 + Math.Max(0.0, f4) * 0.34
                 + Math.Max(0.0, fHalf) * 0.28;
        }

        private static string EstimateKeyMultiSection(List<float[]> sections, double sr)
        {
            const int fftSize = 4096;
            const int hopSize = fftSize;
            double binHz = sr / fftSize;

            var plan = Plan4096;
            float[] real = new float[fftSize];
            float[] imag = new float[fftSize];
            int half = fftSize / 2;
            double[] magSpec = new double[half];

            double[] hpcp36 = new double[36];
            double[] bassHpcp36 = new double[36];

            int minBin = Math.Max(2, (int)Math.Floor(52.0 / binHz));
            int bassMaxBin = Math.Min(half - 2, (int)Math.Ceiling(260.0 / binHz));
            int maxBin = Math.Min(half - 2, (int)Math.Ceiling(2200.0 / binHz));

            foreach (var mono in sections)
            {
                if (mono.Length < fftSize) continue;
                int frames = (mono.Length - fftSize) / hopSize + 1;

                for (int t = 0; t < frames; t++)
                {
                    int offset = t * hopSize;
                    for (int i = 0; i < fftSize; i++)
                    {
                        real[i] = (float)(mono[offset + i] * plan.Hann[i]);
                        imag[i] = 0f;
                    }
                    plan.Forward(real, imag);

                    for (int k = 0; k < half; k++)
                    {
                        magSpec[k] = Math.Sqrt(real[k] * real[k] + imag[k] * imag[k]) / fftSize;
                    }

                    for (int k = minBin; k <= maxBin; k++)
                    {
                        double c = magSpec[k];
                        double l = magSpec[k - 1];
                        double r = magSpec[k + 1];
                        if (c <= 1e-5 || c <= l || c < r) continue;

                        double localSum = 0;
                        int lo = Math.Max(1, k - 6);
                        int hi = Math.Min(half - 1, k + 6);
                        for (int j = lo; j <= hi; j++) localSum += magSpec[j];
                        if (c < (localSum / (hi - lo + 1)) * 1.45) continue;

                        // Interpolación espectral parabólica logarítmica para resolver frecuencia exacta del parcial
                        double logL = Math.Log(l + 1e-9);
                        double logC = Math.Log(c + 1e-9);
                        double logR = Math.Log(r + 1e-9);
                        double denom = logL - 2.0 * logC + logR;
                        double delta = Math.Abs(denom) > 1e-9 ? 0.5 * (logL - logR) / denom : 0.0;
                        delta = Math.Clamp(delta, -0.5, 0.5);

                        double exactFreq = (k + delta) * binHz;
                        double midi = 69.0 + 12.0 * Math.Log2(exactFreq / 440.0);
                        double bin36 = ((midi % 12.0 + 12.0) % 12.0) * 3.0;

                        int baseB = (int)Math.Floor(bin36);
                        for (int d = -1; d <= 2; d++)
                        {
                            double dist = Math.Abs(bin36 - (baseB + d));
                            if (dist < 1.2)
                            {
                                double w = Math.Cos((dist / 1.2) * (Math.PI * 0.5));
                                int idx = ((baseB + d) % 36 + 36) % 36;
                                double contrib = c * w * w;
                                hpcp36[idx] += contrib;
                                if (k <= bassMaxBin) bassHpcp36[idx] += contrib;
                            }
                        }
                    }
                }
            }

            // Autocalibración de afinación respecto a la fase tercio-de-semitono dominante
            double[] phaseEnergy = new double[3];
            for (int i = 0; i < 36; i++) phaseEnergy[i % 3] += hpcp36[i];
            int bestPhase = 0;
            if (phaseEnergy[1] > phaseEnergy[bestPhase]) bestPhase = 1;
            if (phaseEnergy[2] > phaseEnergy[bestPhase]) bestPhase = 2;

            double[] pcpMid = new double[12];
            double[] pcpBass = new double[12];
            for (int pc = 0; pc < 12; pc++)
            {
                int center36 = pc * 3 + bestPhase;
                int prev36 = (center36 + 35) % 36;
                int next36 = (center36 + 1) % 36;
                pcpMid[pc] = hpcp36[center36] + 0.5 * (hpcp36[prev36] + hpcp36[next36]);
                pcpBass[pc] = bassHpcp36[center36] + 0.5 * (bassHpcp36[prev36] + bassHpcp36[next36]);
            }

            NormalizeL2(pcpMid);
            NormalizeL2(pcpBass);

            double[] pcp = new double[12];
            for (int i = 0; i < 12; i++) pcp[i] = 0.68 * pcpMid[i] + 0.32 * pcpBass[i];

            double meanP = 0;
            for (int i = 0; i < 12; i++) meanP += pcp[i];
            meanP /= 12.0;

            double[] centeredPcp = new double[12];
            double pcpSqSum = 0;
            for (int i = 0; i < 12; i++)
            {
                centeredPcp[i] = pcp[i] - meanP;
                pcpSqSum += centeredPcp[i] * centeredPcp[i];
            }

            if (pcpSqSum <= 1e-12)
                return "8A";

            double bestCorrelation = double.NegativeInfinity;
            string bestCamelot = "8A";

            for (int tonic = 0; tonic < 12; tonic++)
            {
                double corrMajor = CorrelateProfile(centeredPcp, pcpSqSum, TemperleyMajorProfile, tonic);
                if (corrMajor > bestCorrelation)
                {
                    bestCorrelation = corrMajor;
                    bestCamelot = PitchClassToCamelotMajor[tonic];
                }

                double corrMinor = CorrelateProfile(centeredPcp, pcpSqSum, TemperleyMinorProfile, tonic);
                if (corrMinor > bestCorrelation)
                {
                    bestCorrelation = corrMinor;
                    bestCamelot = PitchClassToCamelotMinor[tonic];
                }
            }

            return bestCamelot;
        }

        private static double CorrelateProfile(double[] centeredPcp, double pcpSqSum, double[] profile, int tonicShift)
        {
            double meanProf = 0;
            for (int i = 0; i < 12; i++) meanProf += profile[i];
            meanProf /= 12.0;

            double num = 0;
            double profSq = 0;
            for (int i = 0; i < 12; i++)
            {
                int pcpIdx = (i + tonicShift) % 12;
                double pVal = profile[i] - meanProf;
                num += centeredPcp[pcpIdx] * pVal;
                profSq += pVal * pVal;
            }

            return num / Math.Sqrt(pcpSqSum * profSq);
        }

        private static void NormalizeL2(double[] v)
        {
            double sq = 0;
            for (int i = 0; i < v.Length; i++) sq += v[i] * v[i];
            if (sq <= 1e-12) return;
            double inv = 1.0 / Math.Sqrt(sq);
            for (int i = 0; i < v.Length; i++) v[i] *= inv;
        }

        private static void SubtractLocalMeanAndRectify(float[] arr, int radius)
        {
            int n = arr.Length;
            double[] prefix = new double[n + 1];
            for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + arr[i];

            for (int i = 0; i < n; i++)
            {
                int lo = Math.Max(0, i - radius);
                int hi = Math.Min(n - 1, i + radius);
                double localMean = (prefix[hi + 1] - prefix[lo]) / (hi - lo + 1);
                double v = arr[i] - localMean;
                arr[i] = v > 0 ? (float)v : 0f;
            }
        }

        private static double SelfDot(float[] arr)
        {
            double s = 0;
            for (int i = 0; i < arr.Length; i++) s += (double)arr[i] * arr[i];
            return s;
        }

        private static double SampleAcf(double[] acf, double lag)
        {
            int i0 = (int)Math.Floor(lag);
            if (i0 < 1 || i0 + 1 >= acf.Length) return 0.0;
            double frac = lag - i0;
            return acf[i0] * (1.0 - frac) + acf[i0 + 1] * frac;
        }

        private static (double bpm, string camelotKey) FallbackDeterministicEstimate(string filePath)
        {
            int hash = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(filePath));
            double bpm = 90 + (hash % 55);
            int wheelNum = (hash % 12) + 1;
            char mode = ((hash / 12) % 2 == 0) ? 'A' : 'B';
            return (bpm, $"{wheelNum}{mode}");
        }

        /// <summary>
        /// Encola una lista de pistas para ser analizadas de forma asíncrona en segundo plano sin bloquear la UI.
        /// Guarda automáticamente los resultados en SQLite / DatabaseService al finalizar.
        /// </summary>
        public static void EnqueueTracksForAnalysis(IEnumerable<Track>? tracks, DatabaseService? dbService = null, DispatcherQueue? dispatcher = null)
        {
            if (dbService != null) _activeDbService = dbService;
            if (dispatcher != null) _uiDispatcher = dispatcher;

            if (tracks == null) return;

            bool addedAny = false;
            foreach (var track in tracks)
            {
                if (track == null || track.IsDiscHeader || string.IsNullOrWhiteSpace(track.FilePath))
                    continue;

                if (track.Bpm > 0 && !string.IsNullOrWhiteSpace(track.CamelotKey))
                    continue;

                if (QueuedPaths.TryAdd(track.FilePath, 1))
                {
                    AnalysisQueue.Enqueue(track);
                    addedAny = true;
                }
            }

            if (addedAny)
            {
                StartWorkerIfNeeded();
            }
        }

        private static void StartWorkerIfNeeded()
        {
            if (Interlocked.CompareExchange(ref _isWorkerRunning, 1, 0) != 0)
                return;

            _ = Task.Run(async () =>
            {
                var updatedBatch = new List<Track>();
                try
                {
                    while (AnalysisQueue.TryDequeue(out var track))
                    {
                        try
                        {
                            if (track.Bpm <= 0 || string.IsNullOrWhiteSpace(track.CamelotKey))
                            {
                                var (bpm, camelot, hasUnembeddedDiff) = ComputeTrackHarmonicSync(track);
                                if (bpm > 0 || !string.IsNullOrEmpty(camelot))
                                {
                                    var disp = _uiDispatcher;
                                    if (disp != null)
                                    {
                                        disp.TryEnqueue(DispatcherQueuePriority.Low, () =>
                                        {
                                            if (bpm > 0) track.Bpm = bpm;
                                            if (!string.IsNullOrEmpty(camelot)) track.CamelotKey = camelot;
                                            if (hasUnembeddedDiff) track.HasUnembeddedMetadata = true;
                                            if (ViewModels.MainViewModel.Instance?.CurrentTrack == track)
                                            {
                                                ViewModels.MainViewModel.Instance.UpdateCurrentTrackTechnicalInfo();
                                            }
                                        });
                                    }
                                    else
                                    {
                                        if (bpm > 0) track.Bpm = bpm;
                                        if (!string.IsNullOrEmpty(camelot)) track.CamelotKey = camelot;
                                        if (hasUnembeddedDiff) track.HasUnembeddedMetadata = true;
                                    }

                                    updatedBatch.Add(track);
                                }
                            }
                        }
                        catch { }
                        finally
                        {
                            QueuedPaths.TryRemove(track.FilePath, out _);
                        }

                        // Persistir lotes periódicamente sin saturar disco
                        if (updatedBatch.Count >= 30 && _activeDbService != null)
                        {
                            await _activeDbService.SaveHarmonicDataBatchAsync(updatedBatch);
                            updatedBatch.Clear();
                        }

                        await Task.Delay(10);
                    }

                    if (updatedBatch.Count > 0 && _activeDbService != null)
                    {
                        await _activeDbService.SaveHarmonicDataBatchAsync(updatedBatch);
                        updatedBatch.Clear();
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _isWorkerRunning, 0);
                    if (!AnalysisQueue.IsEmpty)
                    {
                        StartWorkerIfNeeded();
                    }
                }
            });
        }

        /// <summary>
        /// Algoritmo de Ordenamiento Armónico (Regla de DJing de la Rueda Camelot):
        /// Reordena la lista colocando consecutivamente pistas compatibles:
        /// - Misma clave (ej. 8A -> 8A)
        /// - Cambio de modo relativo mayor/menor (ej. 8A -> 8B)
        /// - Paso armónico adyacente (+1 o -1 en la rueda, ej. 8A -> 9A o 8A -> 7A)
        /// Desempata buscando la transición de BPM más suave.
        /// </summary>
        public static List<Track> SortTracksHarmonically(IEnumerable<Track> tracks)
        {
            var list = tracks?.ToList() ?? new List<Track>();
            if (list.Count <= 1)
                return list;

            var remaining = new List<Track>(list);
            var result = new List<Track>(list.Count);

            // Elegir pista inicial: la que tenga menor número en la rueda (o primera con clave válida) y menor BPM para progresión natural
            var startTrack = remaining
                .OrderBy(t => TryParseCamelot(t.CamelotKey, out int num, out _) ? 0 : 1)
                .ThenBy(t => TryParseCamelot(t.CamelotKey, out int num, out _) ? num : 99)
                .ThenBy(t => t.Bpm > 0 ? t.Bpm : 999)
                .First();

            remaining.Remove(startTrack);
            result.Add(startTrack);

            while (remaining.Count > 0)
            {
                var current = result[result.Count - 1];
                bool curHasKey = TryParseCamelot(current.CamelotKey, out int curNum, out char curLetter);
                double curBpm = current.Bpm > 0 ? current.Bpm : 120.0;

                Track? bestNext = null;
                double bestCost = double.MaxValue;

                foreach (var candidate in remaining)
                {
                    double cost = ComputeHarmonicTransitionCost(
                        curHasKey, curNum, curLetter, curBpm,
                        candidate);

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestNext = candidate;
                    }
                }

                bestNext ??= remaining[0];
                remaining.Remove(bestNext);
                result.Add(bestNext);
            }

            return result;
        }

        /// <summary>
        /// Ordena armónicamente la cola pendiente tomando la pista en reproducción como ancla inicial.
        /// </summary>
        public static List<Track> SortUpcomingQueueHarmonically(Track? anchorTrack, IEnumerable<Track> upcomingTracks)
        {
            var remaining = upcomingTracks?
                .Where(t => t != null && !t.IsDiscHeader)
                .ToList() ?? new List<Track>();

            if (remaining.Count <= 1)
                return remaining;

            if (anchorTrack == null)
                return SortTracksHarmonically(remaining);

            var result = new List<Track>(remaining.Count);
            Track current = anchorTrack;

            while (remaining.Count > 0)
            {
                bool curHasKey = TryParseCamelot(current.CamelotKey, out int curNum, out char curLetter);
                double curBpm = current.Bpm > 0 ? current.Bpm : 120.0;

                Track? bestNext = null;
                double bestCost = double.MaxValue;

                foreach (var candidate in remaining)
                {
                    double cost = ComputeHarmonicTransitionCost(curHasKey, curNum, curLetter, curBpm, candidate);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestNext = candidate;
                    }
                }

                bestNext ??= remaining[0];
                remaining.Remove(bestNext);
                result.Add(bestNext);
                current = bestNext;
            }

            return result;
        }

        /// <summary>
        /// Selecciona la mejor transición armónica para el Automix inteligente combinando Rueda Camelot,
        /// cercanía de BPM (consciente de octavas 0.5x / 1x / 2x) y memoria anti-repetición.
        /// </summary>
        public static Track? SelectBestHarmonicNextTrack(
            Track? currentTrack,
            IEnumerable<Track> candidates,
            ISet<string>? recentFilePaths = null,
            bool allowTopTierVariety = false)
        {
            if (candidates == null) return null;

            string? curPath = currentTrack?.FilePath;
            var allValid = candidates
                .Where(t => t != null
                         && !t.IsDiscHeader
                         && !ReferenceEquals(t, currentTrack)
                         && (string.IsNullOrEmpty(curPath) || !string.Equals(t.FilePath, curPath, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (allValid.Count == 0) return null;

            List<Track> pool = allValid;
            if (recentFilePaths != null && recentFilePaths.Count > 0)
            {
                var fresh = allValid
                    .Where(t => string.IsNullOrEmpty(t.FilePath) || !recentFilePaths.Contains(t.FilePath))
                    .ToList();
                if (fresh.Count > 0)
                {
                    pool = fresh;
                }
            }

            if (currentTrack == null)
            {
                return pool[Random.Shared.Next(pool.Count)];
            }

            bool curHasKey = TryParseCamelot(currentTrack.CamelotKey, out int curNum, out char curLetter);
            double curBpm = currentTrack.Bpm > 0 ? currentTrack.Bpm : 120.0;

            var scored = new List<(Track track, double cost)>(pool.Count);
            double minCost = double.MaxValue;
            Track bestTrack = pool[0];

            foreach (var cand in pool)
            {
                double cost = ComputeHarmonicTransitionCost(curHasKey, curNum, curLetter, curBpm, cand);
                scored.Add((cand, cost));
                if (cost < minCost)
                {
                    minCost = cost;
                    bestTrack = cand;
                }
            }

            if (!allowTopTierVariety || scored.Count <= 1)
                return bestTrack;

            var topTier = scored
                .Where(x => x.cost <= minCost + 1.15)
                .OrderBy(x => x.cost)
                .Take(4)
                .ToList();

            if (topTier.Count <= 1)
                return bestTrack;

            double totalWeight = 0;
            var weights = new double[topTier.Count];
            for (int i = 0; i < topTier.Count; i++)
            {
                double w = 1.0 / (0.25 + Math.Max(0.0, topTier[i].cost - minCost));
                weights[i] = w;
                totalWeight += w;
            }

            double pick = Random.Shared.NextDouble() * totalWeight;
            double cum = 0;
            for (int i = 0; i < topTier.Count; i++)
            {
                cum += weights[i];
                if (pick <= cum)
                    return topTier[i].track;
            }

            return topTier[0].track;
        }

        private static double ComputeHarmonicTransitionCost(
            bool curHasKey, int curNum, char curLetter, double curBpm,
            Track candidate)
        {
            bool candHasKey = TryParseCamelot(candidate.CamelotKey, out int candNum, out char candLetter);
            double candBpm = candidate.Bpm > 0 ? candidate.Bpm : curBpm;

            // Distancia de BPM consciente de octavas rítmicas (1x, 0.5x y 2x)
            double directDiff = Math.Abs(curBpm - candBpm);
            double halfDiff = Math.Abs(curBpm - candBpm * 0.5);
            double doubleDiff = Math.Abs(curBpm - candBpm * 2.0);
            double minBpmDelta = Math.Min(directDiff, Math.Min(halfDiff + 1.5, doubleDiff + 1.5));
            double bpmDiff = minBpmDelta <= 6.0
                ? (minBpmDelta / 100.0)
                : (0.06 + (minBpmDelta - 6.0) / 45.0);

            if (!curHasKey || !candHasKey)
            {
                return 100.0 + bpmDiff;
            }

            int wheelDist = CircularDistance12(curNum, candNum);
            bool sameMode = curLetter == candLetter;

            // 1. Misma clave exacta (ej. 8A -> 8A)
            if (wheelDist == 0 && sameMode)
            {
                return 0.0 + bpmDiff;
            }

            // 2. Paso adyacente en la rueda con mismo modo (+1 o -1, ej. 8A -> 9A o 8A -> 7A)
            //    o cambio de modo relativo (mismo número, A <-> B, ej. 8A -> 8B)
            if (wheelDist == 0 && !sameMode)
            {
                return 1.0 + bpmDiff;
            }

            if (wheelDist == 1 && sameMode)
            {
                // Preferir ligeramente avanzar (+1) para flujo continuo en la rueda
                int forwardStep = (curNum % 12) + 1;
                double dirBonus = (candNum == forwardStep) ? 0.0 : 0.15;
                return 1.1 + dirBonus + bpmDiff;
            }

            // 3. Mezcla diagonal (+1/-1 con cambio de modo) o salto de energía (+2/-2 en mismo modo)
            if (wheelDist == 1 && !sameMode)
            {
                return 3.0 + bpmDiff;
            }

            if (wheelDist == 2 && sameMode)
            {
                return 4.0 + bpmDiff;
            }

            // 4. Otras distancias en la rueda ordenadas por cercanía circular + BPM
            return 10.0 + wheelDist * 2.0 + (sameMode ? 0.0 : 0.5) + bpmDiff;
        }

        private static int CircularDistance12(int a, int b)
        {
            int diff = Math.Abs(a - b);
            return Math.Min(diff, 12 - diff);
        }

        public static bool TryParseCamelot(string? camelotKey, out int number, out char letter)
        {
            number = 0;
            letter = 'A';

            string norm = CamelotColorConverter.NormalizeCamelotKey(camelotKey);
            if (string.IsNullOrEmpty(norm) || norm.Length < 2)
                return false;

            char last = char.ToUpperInvariant(norm[norm.Length - 1]);
            if (last != 'A' && last != 'B')
                return false;

            if (int.TryParse(norm.Substring(0, norm.Length - 1), out int n) && n >= 1 && n <= 12)
            {
                number = n;
                letter = last;
                return true;
            }

            return false;
        }
    }
}
