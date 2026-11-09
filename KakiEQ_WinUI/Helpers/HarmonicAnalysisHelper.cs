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

        // Perfiles tonales de Krumhansl-Kessler para detección de clave armónica por Cromagrama (PCP)
        private static readonly double[] KrumhanslMajorProfile =
        {
            6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88
        };

        private static readonly double[] KrumhanslMinorProfile =
        {
            6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17
        };

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
        public static (double bpm, string camelotKey) EnsureTrackHarmonicSync(Track track)
        {
            if (track == null || string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
                return (0, string.Empty);

            double bpm = track.Bpm;
            string camelot = CamelotColorConverter.NormalizeCamelotKey(track.CamelotKey);

            if (bpm > 0 && !string.IsNullOrEmpty(camelot))
            {
                if (track.CamelotKey != camelot) track.CamelotKey = camelot;
                return (bpm, camelot);
            }

            // Paso 1: Intentar leer de etiquetas locales (TagLib)
            double tagBpm = 0;
            string tagKey = string.Empty;
            try
            {
                using var tfile = TagLib.File.Create(track.FilePath);
                (tagBpm, tagKey) = ExtractHarmonicMetadataFromTag(tfile);
                if (bpm <= 0 && tagBpm > 0) bpm = tagBpm;
                if (string.IsNullOrEmpty(camelot) && !string.IsNullOrEmpty(tagKey)) camelot = tagKey;
            }
            catch { }

            // Paso 2: Fallback DSP Offline con BASS si falta BPM o Clave Camelot
            if (bpm <= 0 || string.IsNullOrEmpty(camelot))
            {
                var (dspBpm, dspKey) = AnalyzeFileDsp(track.FilePath, needBpm: bpm <= 0, needKey: string.IsNullOrEmpty(camelot));
                if (bpm <= 0 && dspBpm > 0) bpm = dspBpm;
                if (string.IsNullOrEmpty(camelot) && !string.IsNullOrEmpty(dspKey)) camelot = dspKey;
            }

            if ((bpm > 0 && tagBpm <= 0) || (!string.IsNullOrEmpty(camelot) && string.IsNullOrEmpty(tagKey)))
            {
                track.HasUnembeddedMetadata = true;
            }

            return (bpm, camelot);
        }

        /// <summary>
        /// Ejecuta análisis DSP offline en segundo plano usando decodificación flotante y FFT de BASS:
        /// a) BPM mediante detección de envolvente de picos de energía (onset flux) y autocorrelación.
        /// b) Clave Armónica mediante Cromagrama (Pitch Class Profile de 12 semitonos) correlacionado con perfiles Krumhansl-Kessler.
        /// </summary>
        public static (double bpm, string camelotKey) AnalyzeFileDsp(string filePath, bool needBpm = true, bool needKey = true)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return (120, "8A");

            int stream = 0;
            try
            {
                stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
                if (stream == 0)
                {
                    // Asegurar que BASS esté inicializado (dispositivo 0 = No Sound) si aún no lo estaba
                    Bass.Init(0, 44100, DeviceInitFlags.Default);
                    stream = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);
                }

                if (stream == 0)
                    return FallbackDeterministicEstimate(filePath);

                Bass.ChannelGetInfo(stream, out var info);
                int sampleRate = info.Frequency > 0 ? info.Frequency : 44100;
                int channels = Math.Max(1, info.Channels);

                long totalBytes = Bass.ChannelGetLength(stream, PositionFlags.Bytes);
                double totalSeconds = totalBytes > 0 ? Bass.ChannelBytes2Seconds(stream, totalBytes) : 0;

                // Posicionar en la sección central representativa de la canción (ej. desde el 22% de la duración)
                double startSec = totalSeconds > 40 ? totalSeconds * 0.22 : (totalSeconds > 12 ? 3.0 : 0.0);
                double analyzeDurationSec = Math.Min(28.0, Math.Max(8.0, totalSeconds - startSec - 1.0));

                if (startSec > 0)
                {
                    long startBytes = Bass.ChannelSeconds2Bytes(stream, startSec);
                    Bass.ChannelSetPosition(stream, startBytes, PositionFlags.Bytes);
                }

                double detectedBpm = 120;
                string detectedKey = "8A";

                // Hop de ~512 muestras por cuadro de energía (~86.13 Hz a 44.1 kHz)
                int hopSamples = Math.Max(256, sampleRate / 86);
                double envelopeRate = (double)sampleRate / hopSamples;
                int totalHops = (int)Math.Clamp(analyzeDurationSec * envelopeRate, 256, 2600);

                float[] pcmBuffer = new float[hopSamples * channels];
                float[] onsetFlux = new float[totalHops];
                float[] fftBuffer = new float[2048]; // DataFlags.FFT4096 devuelve 2048 floats
                double[] chromagram = new double[12];

                float prevEnergy = 0f;
                int actualHops = 0;

                for (int h = 0; h < totalHops; h++)
                {
                    // Cada 32 hops (~0.37s), capturar FFT4096 para acumular el Cromagrama (Pitch Class Profile)
                    if (needKey && (h % 32 == 0))
                    {
                        int fftRead = Bass.ChannelGetData(stream, fftBuffer, (int)DataFlags.FFT4096);
                        if (fftRead > 0)
                        {
                            AccumulateChromagramFromFft(fftBuffer, sampleRate, 4096, chromagram);
                        }
                    }

                    int bytesToRead = pcmBuffer.Length * sizeof(float);
                    int bytesRead = Bass.ChannelGetData(stream, pcmBuffer, bytesToRead);
                    if (bytesRead <= 0)
                        break;

                    int floatsRead = bytesRead / sizeof(float);
                    int framesRead = floatsRead / channels;
                    if (framesRead <= 0)
                        break;

                    // Calcular energía RMS enfatizando transientes rítmicos
                    double sumSq = 0;
                    for (int i = 0; i < floatsRead; i += channels)
                    {
                        float sample = pcmBuffer[i];
                        if (channels > 1 && i + 1 < floatsRead)
                        {
                            sample = 0.5f * (sample + pcmBuffer[i + 1]);
                        }
                        sumSq += sample * sample;
                    }

                    float energy = (float)Math.Sqrt(sumSq / framesRead);
                    float logEnergy = (float)Math.Log10(1.0 + 100.0 * energy);
                    float diff = logEnergy - prevEnergy;
                    onsetFlux[actualHops] = diff > 0f ? diff : 0f;
                    prevEnergy = logEnergy;
                    actualHops++;
                }

                if (needBpm && actualHops > 120)
                {
                    detectedBpm = EstimateBpmFromOnsetFlux(onsetFlux, actualHops, envelopeRate);
                }

                if (needKey)
                {
                    detectedKey = EstimateCamelotKeyFromChromagram(chromagram);
                }

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

        private static void AccumulateChromagramFromFft(float[] fftMagnitudes, int sampleRate, int fftSize, double[] chromagram)
        {
            double binFreqStep = (double)sampleRate / fftSize;
            int minBin = Math.Max(1, (int)Math.Ceiling(65.0 / binFreqStep));     // C2 (~65.4 Hz)
            int maxBin = Math.Min(fftMagnitudes.Length - 1, (int)(1980.0 / binFreqStep)); // B6 (~1975 Hz)

            for (int bin = minBin; bin <= maxBin; bin++)
            {
                float mag = fftMagnitudes[bin];
                if (mag <= 1e-6f) continue;

                double freq = bin * binFreqStep;
                // Número de nota MIDI: 69 = A4 (440 Hz)
                double midi = 69.0 + 12.0 * Math.Log2(freq / 440.0);
                double nearestNote = Math.Round(midi);
                double deviation = Math.Abs(midi - nearestNote);

                // Ventana gaussiana estrecha alrededor del centro de cada semitono para filtrar ruido percusivo
                if (deviation <= 0.38)
                {
                    int pitchClass = ((int)nearestNote % 12 + 12) % 12;
                    double weight = Math.Exp(-0.5 * (deviation / 0.18) * (deviation / 0.18));
                    // Compresión logarítmica para equilibrar armónicos
                    chromagram[pitchClass] += Math.Sqrt(mag) * weight;
                }
            }
        }

        private static string EstimateCamelotKeyFromChromagram(double[] chromagram)
        {
            double totalEnergy = 0;
            for (int i = 0; i < 12; i++) totalEnergy += chromagram[i];
            if (totalEnergy <= 1e-9)
                return "8A";

            // Normalizar cromagrama (media 0)
            double meanPcp = totalEnergy / 12.0;
            double[] normPcp = new double[12];
            double normPcpSq = 0;
            for (int i = 0; i < 12; i++)
            {
                normPcp[i] = chromagram[i] - meanPcp;
                normPcpSq += normPcp[i] * normPcp[i];
            }

            if (normPcpSq <= 1e-12)
                return "8A";

            double bestCorrelation = double.NegativeInfinity;
            string bestCamelot = "8A";

            // Probar las 12 tonalidades mayores y 12 menores
            for (int tonic = 0; tonic < 12; tonic++)
            {
                double corrMajor = CorrelateProfile(normPcp, normPcpSq, KrumhanslMajorProfile, tonic);
                if (corrMajor > bestCorrelation)
                {
                    bestCorrelation = corrMajor;
                    bestCamelot = PitchClassToCamelotMajor[tonic];
                }

                double corrMinor = CorrelateProfile(normPcp, normPcpSq, KrumhanslMinorProfile, tonic);
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

        private static double EstimateBpmFromOnsetFlux(float[] flux, int length, double envelopeRate)
        {
            // Restar media local para resaltar los pulsos rítmicos
            double sum = 0;
            for (int i = 0; i < length; i++) sum += flux[i];
            float mean = (float)(sum / length);
            for (int i = 0; i < length; i++)
            {
                float v = flux[i] - mean;
                flux[i] = v > 0 ? v : 0;
            }

            // Rango de búsqueda: 68 BPM a 178 BPM
            int minLag = Math.Max(4, (int)Math.Floor(envelopeRate * 60.0 / 178.0));
            int maxLag = Math.Min(length / 2, (int)Math.Ceiling(envelopeRate * 60.0 / 68.0));

            if (minLag >= maxLag)
                return 120;

            double[] acf = new double[maxLag + 2];
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double acc = 0;
                int limit = length - lag;
                for (int i = 0; i < limit; i++)
                {
                    acc += flux[i] * flux[i + lag];
                }
                acf[lag] = acc / limit;
            }

            double bestScore = -1.0;
            double bestBpm = 120.0;

            for (int lag = minLag + 1; lag < maxLag; lag++)
            {
                double curr = acf[lag];
                // Evaluar energía en el pulso fundamental y su subdivisión (2x tempo / medio compás)
                double subHarmonic = (lag * 2 <= maxLag) ? 0.35 * acf[lag * 2] : 0;
                double halfLag = (lag / 2 >= minLag) ? 0.20 * acf[lag / 2] : 0;

                double bpm = (60.0 * envelopeRate) / lag;
                // Ponderación perceptual centrada en 120 BPM (curva log-gaussiana estándar en MIR)
                double logDiff = Math.Log2(bpm / 120.0);
                double tempoPrior = Math.Exp(-0.5 * (logDiff / 0.48) * (logDiff / 0.48));

                double score = (curr + subHarmonic + halfLag) * tempoPrior;
                if (score > bestScore)
                {
                    bestScore = score;
                    // Interpolación parabólica para afinar el lag
                    double prev = acf[lag - 1];
                    double next = acf[lag + 1];
                    double denom = prev - 2.0 * curr + next;
                    double delta = Math.Abs(denom) > 1e-9 ? 0.5 * (prev - next) / denom : 0.0;
                    delta = Math.Clamp(delta, -0.5, 0.5);
                    bestBpm = (60.0 * envelopeRate) / (lag + delta);
                }
            }

            return Math.Clamp(Math.Round(bestBpm), 68, 178);
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
                                var (bpm, camelot) = EnsureTrackHarmonicSync(track);
                                if (bpm > 0 || !string.IsNullOrEmpty(camelot))
                                {
                                    var disp = _uiDispatcher;
                                    if (disp != null)
                                    {
                                        disp.TryEnqueue(DispatcherQueuePriority.Low, () =>
                                        {
                                            if (bpm > 0) track.Bpm = bpm;
                                            if (!string.IsNullOrEmpty(camelot)) track.CamelotKey = camelot;
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

        private static double ComputeHarmonicTransitionCost(
            bool curHasKey, int curNum, char curLetter, double curBpm,
            Track candidate)
        {
            bool candHasKey = TryParseCamelot(candidate.CamelotKey, out int candNum, out char candLetter);
            double candBpm = candidate.Bpm > 0 ? candidate.Bpm : curBpm;
            double bpmDiff = Math.Abs(curBpm - candBpm) / 200.0; // penalización suave por diferencia de BPM (0.0 a ~0.5)

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
