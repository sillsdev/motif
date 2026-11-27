using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Guide;

namespace SIL.Motif.App.Views;

/// <summary>
/// The project's Grammar Guide: parts and sections from the Grammar Design Records' own hierarchy, the selected section's
/// Discussion to read or, after Edit, to change, and the grammar its text links to. The grammar itself is read and edited
/// in FieldWorks; edits to a section's text go to the pending-changes Draft and the project only changes when they are
/// applied. The generated examples block stays hidden until a reviewed source supplies it.
/// </summary>
public sealed partial class GrammarGuidePage : UserControl
{
    /// <summary>The width below which the outline sits above the section; a style sets it.</summary>
    public static readonly AttachedProperty<double> PanelBreakpointProperty =
        AvaloniaProperty.RegisterAttached<GrammarGuidePage, Control, double>("PanelBreakpoint");

    public static double GetPanelBreakpoint(Control control) => control.GetValue(PanelBreakpointProperty);

    public static void SetPanelBreakpoint(Control control, double value) => control.SetValue(PanelBreakpointProperty, value);

    private readonly GrammarGuidePageModel _model;
    private GuideSectionEditor _editor;
    private bool _rebuild;
    private bool _sideBySide = true;
    private bool _arranged;

    /// <summary>Creates the page over the workspace's shared page model.</summary>
    public GrammarGuidePage(WorkspaceContext context) : this(
        (context ?? throw new ArgumentNullException(nameof(context))).GetOrCreatePage(static owner => new GrammarGuidePageModel(owner))) { }

    public GrammarGuidePage(GrammarGuidePageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
        _editor = BuildEditor();
        this.FindControl<ContentControl>("ExamplesHost")!.Content = new GuideExamples(model.Examples);
        this.FindControl<ContentControl>("InterlinearHost")!.Content = new GuideInterlinear(model.Interlinear);
        this.FindControl<ContentControl>("NamesHost")!.Content = new GuideGrammarNames(model);
        this.FindControl<ContentControl>("QueriesHost")!.Content = new GuideQueryTables(model.Queries);
        model.Queries.SaveBody = (id, body) => _editor.ReplaceQueryBodyAsync(id, body);
        var toggle = this.FindControl<ToggleButton>("EditToggle")!;
        ShowEditLabel(toggle);
        toggle.IsCheckedChanged += (_, _) =>
        {
            _editor.IsEditing = toggle.IsChecked == true;
            ShowEditLabel(toggle);
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GrammarGuidePageModel.ShownRecord) && model.ShownRecord is { } shown) _editor.Show(shown);
            if (e.PropertyName == nameof(GrammarGuidePageModel.CanEdit) && !model.CanEdit) toggle.IsChecked = false;
        };
        model.RecordsReloaded += (_, _) =>
        {
            // The edit store belongs to one project, so the first Baseline of a newly opened project gets a new editor.
            if (_rebuild)
            {
                _rebuild = false;
                _editor = BuildEditor();
                return;
            }
            _editor.SetRecords(model.Records.ToArray());
        };
        model.ProjectWasCleared += (_, _) =>
        {
            _editor.Clear();
            _rebuild = true;
            toggle.IsChecked = false;
        };
        SizeChanged += (_, _) => Arrange();
        if (model.ShownRecord is { } already) _editor.Show(already);
    }

    private GuideSectionEditor BuildEditor()
    {
        var editor = new GuideSectionEditor(_model.Records.ToArray(), _model.ReadRecordAsync, _model.EditStore, null,
            _model.FieldWorksProjectName, null, _model.WritingSystemName, _model.LinkToPathAsync)
        {
            IsEditing = this.FindControl<ToggleButton>("EditToggle")?.IsChecked == true,
        };
        editor.SelectionRefused += (_, id) => _model.Select(id);
        this.FindControl<ContentControl>("DiscussionHost")!.Content = editor;
        return editor;
    }

    /// <summary>The embedded editor of the selected section.</summary>
    internal GuideSectionEditor Editor => _editor;

    private static void ShowEditLabel(ToggleButton toggle) =>
        toggle.Content = toggle.IsChecked == true ? MotifAppStrings.GuideDone : MotifAppStrings.GuideEdit;

    /// <summary>Puts the outline beside the section when there is room and above it when there is not.</summary>
    private void Arrange()
    {
        var body = this.FindControl<Grid>("GuideBody")!;
        var breakpoint = GetPanelBreakpoint(body);
        if (breakpoint <= 0 || Bounds.Width <= 0) return;
        var side = Bounds.Width >= breakpoint;
        if (_arranged && _sideBySide == side) return;
        _arranged = true;
        _sideBySide = side;
        body.ColumnDefinitions[0].Width = side ? GridLength.Auto : new GridLength(0);
        var outline = this.FindControl<Border>("Outline")!;
        var reading = this.FindControl<ScrollViewer>("Reading")!;
        if (side)
        {
            outline.Classes.Set("beside", true);
            outline.Classes.Set("stacked", false);
            Grid.SetRow(outline, 0);
            Grid.SetRowSpan(outline, 2);
            Grid.SetColumn(outline, 0);
            Grid.SetColumnSpan(outline, 1);
            Grid.SetRow(reading, 0);
            Grid.SetRowSpan(reading, 2);
            Grid.SetColumn(reading, 1);
            Grid.SetColumnSpan(reading, 1);
            return;
        }
        outline.Classes.Set("beside", false);
        outline.Classes.Set("stacked", true);
        Grid.SetRow(outline, 0);
        Grid.SetRowSpan(outline, 1);
        Grid.SetColumn(outline, 0);
        Grid.SetColumnSpan(outline, 2);
        Grid.SetRow(reading, 1);
        Grid.SetRowSpan(reading, 1);
        Grid.SetColumn(reading, 0);
        Grid.SetColumnSpan(reading, 2);
    }
}
