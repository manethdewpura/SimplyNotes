// ============================================================
//  Services/NoteTheme.cs
//  Centralised colour palette & accent colour helpers.
// ============================================================
using System.Windows.Media;

namespace StickyNotes.Services
{
    /// <summary>
    /// Provides the five built-in pastel colour themes and derived accent colours
    /// used by the header and topic bar.
    /// </summary>
    public static class NoteTheme
    {
        // ── Palette definition ───────────────────────────────────────────

        /// <summary>
        /// Ordered list of (Display-Name, Hex) pairs shown in the colour picker.
        /// The ordering is reflected in the UI.
        /// </summary>
        public static readonly IReadOnlyList<(string Name, string Hex)> Palette =
            new List<(string, string)>
            {
                ("Yellow", "#FDE047"),
                ("Blue",   "#93C5FD"),
                ("Green",  "#86EFAC"),
                ("Pink",   "#F9A8D4"),
                ("Purple", "#C4B5FD"),
                ("Red",    "#FCA5A5"),
                ("Orange", "#FDBA74"),
            };

        /// <summary>The hex colour used when creating a brand-new note.</summary>
        public const string DefaultColor = "#FDE047";  // Vibrant Yellow

        // ── Derived accent helpers ───────────────────────────────────────

        /// <summary>
        /// Returns a slightly-darkened version of <paramref name="baseHex"/>
        /// used for the header / topic-bar background, giving the note visual
        /// depth without requiring extra resources.
        /// </summary>
        public static Color GetAccentColor(string baseHex)
        {
            var c = ParseHex(baseHex);
            return DarkenBy(c, factor: 0.10);  // 10% darker
        }

        /// <summary>Converts a "#RRGGBB" string to a <see cref="Color"/>.</summary>
        public static Color ParseHex(string hex)
            => (Color)ColorConverter.ConvertFromString(hex);

        /// <summary>Creates a <see cref="SolidColorBrush"/> from a hex string.</summary>
        public static SolidColorBrush BrushFromHex(string hex)
            => new SolidColorBrush(ParseHex(hex));

        // ── Private helpers ──────────────────────────────────────────────

        private static Color DarkenBy(Color c, double factor)
        {
            double inv = 1.0 - factor;
            return Color.FromRgb(
                (byte)(c.R * inv),
                (byte)(c.G * inv),
                (byte)(c.B * inv));
        }
    }
}
