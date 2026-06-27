// ============================================================
//  Views/MainWindow.xaml.cs — Per-note window logic
// ============================================================
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using SimplyNotes.Models;
using SimplyNotes.Services;

namespace SimplyNotes.Views
{
    /// <summary>
    /// Code-behind for a single Simply-note window.
    ///
    /// Design decisions:
    ///   • The window owns a <see cref="NoteData"/> object and mutates it in-place;
    ///     <see cref="NoteStore"/> is only called via the debounce timer.
    ///   • The debounce timer resets on every content/geometry change and only fires
    ///     once the user has been idle for 500 ms, keeping disk I/O minimal.
    ///   • The colour-picker <see cref="Popup"/> is built lazily on first use to
    ///     keep startup allocation low across many open notes.
    ///   • The topic editor overlays the label using a collapsed <see cref="Border"/>
    ///     so no layout recalculation occurs on toggle.
    /// </summary>
    public partial class MainWindow : Window
    {
        // ── Fields ───────────────────────────────────────────────────────

        private readonly NoteData _note;
        private readonly DispatcherTimer _debounce;
        private Popup? _colorPopup;             // lazy-created on first click

        public ObservableCollection<TodoItem> Todos { get; } = new();

        // Prevent saves while we are loading initial data into controls
        private bool _isInitialising = true;

        // ── Construction ─────────────────────────────────────────────────

        /// <summary>
        /// Creates a window bound to <paramref name="note"/>.
        /// The caller is responsible for calling <see cref="Window.Show"/>.
        /// </summary>
        public MainWindow(NoteData note)
        {
            _note = note ?? throw new ArgumentNullException(nameof(note));

            InitializeComponent();

            // 500 ms debounce — fires once after the user stops typing / moving
            _debounce = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _debounce.Tick += async (_, _) =>
            {
                _debounce.Stop();
                SnapshotGeometry();
                NoteStore.Instance.Upsert(_note);
                await NoteStore.Instance.FlushAsync();
            };

            LocationChanged += OnGeometryChanged;
            SizeChanged     += OnGeometryChanged;

            BuildContextColorMenu();
            ApplyTheme(_note.Color, animate: false);
            LoadContent();

            Left   = _note.Left;
            Top    = _note.Top;
            Width  = _note.Width;
            Height = _note.Height;

            BtnPin.IsChecked = _note.IsTopmost;
            Topmost = _note.IsTopmost;
            
            ChecklistItemsControl.ItemsSource = Todos;
            BtnChecklist.IsChecked = _note.IsChecklistMode;

            _isInitialising = false;
        }

        // ── Private: Initialisation ──────────────────────────────────────

        /// <summary>Populates the UI controls from the bound <see cref="NoteData"/>.</summary>
        private void LoadContent()
        {
            if (!string.IsNullOrEmpty(_note.Content))
            {
                if (_note.Content.StartsWith(@"{\rtf1"))
                {
                    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_note.Content));
                    var textRange = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
                    textRange.Load(stream, DataFormats.Rtf);
                }
                else
                {
                    var textRange = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
                    textRange.Text = _note.Content;
                }
            }
            
            NoteRichTextBox.CaretPosition = NoteRichTextBox.Document.ContentEnd;

            if (!string.IsNullOrWhiteSpace(_note.Topic))
                TopicLabel.Text = _note.Topic;

            foreach (var item in _note.Checklist)
            {
                var todo = new TodoItem { Id = item.Id, IsCompleted = item.IsCompleted, Text = item.Text };
                todo.PropertyChanged += (s, e) => SyncChecklist();
                Todos.Add(todo);
            }

