using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageMagick;
using ImageMagick.Drawing;
using KakiEQ_WinUI.Models;

namespace KakiEQ_WinUI.Helpers
{
    public static class KronatlPosterExporter
    {
        public static async Task<string?> ExportPosterAsync(KronatlSeason season)
        {
            if (season == null) return null;

            return await Task.Run(() =>
            {
                try
                {
                    uint width = 1920;
                    uint height = 1080;

                    // Deep obsidian base palette matching Kaki EQ dark aesthetics
                    var bgColor = new MagickColor("#0F0C16");
                    using var image = new MagickImage(bgColor, width, height);
                    image.Format = MagickFormat.Png;

                    var stats = season.Stats;

                    // Season colors
                    var primaryAccent = ParseMagickColor(season.AccentColor, "#FF6E1A");     // Orange for Autumn
                    var secondaryAccent = ParseMagickColor(season.SecondaryAccentColor, "#00E676"); // Green
                    var tertiaryAccent = ParseMagickColor(season.TertiaryAccentColor, "#9C27B0");  // Purple

                    var textWhite = new MagickColor("#FFFFFF");
                    var textSubtle = new MagickColor("#D0C8DE");
                    var textMuted = new MagickColor("#837B96");
                    var cardBg = new MagickColor("#161122");
                    var cardBorder = new MagickColor("#302042");

                    string? fontTitle = GetAztecFontPath() ?? GetBestFontPath(true);
                    string? fontBold = GetBestFontPath(true);
                    string? fontReg = GetBestFontPath(false);

                    var drawables = new Drawables();

                    // 1. Atmospheric Ambient Glow Orbs in Background
                    drawables
                        .FillColor(new MagickColor(primaryAccent.R, primaryAccent.G, primaryAccent.B, (byte)42))
                        .Circle(360, 280, 540, 280) // Left aura behind cover
                        .FillColor(new MagickColor(tertiaryAccent.R, tertiaryAccent.G, tertiaryAccent.B, (byte)36))
                        .Circle(1550, 380, 1730, 380) // Right aura
                        .FillColor(new MagickColor(secondaryAccent.R, secondaryAccent.G, secondaryAccent.B, (byte)28))
                        .Circle(1050, 780, 1210, 780); // Bottom center aura

                    // 2. Top Header Bar (Kaki Official Logo + Brand + Season Badges)
                    int logoX = 80;
                    int logoY = 46;
                    int logoSize = 64;

                    string? logoPath = FindAssetPath("Kaki_Oficial_Blanco.png");
                    if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                    {
                        try
                        {
                            using var logo = new MagickImage(logoPath);
                            logo.Resize((uint)logoSize, (uint)logoSize);
                            image.Composite(logo, logoX, logoY, CompositeOperator.Over);
                        }
                        catch { }
                    }

                    // Brand Title in New Wave Aztec + Subtitle in Montserrat
                    if (fontTitle != null) drawables.Font(fontTitle);
                    drawables
                        .FillColor(textWhite)
                        .FontPointSize(30)
                        .TextAlignment(TextAlignment.Left)
                        .Text(logoX + logoSize + 18, logoY + 32, "KAKI EQ");

                    if (fontReg != null) drawables.Font(fontReg);
                    drawables
                        .FillColor(textMuted)
                        .FontPointSize(12.5)
                        .Text(logoX + logoSize + 20, logoY + 56, "RECAP TEMPORAL · ALTA FIDELIDAD");

                    // Composite Season Emblem for Any Season
                    string emblemFileName = season.Type switch
                    {
                        KronatlSeasonType.Invierno => "kronatl_emblem_invierno.png",
                        KronatlSeasonType.Primavera => "kronatl_emblem_primavera.png",
                        KronatlSeasonType.Verano => "kronatl_emblem_verano.png",
                        KronatlSeasonType.Otoño => "kronatl_emblem_otono.png",
                        _ => "kronatl_emblem_anual.png"
                    };
                    string? emblemPath = FindAssetPath(emblemFileName);
                    int emblemX = 510;
                    int emblemY = logoY + 2;
                    int emblemSize = 58;

                    if (!string.IsNullOrEmpty(emblemPath) && File.Exists(emblemPath))
                    {
                        try
                        {
                            using var emblem = new MagickImage(emblemPath);
                            emblem.Resize((uint)emblemSize, (uint)emblemSize);
                            image.Composite(emblem, emblemX, emblemY, CompositeOperator.Over);
                        }
                        catch { }
                    }

                    // Season Pill Header (with New Wave Aztec Title)
                    int pillX = emblemX + emblemSize + 16;
                    int pillY = logoY + 6;
                    int pillW = 380;
                    int pillH = 48;

                    drawables
                        .FillColor(cardBg)
                        .StrokeColor(primaryAccent)
                        .StrokeWidth(1.8)
                        .RoundRectangle(pillX, pillY, pillX + pillW, pillY + pillH, 16, 16);

                    if (fontTitle != null) drawables.Font(fontTitle);
                    drawables
                        .StrokeWidth(0)
                        .FillColor(primaryAccent)
                        .FontPointSize(19)
                        .TextAlignment(TextAlignment.Center)
                        .Text(pillX + pillW / 2, pillY + 31, season.Title.ToUpperInvariant());

                    // Badges Next to Season Title (in Montserrat Bold/Regular)
                    int b1X = pillX + pillW + 18;
                    int b1W = 215;
                    drawables
                        .FillColor(primaryAccent)
                        .RoundRectangle(b1X, pillY, b1X + b1W, pillY + pillH, 14, 14);

                    if (fontBold != null) drawables.Font(fontBold);
                    drawables
                        .FillColor(new MagickColor("#000000"))
                        .FontPointSize(12.5)
                        .TextAlignment(TextAlignment.Center)
                        .Text(b1X + b1W / 2, pillY + 30, season.SeasonalBadgeText);

                    int b2X = b1X + b1W + 14;
                    int b2W = 195;
                    drawables
                        .FillColor(new MagickColor("#2D163D"))
                        .StrokeColor(new MagickColor("#4F2368"))
                        .StrokeWidth(1)
                        .RoundRectangle(b2X, pillY, b2X + b2W, pillY + pillH, 14, 14);

                    if (fontReg != null) drawables.Font(fontReg);
                    drawables
                        .StrokeWidth(0)
                        .FillColor(new MagickColor("#D8B4FE"))
                        .FontPointSize(12.5)
                        .TextAlignment(TextAlignment.Center)
                        .Text(b2X + b2W / 2, pillY + 30, season.LoreBadgeText);

                    // 3. LEFT COLUMN: Hero Top 1 Track Card (x: 80, y: 145, w: 550, h: 875 -> ends at 1020)
                    int leftCardX = 80;
                    int leftCardY = 145;
                    int leftCardW = 550;
                    int leftCardH = 875;

                    drawables
                        .FillColor(cardBg)
                        .StrokeColor(cardBorder)
                        .StrokeWidth(1.5)
                        .RoundRectangle(leftCardX, leftCardY, leftCardX + leftCardW, leftCardY + leftCardH, 24, 24);

                    if (fontTitle != null) drawables.Font(fontTitle);
                    drawables
                        .StrokeWidth(0)
                        .FillColor(primaryAccent)
                        .FontPointSize(17)
                        .TextAlignment(TextAlignment.Center)
                        .Text(leftCardX + leftCardW / 2, leftCardY + 42, "PISTA DEL ALMA DE LA ESTACIÓN");

                    // Album Cover (Size: 370x370 at center of left card)
                    int coverSize = 370;
                    int coverX = leftCardX + (leftCardW - coverSize) / 2;
                    int coverY = leftCardY + 68;

                    // Draw image first so we can composite cover on top
                    drawables.Draw(image);
                    drawables = new Drawables();

                    if (!string.IsNullOrEmpty(stats.TopTrackCoverPath) && File.Exists(stats.TopTrackCoverPath))
                    {
                        try
                        {
                            using var cover = new MagickImage(stats.TopTrackCoverPath);
                            cover.Resize((uint)coverSize, (uint)coverSize);
                            image.Composite(cover, coverX, coverY, CompositeOperator.Over);
                        }
                        catch
                        {
                            DrawFallbackCover(image, coverX, coverY, coverSize, primaryAccent);
                        }
                    }
                    else
                    {
                        DrawFallbackCover(image, coverX, coverY, coverSize, primaryAccent);
                    }

                    // Cover Art Border & Subtle Glow
                    drawables
                        .FillColor(MagickColors.Transparent)
                        .StrokeColor(new MagickColor(primaryAccent.R, primaryAccent.G, primaryAccent.B, (byte)180))
                        .StrokeWidth(2)
                        .RoundRectangle(coverX, coverY, coverX + coverSize, coverY + coverSize, 20, 20);

                    // Track Title & Artist Below Cover
                    string trackTitle = Truncate(stats.TopTrackTitle, 26);
                    string trackArtist = Truncate(stats.TopTrackArtist, 32);
                    int textCenterY = coverY + coverSize + 52;

                    if (fontBold != null) drawables.Font(fontBold);
                    drawables
                        .StrokeWidth(0)
                        .FillColor(textWhite)
                        .FontPointSize(34)
                        .TextAlignment(TextAlignment.Center)
                        .Text(leftCardX + leftCardW / 2, textCenterY, trackTitle);

                    if (fontReg != null) drawables.Font(fontReg);
                    drawables
                        .FillColor(new MagickColor("#D0BCFF"))
                        .FontPointSize(20)
                        .Text(leftCardX + leftCardW / 2, textCenterY + 40, trackArtist)
                        .FillColor(textMuted)
                        .FontPointSize(14)
                        .Text(leftCardX + leftCardW / 2, textCenterY + 72, $"Pico de escucha: {stats.TopTrackPeakTime}");

                    // Badges Row at Bottom of Left Card (Plays, Camelot, BPM)
                    int badgesY = leftCardY + leftCardH - 72;
                    int badgeH = 48;

                    // 1) Plays Badge
                    int p1X = leftCardX + 35;
                    int p1W = 145;
                    drawables
                        .FillColor(new MagickColor("#201630"))
                        .StrokeColor(cardBorder)
                        .StrokeWidth(1.2)
                        .RoundRectangle(p1X, badgesY, p1X + p1W, badgesY + badgeH, 14, 14);

                    if (fontBold != null) drawables.Font(fontBold);
                    drawables
                        .StrokeWidth(0)
                        .FillColor(textWhite)
                        .FontPointSize(15)
                        .TextAlignment(TextAlignment.Center)
                        .Text(p1X + p1W / 2, badgesY + 30, $"{stats.TopTrackPlayCount} Plays");

                    // 2) Camelot Key Pill with Official Color and Automatic Contrast Calculation
                    int p2X = p1X + p1W + 16;
                    int p2W = 160;
                    var winColor = CamelotColorConverter.GetColor(stats.DominantCamelotKey);
                    var camelotMagick = new MagickColor(winColor.R, winColor.G, winColor.B);
                    double luminosity = (winColor.R * 0.299 + winColor.G * 0.587 + winColor.B * 0.114);
                    var camelotText = luminosity > 135 ? new MagickColor("#000000") : new MagickColor("#FFFFFF");

                    string camelotLabel = string.IsNullOrEmpty(stats.DominantCamelotKey) ? "8A" : stats.DominantCamelotKey;
                    if (!string.IsNullOrEmpty(stats.SecondaryCamelotKey) && stats.SecondaryCamelotKey != stats.DominantCamelotKey)
                    {
                        camelotLabel = $"{stats.DominantCamelotKey} / {stats.SecondaryCamelotKey}";
                    }

                    drawables
                        .FillColor(camelotMagick)
                        .StrokeColor(cardBorder)
                        .StrokeWidth(1)
                        .RoundRectangle(p2X, badgesY, p2X + p2W, badgesY + badgeH, 14, 14)
                        .StrokeWidth(0)
                        .FillColor(camelotText)
                        .FontPointSize(16)
                        .Text(p2X + p2W / 2, badgesY + 30, camelotLabel);

                    // 3) BPM Badge
                    int p3X = p2X + p2W + 16;
                    int p3W = 125;
                    drawables
                        .FillColor(new MagickColor("#201630"))
                        .StrokeColor(cardBorder)
                        .StrokeWidth(1.2)
                        .RoundRectangle(p3X, badgesY, p3X + p3W, badgesY + badgeH, 14, 14);

                    if (fontBold != null) drawables.Font(fontBold);
                    drawables
                        .StrokeWidth(0)
                        .FillColor(textWhite)
                        .FontPointSize(15)
                        .Text(p3X + p3W / 2, badgesY + 30, $"{stats.AverageBpm} BPM");

                    // 4. RIGHT SIDE: 4 BENTO STAT CARDS (2x2 Grid) + Wide Waveform Spectrum Panel
                    int col0X = 660;
                    int col1X = 1260;
                    int bentoW = 580;
                    int bentoH = 260;
                    int row0Y = 145;
                    int row1Y = 430;

                    // Bento Card 1: Tiempo Total de Vibración
                    DrawBentoCard(drawables, col0X, row0Y, bentoW, bentoH, cardBg, cardBorder,
                        "TIEMPO TOTAL DE VIBRACIÓN", stats.TotalListeningFormatted, primaryAccent,
                        "Horas inmersivas en la estación",
                        "El flujo continuo de música que acompañó tus días sin interrupción.", textWhite, textSubtle, textMuted, fontTitle, fontBold, fontReg);

                    // Bento Card 2: Artista Refugio
                    string artistLore = stats.TopArtistTopSongs != null && stats.TopArtistTopSongs.Count > 0
                        ? $"Pistas clave: {string.Join(" · ", stats.TopArtistTopSongs)}"
                        : "Tu compañía sonora más leal este ciclo.";
                    DrawBentoCard(drawables, col1X, row0Y, bentoW, bentoH, cardBg, cardBorder,
                        "ARTISTA REFUGIO", Truncate(stats.TopArtist, 22), new MagickColor("#BB86FC"),
                        $"{stats.TopArtistTimeFormatted} escuchadas en total",
                        artistLore, textWhite, textSubtle, textMuted, fontTitle, fontBold, fontReg);

                    // Bento Card 3: Pico de Obsidiana
                    DrawBentoCard(drawables, col0X, row1Y, bentoW, bentoH, cardBg, cardBorder,
                        "PICO DE OBSIDIANA", stats.ObsidianPeakDateFormatted, primaryAccent,
                        $"{stats.ObsidianPeakHoursFormatted} de reproducción continua",
                        "El día exacto en que la música tomó el control total de tu jornada.", textWhite, textSubtle, textMuted, fontTitle, fontBold, fontReg);

                    // Bento Card 4: Fidelidad Auditiva & Álbum
                    string pureAlbumName = string.IsNullOrEmpty(stats.PureAlbumTitle) || stats.PureAlbumTitle == "—" ? "Todo el catálogo" : stats.PureAlbumTitle;
                    string compactFidelity = $"{stats.LosslessPercentage}% Lossless / Hi-Res";
                    DrawBentoCard(drawables, col1X, row1Y, bentoW, bentoH, cardBg, cardBorder,
                        "ÍNDICE DE FIDELIDAD AUDITIVA", compactFidelity, secondaryAccent,
                        $"Audio sin pérdidas (FLAC / WAV / 24-bit)",
                        $"Álbum Puro: {pureAlbumName} (reproducido íntegramente sin saltos).", textWhite, textSubtle, textMuted, fontTitle, fontBold, fontReg);

                    // 5. BOTTOM SECTION: Wide Acoustic Spectrum & Waveform Panel (y=715 to 1020, h=305)
                    // Perfectly balanced to align with the bottom of the left card at y=1020!
                    int row2Y = 715;
                    int row2H = 305;
                    int wideW = col1X + bentoW - col0X; // 1180px

                    drawables
                        .FillColor(cardBg)
                        .StrokeColor(cardBorder)
                        .StrokeWidth(1.5)
                        .RoundRectangle(col0X, row2Y, col0X + wideW, row2Y + row2H, 20, 20);

                    if (fontTitle != null) drawables.Font(fontTitle);
                    drawables
                        .StrokeWidth(0)
                        .FillColor(secondaryAccent)
                        .FontPointSize(17)
                        .TextAlignment(TextAlignment.Left)
                        .Text(col0X + 28, row2Y + 40, "ESPECTRO ARMÓNICO & PULSO CARDINAL");

                    // 3 Metric Pills inside bottom panel
                    int spY = row2Y + 66;
                    int spH = 38;

                    // Pill 1: Camelot Harmony
                    int sp1X = col0X + 28;
                    int sp1W = 330;
                    drawables
                        .FillColor(new MagickColor("#201630"))
                        .StrokeColor(cardBorder)
                        .StrokeWidth(1)
                        .RoundRectangle(sp1X, spY, sp1X + sp1W, spY + spH, 12, 12);

                    if (fontReg != null) drawables.Font(fontReg);
                    drawables
                        .StrokeWidth(0)
                        .FillColor(secondaryAccent)
                        .FontPointSize(13.5)
                        .TextAlignment(TextAlignment.Center)
                        .Text(sp1X + sp1W / 2, spY + 24, $"{stats.CamelotSpectrumFormatted} · Armonía Introspectiva");

                    // Pill 2: Cardinal Pulse
                    int sp2X = sp1X + sp1W + 16;
                    int sp2W = 320;
                    drawables
                        .FillColor(new MagickColor("#201630"))
                        .StrokeColor(cardBorder)
                        .StrokeWidth(1)
                        .RoundRectangle(sp2X, spY, sp2X + sp2W, spY + spH, 12, 12);

                    drawables
                        .StrokeWidth(0)
                        .FillColor(primaryAccent)
                        .FontPointSize(13.5)
                        .TextAlignment(TextAlignment.Center)
                        .Text(sp2X + sp2W / 2, spY + 24, $"{stats.AverageBpm} BPM · {stats.BpmRhythmDescription}");

                    // Pill 3: Master Lossless Engine
                    int sp3X = sp2X + sp2W + 16;
                    int sp3W = wideW - (sp3X - col0X) - 28;
                    drawables
                        .FillColor(new MagickColor("#201630"))
                        .StrokeColor(cardBorder)
                        .StrokeWidth(1)
                        .RoundRectangle(sp3X, spY, sp3X + sp3W, spY + spH, 12, 12);

                    drawables
                        .StrokeWidth(0)
                        .FillColor(new MagickColor("#D0BCFF"))
                        .FontPointSize(13.5)
                        .TextAlignment(TextAlignment.Center)
                        .Text(sp3X + sp3W / 2, spY + 24, "FLAC 24-bit · Procesamiento BASS de 32-bit");

                    // Full-Width Waveform Visualizer (76 bars centered inside panel)
                    int waveY = row2Y + 205;
                    int waveBars = 76;
                    int barW = 10;
                    int barGap = 5;
                    int totalWaveW = waveBars * (barW + barGap) - barGap;
                    int waveStartX = col0X + (wideW - totalWaveW) / 2;
                    var waveRnd = new Random(season.Title.GetHashCode());

                    for (int b = 0; b < waveBars; b++)
                    {
                        double norm = Math.Sin((double)b / waveBars * Math.PI);
                        int barH = (int)(norm * (28 + waveRnd.Next(10, 52)));
                        int bx = waveStartX + b * (barW + barGap);

                        MagickColor barColor;
                        if (b < waveBars / 3)
                        {
                            barColor = primaryAccent;
                        }
                        else if (b < (waveBars * 2) / 3)
                        {
                            barColor = tertiaryAccent;
                        }
                        else
                        {
                            barColor = secondaryAccent;
                        }

                        drawables
                            .FillColor(new MagickColor(barColor.R, barColor.G, barColor.B, (byte)(150 + norm * 105)))
                            .RoundRectangle(bx, waveY - barH, bx + barW, waveY + barH, 4, 4);
                    }

                    // Draw all elements onto the master image
                    drawables.Draw(image);

                    // 7. Save to disk in Pictures\KakiEQ\Kronatl
                    string picturesFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    string outputDir = Path.Combine(picturesFolder, "KakiEQ", "Kronatl");
                    Directory.CreateDirectory(outputDir);

                    string fileName = $"Kronatl_{season.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    string fullPath = Path.Combine(outputDir, fileName);

                    image.Write(fullPath);
                    return fullPath;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[KronatlPosterExporter] Error exporting horizontal poster: {ex.Message}");
                    return null;
                }
            });
        }

