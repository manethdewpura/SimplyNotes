// ============================================================
//  Models/NoteData.cs
//  Immutable-friendly data model for a single Simply note.
// ============================================================
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimplyNotes.Models
{
    /// <summary>
    /// Represents the persisted data for a single Simply note.
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

        /// <summary>Whether the note is pinned to remain always on top.</summary>
        public bool IsTopmost { get; set; } = false;

        // ── Checklist mode ───────────────────────────────────────────────

        /// <summary>Whether the note is displaying interactive checkboxes instead of rich text.</summary>
        public bool IsChecklistMode { get; set; } = false;

        /// <summary>The items for the checklist.</summary>
        public List<TodoItem> Checklist { get; set; } = new();
    }

    /// <summary>
    /// Represents a single item in a Simply note checklist.
    /// </summary>
    public class TodoItem : INotifyPropertyChanged
    {
        private bool _isCompleted;
        private string _text = string.Empty;

        public Guid Id { get; set; } = Guid.NewGuid();

        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