            if (_note.IsChecklistMode)
            {
                NoteRichTextBox.Visibility = Visibility.Collapsed;
                ChecklistScrollViewer.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Populates the "Change Color" context-menu item with one child
        /// <see cref="MenuItem"/> per palette entry.
        /// </summary>
        private void BuildContextColorMenu()
        {
            foreach (var (name, hex) in NoteTheme.Palette)
            {
                var capturedHex = hex;
                var item = new MenuItem
                {
                    Header = name,
                    Icon   = CreateSwatchEllipse(hex),
                };
                item.Click += (_, _) => ApplyTheme(capturedHex, animate: true);
                ContextColorMenu.Items.Add(item);
            }
        }

        /// <summary>Creates a small circular colour swatch used as a menu icon.</summary>
        private static Ellipse CreateSwatchEllipse(string hex) => new()
        {
            Width           = 14,
            Height          = 14,
            Fill            = NoteTheme.BrushFromHex(hex),
            Stroke          = new SolidColorBrush(Color.FromRgb(156, 163, 175)),
            StrokeThickness = 1,
        };

        // ── Private: Theming ─────────────────────────────────────────────

        /// <summary>
        /// Applies a colour theme to all coloured surfaces.
        /// When <paramref name="animate"/> is <c>true</c> a quick cross-fade
        /// is used; during initial load we set colours instantly.
        /// </summary>
        private void ApplyTheme(string hex, bool animate)
        {
            _note.Color = hex;

            var baseBrush   = new SolidColorBrush(NoteTheme.ParseHex(hex));
            var accentBrush = new SolidColorBrush(NoteTheme.GetAccentColor(hex));

            if (animate)
            {
                CrossFadeBrush(RootBorder,   baseBrush);
                CrossFadeBrush(HeaderBorder, accentBrush);
                CrossFadeBrush(TopicBorder,  accentBrush);
            }
            else
            {
                RootBorder.Background   = baseBrush;
                HeaderBorder.Background = accentBrush;
                TopicBorder.Background  = accentBrush;
            }

            if (_colorPopup is { IsOpen: true })
                _colorPopup.IsOpen = false;

            ScheduleSave();
        }

        /// <summary>
        /// Animates a <see cref="Border"/>'s background to <paramref name="target"/>
        /// using a 200 ms <see cref="ColorAnimation"/> with cubic ease-out.
        /// Falls back to an instant swap if animation fails.
        /// </summary>
        private static void CrossFadeBrush(Border border, SolidColorBrush target)
        {
            try
            {
                var from     = (border.Background as SolidColorBrush)?.Color ?? target.Color;
                var animated = new SolidColorBrush(from);
                var anim     = new ColorAnimation
                {
                    To             = target.Color,
                    Duration       = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                };
                animated.BeginAnimation(SolidColorBrush.ColorProperty, anim);
                border.Background = animated;
            }
            catch
            {
                border.Background = target;
            }
        }

        // ── Private: Colour-picker Popup ─────────────────────────────────

        /// <summary>
        /// Lazily creates and toggles the floating colour-picker
        /// <see cref="Popup"/> anchored to <see cref="BtnColorPicker"/>.
        /// </summary>
        private void ShowColorPickerPopup()
        {
            _colorPopup ??= BuildColorPopup();
            _colorPopup.IsOpen = !_colorPopup.IsOpen;
        }

        /// <summary>Builds the colour-picker popup with one swatch button per palette entry.</summary>
        private Popup BuildColorPopup()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin      = new Thickness(6),
            };

            foreach (var (name, hex) in NoteTheme.Palette)
            {
                var capturedHex = hex;
                var btn = new Button
                {
                    Style      = (Style)FindResource("SwatchButtonStyle"),
                    Background = NoteTheme.BrushFromHex(hex),
                    ToolTip    = name,
                };
                System.Windows.Automation.AutomationProperties.SetName(btn, $"{name} color");
                btn.Click += (_, _) => ApplyTheme(capturedHex, animate: true);
                panel.Children.Add(btn);
            }

            var border = new Border
            {
                Background      = new SolidColorBrush(Color.FromRgb(255, 253, 246)),
                BorderBrush     = new SolidColorBrush(Color.FromRgb(209, 213, 219)),
                BorderThickness = new Thickness(1),
                CornerRadius    = new CornerRadius(8),
                Child           = panel,
                Effect          = new DropShadowEffect
                {
                    BlurRadius  = 10,
                    ShadowDepth = 3,
                    Opacity     = 0.18,
                    Color       = Colors.Black,
                    Direction   = 270,
                },
            };

