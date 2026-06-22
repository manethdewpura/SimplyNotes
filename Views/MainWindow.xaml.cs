// ============================================================
//  Views/MainWindow.xaml.cs — Per-note window logic
// ============================================================
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using StickyNotes.Models;
using StickyNotes.Services;

namespace StickyNotes.Views
{
    /// <summary>
    /// Code-behind for a single sticky-note window.
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

            _isInitialising = false;
        }

        // ── Private: Initialisation ──────────────────────────────────────

        /// <summary>Populates the UI controls from the bound <see cref="NoteData"/>.</summary>
        private void LoadContent()
        {
            NoteTextBox.Text       = _note.Content;
            NoteTextBox.CaretIndex = NoteTextBox.Text.Length;  // cursor at end

            if (!string.IsNullOrWhiteSpace(_note.Topic))
                TopicLabel.Text = _note.Topic;
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

        private void NoteTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _note.Content = NoteTextBox.Text;
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
