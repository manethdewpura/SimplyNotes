// ============================================================
//  Services/NoteStore.cs
//  Thread-safe JSON persistence layer.
// ============================================================
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimplyNotes.Models;

namespace SimplyNotes.Services
{
    /// <summary>
    /// Manages reading and writing the <c>notes.json</c> file that stores all
    /// Simply-note data.  All I/O is marshalled through a <see cref="SemaphoreSlim"/>
    /// so concurrent debounced saves never corrupt the file.
    /// </summary>
    public sealed class NoteStore : IDisposable
    {
        // ── Singleton ────────────────────────────────────────────────────

        /// <summary>Application-wide singleton, initialised once in App.xaml.cs.</summary>
        public static NoteStore Instance { get; } = new NoteStore();

        // ── Constants ────────────────────────────────────────────────────

        private static readonly string StorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimplyNotes",
            "notes.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        // ── Fields ───────────────────────────────────────────────────────

        /// <summary>In-memory working copy; keyed by note <see cref="NoteData.Id"/>.</summary>
        private readonly Dictionary<Guid, NoteData> _notes = new();

        /// <summary>Guards concurrent file I/O so only one write runs at a time.</summary>
        private readonly SemaphoreSlim _ioLock = new(initialCount: 1, maxCount: 1);

        // ── Construction (private — use <see cref="Instance"/>) ──────────

        private NoteStore() { }

        // ── Public API ───────────────────────────────────────────────────

        /// <summary>
        /// Loads all notes from disk into the in-memory store.
        /// Returns an empty list when the file does not yet exist or is malformed
        /// (fault-tolerant behaviour — the app always starts).
        /// </summary>
        public List<NoteData> LoadAll()
        {
            EnsureDirectory();
            _notes.Clear();

            if (!File.Exists(StorePath))
                return new List<NoteData>();

            try
            {
                var json = File.ReadAllText(StorePath);
                var list = JsonSerializer.Deserialize<List<NoteData>>(json, JsonOptions)
                           ?? new List<NoteData>();

                foreach (var note in list)
                    _notes[note.Id] = note;

                return list;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteStore] LoadAll failed: {ex.Message}");
                return new List<NoteData>();
            }
        }

        /// <summary>
        /// Upserts <paramref name="note"/> in the in-memory store.
        /// The caller is responsible for calling <see cref="FlushAsync"/> (typically
        /// via the debounce timer) to actually persist the change.
        /// </summary>
        public void Upsert(NoteData note) => _notes[note.Id] = note;

        /// <summary>
        /// Removes the note with <paramref name="id"/> from the in-memory store
        /// and immediately persists the change to disk.
        /// </summary>
        public async Task DeleteAsync(Guid id)
        {
            _notes.Remove(id);
            await FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Writes the entire in-memory store to <c>notes.json</c> atomically
        /// (write-to-temp → rename) so a crash mid-write never corrupts the file.
        /// Thread-safe via an async semaphore.
        /// </summary>
        public async Task FlushAsync()
        {
            await _ioLock.WaitAsync().ConfigureAwait(false);
            try
            {
                EnsureDirectory();
                var list = new List<NoteData>(_notes.Values);
                var json = JsonSerializer.Serialize(list, JsonOptions);

                var tmpPath = StorePath + ".tmp";
                await File.WriteAllTextAsync(tmpPath, json).ConfigureAwait(false);
                File.Move(tmpPath, StorePath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteStore] FlushAsync failed: {ex.Message}");
            }
            finally
            {
                _ioLock.Release();
            }
        }

        // ── IDisposable ──────────────────────────────────────────────────

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ioLock.Dispose();
        }

        // ── Private helpers ──────────────────────────────────────────────

        private static void EnsureDirectory()
        {
            var dir = Path.GetDirectoryName(StorePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