            return new Popup
            {
                Child              = border,
                PlacementTarget    = BtnColorPicker,
                Placement          = PlacementMode.Bottom,
                StaysOpen          = false,
                AllowsTransparency = true,
                PopupAnimation     = PopupAnimation.Fade,
            };
        }

        // ── Private: Topic editing ───────────────────────────────────────

        /// <summary>Switches the topic bar into edit mode on double-click.</summary>
        private void BeginTopicEdit()
        {
            TopicTextBox.Text            = _note.Topic;
            TopicEditorBorder.Visibility = Visibility.Visible;
            TopicLabel.Visibility        = Visibility.Collapsed;
            TopicTextBox.Focus();
            TopicTextBox.SelectAll();
        }

        /// <summary>Commits the edited topic and returns to read-only mode.</summary>
        private void CommitTopicEdit()
        {
            _note.Topic = TopicTextBox.Text.Trim();
            TopicLabel.Text = string.IsNullOrWhiteSpace(_note.Topic)
                ? "Double-click to set topic…"
                : _note.Topic;

            TopicEditorBorder.Visibility = Visibility.Collapsed;
            TopicLabel.Visibility        = Visibility.Visible;
            ScheduleSave();
        }

        /// <summary>Cancels editing and reverts the topic to its previous value.</summary>
        private void CancelTopicEdit()
        {
            TopicEditorBorder.Visibility = Visibility.Collapsed;
            TopicLabel.Visibility        = Visibility.Visible;
        }

        // ── Private: Geometry & save scheduling ─────────────────────────

        /// <summary>Snapshots the current window position and size into the model.</summary>
        private void SnapshotGeometry()
        {
            if (WindowState != WindowState.Normal) return;
            _note.Left   = Left;
            _note.Top    = Top;
            _note.Width  = ActualWidth;
            _note.Height = ActualHeight;
        }

        /// <summary>
        /// Resets the 500 ms debounce timer.  Repeated calls collapse into a
        /// single save 500 ms after the last activity.
        /// </summary>
        private void ScheduleSave()
        {
            if (_isInitialising) return;
            _debounce.Stop();
            _debounce.Start();
        }

        // ── Event handlers ───────────────────────────────────────────────

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
                DragMove();
        }

        private void BtnPin_Checked(object sender, RoutedEventArgs e)
        {
            Topmost = true;
            _note.IsTopmost = true;
            ScheduleSave();
        }

        private void BtnPin_Unchecked(object sender, RoutedEventArgs e)
        {
            Topmost = false;
            _note.IsTopmost = false;
            ScheduleSave();
        }

        // ── Checklist mode ───────────────────────────────────────────────

        private void BtnChecklist_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitialising) return;
            _note.IsChecklistMode = true;
            
            NoteRichTextBox.Visibility = Visibility.Collapsed;
            ChecklistScrollViewer.Visibility = Visibility.Visible;

            if (Todos.Count == 0)
            {
                var textRange = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
                var lines = textRange.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var todo = new TodoItem { Text = line.Trim() };
                    todo.PropertyChanged += (s, ev) => SyncChecklist();
                    Todos.Add(todo);
                }
                SyncChecklist();
            }

            ScheduleSave();
        }

        private void BtnChecklist_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitialising) return;
            _note.IsChecklistMode = false;
            
            ChecklistScrollViewer.Visibility = Visibility.Collapsed;
            NoteRichTextBox.Visibility = Visibility.Visible;

            ScheduleSave();
        }

        private void TodoCheckBox_Checked(object sender, RoutedEventArgs e) => SyncChecklist();
        private void TodoTextBox_TextChanged(object sender, TextChangedEventArgs e) => SyncChecklist();

        private void BtnAddTodo_Click(object sender, RoutedEventArgs e)
        {
            var todo = new TodoItem();
            todo.PropertyChanged += (s, ev) => SyncChecklist();
            Todos.Add(todo);
            SyncChecklist();
        }

        private void BtnDeleteTodo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TodoItem item)
            {
                Todos.Remove(item);
                SyncChecklist();
            }
        }

        private void SyncChecklist()
        {
            if (_isInitialising) return;
            _note.Checklist = Todos.Select(t => new TodoItem { Id = t.Id, IsCompleted = t.IsCompleted, Text = t.Text }).ToList();
            ScheduleSave();
        }

        private void BtnStrikethrough_Click(object sender, RoutedEventArgs e)
        {
            var selection = NoteRichTextBox.Selection;
            if (selection.IsEmpty) return;

            var currentDecorations = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            var newDecorations = new TextDecorationCollection();
            
            if (currentDecorations != null)
                newDecorations.Add(currentDecorations);

            var hasStrikethrough = false;
            foreach (var decoration in newDecorations)
            {
                if (decoration.Location == TextDecorationLocation.Strikethrough)
                {
                    hasStrikethrough = true;
                    newDecorations.Remove(decoration);
                    break;
                }
            }

            if (!hasStrikethrough)
                newDecorations.Add(TextDecorations.Strikethrough);

            selection.ApplyPropertyValue(Inline.TextDecorationsProperty, newDecorations);
            NoteRichTextBox.Focus();
        }

        private void BtnNewNote_Click(object sender, RoutedEventArgs e)
            => ((App)Application.Current).CreateNewNote();

        private void BtnColorPicker_Click(object sender, RoutedEventArgs e)
            => ShowColorPickerPopup();

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            _debounce.Stop();
            ((App)Application.Current).DeleteNote(_note.Id);
            Close();
        }

        private void NoteRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitialising) return;
            
            var textRange = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            using var stream = new MemoryStream();
            textRange.Save(stream, DataFormats.Rtf);
            _note.Content = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            
            ScheduleSave();
        }

        private void OnGeometryChanged(object? sender, EventArgs e)
            => ScheduleSave();

        private void TopicLabel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
                BeginTopicEdit();
        }

        private void TopicTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    e.Handled = true;
                    CommitTopicEdit();
                    break;
                case Key.Escape:
                    CancelTopicEdit();
                    break;
            }
        }

        private void TopicTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (TopicEditorBorder.Visibility == Visibility.Visible)
                CommitTopicEdit();
        }

        // ── Window lifecycle ─────────────────────────────────────────────

        protected override void OnClosed(EventArgs e)
        {
            _debounce.Stop();
            ((App)Application.Current).UnregisterWindow(_note.Id);
            base.OnClosed(e);
        }
    }
}
