// ============================================================
//  Models/NoteData.cs
//  Immutable-friendly data model for a single sticky note.
// ============================================================
namespace StickyNotes.Models
{
    /// <summary>
    /// Represents the persisted data for a single sticky note.
    /// All members are get/set so System.Text.Json can deserialize them.
    /// </summary>
    public sealed class NoteData
    {
        /// <summary>Unique, stable identifier (used as the JSON primary key).</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Plain-text body of the note.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Optional user-defined category label (e.g. "Work", "Ideas").</summary>
        public string Topic { get; set; } = string.Empty;

        /// <summary>Background hex colour string (e.g. "#FEF3C7").</summary>
        public string Color { get; set; } = Services.NoteTheme.DefaultColor;

        // ── Window geometry ──────────────────────────────────────────────

        /// <summary>Screen X-coordinate of the window's left edge.</summary>
        public double Left { get; set; } = 100;

        /// <summary>Screen Y-coordinate of the window's top edge.</summary>
        public double Top { get; set; } = 100;

        /// <summary>Window width in device-independent pixels.</summary>
        public double Width { get; set; } = 280;

        /// <summary>Window height in device-independent pixels.</summary>
        public double Height { get; set; } = 320;
    }
}
