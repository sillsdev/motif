using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;
using SIL.TextEditing.Markdown;

namespace SIL.TextEditing.Playground
{
    /// <summary>
    /// The shared playground: a <see cref="RichTextField"/> (or many), a scenario picker, a writing-system bar, format and
    /// undo buttons, copy and paste, a Markdown tab (the field as FieldWorks Markdown, editable), a model tab (the runs) and an event log. Both sample hosts and the
    /// integration tests use this one control. Controls are named (see the <c>Names</c> class) so tests and screen
    /// readers can find them.
    /// </summary>
    public sealed partial class PlaygroundView : UserControl
    {
        /// <summary>The names of the controls a test or a UI automation client looks for.</summary>
        public static class Names
        {
            public const string ScenarioPicker = "ScenarioPicker";
            public const string FieldCountPicker = "FieldCountPicker";
            public const string Bold = "BoldButton";
            public const string Italic = "ItalicButton";
            public const string Underline = "UnderlineButton";
            public const string CharStyle = "CharStyleBox";
            public const string ParaStyle = "ParaStyleBox";
            public const string Undo = "UndoButton";
            public const string Redo = "RedoButton";
            public const string Picture = "PictureButton";
            public const string Cut = "CutButton";
            public const string Copy = "CopyButton";
            public const string Paste = "PasteButton";
            public const string ReadOnly = "ReadOnlyCheck";
            public const string SingleParagraph = "SingleParagraphCheck";
            public const string AllowTables = "AllowTablesCheck";
            public const string AllowLists = "AllowListsCheck";
            public const string AllowHeadings = "AllowHeadingsCheck";
            public const string HostHandlesLeave = "HostHandlesLeaveCheck";
            public const string SpellCheck = "SpellCheckBox";
            public const string SpellShow = "SpellShowButton";
            public const string SpellReplace = "SpellReplaceButton";
            public const string SpellSpaces = "SpellSpacesButton";
            public const string SpellWholeFirst = "SpellWholeFirstButton";
            public const string SpellWholeLast = "SpellWholeLastButton";
            public const string SpellAdd = "SpellAddButton";
            public const string SpellIgnore = "SpellIgnoreButton";
            public const string HyphenLetter = "HyphenLetterBox";
            public const string EnvSlash = "EnvSlashButton";
            public const string EnvBar = "EnvBarButton";
            public const string EnvClass = "EnvClassBox";
            public const string EnvOptional = "EnvOptionalButton";
            public const string EnvHash = "EnvHashButton";
            public const string EnvDropClass = "EnvDropClassButton";
            public const string EnvRestoreClass = "EnvRestoreClassButton";
            public const string SpellBackground = "SpellBackgroundBox";
            public const string DefaultAnalysis = "DefaultAnalysisButton";
            public const string DefaultVernacular = "DefaultVernacularButton";
            public const string WsLabel = "WsLabel";
            public const string SelectionLabel = "SelectionLabel";
            public const string Inspector = "Inspector";
            public const string InspectorTabs = "InspectorTabs";
            public const string MarkdownTab = "MarkdownTab";
            public const string ModelTab = "ModelTab";
            public const string MarkdownSource = "MarkdownSource";
            public const string MarkdownApply = "MarkdownApplyButton";
            public const string MarkdownRevert = "MarkdownRevertButton";
            public const string MarkdownStatus = "MarkdownStatus";
            public const string DeveloperMode = "DeveloperModeCheck";
            public const string DeveloperPanel = "DeveloperPanel";
            public const string SelectionBar = "SelectionBar";
            public const string BarBold = "BarBoldButton";
            public const string BarItalic = "BarItalicButton";
            public const string BarUnderline = "BarUnderlineButton";
            public const string BarWritingSystem = "BarWritingSystemBox";
            public const string BarCharStyle = "BarCharStyleBox";
            public const string BarMore = "BarMoreToggle";
            public const string BarParaStyle = "BarParaStyleBox";
            public const string BarPicture = "BarPictureButton";
            public const string EventLog = "EventLog";
            public const string ScenarioDescription = "ScenarioDescription";
            public const string FindText = "FindText";
            public const string ReplaceText = "ReplaceText";
            public const string MatchCase = "MatchCaseCheck";
            public const string MatchDiacritics = "MatchDiacriticsCheck";
            public const string WholeWord = "WholeWordCheck";
            public const string RegularExpression = "RegularExpressionCheck";
            public const string HighlightAll = "HighlightAllCheck";
            public const string FindNext = "FindNextButton";
            public const string FindPrevious = "FindPreviousButton";
            public const string Replace = "ReplaceButton";
            public const string ReplaceAll = "ReplaceAllButton";
            public const string FindStatus = "FindStatus";
            public static string WritingSystem(string tag) => "Ws_" + tag;
        }

        const int MaxLogLines = 300;
        static readonly int[] FieldCounts = { 10, 50, 200, 1000 };

        sealed class Session
        {
            public Session(RichTextField field, int number)
            {
                Field = field;
                Number = number;
            }

            public RichTextField Field { get; }
            public int Number { get; }
            public SimpleUndoStack Undo { get; } = new SimpleUndoStack();
        }

        readonly WritingSystemRegistry _registry = SampleWritingSystems.CreateRegistry();
        readonly SamplePictures _pictures = new SamplePictures();
        readonly List<Session> _sessions = new List<Session>();
        readonly List<string> _log = new List<string>();
        readonly StackPanel _fieldStack = new StackPanel();
        readonly ComboBox _picker;
        readonly ComboBox _countPicker;
        CheckBox _readOnlyCheck = null!;
        CheckBox _singleParagraphCheck = null!;
        CheckBox _allowTables = null!, _allowLists = null!, _allowHeadings = null!;
        ComboBox _paraStyleBox = null!;
        CheckBox _leaveHandled = null!;
        CheckBox _spellCheck = null!;
        SampleSpelling? _spelling;
        SampleEnvironmentService? _env;
        Button _envSlash = null!, _envBar = null!, _envOptional = null!, _envHash = null!;
        ComboBox _envClass = null!;
        CheckBox _hyphenLetter = null!;
        CheckBox _spellBackground = null!;
        bool _spellOffUiThread;
        TextBox _findBox = null!;
        TextBox _replaceBox = null!;
        CheckBox _matchCase = null!;
        CheckBox _matchDiacritics = null!;
        CheckBox _wholeWord = null!;
        CheckBox _regex = null!;
        CheckBox _highlightAll = null!;
        TextBlock _findStatus = null!;
        readonly TextBlock _description;
        readonly TextBlock _wsLabel;
        readonly TextBlock _selectionLabel;
        readonly TextBox _inspector;
        readonly TextBox _markdown;
        readonly TextBlock _markdownStatus;
        readonly TabControl _inspectorTabs;
        readonly SampleMarkdown _markdownHost;
        bool _markdownEdited;
        bool _developerMode;
        CheckBox _developerCheck = null!;
        Control _developerPanel = null!;
        Control _logSplitter = null!;
        Grid _rightGrid = null!;
        TabItem _modelTab = null!;
        bool _settingMarkdown;
        bool _markdownRefreshPosted;
        readonly TextBox _logBox;
        Session _active;
        bool _syncing;
        int _pictureCount;
        string _selectionState = string.Empty;

        public PlaygroundView() : this(Scenarios.MixedBidi) { }

