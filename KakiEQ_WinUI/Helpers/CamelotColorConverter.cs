using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace KakiEQ_WinUI.Helpers
{
    /// <summary>
    /// Mapea códigos de la Rueda Camelot (1A–12A y 1B–12B) a sus colores Hexadecimales oficiales
    /// y calcula automáticamente el color de texto de contraste (#FFFFFF o #000000) según luminosidad.
    /// </summary>
    public sealed class CamelotColorConverter : IValueConverter
    {
        private static readonly Dictionary<string, Color> CamelotColors = new(StringComparer.OrdinalIgnoreCase)
        {
            // 1A/1B: #00B074 (Verde)
            ["1A"] = Color.FromArgb(255, 0x00, 0xB0, 0x74),
            ["1B"] = Color.FromArgb(255, 0x00, 0xB0, 0x74),

            // 2A/2B: #00B894 (Turquesa)
            ["2A"] = Color.FromArgb(255, 0x00, 0xB8, 0x94),
            ["2B"] = Color.FromArgb(255, 0x00, 0xB8, 0x94),

            // 3A/3B: #00CEC9 (Cian)
            ["3A"] = Color.FromArgb(255, 0x00, 0xCE, 0xC9),
            ["3B"] = Color.FromArgb(255, 0x00, 0xCE, 0xC9),

            // 4A/4B: #0984E3 (Azul Cielo)
            ["4A"] = Color.FromArgb(255, 0x09, 0x84, 0xE3),
            ["4B"] = Color.FromArgb(255, 0x09, 0x84, 0xE3),

            // 5A/5B: #4834D4 (Azul Cobalto)
            ["5A"] = Color.FromArgb(255, 0x48, 0x34, 0xD4),
            ["5B"] = Color.FromArgb(255, 0x48, 0x34, 0xD4),

            // 6A/6B: #6C5CE7 (Lila Kaki)
            ["6A"] = Color.FromArgb(255, 0x6C, 0x5C, 0xE7),
            ["6B"] = Color.FromArgb(255, 0x6C, 0x5C, 0xE7),

            // 7A/7B: #E84393 (Magenta)
            ["7A"] = Color.FromArgb(255, 0xE8, 0x43, 0x93),
            ["7B"] = Color.FromArgb(255, 0xE8, 0x43, 0x93),

            // 8A/8B: #D63031 (Rojo Rubí)
            ["8A"] = Color.FromArgb(255, 0xD6, 0x30, 0x31),
            ["8B"] = Color.FromArgb(255, 0xD6, 0x30, 0x31),

            // 9A/9B: #E17055 (Naranja)
            ["9A"] = Color.FromArgb(255, 0xE1, 0x70, 0x55),
            ["9B"] = Color.FromArgb(255, 0xE1, 0x70, 0x55),

            // 10A/10B: #F0932B (Ámbar)
            ["10A"] = Color.FromArgb(255, 0xF0, 0x93, 0x2B),
            ["10B"] = Color.FromArgb(255, 0xF0, 0x93, 0x2B),

            // 11A/11B: #FDCB6E (Amarillo)
            ["11A"] = Color.FromArgb(255, 0xFD, 0xCB, 0x6E),
            ["11B"] = Color.FromArgb(255, 0xFD, 0xCB, 0x6E),

            // 12A/12B: #6AB04C (Verde Lima)
            ["12A"] = Color.FromArgb(255, 0x6A, 0xB0, 0x4C),
            ["12B"] = Color.FromArgb(255, 0x6A, 0xB0, 0x4C)
        };

        private static readonly ConcurrentDictionary<string, SolidColorBrush> BackgroundBrushCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SolidColorBrush DefaultBackgroundBrush = new(Color.FromArgb(255, 0x6C, 0x5C, 0xE7));
        private static readonly SolidColorBrush WhiteForegroundBrush = new(Color.FromArgb(255, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush BlackForegroundBrush = new(Color.FromArgb(255, 0x00, 0x00, 0x00));

        /// <summary>
        /// Normaliza una cadena de clave Camelot (ej. "08a" -> "8A"). Devuelve cadena vacía si no es válida.
        /// </summary>
        public static string NormalizeCamelotKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            string trimmed = key.Trim().ToUpperInvariant();
            if (CamelotColors.ContainsKey(trimmed))
                return trimmed;

            // Soportar ceros a la izquierda como "08A" -> "8A"
            if (trimmed.Length == 3 && trimmed[0] == '0')
            {
                string withoutZero = trimmed.Substring(1);
                if (CamelotColors.ContainsKey(withoutZero))
                    return withoutZero;
            }

            // Intentar mapear desde notación tradicional si aún no está en formato Camelot
            string mapped = HarmonicAnalysisHelper.MapKeyToCamelot(trimmed);
            if (!string.IsNullOrEmpty(mapped) && CamelotColors.ContainsKey(mapped))
                return mapped;

            return string.Empty;
        }

        public static Color GetColor(string? camelotKey)
        {
            string normalized = NormalizeCamelotKey(camelotKey);
            if (!string.IsNullOrEmpty(normalized) && CamelotColors.TryGetValue(normalized, out var color))
            {
                return color;
            }
            return Color.FromArgb(255, 0x6C, 0x5C, 0xE7);
        }

        public static SolidColorBrush GetBackgroundBrush(string? camelotKey)
        {
            string normalized = NormalizeCamelotKey(camelotKey);
            if (string.IsNullOrEmpty(normalized))
                return DefaultBackgroundBrush;

            return BackgroundBrushCache.GetOrAdd(normalized, k =>
            {
                var c = GetColor(k);
                return new SolidColorBrush(c);
            });
        }

        /// <summary>
        /// Calcula si el texto sobre el color de fondo debe ser Blanco (#FFFFFF) o Negro (#000000)
        /// en función de la luminosidad percibida (ITU-R BT.601 / sRGB).
        /// </summary>
        public static SolidColorBrush GetForegroundBrush(string? camelotKey)
        {
            string normalized = NormalizeCamelotKey(camelotKey);
            if (string.IsNullOrEmpty(normalized))
                return WhiteForegroundBrush;

            var c = GetColor(normalized);
            double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return luminance >= 0.64 ? BlackForegroundBrush : WhiteForegroundBrush;
        }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string? key = value as string;
            bool isForeground = parameter is string p && p.Equals("Foreground", StringComparison.OrdinalIgnoreCase);
            return isForeground ? GetForegroundBrush(key) : GetBackgroundBrush(key);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}