        private static void DrawBentoCard(Drawables draw, int x, int y, int w, int h, MagickColor bg, MagickColor border,
            string label, string value, MagickColor accent, string subtitle, string lore,
            MagickColor textWhite, MagickColor textSubtle, MagickColor textMuted, string? fontTitle, string? fontBold, string? fontReg)
        {
            draw
                .FillColor(bg)
                .StrokeColor(border)
                .StrokeWidth(1.5)
                .RoundRectangle(x, y, x + w, y + h, 20, 20);

            // Section Title in New Wave Aztec
            if (fontTitle != null) draw.Font(fontTitle);
            draw
                .StrokeWidth(0)
                .FillColor(accent)
                .FontPointSize(16.5)
                .TextAlignment(TextAlignment.Left)
                .Text(x + 28, y + 40, label);

            // Big Value in Montserrat Bold (auto-scaled to always fit cleanly within 580px)
            if (fontBold != null) draw.Font(fontBold);
            double valueFontSize = value.Length > 20 ? 27 : (value.Length > 14 ? 29 : 32);
            draw
                .FillColor(textWhite)
                .FontPointSize(valueFontSize)
                .Text(x + 28, y + 96, value);

            // Subtitle in Montserrat Regular
            if (fontReg != null) draw.Font(fontReg);
            draw
                .FillColor(textSubtle)
                .FontPointSize(15.5)
                .Text(x + 28, y + 150, subtitle);

            // Narrative Lore in Montserrat Regular
            draw
                .FillColor(textMuted)
                .FontPointSize(13.5)
                .Text(x + 28, y + 196, Truncate(lore, 72));
        }

