using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using SIL.TextEditing.Avalonia;

namespace SIL.TextEditing.Playground
{
    // The selection bar: a small toolbar that floats above the selection while there is one, the way word processors show their mini toolbar.
    // The first row is the simple set (bold, italic, underline, writing system, character style); "More" opens the second row with the rest
    // (paragraph style, a picture). It is the host's control, built on Avalonia's Popup: the field only says where the caret is (CaretRect).
    public sealed partial class PlaygroundView
    {
        Popup _selectionBar = null!;
        StackPanel _selectionMore = null!;
        ComboBox _selectionWs = null!;
        int _selectionBarGeneration;   // a posted open runs only if nothing has hidden or moved the bar since it was posted

        /// <summary>True while the selection bar is shown over the active field's selection.</summary>
        public bool SelectionBarOpen => _selectionBar.IsOpen;

        /// <summary>Whether the bar appears over a selection (default true).</summary>
        public bool SelectionBarEnabled
        {
            get => _selectionBarEnabled;
            set
            {
                _selectionBarEnabled = value;
                UpdateSelectionBar();
            }
        }

        bool _selectionBarEnabled = true;

        /// <summary>True while the bar's second row (paragraph style, picture) is open.</summary>
        public bool SelectionBarExpanded => _selectionMore.IsVisible;

        Popup BuildSelectionBar()
        {
            var simple = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            simple.Children.Add(BarTool(Names.BarBold, new TextBlock { Text = "B", FontWeight = FontWeight.Bold }, "Bold (Ctrl+B)", () => ToggleBold()));
            simple.Children.Add(BarTool(Names.BarItalic, new TextBlock { Text = "I", FontStyle = FontStyle.Italic }, "Italic (Ctrl+I)", () => ToggleItalic()));
            simple.Children.Add(BarTool(Names.BarUnderline, new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline }, "Underline (Ctrl+U)", () => ToggleUnderline()));
            _selectionWs = new ComboBox
            {
                Name = Names.BarWritingSystem,
                MinWidth = 120,
                IsTabStop = false,
                ItemsSource = SampleWritingSystems.All.Select(w => w.Tag + "  " + w.Name).ToList(),
            };
            ToolTip.SetTip(_selectionWs, "Writing system of the selection");
            _selectionWs.SelectionChanged += (s, e) =>
            {
                if (_syncing || _selectionWs.SelectedIndex < 0) return;
                SetWritingSystem(SampleWritingSystems.All[_selectionWs.SelectedIndex].Tag);
                _active.Field.Focus();
            };
            _selectionWs.DropDownClosed += (s, e) => _active.Field.Focus();   // Esc too: typing goes back to the field
            simple.Children.Add(_selectionWs);
            simple.Children.Add(StyleBox(Names.BarCharStyle, SampleStyles.CharacterStyles, "Character style", name => ApplyCharacterStyle(name), "Style"));
            var more = new ToggleButton { Name = Names.BarMore, Content = "More", Focusable = false, Padding = new Thickness(8, 3) };
            ToolTip.SetTip(more, "More: paragraph style and pictures");
            simple.Children.Add(more);

            _selectionMore = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, IsVisible = false, Margin = new Thickness(0, 2, 0, 0) };
            _selectionMore.Children.Add(StyleBox(Names.BarParaStyle, SampleStyles.ParagraphStyles, "Paragraph style of the selected paragraphs", name => ApplyParagraphStyle(name), "Paragraph style"));
            _selectionMore.Children.Add(BarTool(Names.BarPicture, "Picture", "Put a picture in place of the selection", () => InsertPicture()));
            more.IsCheckedChanged += (s, e) => _selectionMore.IsVisible = more.IsChecked == true;

            var rows = new StackPanel();
            rows.Children.Add(simple);
            rows.Children.Add(_selectionMore);
            var frame = new Border
            {
                Child = rows,
                Background = Brushes.White,
                BorderBrush = Brushes.Silver,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 8, Color = Color.FromArgb(60, 0, 0, 0) }),
            };
            global::Avalonia.Automation.AutomationProperties.SetName(frame, "Formatting");
            var popup = new Popup
            {
                Name = Names.SelectionBar,
                // Above the caret's line, growing up and to the right: the bar's bottom-left corner sits on the top-left of the caret.
                Placement = PlacementMode.AnchorAndGravity,
                PlacementAnchor = PopupAnchor.TopLeft,
                PlacementGravity = PopupGravity.TopRight,
                IsLightDismissEnabled = false,
                TakesFocusFromNativeControl = false,   // in WinForms (Host48) opening it must not take focus from the field
                Child = frame,
                VerticalOffset = -4,
            };
            ((ISetLogicalParent)popup).SetParent(this);
            LogicalChildren.Add(popup);
            return popup;
        }

        Button BarTool(string name, object content, string tip, System.Action click)
        {
            var button = Tool(name, content, tip, click);
            button.Margin = new Thickness(0);
            return button;
        }

        /// <summary>Hides the bar until the selection changes again: a context menu replaces it, and focus leaving the field hides it.</summary>
        void HideSelectionBar()
        {
            _selectionBarGeneration++;
            _selectionBar.IsOpen = false;
        }

        /// <summary>Shows the bar over the active field's caret while it has a selection whose caret shows, and hides it otherwise. Called when the
        /// caret moves, the document changes, the view scrolls, read-only changes or another field becomes active; the open waits for layout so the
        /// caret's place is current.</summary>
        void UpdateSelectionBar()
        {
            var field = _active?.Field;
            bool show = _selectionBarEnabled && field != null && field.Editor.HasSelection && !field.IsReadOnly && TopLevel.GetTopLevel(this) != null;
            if (!show)
            {
                HideSelectionBar();
                return;
            }
            bool outer = _syncing;
            _syncing = true;
            _selectionWs.SelectedIndex = SampleWritingSystems.All.ToList().FindIndex(w => w.Tag == field!.Editor.SelectionWs);
            _syncing = outer;
            int generation = ++_selectionBarGeneration;
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _selectionBarGeneration || !_selectionBarEnabled || _active?.Field != field || !field!.Editor.HasSelection) return;
                var caret = field.CaretRect();
                if (!field.VisibleBounds.Intersects(caret)) { _selectionBar.IsOpen = false; return; }   // the selection's end is scrolled out of sight
                _selectionBar.PlacementTarget = field;
                _selectionBar.PlacementRect = caret;
                try { _selectionBar.IsOpen = true; }
                catch (System.InvalidOperationException) { }   // no overlay layer (a window with no theme): the bar is not shown
            }, DispatcherPriority.Background);
        }

        /// <summary>Focus moved: the bar stays while focus is in the field or in the bar itself (a box's drop-down), and goes otherwise.</summary>
        void SelectionBarFocusMoved()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as global::Avalonia.LogicalTree.ILogical;
                bool inBar = focused != null && (focused == _selectionBar || focused.GetLogicalAncestors().Contains(_selectionBar));
                if (_active?.Field.IsFocused == true || inBar) return;
                HideSelectionBar();
            }, DispatcherPriority.Background);
        }
    }
}
