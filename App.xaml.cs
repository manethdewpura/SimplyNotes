// ============================================================
//  App.xaml.cs — Application entry-point
// ============================================================
using System.Windows;
using StickyNotes.Models;
using StickyNotes.Services;
using StickyNotes.Views;

namespace StickyNotes
{
    /// <summary>
    /// Application bootstrap.
    ///
    /// Responsibilities:
    ///   • Load persisted notes from disk on startup.
    ///   • Open a <see cref="MainWindow"/> for every saved note.
    ///   • Create a default welcome note on first run.
    ///   • Track open windows and shut down when the last one closes.
    ///   • Flush pending saves before process exit.
    /// </summary>
    public partial class App : Application
    {
        // ── Window registry ──────────────────────────────────────────────

        /// <summary>
        /// All currently-open note windows keyed by <see cref="NoteData.Id"/>.
        /// Managed via <see cref="RegisterWindow"/> / <see cref="UnregisterWindow"/>.
        /// </summary>
        private readonly Dictionary<Guid, MainWindow> _openWindows = new();

        // ── Startup ──────────────────────────────────────────────────────

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Global safety nets — avoid silent crashes in production
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException               += OnDispatcherUnhandledException;

            var notes = NoteStore.Instance.LoadAll();

            if (notes.Count == 0)
                notes.Add(CreateWelcomeNote());

            // Stagger windows so they don't stack perfectly on first run
            for (int i = 0; i < notes.Count; i++)
                OpenNoteWindow(notes[i], cascadeOffset: i * 20);
        }

        // ── Shutdown ─────────────────────────────────────────────────────

        protected override async void OnExit(ExitEventArgs e)
        {
            await NoteStore.Instance.FlushAsync().ConfigureAwait(false);
            NoteStore.Instance.Dispose();
            base.OnExit(e);
        }

        // ── Public API (called by MainWindow) ────────────────────────────

        /// <summary>
        /// Creates a brand-new note and opens its window.
        /// New notes cascade 30 px from the most recently opened note.
        /// </summary>
        public void CreateNewNote()
        {
            double left = 200, top = 200;
            if (_openWindows.Count > 0)
            {
                var last = _openWindows.Values.Last();
                left = last.Left + 30;
                top  = last.Top  + 30;
            }

            var note = new NoteData { Left = left, Top = top };
            NoteStore.Instance.Upsert(note);
            OpenNoteWindow(note);
        }

        /// <summary>
        /// Deletes <paramref name="id"/> from the store and shuts down the
        /// application if no windows remain.
        /// </summary>
        public async void DeleteNote(Guid id)
        {
            _openWindows.Remove(id);
            await NoteStore.Instance.DeleteAsync(id).ConfigureAwait(false);

            if (_openWindows.Count == 0)
                Dispatcher.Invoke(Shutdown);
        }

        /// <summary>Registers a window so the app knows it is open.</summary>
        public void RegisterWindow(Guid id, MainWindow window)
            => _openWindows[id] = window;

        /// <summary>
        /// Unregisters a window.  Shuts the app down if no windows remain.
        /// </summary>
        public void UnregisterWindow(Guid id)
        {
            _openWindows.Remove(id);
            if (_openWindows.Count == 0)
                Shutdown();
        }

        // ── Private helpers ──────────────────────────────────────────────

        private static NoteData CreateWelcomeNote() => new()
        {
            Content = "Welcome to Sticky Notes! ✏️\n\nDouble-click the topic bar below to set a category.",
            Topic   = "Welcome",
            Color   = NoteTheme.DefaultColor,
        };

        private void OpenNoteWindow(NoteData note, int cascadeOffset = 0)
        {
            var win = new MainWindow(note);

            if (cascadeOffset > 0)
            {
                win.Left = note.Left + cascadeOffset;
                win.Top  = note.Top  + cascadeOffset;
            }

            RegisterWindow(note.Id, win);
            win.Show();
        }

        // ── Unhandled exception handlers ─────────────────────────────────

        private void OnDispatcherUnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[App] UI exception: {e.Exception}");
            e.Handled = true;  // keep the dispatcher loop alive
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
            => System.Diagnostics.Debug.WriteLine($"[App] Fatal exception: {e.ExceptionObject}");
    }
}