        private static void DrawFallbackCover(MagickImage image, int x, int y, int size, MagickColor accent)
        {
            var draw = new Drawables()
                .FillColor(new MagickColor("#1E172A"))
                .StrokeColor(accent)
                .StrokeWidth(2)
                .RoundRectangle(x, y, x + size, y + size, 16, 16)
                .FillColor(accent)
                .FontPointSize(68)
                .TextAlignment(TextAlignment.Center)
                .Text(x + size / 2, y + size / 2 + 20, "♫");
            draw.Draw(image);
        }

        private static MagickColor ParseMagickColor(string? hex, string fallback)
        {
            try
            {
                if (!string.IsNullOrEmpty(hex))
                {
                    string clean = hex.Trim().TrimStart('#');
                    if (clean.Length == 6)
                    {
                        byte r = Convert.ToByte(clean.Substring(0, 2), 16);
                        byte g = Convert.ToByte(clean.Substring(2, 2), 16);
                        byte b = Convert.ToByte(clean.Substring(4, 2), 16);
                        return new MagickColor(r, g, b);
                    }
                }
            }
            catch { }
            return new MagickColor(fallback);
        }

        private static string? GetAztecFontPath()
        {
            try
            {
                // First prefer the Unicode-mapped NewWaveAztec.ttf bundled in Assets
                string? bundledAztec = FindAssetPath("NewWaveAztec.ttf");
                if (!string.IsNullOrEmpty(bundledAztec) && File.Exists(bundledAztec))
                {
                    return bundledAztec;
                }

                string localFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts");
                string userAztec = Path.Combine(localFonts, "New Wave Aztec.ttf");
                if (File.Exists(userAztec)) return userAztec;
            }
            catch { }

            return null;
        }

        private static string? GetBestFontPath(bool bold)
        {
            try
            {
                string localFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts");
                string montserratBold = Path.Combine(localFonts, "Montserrat-Bold.ttf");
                string montserratReg = Path.Combine(localFonts, "Montserrat-Regular.ttf");

                if (bold && File.Exists(montserratBold)) return montserratBold;
                if (!bold && File.Exists(montserratReg)) return montserratReg;
                if (File.Exists(montserratBold)) return montserratBold;

                string winFonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
                string winMontserrat = Path.Combine(winFonts, bold ? "Montserrat-Bold.ttf" : "Montserrat-Regular.ttf");
                if (File.Exists(winMontserrat)) return winMontserrat;
            }
            catch { }

            return null;
        }

        private static string? FindAssetPath(string fileName)
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", fileName),
                Path.Combine(AppContext.BaseDirectory, "AppX", "Assets", fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName),
                Path.Combine(Environment.CurrentDirectory, "Assets", fileName)
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private static string Truncate(string? value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value)) return "—";
            return value.Length <= maxChars ? value : value.Substring(0, maxChars - 1) + "…";
        }
    }
}