        /// <summary>The playground with <paramref name="scenario"/> loaded. Without <paramref name="developerMode"/> it shows only what a person
        /// writing sees: the field, the selection bar and the Markdown; developer mode adds every switch, the model and the event log.</summary>
        public PlaygroundView(string scenario, bool developerMode = false)
        {
            _developerMode = developerMode;
            _picker = new ComboBox { Name = Names.ScenarioPicker, MinWidth = 220, Margin = new Thickness(2), IsTabStop = false, ItemsSource = Scenarios.All.Select(s => s.Name).ToList() };
            ToolTip.SetTip(_picker, "Load a ready-made document");
            _picker.SelectionChanged += (s, e) =>
            {
                if (!_syncing && _picker.SelectedItem is string name) LoadScenario(name);
            };
            _countPicker = new ComboBox { Name = Names.FieldCountPicker, MinWidth = 80, Margin = new Thickness(2), IsTabStop = false, ItemsSource = FieldCounts, SelectedItem = 50 };
            ToolTip.SetTip(_countPicker, "How many fields the Many fields scenario creates");
            _countPicker.SelectionChanged += (s, e) =>
            {
                if (!_syncing && CurrentScenario.Many) LoadScenario(CurrentScenario.Name);
            };
            _description = new TextBlock { Name = Names.ScenarioDescription, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 2), Foreground = Brushes.DimGray };
            _wsLabel = new TextBlock { Name = Names.WsLabel, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2) };
            _selectionLabel = new TextBlock { Name = Names.SelectionLabel, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2) };
            _inspector = new TextBox { Name = Names.Inspector, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 12, IsTabStop = false };
            ScrollViewer.SetHorizontalScrollBarVisibility(_inspector, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_inspector, ScrollBarVisibility.Auto);
            _markdownHost = new SampleMarkdown(_registry);
            _markdownStatus = new TextBlock { Name = Names.MarkdownStatus, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(2, 4) };
            _markdown = new TextBox { Name = Names.MarkdownSource, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 13 };
            ScrollViewer.SetVerticalScrollBarVisibility(_markdown, ScrollBarVisibility.Auto);
            global::Avalonia.Automation.AutomationProperties.SetName(_markdown, "The field as FieldWorks Markdown");
            _markdown.PropertyChanged += (s, e) =>
            {
                if (e.Property != TextBox.TextProperty || _settingMarkdown || _markdownEdited) return;
                _markdownEdited = true;
                _markdownStatus.Text = "Edited. Apply puts it in the field as one undo step; Revert shows the field again.";
            };
            _inspectorTabs = new TabControl { Name = Names.InspectorTabs };
            _logBox = new TextBox { Name = Names.EventLog, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 12, IsTabStop = false };
            ScrollViewer.SetHorizontalScrollBarVisibility(_logBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_logBox, ScrollBarVisibility.Auto);

            Content = BuildLayout();
            _selectionBar = BuildSelectionBar();
            ApplyDeveloperMode();
            AddHandler(InputElement.KeyDownEvent, OnPreviewKey, RoutingStrategies.Tunnel);

            _active = null!;
            LoadScenario(scenario);
            AttachedToVisualTree += (s, e) => Dispatcher.UIThread.Post(() => _active.Field.Focus());
        }

        // ------------------------------------------------------------ what a test or a host can read

        public Scenario CurrentScenario { get; private set; } = Scenarios.All[0];
        /// <summary>The field the toolbar, writing-system bar and inspector act on: the focused one.</summary>
        public RichTextField Field => _active.Field;

        /// <summary>The playground's dictionaries while spell checking is on, else null.</summary>
        public SampleSpelling? Spelling => _spelling;

        /// <summary>The sample environment language service while the Environment scenario is loaded, else null.</summary>
        public SampleEnvironmentService? EnvironmentService => _env;
        public IReadOnlyList<RichTextField> Fields => _sessions.Select(s => s.Field).ToList();
        /// <summary>The host-owned undo stack of the active field.</summary>
        public SimpleUndoStack Undo => _active.Undo;
        public string WsLabelText => _wsLabel.Text ?? string.Empty;
        public string InspectorText => _inspector.Text ?? string.Empty;
        /// <summary>The Markdown tab's text: the active field as FieldWorks Markdown, or what was typed there and not yet applied.</summary>
        public string MarkdownText
        {
            get => _markdown.Text ?? string.Empty;
            set => _markdown.Text = value;
        }
        public string MarkdownStatusText => _markdownStatus.Text ?? string.Empty;
        /// <summary>Developer mode: every switch and tool bar, the model tab and the event log. Off, the playground shows the field, the selection
        /// bar and the Markdown only.</summary>
        public bool DeveloperMode
        {
            get => _developerMode;
            set
            {
                if (_developerMode == value) return;
                _developerMode = value;
                ApplyDeveloperMode();
            }
        }

        void ApplyDeveloperMode()
        {
            _developerPanel.IsVisible = _developerMode;
            _logSplitter.IsVisible = _developerMode;
            _logBox.IsVisible = _developerMode;
            _rightGrid.RowDefinitions[2].Height = _developerMode ? new GridLength(1, GridUnitType.Star) : new GridLength(0);   // the Markdown takes the whole height
            _modelTab.IsVisible = _developerMode;
            if (!_developerMode) ShowsMarkdown = true;
            _syncing = true;
            _developerCheck.IsChecked = _developerMode;
            _syncing = false;
        }

        /// <summary>True while the Markdown tab (the default) is shown; false while the model dump is.</summary>
        public bool ShowsMarkdown
        {
            get => _inspectorTabs.SelectedIndex == 0;
            set => _inspectorTabs.SelectedIndex = value ? 0 : 1;
        }
        public IReadOnlyList<string> EventLog => _log;
        /// <summary>Completes when the last copy, cut or paste started from a button has finished.</summary>
        public Task ClipboardIdle { get; private set; } = Task.CompletedTask;
        public int FieldCount => (int)(_countPicker.SelectedItem ?? 50);

        // ------------------------------------------------------------ layout

        Control BuildLayout()
        {
            var slim = new DockPanel { Margin = new Thickness(4, 2), LastChildFill = false };
            _developerCheck = new CheckBox { Name = Names.DeveloperMode, Content = "Developer mode", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_developerCheck, "Every switch and tool bar, the model and the event log");
            _developerCheck.IsCheckedChanged += (s, e) => { if (!_syncing) DeveloperMode = _developerCheck.IsChecked == true; };
            DockPanel.SetDock(_developerCheck, Dock.Right);
            slim.Children.Add(_developerCheck);
            slim.Children.Add(_picker);

            var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2) };
            toolbar.Children.Add(Label("Fields:"));
            toolbar.Children.Add(_countPicker);
            toolbar.Children.Add(Spacer());
            toolbar.Children.Add(Tool(Names.Bold, new TextBlock { Text = "B", FontWeight = FontWeight.Bold }, "Bold on the selection (Ctrl+B)", () => ToggleBold()));
            toolbar.Children.Add(Tool(Names.Italic, new TextBlock { Text = "I", FontStyle = FontStyle.Italic }, "Italic on the selection (Ctrl+I)", () => ToggleItalic()));
            toolbar.Children.Add(Tool(Names.Underline, new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline }, "Underline on the selection (Ctrl+U)", () => ToggleUnderline()));
            toolbar.Children.Add(Spacer());
            toolbar.Children.Add(Label("Character style:"));
            toolbar.Children.Add(StyleBox(Names.CharStyle, SampleStyles.CharacterStyles, "Apply a character style to the selection (the host's stylesheet decides how it looks)", name => ApplyCharacterStyle(name)));
            toolbar.Children.Add(Label("Paragraph style:"));
            _paraStyleBox = StyleBox(Names.ParaStyle, SampleStyles.ParagraphStyles, "Apply a paragraph style to the paragraphs of the selection", name => ApplyParagraphStyle(name));
            toolbar.Children.Add(_paraStyleBox);
            toolbar.Children.Add(Spacer());
            toolbar.Children.Add(Tool(Names.Undo, "Undo", "Ask the host's undo stack to undo (Ctrl+Z)", () => UndoLast("button")));
            toolbar.Children.Add(Tool(Names.Redo, "Redo", "Redo (Ctrl+Y)", () => RedoLast("button")));
            toolbar.Children.Add(Spacer());
            toolbar.Children.Add(Tool(Names.Picture, "Picture", "Insert an inline picture object at the caret", () => InsertPicture()));
            toolbar.Children.Add(Tool(Names.Cut, "Cut", "Cut the selection (Ctrl+X)", () => ClipboardIdle = CopyAsync(true)));
            toolbar.Children.Add(Tool(Names.Copy, "Copy", "Copy the selection (Ctrl+C)", () => ClipboardIdle = CopyAsync(false)));
            toolbar.Children.Add(Tool(Names.Paste, "Paste", "Paste (Ctrl+V)", () => ClipboardIdle = PasteAsync()));
            toolbar.Children.Add(Spacer());
            _readOnlyCheck = new CheckBox { Name = Names.ReadOnly, Content = "Read-only field", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_readOnlyCheck, "Field-level read-only: selection and copy work, nothing changes the text");
            _readOnlyCheck.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                _active.Field.IsReadOnly = _readOnlyCheck.IsChecked == true;
                Log("field read-only: " + (_active.Field.IsReadOnly ? "on" : "off"));
                UpdateSelectionBar();
            };
            toolbar.Children.Add(_readOnlyCheck);
            _singleParagraphCheck = new CheckBox { Name = Names.SingleParagraph, Content = "Single paragraph", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_singleParagraphCheck, "Single-paragraph mode: Enter goes to the host instead of splitting the paragraph");
            _singleParagraphCheck.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                _active.Field.SingleParagraph = _singleParagraphCheck.IsChecked == true;
                Log("single paragraph: " + (_active.Field.SingleParagraph ? "on" : "off"));
            };
            toolbar.Children.Add(_singleParagraphCheck);
            _allowTables = BlockCheck(Names.AllowTables, "Tables", BlockFeatures.Tables);
            _allowLists = BlockCheck(Names.AllowLists, "Lists", BlockFeatures.Lists);
            _allowHeadings = BlockCheck(Names.AllowHeadings, "Headings", BlockFeatures.Headings);
            _leaveHandled = new CheckBox { Name = Names.HostHandlesLeave, Content = "Host takes leave requests", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_leaveHandled, "Tab, Shift+Tab, Ctrl+Tab and arrows at the field's edge raise LeaveRequested; when ticked the host marks it handled, so Tab does not move focus");
            toolbar.Children.Add(_leaveHandled);
            toolbar.Children.Add(Spacer());
            _spellCheck = new CheckBox { Name = Names.SpellCheck, Content = "Spell checking", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_spellCheck, "Give the fields the playground's English and French word lists as the host's dictionaries; the field checks the paragraphs in view when idle");
            _spellCheck.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                SetSpelling(_spellCheck.IsChecked == true);
            };
            toolbar.Children.Add(_spellCheck);
            _hyphenLetter = new CheckBox { Name = Names.HyphenLetter, Content = "Hyphen is a letter", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_hyphenLetter, "The host's word rule (RichTextField.WordRules): a writing system that makes the hyphen a letter makes well-known one word for a double-click, Ctrl+Left and Right, find and the spell checker");
            _hyphenLetter.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                SetWordRules(_hyphenLetter.IsChecked == true);
            };
            toolbar.Children.Add(_hyphenLetter);
            _spellBackground = new CheckBox { Name = Names.SpellBackground, Content = "Slow dictionary, off the UI thread", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_spellBackground, "The playground's dictionaries take 15 ms a word and say they are safe off the UI thread (IBackgroundSpellChecker): the field finds the words, a worker asks them and the squiggles arrive without holding up typing");
            _spellBackground.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                _spellOffUiThread = _spellBackground.IsChecked == true;
                if (_spelling != null)
                {
                    _spelling.OffUiThread = _spellOffUiThread;
                    _spelling.Delay = _spellOffUiThread ? 15 : 0;
                    _spelling.RaiseDictionariesChanged();       // the fields ask again, now of the other kind of checker
                }
                Log("dictionary: " + (_spellOffUiThread ? "slow, asked off the UI thread" : "asked on the UI thread"));
            };
            toolbar.Children.Add(_spellBackground);
            toolbar.Children.Add(Tool(Names.SpellShow, "Spelling at caret", "What the context menu would offer for the marked word at the caret: the word, why it is marked and the suggestions", () => ShowSpelling()));
            toolbar.Children.Add(Tool(Names.SpellReplace, "Use first suggestion", "Replace the marked word at the caret with the first suggestion (one undo step)", () => ReplaceSpelling()));
            toolbar.Children.Add(Tool(Names.SpellSpaces, "Insert missing spaces", "For a word that mixes writing systems or holds an object: put a space between the parts, each part keeping its own writing system", () => SpacesSpelling()));
            toolbar.Children.Add(Tool(Names.SpellWholeFirst, "Whole word, first writing system", "For a word that mixes writing systems: make the whole word the first writing system in it, as FieldWorks' menu item does", () => WholeWordSpelling(false)));
            toolbar.Children.Add(Tool(Names.SpellWholeLast, "Whole word, last writing system", "For a word that mixes writing systems: make the whole word the last writing system in it", () => WholeWordSpelling(true)));
            toolbar.Children.Add(Tool(Names.SpellAdd, "Add to dictionary", "Add the marked word at the caret to its dictionary", () => AddSpelling(false)));
            toolbar.Children.Add(Tool(Names.SpellIgnore, "Ignore", "Ignore the marked word at the caret", () => AddSpelling(true)));

            toolbar.Children.Add(Spacer());
            toolbar.Children.Add(Label("Insert:"));
            _envSlash = Tool(Names.EnvSlash, "/", "Insert the environment slash: allowed while there is none (Environment scenario)", () => InsertEnvironment(SampleEnvironmentService.InsertKinds.Slash, "/"));
            _envBar = Tool(Names.EnvBar, "_", "Insert the underscore, where the allomorph stands: after the slash, once", () => InsertEnvironment(SampleEnvironmentService.InsertKinds.Bar, "_"));
            _envOptional = Tool(Names.EnvOptional, "( )", "Insert an optional item, with the caret between the parentheses", () => InsertEnvironment(SampleEnvironmentService.InsertKinds.OptionalItem, "()"));
            _envHash = Tool(Names.EnvHash, "#", "Insert a word boundary: at the very start or end of the environment", () => InsertEnvironment(SampleEnvironmentService.InsertKinds.WordBoundary, "#"));
            _envClass = new ComboBox { Name = Names.EnvClass, MinWidth = 110, Margin = new Thickness(2), IsTabStop = false, PlaceholderText = "[class]" };
            ToolTip.SetTip(_envClass, "Insert a natural class: the abbreviation in square brackets, where the recognizer accepts an item");
            _envClass.SelectionChanged += (s, e) =>
            {
                if (_syncing || !(_envClass.SelectedItem is string abbreviation)) return;
                InsertEnvironment(SampleEnvironmentService.InsertKinds.NaturalClass, "[" + abbreviation + "]");
                _syncing = true;
                _envClass.SelectedIndex = -1;
                _syncing = false;
            };
            toolbar.Children.Add(_envSlash);
            toolbar.Children.Add(_envBar);
            toolbar.Children.Add(_envClass);
            toolbar.Children.Add(_envOptional);
            toolbar.Children.Add(_envHash);
            toolbar.Children.Add(Tool(Names.EnvDropClass, "Drop class V", "The host's phonology loses the natural class V and tells the fields (ITextLanguageService.Changed): every environment that names it is asked again", () => EnvironmentClass(false)));
            toolbar.Children.Add(Tool(Names.EnvRestoreClass, "Restore class V", "The host's phonology gets the natural class V back", () => EnvironmentClass(true)));

            var wsBar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2) };
            wsBar.Children.Add(_wsLabel);
            wsBar.Children.Add(Tool(Names.DefaultAnalysis, "Default analysis (" + SampleWritingSystems.DefaultAnalysis + ")", "Use the default analysis writing system", () => SetWritingSystem(SampleWritingSystems.DefaultAnalysis)));
            wsBar.Children.Add(Tool(Names.DefaultVernacular, "Default vernacular (" + SampleWritingSystems.DefaultVernacular + ")", "Use the default vernacular writing system", () => SetWritingSystem(SampleWritingSystems.DefaultVernacular)));
            foreach (var vernacular in new[] { false, true })
            {
                wsBar.Children.Add(Label(vernacular ? "Vernacular:" : "Analysis:"));
                foreach (var ws in SampleWritingSystems.All.Where(w => w.Vernacular == vernacular))
                {
                    var tag = ws.Tag;
                    wsBar.Children.Add(Tool(Names.WritingSystem(tag), tag, ws.Name + " (" + ws.Kind + "): sets the selection, or what you type next", () => SetWritingSystem(tag)));
                }
            }
            wsBar.Children.Add(_selectionLabel);

            var findBar = BuildFindBar();

            var fieldScroll = new ScrollViewer
            {
                Content = _fieldStack,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            var fieldBorder = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(4), Child = fieldScroll };

            var right = new Grid();
            _rightGrid = right;
            right.RowDefinitions.Add(new RowDefinition(2, GridUnitType.Star));
            right.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            right.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            var splitter = new GridSplitter { Height = 5, Background = Brushes.LightGray, ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch };
            _modelTab = new TabItem { Name = Names.ModelTab, Header = "Model (debug)", FontSize = 14, Content = _inspector };
            _inspectorTabs.ItemsSource = new[] { BuildMarkdownTab(), _modelTab };
            _inspectorTabs.SelectedIndex = 0;
            _inspectorTabs.SelectionChanged += (s, e) => { if (ShowsMarkdown) RefreshMarkdown(); };
            Grid.SetRow(_inspectorTabs, 0);
            Grid.SetRow(splitter, 1);   // a GridSplitter works only as a direct child of its Grid
            Grid.SetRow(_logBox, 2);
            _logSplitter = splitter;
            right.Children.Add(_inspectorTabs);
            right.Children.Add(splitter);
            right.Children.Add(_logBox);
            var rightBorder = new Border { Margin = new Thickness(4), Child = right };

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition(3, GridUnitType.Star) { MinWidth = 200 });
            body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            body.ColumnDefinitions.Add(new ColumnDefinition(2, GridUnitType.Star) { MinWidth = 150 });
            var vsplit = new GridSplitter { Width = 5, Background = Brushes.LightGray, ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(fieldBorder, 0);
            Grid.SetColumn(vsplit, 1);
            Grid.SetColumn(rightBorder, 2);
            body.Children.Add(fieldBorder);
            body.Children.Add(vsplit);
            body.Children.Add(rightBorder);

            var developer = new StackPanel { Name = Names.DeveloperPanel };
            developer.Children.Add(toolbar);
            developer.Children.Add(_description);
            developer.Children.Add(wsBar);
            developer.Children.Add(findBar);
            _developerPanel = developer;
            var top = new StackPanel();
            top.Children.Add(slim);
            top.Children.Add(developer);
            var root = new DockPanel { Background = Brushes.WhiteSmoke };
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            root.Children.Add(body);
            return root;
        }

        /// <summary>The Markdown tab: the active field as FieldWorks Markdown, which a person or an agent edits, with Apply and Revert. A host-side
        /// sketch of the planned source mode (issue 170): Apply parses the text and replaces the field's paragraphs through the editor, so the change
        /// is one host undo step, and an error leaves the field as it was.</summary>
        TabItem BuildMarkdownTab()
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Tool(Names.MarkdownApply, "Apply", "Read the Markdown and put it in the field as one undo step (nothing changes while it has an error)", () => ApplyMarkdown()));
            buttons.Children.Add(Tool(Names.MarkdownRevert, "Revert", "Drop the edits and show the field as Markdown again", () => RevertMarkdown()));
            var panel = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Bottom);
            DockPanel.SetDock(_markdownStatus, Dock.Bottom);
            panel.Children.Add(buttons);
            panel.Children.Add(_markdownStatus);
            panel.Children.Add(_markdown);
            return new TabItem { Name = Names.MarkdownTab, Header = "Markdown", FontSize = 14, Content = panel };
        }

        /// <summary>Shows the active field as Markdown, unless the tab is hidden or holds edits that were neither applied nor reverted.</summary>
        void RefreshMarkdown()
        {
            _markdownRefreshPosted = false;
            if (_markdownEdited || !ShowsMarkdown) return;
            _settingMarkdown = true;
            try { _markdown.Text = MarkdownConverter.ToMarkdown(_active.Field.Editor.Document, _markdownHost.Options()); }
            finally { _settingMarkdown = false; }
            _markdownStatus.Text = string.Empty;
        }

        /// <summary>After an edit in the field: the Markdown is written again once the dispatcher is idle, so a burst of typing writes it once, not per key.</summary>
        void PostMarkdownRefresh()
        {
            if (_markdownRefreshPosted || _markdownEdited || !ShowsMarkdown) return;
            _markdownRefreshPosted = true;
            Dispatcher.UIThread.Post(RefreshMarkdown, DispatcherPriority.Background);
        }

        /// <summary>Before the tab shows another field or scenario: edits that were not applied are dropped, and the log says so.</summary>
        void DropMarkdownEdits(string why)
        {
            if (_markdownEdited) Log("markdown: edits not applied were dropped (" + why + ")");
            _markdownEdited = false;
        }

        /// <summary>Reads the Markdown tab and, when it has no error, puts what it says in the field as one host undo step. The host applies the
        /// change itself, as a delta its undo stack records (ADR 0003), so every paragraph's style, direction and properties are exactly what the
        /// Markdown says. Before that it does what the field would: it refuses a change to a read-only field, to read-only text or that would give a
        /// single-paragraph field more than one paragraph, makes new blocks the field does not allow plain (ADR 0033), keeps read-only marks out of
        /// new text and applies the field's normalisation form. Unchanged paragraphs keep their keys (see CarryKeys). Returns true when the field changed.</summary>
        public bool ApplyMarkdown()
        {
            var editor = _active.Field.Editor;
            var result = MarkdownConverter.Parse(MarkdownText, _markdownHost.Options());
            var errors = result.Diagnostics.Where(d => d.Severity == MarkdownSeverity.Error).ToList();
            if (errors.Count > 0)
                return NotApplied(string.Join("; ", errors.Select(d => d.Id + " at line " + d.Line + ", column " + d.Column + ": " + d.Message)),
                    errors.Count + " error(s): " + string.Join(", ", errors.Select(d => d.Id)));

            var old = editor.Document.Paragraphs;
            var paragraphs = result.Document.Paragraphs.ToList();
            var fresh = paragraphs.Where(n => !old.Any(o => MarkdownConverter.AreEquivalent(o, n))).ToList();
            var flattening = BlockFlattener.Flatten(fresh, editor.BlockFeatures, editor.BlockRecognizer);
            if (MarkdownConverter.AreEquivalent(old, paragraphs))
            {
                _markdownEdited = false;
                RefreshMarkdown();
                _markdownStatus.Text = "No change: the Markdown says what the field already holds." + Findings(result);
                Log("markdown: no change");
                return false;
            }
            int last = old.Count - 1;
            if (editor.IsReadOnly) return NotApplied("the field is read-only.", "the field is read-only");
            if (!editor.CanEditRange(new TextPosition(0, 0), new TextPosition(last, old[last].Length)))
                return NotApplied("the field holds read-only text, which an edit as Markdown would replace.", "read-only text");
            if (editor.SingleParagraph && paragraphs.Count > 1)
                return NotApplied("the field holds one paragraph and the Markdown has " + paragraphs.Count + ".", "one paragraph only");

            // What the field does to anything inserted: new text cannot bring read-only marks in, and takes the field's normalisation form.
            foreach (var p in fresh)
                foreach (var r in p.Runs) r.Props.IntProps.Remove(TextPropertyNumbers.Editable);
            if (editor.Document.Normalization is System.Text.NormalizationForm form)
                foreach (var p in paragraphs)
                    foreach (var r in p.Runs)
                        if (!r.IsObject) r.Text = TextNormalizer.Normalize(r.Text, form);
            CarryKeys(old, paragraphs);
            var delta = new EditDelta
            {
                Start = 0,
                Before = old.Select(o => o.Clone()).ToList(),
                After = paragraphs,
                CaretBefore = editor.Caret,
                CaretAfter = new TextPosition(0, 0),
                Label = "edit as Markdown",
            };
            editor.Apply(delta);
            _active.Undo.Done.Add(delta);
            _active.Undo.Undone.Clear();
            _markdownEdited = false;
            RefreshMarkdown();
            _markdownStatus.Text = "Applied as one undo step." + (flattening.Total > 0 ? " The field does not allow " + flattening + "; they are now plain paragraphs." : string.Empty) + Findings(result);
            Log("markdown: applied as one undo step" + (flattening.Total > 0 ? ", " + flattening + " made plain" : string.Empty));
            return true;
        }

        /// <summary>Keys go with paragraphs: a new paragraph equivalent to an old one, matched in order, takes its key; a changed paragraph between two
        /// matches takes the key of the old paragraph at the same place between them when there is one; any other paragraph has none for the host to assign.</summary>
        static void CarryKeys(IReadOnlyList<Paragraph> old, List<Paragraph> paragraphs)
        {
            var taken = new bool[old.Count];
            var match = new int[paragraphs.Count];
            int from = 0;
            for (int i = 0; i < paragraphs.Count; i++)
            {
                match[i] = -1;
                for (int j = from; j < old.Count; j++)
                    if (MarkdownConverter.AreEquivalent(old[j], paragraphs[i])) { match[i] = j; taken[j] = true; from = j + 1; break; }
            }
            int prev = -1;
            for (int i = 0; i < paragraphs.Count; i++)
            {
                if (match[i] >= 0) { paragraphs[i].Key = old[match[i]].Key; prev = match[i]; continue; }
                int next = old.Count;
                for (int k = i + 1; k < paragraphs.Count; k++) if (match[k] >= 0) { next = match[k]; break; }
                int candidate = prev + 1;
                if (candidate < next && candidate < old.Count && !taken[candidate]) { paragraphs[i].Key = old[candidate].Key; taken[candidate] = true; prev = candidate; }
                else paragraphs[i].Key = null;
            }
        }

        bool NotApplied(string status, string log)
        {
            _markdownStatus.Text = "Not applied: " + status;
            Log("markdown: not applied, " + log);
            return false;
        }

        static string Findings(MarkdownParseResult result) =>
            result.Diagnostics.Count > 0 ? " Findings: " + string.Join(", ", result.Diagnostics.Select(d => d.Id)) + "." : string.Empty;

        /// <summary>Drops edits in the Markdown tab and shows the field again.</summary>
        public void RevertMarkdown()
        {
            _markdownEdited = false;
            RefreshMarkdown();
        }

        // ------------------------------------------------------------ find and replace (the host's dialog, ADR 0021)

        Control BuildFindBar()
        {
            var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2) };
            bar.Children.Add(Label("Find:"));
            _findBox = new TextBox { Name = Names.FindText, MinWidth = 150, Margin = new Thickness(2), IsTabStop = false };
            ToolTip.SetTip(_findBox, "The text to find in the focused field");
            _findBox.TextChanged += (s, e) => UpdateFindHighlight();
            bar.Children.Add(_findBox);
            bar.Children.Add(Label("Replace with:"));
            _replaceBox = new TextBox { Name = Names.ReplaceText, MinWidth = 150, Margin = new Thickness(2), IsTabStop = false };
            ToolTip.SetTip(_replaceBox, "The replacement text; a regular expression may use $1");
            bar.Children.Add(_replaceBox);
            CheckBox Option(string name, string text, string tip, bool on)
            {
                var box = new CheckBox { Name = name, Content = text, IsChecked = on, Focusable = false, Margin = new Thickness(2) };
                ToolTip.SetTip(box, tip);
                box.IsCheckedChanged += (s, e) => UpdateFindHighlight();
                bar.Children.Add(box);
                return box;
            }
            _matchCase = Option(Names.MatchCase, "Match case", "Upper and lower case differ (diacritics are then matched too)", false);
            _matchDiacritics = Option(Names.MatchDiacritics, "Match diacritics", "Combining marks must agree; off, they are ignored", true);
            _wholeWord = Option(Names.WholeWord, "Whole word", "A match starts and ends at word boundaries", false);
            _regex = Option(Names.RegularExpression, "Regular expression", "The text is a regular expression", false);
            _highlightAll = Option(Names.HighlightAll, "Highlight all", "Highlight every match, not only the current one", true);
            bar.Children.Add(Tool(Names.FindPrevious, "Find Previous", "Select the previous match", () => DoFind(false)));
            bar.Children.Add(Tool(Names.FindNext, "Find Next", "Select the next match", () => DoFind(true)));
            bar.Children.Add(Tool(Names.Replace, "Replace", "Replace the selected match, then find the next", () => DoReplace()));
            bar.Children.Add(Tool(Names.ReplaceAll, "Replace All", "Replace every match in the field as one undo step", () => DoReplaceAll()));
            _findStatus = new TextBlock { Name = Names.FindStatus, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2) };
            bar.Children.Add(_allowTables);
            bar.Children.Add(_allowLists);
            bar.Children.Add(_allowHeadings);
            bar.Children.Add(_findStatus);
            return bar;
        }

        FindOptions CurrentFindOptions() => new FindOptions
        {
            Pattern = _findBox.Text ?? string.Empty,
            MatchCase = _matchCase.IsChecked == true,
            MatchDiacritics = _matchDiacritics.IsChecked == true,
            WholeWord = _wholeWord.IsChecked == true,
            UseRegularExpression = _regex.IsChecked == true,
        };

        public string FindStatusText => _findStatus.Text ?? string.Empty;

        void UpdateFindHighlight()
        {
            if (_active == null || _highlightAll == null) return;
            var options = CurrentFindOptions();
            _active.Field.SetFindHighlight(options.Pattern.Length == 0 || !options.IsValid(out _) ? null : options, _highlightAll.IsChecked == true);
        }

        bool FindOptionsUsable(out FindOptions options)
        {
            options = CurrentFindOptions();
            if (!options.IsValid(out var error))
            {
                _findStatus.Text = "Not a valid regular expression: " + error;
                Log("find: " + _findStatus.Text);
                return false;
            }
            if (options.Pattern.Length == 0)
            {
                _findStatus.Text = "Type something to find";
                return false;
            }
            return true;
        }

        /// <summary>Find Next or Find Previous: the host's dialog asks the focused field to search, select and highlight, wrapping
        /// round and saying so, as FieldWorks' dialog does.</summary>
        public bool DoFind(bool forward)
        {
            if (!FindOptionsUsable(out var options)) return false;
            var field = _active.Field;
            UpdateFindHighlight();
            bool found = field.Find(options, forward, true, out bool wrapped);
            _findStatus.Text = found ? (wrapped ? "Wrapped. " : string.Empty) + "Found at " + field.Editor.SelectionRange : "Not found";
            Log("find " + (forward ? "next" : "previous") + ": " + _findStatus.Text);
            return found;
        }

        /// <summary>Replace: replaces the selected match (one undo step), then finds the next, as FieldWorks' dialog does.</summary>
        public bool DoReplace()
        {
            if (!FindOptionsUsable(out var options)) return false;
            var field = _active.Field;
            bool done = field.Editor.ReplaceCurrent(options, _replaceBox.Text ?? string.Empty);
            if (!done)
            {
                // Nothing selected is a match yet (or it may not be edited): select the next one, as Replace does in FieldWorks.
                _findStatus.Text = "Select a match first; found the next one";
                DoFind(true);
                return false;
            }
            Log("replace: replaced the selected match");
            UpdateFindHighlight();
            bool next = field.Find(options, true, true, out bool wrapped);
            _findStatus.Text = "Replaced" + (next ? "; next match at " + field.Editor.SelectionRange : "; no more matches");
            return true;
        }

        /// <summary>Replace All: every match in the focused field, one undo step.</summary>
        public int DoReplaceAll()
        {
            if (!FindOptionsUsable(out var options)) return 0;
            var field = _active.Field;
            int count = field.Editor.ReplaceAll(options, _replaceBox.Text ?? string.Empty);
            _findStatus.Text = count == 0 ? "Not found" : "Replaced " + count + (count == 1 ? " match" : " matches");
            Log("replace all: " + _findStatus.Text);
            UpdateFindHighlight();
            return count;
        }

        // ------------------------------------------------------------ context menu (the host builds it, ADR 0021)

        /// <summary>What the last context-menu request told the host.</summary>
        public ContextInfo? LastContextInfo { get; private set; }
        /// <summary>The captions of the menu the playground built for it.</summary>
        public IReadOnlyList<string> LastContextMenuItems { get; private set; } = new List<string>();
        ContextMenu? _contextMenu;
        /// <summary>Closes the menu the last context-menu request opened, if it is still open.</summary>
        public void CloseContextMenu() => _contextMenu?.Close();


        void ShowContextMenu(Session session, ContextMenuRequestedEventArgs e)
        {
            var info = e.Info;
            LastContextInfo = info;
            var field = session.Field;
            var items = new List<Control>();
            var captions = new List<string>();
            void Add(string caption, bool enabled, Action? click = null)
            {
                var item = new MenuItem { Header = caption, IsEnabled = enabled };
                if (click != null) item.Click += (s, a) => click();
                items.Add(item);
                captions.Add(caption);
            }
            if (info.Word.Length > 0)
            {
                Add("Word: " + info.Word + " (" + info.WritingSystem + ")", false);
                if (info.SpellingSuggestions.Count == 0) Add("(no spelling suggestions)", false);
                foreach (var s in info.SpellingSuggestions) Add(s, true);
                Add("Find \"" + info.Word + "\"", true, () => { _findBox.Text = info.Word; });
            }
            foreach (var d in info.Diagnostics)
            {
                Add(d.Severity + ": " + d.Message, false);
                if (d.Fix != null) { var diagnostic = d; Add(d.Fix.Title, true, () => Log("fix \"" + diagnostic.Fix!.Title + "\": " + (field.ApplyDiagnosticFix(diagnostic) ? "done" : "refused"))); }
            }
            if (_env != null)
            {
                foreach (var (caption, kind, text) in new[]
                {
                    ("Insert slash", SampleEnvironmentService.InsertKinds.Slash, "/"), ("Insert underscore", SampleEnvironmentService.InsertKinds.Bar, "_"),
                    ("Insert optional item", SampleEnvironmentService.InsertKinds.OptionalItem, "()"), ("Insert word boundary", SampleEnvironmentService.InsertKinds.WordBoundary, "#"),
                })
                {
                    var k = kind;
                    var t = text;
                    Add(caption, field.CanInsert(k), () => InsertEnvironment(k, t));
                }
            }
            if (info.EmbeddedObject != null)
                Add((info.EmbeddedObject.IsLink ? "Open link" : "Object") + ": " + info.EmbeddedObject.Data.Substring(Math.Min(1, info.EmbeddedObject.Data.Length)), info.EmbeddedObject.IsLink);
            Add("Cut", field.Editor.HasSelection && field.Editor.CanEdit, () => ClipboardIdle = CopyAsync(true));
            Add("Copy", field.Editor.HasSelection, () => ClipboardIdle = CopyAsync(false));
            Add("Paste", field.Editor.CanEdit, () => ClipboardIdle = PasteAsync());
            var copyAs = new MenuItem { Header = CopyAsCaption, IsEnabled = field.Editor.HasSelection };
            var copyAsItems = new List<Control>();
            void AddCopyAs(string caption, bool enabled, string? formId)
            {
                var item = new MenuItem { Header = caption, IsEnabled = enabled };
                if (formId != null) item.Click += (s, a) => ClipboardIdle = CopyAsAsync(formId);
                copyAsItems.Add(item);
                LastCopyAsItems.Add(caption);
            }
            LastCopyAsItems.Clear();
            AddCopyAs("Plain text", true, ClipboardForms.PlainTextId);
            foreach (var form in field.ClipboardForms.CopyAsForms) AddCopyAs(form.DisplayName, true, form.Id);
            if (field.ClipboardForms.Find(ClipboardForms.FieldWorksMarkdownId) == null) AddCopyAs("FieldWorks Markdown (no converter registered)", false, null);
            copyAs.ItemsSource = copyAsItems;
            items.Add(copyAs);
            captions.Add(CopyAsCaption);
            LastContextMenuItems = captions;
            Log("context menu (" + e.Trigger + ") at " + e.Position.X.ToString("0", CultureInfo.InvariantCulture) + "," + e.Position.Y.ToString("0", CultureInfo.InvariantCulture)
                + ": word " + (info.Word.Length == 0 ? "none" : "\"" + info.Word + "\" " + info.WordRange)
                + ", ws " + info.WritingSystem
                + (info.EmbeddedObject != null ? ", " + (info.EmbeddedObject.IsLink ? "link" : "object") + " at " + info.EmbeddedObject.Range : string.Empty)
                + ", selection " + (info.HasSelection ? info.Selection.ToString() : "none"));
            _contextMenu?.Close();
            _contextMenu = new ContextMenu { ItemsSource = items };
            _contextMenu.Open(field);
        }

        /// <summary>One of the block-feature switches (ADR 0033): off hides the paragraph styles of that kind from the style box, refuses them in
        /// <see cref="ApplyParagraphStyle"/>, and turns a pasted block of that kind into plain paragraphs.</summary>
        CheckBox BlockCheck(string name, string caption, BlockFeatures feature)
        {
            var box = new CheckBox { Name = name, Content = caption, IsChecked = true, Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(box, caption + " allowed in this field (RichTextField.BlockFeatures). Off: the commands for it are hidden, and a paste or Markdown import turns it into plain paragraphs");
            box.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                var field = _active.Field;
                field.BlockFeatures = box.IsChecked == true ? field.BlockFeatures | feature : field.BlockFeatures & ~feature;
                Log("block features: " + (field.BlockFeatures == BlockFeatures.All ? "all" : field.BlockFeatures == BlockFeatures.None ? "none" : field.BlockFeatures.ToString()));
                UpdateParagraphStyles();
            };
            return box;
        }

        /// <summary>Shows in the paragraph-style box only the styles the active field can take (a style of a feature that is off is hidden).</summary>
        void UpdateParagraphStyles()
        {
            var field = _active.Field;
            _paraStyleBox.ItemsSource = SampleStyles.ParagraphStyles.Where(s => s == SampleStyles.None || field.IsParagraphStyleAllowed(s)).ToList();
        }

        void SyncBlockChecks()
        {
            var features = _active.Field.BlockFeatures;
            _allowTables.IsChecked = (features & BlockFeatures.Tables) != 0;
            _allowLists.IsChecked = (features & BlockFeatures.Lists) != 0;
            _allowHeadings.IsChecked = (features & BlockFeatures.Headings) != 0;
            UpdateParagraphStyles();
        }

        static TextBlock Label(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2, 2, 2) };

        static Border Spacer() => new Border { Width = 12 };

        ComboBox StyleBox(string name, IReadOnlyList<string> styles, string tip, Action<string> apply, string placeholder = "(choose)")
        {
            var box = new ComboBox { Name = name, MinWidth = 140, Margin = new Thickness(2), IsTabStop = false, ItemsSource = styles, SelectedIndex = -1, PlaceholderText = placeholder };
            ToolTip.SetTip(box, tip);
            box.DropDownClosed += (s, e) => _active.Field.Focus();   // Esc too: typing goes back to the field
            box.SelectionChanged += (s, e) =>
            {
                if (_syncing || !(box.SelectedItem is string style)) return;
                apply(style);
                _syncing = true;
                box.SelectedIndex = -1;
                _syncing = false;
                _active.Field.Focus();
            };
            return box;
        }

        static Button Tool(string name, object content, string tip, Action click)
        {
            var button = new Button { Name = name, Content = content, Focusable = false, Margin = new Thickness(2), Padding = new Thickness(8, 3) };
            ToolTip.SetTip(button, tip);
            button.Click += (s, e) => click();
            return button;
        }

        // ------------------------------------------------------------ scenarios

        public void LoadScenario(string name)
        {
            var scenario = Scenarios.Find(name);
            CurrentScenario = scenario;
            _syncing = true;
            _picker.SelectedItem = scenario.Name;
            _syncing = false;
            _description.Text = scenario.Description;
            _countPicker.IsEnabled = scenario.Many;

            _sessions.Clear();
            _fieldStack.Children.Clear();
            var documents = scenario.Build(FieldCount);
            for (int i = 0; i < documents.Count; i++) AddSession(documents[i], i + 1, scenario.Many);
            _active = _sessions[0];
            _syncing = true;
            _readOnlyCheck.IsChecked = false;
            _singleParagraphCheck.IsChecked = scenario.Name == Scenarios.Environment;
            SyncBlockChecks();
            _spellCheck.IsChecked = scenario.Name == Scenarios.Spelling;
            _hyphenLetter.IsChecked = false;
            _syncing = false;
            SetSpelling(scenario.Name == Scenarios.Spelling);
            SetEnvironment(scenario.Name == Scenarios.Environment);
            _selectionState = string.Empty;
            Log("scenario: " + scenario.Name + (scenario.Many ? " (" + documents.Count + " fields)" : string.Empty));
            UpdateFindHighlight();
            UpdateWsLabel();
            RefreshInspector();
            DropMarkdownEdits("scenario loaded");
            RefreshMarkdown();
            if (_selectionBar != null) UpdateSelectionBar();
            if (TopLevel.GetTopLevel(this) != null) Dispatcher.UIThread.Post(() => _active.Field.Focus());
        }

        void AddSession(Document document, int number, bool many)
        {
            var field = new RichTextField(document)
            {
                WritingSystems = _registry,
                ObjectImage = _pictures.Get,
                StyleResolver = SampleStyles.Instance,
                BlockRecognizer = SampleStyles.Recognizer,
                ShowCaretAlways = !many,
                PreferredWidth = many ? 300 : 500,
                Margin = new Thickness(many ? 2 : 6),
            };
            // The playground has no liblcm and registers no FieldWorks Markdown clipboard form (issue 168), so its one rich clipboard form is the
            // field's debug dump (a real host registers TsString and FieldWorks Markdown, ADR 0029). The Markdown tab shows the field as Markdown.
            var dump = new InterchangeClipboardForm();
            field.ClipboardForms.Add(dump).PasteOrder.Insert(0, dump.Id);
            field.WritingSystemsMapped += (o, mapped) => Log("clipboard: notice, writing system(s) " + string.Join(", ", mapped.Missing.Select(m => m.Length == 0 ? "(none)" : m))
                + " are not in this project; pasted text is now " + mapped.Target);
            field.BlocksFlattened += (o, flat) => Log("clipboard: notice, " + flat + " pasted into a field that does not allow them are now plain paragraphs");
            global::Avalonia.Automation.AutomationProperties.SetName(field, many ? "Text field " + number.ToString(CultureInfo.InvariantCulture) : "Text field");
            var session = new Session(field, number);
            session.Undo.Attach(field.Editor);
            _sessions.Add(session);

            field.ActiveWsChanged += (o, n) =>
            {
                if (_active != session) return;
                UpdateWsLabel();
                Log(o == n ? "focus gained, ws " + n + ": would switch keyboard to " + n : "caret ws " + o + " -> " + n + ": would switch keyboard to " + n);
            };
            field.LeaveRequested += (o, e) =>
            {
                if (_active != session) return;
                Log("leave requested: " + e.Direction + " by " + e.Key + (e.ToEnd ? " (to the " + (e.Direction == LeaveDirection.Next ? "last" : "first") + " field)" : string.Empty) + (double.IsNaN(e.GoalX) ? string.Empty : " at x " + Math.Round(e.GoalX)) + (_leaveHandled.IsChecked == true ? ": the host moved focus" : string.Empty));
                e.Handled = _leaveHandled.IsChecked == true;
            };
            field.EnterRequested += m => Log("Enter passed to the host" + (m == KeyModifiers.None ? string.Empty : " (" + m + ")") + ": the host's default button would run");
            field.ContextMenuRequested += (o, e) => { Activate(session); HideSelectionBar(); ShowContextMenu(session, e); };
            field.ScrollRequested += (o, e) =>
            {
                // The playground leaves scrolling to Avalonia's scroll viewer (the field asks it when nothing handles the request); it only
                // reports the requests that are not just the caret following typing.
                if (_active == session && e.Reason != ScrollReason.Caret) Log("scroll requested: " + e.Reason + (e.Delta == 0 ? string.Empty : " by " + e.Delta.ToString("0", CultureInfo.InvariantCulture) + " px"));
            };
            field.UndoRequested += () => { Activate(session); UndoLast("Ctrl+Z in the field"); };
            field.RedoRequested += () => { Activate(session); RedoLast("Ctrl+Y in the field"); };
            field.SelectionWsState += state =>
            {
                if (_active != session) return;
                _selectionState = state;
                _selectionLabel.Text = "selection ws: " + state;
            };
            field.Editor.Committed += d =>
            {
                if (_active == session && d.Label != "type") Log("edit: " + d.Label + " (paragraphs " + d.Start + ".." + (d.Start + Math.Max(d.Before.Count, d.After.Count) - 1) + ")");
            };
            field.Editor.UndoGroupEnded += id => { if (_active == session) Log("undo step " + id + " ended: the host closes its undo task"); };
            field.Editor.DocumentChanged += (a, b, c) => { if (_active == session) { RefreshInspector(); PostMarkdownRefresh(); UpdateEnvironmentButtons(); UpdateSelectionBar(); } };
            field.Editor.CaretMoved += () => { if (_active == session) { UpdateWsLabel(); RefreshInspector(); UpdateEnvironmentButtons(); UpdateSelectionBar(); } };
            field.LanguageAnalysisChanged += (o, e) =>
            {
                if (_active == session) Log("language service: " + field.LanguageTokens.Count + " tokens, " + field.LanguageDiagnostics.Count + " diagnostics" + string.Concat(field.LanguageDiagnostics.Select(d => "; " + d.Severity + " " + d.Code + " at " + d.Range.Start.Character + "-" + d.Range.End.Character)));
            };
            field.CompletionChanged += (o, e) =>
            {
                if (_active == session) Log("completion: " + (field.IsCompletionOpen ? field.CompletionItems.Count + " items, highlighted " + field.CompletionItems[field.CompletionIndex].Label : "closed"));
            };
            field.LanguageServiceFailed += error => Log("language service failed: " + error.Message);
            field.GotFocus += (s, e) => Activate(session);
            field.LostFocus += (s, e) => { if (_active == session) SelectionBarFocusMoved(); };
            field.EffectiveViewportChanged += (s, e) => { if (_active == session && field.Editor.HasSelection) UpdateSelectionBar(); };   // the bar follows scrolling

            if (many)
            {
                var label = new TextBlock { Text = number.ToString(CultureInfo.InvariantCulture), Width = 36, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(4, 0) };
                var row = new DockPanel { Margin = new Thickness(2) };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);
                row.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = field });
                _fieldStack.Children.Add(row);
            }
            else _fieldStack.Children.Add(field);
        }

        void Activate(Session session)
        {
            if (_active == session) return;
            _active = session;
            _selectionState = string.Empty;
            _selectionLabel.Text = string.Empty;
            _syncing = true;
            _readOnlyCheck.IsChecked = session.Field.IsReadOnly;
            _singleParagraphCheck.IsChecked = session.Field.SingleParagraph;
            SyncBlockChecks();
            _spellCheck.IsChecked = session.Field.SpellChecking != null;
            _syncing = false;
            Log("focus: field " + session.Number);
            UpdateWsLabel();
            RefreshInspector();
            DropMarkdownEdits("focus moved to field " + session.Number);
            RefreshMarkdown();
            UpdateSelectionBar();
        }

        // ------------------------------------------------------------ spelling

        /// <summary>The host's word rule for every field: with <paramref name="hyphenIsLetter"/> the hyphen forms words, as a writing system that says so would make it.</summary>
        void SetWordRules(bool hyphenIsLetter)
        {
            foreach (var session in _sessions) session.Field.WordRules = hyphenIsLetter ? SampleWordRules.HyphenIsLetter : null;
            Log("word rule: " + (hyphenIsLetter ? "the hyphen is a letter (a double-click, Ctrl+arrows, find and spelling take it)" : "the defaults"));
        }

        void SetSpelling(bool on)
        {
            _spelling = on ? new SampleSpelling(Log) { OffUiThread = _spellOffUiThread, Delay = _spellOffUiThread ? 15 : 0 } : null;
            foreach (var session in _sessions) session.Field.SpellChecking = _spelling;
            Log("spell checking: " + (on ? "on (English and French word lists)" : "off"));
        }

        // ------------------------------------------------------------ the environment box (a language service, ADR 0030)

        void SetEnvironment(bool on)
        {
            _env = on ? new SampleEnvironmentService() : null;
            _envClass.ItemsSource = _env?.Classes.Select(c => c.Abbreviation).ToList();
            foreach (var session in _sessions)
            {
                session.Field.SingleLine = on;
                session.Field.TokenStyles = on ? SampleTokenStyles.Environment() : null;
                session.Field.LanguageService = _env;
            }
            Log("language service: " + (on ? "on (the environment notation, over a made-up phonology); the box is one line" : "off"));
            UpdateEnvironmentButtons();
        }

        /// <summary>Enables each Insert button as the service says it may be used at the selection, as FieldWorks' Insert menu is enabled.</summary>
        void UpdateEnvironmentButtons()
        {
            var field = _active?.Field;
            bool on = _env != null && field != null;
            _envSlash.IsEnabled = on && field!.CanInsert(SampleEnvironmentService.InsertKinds.Slash);
            _envBar.IsEnabled = on && field!.CanInsert(SampleEnvironmentService.InsertKinds.Bar);
            _envOptional.IsEnabled = on && field!.CanInsert(SampleEnvironmentService.InsertKinds.OptionalItem);
            _envHash.IsEnabled = on && field!.CanInsert(SampleEnvironmentService.InsertKinds.WordBoundary);
            _envClass.IsEnabled = on && field!.CanInsert(SampleEnvironmentService.InsertKinds.NaturalClass);
        }

        /// <summary>Types the text of an insert command at the selection as one undo step, if the service allows it there; an optional item leaves the caret between its parentheses.</summary>
        public bool InsertEnvironment(string kind, string text)
        {
            var field = _active.Field;
            if (!field.CanInsert(kind))
            {
                Log("insert " + kind + ": not allowed here");
                return false;
            }
            field.Editor.CloseUndoGroup();
            field.Editor.TypeText(text);
            field.Editor.CloseUndoGroup();
            if (kind == SampleEnvironmentService.InsertKinds.OptionalItem) field.Editor.SetCaret(new TextPosition(field.Editor.Caret.Paragraph, field.Editor.Caret.Offset - 1));
            Log("insert " + kind + ": \"" + text + "\"");
            field.Focus();
            return true;
        }

        void EnvironmentClass(bool restore)
        {
            if (_env == null) { Log("natural classes: load the Environment scenario first"); return; }
            if (restore) { if (!_env.Classes.Any(c => c.Abbreviation == "V")) _env.AddClass("V", "Vowels"); }
            else _env.RemoveClass("V");
            _envClass.ItemsSource = _env.Classes.Select(c => c.Abbreviation).ToList();
            Log("natural classes: " + string.Join(" ", _env.Classes.Select(c => "[" + c.Abbreviation + "]")));
        }

        SpellingSuggestions? SpellingAtCaret(string action)
        {
            var field = _active.Field;
            var at = field.SpellingSuggestionsAt(field.Editor.Caret);
            if (at == null) Log(action + ": no marked word at the caret");
            return at;
        }

        void ShowSpelling()
        {
            var at = SpellingAtCaret("context menu");
            if (at == null) return;
            Log("context menu: '" + at.Word + "' (" + at.Ws + ", " + at.Problem + "): " + (at.All.Count == 0 ? "No suggestions" : string.Join(", ", at.Inline) + (at.Additional.Any() ? " + Additional suggestions: " + string.Join(", ", at.Additional) : string.Empty)) + (at.CanAddToDictionary ? "; Add to dictionary" : "; (Add to dictionary disabled)"));
        }

        void ReplaceSpelling()
        {
            var at = SpellingAtCaret("replace");
            if (at == null) return;
            if (at.All.Count == 0) { Log("replace: no suggestions for '" + at.Word + "'"); return; }
            Log("replace: '" + at.Word + "' -> '" + at.All[0] + "': " + (_active.Field.ApplySpellingSuggestion(at, at.All[0]) ? "done" : "refused"));
        }

        void SpacesSpelling()
        {
            var at = SpellingAtCaret("insert spaces");
            if (at == null) return;
            Log("insert spaces: '" + at.Word + "': " + (_active.Field.ApplySpellingSuggestion(at, at.WithMissingSpaces()) ? "done" : "refused"));
        }

        void WholeWordSpelling(bool last)
        {
            var at = SpellingAtCaret("whole word");
            if (at == null) return;
            if (at.WritingSystems.Count < 2) { Log("whole word: '" + at.Word + "' is in one writing system"); return; }
            var ws = last ? at.WritingSystems[at.WritingSystems.Count - 1] : at.WritingSystems[0];
            Log("whole word: '" + at.Word + "' -> " + ws + ": " + (_active.Field.ApplySpellingSuggestion(at, at.WithWholeWordIn(ws)) ? "done" : "refused"));
        }

        void AddSpelling(bool ignore)
        {
            var at = SpellingAtCaret(ignore ? "ignore" : "add");
            if (at == null) return;
            bool done = ignore ? _active.Field.IgnoreWord(at) : _active.Field.AddToDictionary(at);
            if (!done) Log((ignore ? "ignore" : "add") + ": '" + at.Word + "' is not a plain misspelling" + (ignore && at.CanAddToDictionary ? " or its dictionary cannot ignore" : string.Empty));
        }

        // ------------------------------------------------------------ label, inspector, log

        void UpdateWsLabel()
        {
            var tag = _active.Field.Editor.ActiveWs;
            var ws = SampleWritingSystems.Find(tag);
            _wsLabel.Text = "ws: " + tag + (ws != null ? " - " + ws.Name + " (" + ws.Kind + ")" : string.Empty);
        }

        void RefreshInspector() => _inspector.Text = Inspector.Describe(_active.Field.Editor, _active.Undo);

        void Log(string message)
        {
            _log.Add(message);
            if (_log.Count > MaxLogLines) _log.RemoveRange(0, _log.Count - MaxLogLines);
            _logBox.Text = string.Join("\n", _log);
            _logBox.CaretIndex = _logBox.Text.Length;
        }

        // ------------------------------------------------------------ keys

        void OnPreviewKey(object? sender, KeyEventArgs e)
        {
            if (!(e.Source is RichTextField) || (e.KeyModifiers & KeyModifiers.Control) == 0) return;
            switch (e.Key)
            {
                case Key.B: ToggleBold(); e.Handled = true; break;
                case Key.I: ToggleItalic(); e.Handled = true; break;
                case Key.U: ToggleUnderline(); e.Handled = true; break;
                case Key.C: Log("clipboard: Ctrl+C, copy through the library"); break;
                case Key.X: Log("clipboard: Ctrl+X, cut through the library"); break;
                case Key.V: Log("clipboard: Ctrl+V, paste through the library"); break;
            }
        }

        // ------------------------------------------------------------ undo and redo

        public bool UndoLast(string source = "button")
        {
            var undo = _active.Undo;
            string label = undo.Done.Count > 0 ? undo.Done[undo.Done.Count - 1].Label : string.Empty;
            bool ok = undo.Undo();
            Log("undo requested (" + source + "): " + (ok ? "undid " + label : "nothing to undo"));
            RefreshInspector();
            return ok;
        }

        public bool RedoLast(string source = "button")
        {
            var undo = _active.Undo;
            string label = undo.Undone.Count > 0 ? undo.Undone[undo.Undone.Count - 1].Label : string.Empty;
            bool ok = undo.Redo();
            Log("redo requested (" + source + "): " + (ok ? "redid " + label : "nothing to redo"));
            RefreshInspector();
            return ok;
        }

        // ------------------------------------------------------------ formatting and writing system

        public bool ToggleBold() => Toggle("bold", TextPropertyNumbers.Bold);
        public bool ToggleItalic() => Toggle("italic", TextPropertyNumbers.Italic);
        public bool ToggleUnderline() => Toggle("underline", TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle);

        bool Toggle(string label, int property, int on = TextPropertyNumbers.ToggleOn)
        {
            return FormatSelection(label, runs =>
            {
                bool allOn = runs.All(r => r.Props.TryGetInt(property, out var v) && v == on);
                Log("format: " + label + (allOn ? " off" : " on"));
                return r =>
                {
                    if (allOn) r.Props.IntProps.Remove(property);
                    else r.Props.SetInt(property, on, TextPropertyNumbers.VariationEnum);   // FieldWorks stores toggles and enums with ktpvEnum
                };
            });
        }

        /// <summary>Sets (or, with "(none)", removes) the named character style on the selected runs.</summary>
        public bool ApplyCharacterStyle(string style)
        {
            bool none = style == SampleStyles.None;
            return FormatSelection("character style " + style, runs =>
            {
                Log("format: character style " + style);
                return r =>
                {
                    if (none) r.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
                    else r.Props.SetStr(TextPropertyNumbers.NamedStyle, style);
                };
            });
        }

        /// <summary>Gives the paragraphs of the selection (or the caret's paragraph) the named paragraph style; "(none)" clears it.</summary>
        public bool ApplyParagraphStyle(string style)
        {
            var session = _active;
            var editor = session.Field.Editor;
            if (style != SampleStyles.None && !session.Field.IsParagraphStyleAllowed(style))
            {
                Log("format: paragraph style " + style + " refused: " + session.Field.BlockRecognizer.RecognizeStyle(style) + " are off in this field");
                return false;
            }
            editor.Ordered(out var a, out var b);
            var before = new List<Paragraph>();
            var after = new List<Paragraph>();
            for (int pi = a.Paragraph; pi <= b.Paragraph; pi++)
            {
                var original = editor.Document.Paragraphs[pi];
                var edited = original.Clone();
                edited.Style = style == SampleStyles.None ? string.Empty : style;
                before.Add(original.Clone());
                after.Add(edited);
            }
            Log("format: paragraph style " + style);
            var delta = new EditDelta { Start = a.Paragraph, Before = before, After = after, CaretBefore = editor.Caret, CaretAfter = editor.Caret, Label = "paragraph style " + style };
            session.Undo.Done.Add(delta);
            session.Undo.Undone.Clear();
            var anchor = editor.Anchor;
            var caret = editor.Caret;
            editor.Apply(delta);
            editor.SetCaret(anchor);
            editor.SetCaret(caret, true);
            return true;
        }

        /// <summary>A writing-system button: with a selection it changes the writing system of the selected text; with
        /// a caret it sets the writing system of the next text typed (what a keyboard switch does).</summary>
        public void SetWritingSystem(string tag)
        {
            var editor = _active.Field.Editor;
            if (editor.HasSelection) FormatSelection("writing system " + tag, runs => r => r.Ws = tag);
            else editor.SetActiveWs(tag);
        }

        /// <summary>
        /// Changes every non-object run in the selection and pushes the change on the host-owned undo stack: the
        /// editor has no formatting command, so the playground plays the host. The edit is an EditDelta applied with
        /// <c>Apply</c>, so paragraph ids are kept.
        /// </summary>
        bool FormatSelection(string label, Func<List<Run>, Action<Run>> plan)
        {
            var session = _active;
            var editor = session.Field.Editor;
            if (!editor.HasSelection)
            {
                Log("format: " + label + " needs a selection");
                return false;
            }
            if (!editor.CanFormatSelection)
            {
                Log("format: " + label + " refused, the selection holds text that may not be edited");
                return false;
            }
            editor.Ordered(out var a, out var b);
            var before = new List<Paragraph>();
            var after = new List<Paragraph>();
            var targets = new List<Run>();
            for (int pi = a.Paragraph; pi <= b.Paragraph; pi++)
            {
                var original = editor.Document.Paragraphs[pi];
                var edited = original.Clone();
                int start = pi == a.Paragraph ? a.Offset : 0;
                int end = pi == b.Paragraph ? b.Offset : edited.Length;
                int first = edited.SplitRunAt(start);
                int last = edited.SplitRunAt(end);
                for (int i = first; i < last; i++)
                    if (!edited.Runs[i].IsObject) targets.Add(edited.Runs[i]);
                before.Add(original.Clone());
                after.Add(edited);
            }
            if (targets.Count == 0)
            {
                Log("format: " + label + " has nothing to change in the selection");
                return false;
            }
            var change = plan(targets);
            foreach (var run in targets) change(run);
            foreach (var p in after) p.Normalize();

            var delta = new EditDelta { Start = a.Paragraph, Before = before, After = after, CaretBefore = editor.Caret, CaretAfter = b, Label = label };
            session.Undo.Done.Add(delta);
            session.Undo.Undone.Clear();
            editor.Apply(delta);
            editor.SetCaret(a);
            editor.SetCaret(b, true);
            return true;
        }

        // ------------------------------------------------------------ pictures and clipboard

        /// <summary>Inserts an inline picture object (an object run) at the caret, replacing the selection.</summary>
        public void InsertPicture()
        {
            var editor = _active.Field.Editor;
            var fragment = new Paragraph { DefaultWs = editor.ActiveWs };
            fragment.Runs.Add(Run.Object(editor.ActiveWs, (_pictureCount++ % 2 == 0 ? Scenarios.RedPicture : Scenarios.BluePicture)));
            editor.Paste(new List<Paragraph> { fragment });
            Log("picture inserted at " + editor.Caret);
        }

        /// <summary>The caption of the "Copy as" submenu in the context menu.</summary>
        public const string CopyAsCaption = "Copy as ▸";

        /// <summary>The captions in the "Copy as" submenu the last time the context menu was shown.</summary>
        public List<string> LastCopyAsItems { get; } = new List<string>();

        /// <summary>Copy as: one form on the clipboard as text (a registered form's id, or <see cref="ClipboardForms.PlainTextId"/>).</summary>
        public async Task CopyAsAsync(string formId)
        {
            var field = _active.Field;
            try
            {
                bool done = await field.CopyAsAsync(formId);
                Log("clipboard: " + (done ? "copied as " + formId : "copy as " + formId + " ignored, nothing to copy"));
            }
            catch (Exception ex)
            {
                Log("clipboard: copy as " + formId + " failed: " + ex.Message);
            }
        }

        public async Task CopyAsync(bool cut)
        {
            var field = _active.Field;
            if (!field.Editor.HasSelection)
            {
                Log("clipboard: " + (cut ? "cut" : "copy") + " ignored, nothing selected");
                return;
            }
            var fragment = field.Editor.GetFragment();
            int chars = fragment.Sum(p => p.Length);
            try
            {
                await field.CopyAsync(cut);
                Log("clipboard: " + (cut ? "cut " : "copied ") + fragment.Count + " paragraph(s), " + chars + " chars, as " + string.Join(", ", field.ClipboardForms.Select(f => f.Id).Concat(new[] { "plain text" })));
            }
            catch (Exception ex)
            {
                Log("clipboard: " + (cut ? "cut" : "copy") + " failed: " + ex.Message);
            }
        }

        public async Task PasteAsync()
        {
            var field = _active.Field;
            var before = field.Editor.Document.Paragraphs.Count;
            try
            {
                await field.PasteAsync();
                Log("clipboard: pasted, paragraphs " + before + " -> " + field.Editor.Document.Paragraphs.Count + ", caret " + field.Editor.Caret);
            }
            catch (Exception ex)
            {
                Log("clipboard: paste failed: " + ex.Message);
            }
        }
    }
}
