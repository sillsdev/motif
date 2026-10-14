using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>The Texts page: Compare, Analyze texts and fixed word lists, with shared pending changes above them.</summary>
public sealed partial class TextsPage : UserControl, IWorkspacePaneProvider
{
    private readonly ContentControl _selectionHost;

    public TextsPage(TextsPageModel page) : this(page, null)
    {
    }

    internal TextsPage(TextsPageModel page, SettingsViewModel? settings)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);
        _selectionHost = Host("SelectionHost");

        var header = this.FindControl<Grid>("TextsPageHeader")!;
        var tabs = this.FindControl<WrapPanel>("TextsHeaderTabs")!;
        var actions = this.FindControl<StackPanel>("TextsHeaderActions")!;
        bool? actionsOnOwnRow = null;
        void ArrangeHeader(double width)
        {
            tabs.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
            var visibleActions = actions.Children.OfType<Control>().Where(control => control.IsVisible).ToArray();
            foreach (var action in visibleActions) action.Measure(infinite);
            var actionWidth = visibleActions.Sum(action => action.DesiredSize.Width) +
                Math.Max(0, visibleActions.Length - 1) * actions.Spacing;
            var onOwnRow = width + 0.5 < tabs.DesiredSize.Width + actionWidth + header.ColumnSpacing;
            if (actionsOnOwnRow == onOwnRow) return;

            actionsOnOwnRow = onOwnRow;
            actions.Orientation = onOwnRow ? Orientation.Vertical : Orientation.Horizontal;
            Grid.SetColumn(tabs, 0);
            Grid.SetColumnSpan(tabs, onOwnRow ? 2 : 1);
            Grid.SetColumn(actions, onOwnRow ? 0 : 1);
            Grid.SetColumnSpan(actions, onOwnRow ? 2 : 1);
            Grid.SetRow(actions, onOwnRow ? 1 : 0);
        }

        header.SizeChanged += (_, args) => ArrangeHeader(args.NewSize.Width);
        actions.SizeChanged += (_, _) => ArrangeHeader(header.Bounds.Width);
        ArrangeHeader(header.Bounds.Width);

        Host("CompareHost").Content = new ComparePanel(page.Assess.Compare);
        var selection = new SelectionPanel(page.Selection, page.Words, settings);
        _selectionHost.Content = selection;
        PrepareSidePanel();
        Host("AnalyzeReaderHost").Content = new ResultsInTextPanel(page.ResultsInText);
        Host("WordListHost").Content = new TextWordsPanel(page.Words);
        Host("ListsHost").Content = new TextsListsPanel(page.TextsLists);
        Host("DifferenceHost").Content = new DifferencePanel(page.Assess.Difference);

        // One prompt, moved between its two places, so the page never holds two buttons of the same name.
        var prompt = new Border { Classes = { "card" }, Child = new ParsePrompt { DataContext = page.Context } };
        var above = Host("AnalyzeParsePromptHost");
        var centred = Host("CentredParsePromptHost");
        void Place()
        {
            var host = page.ShowAnalyzeTexts ? above : centred;
            if (ReferenceEquals(host.Content, prompt)) return;
            (host == above ? centred : above).Content = null;
            host.Content = prompt;
        }
        Place();
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TextsPageModel.ShowAnalyzeTexts)) Place();
        };
    }

    /// <summary>The frame label for the existing Texts selection control.</summary>
    public string SidePanelTitle => PageRegistry.For(WorkspacePage.Texts).Title;

    /// <summary>The existing selection control displayed in the frame's side panel.</summary>
    public Control SidePanel => _selectionHost;

    private void PrepareSidePanel()
    {
        _selectionHost.DataContext = DataContext;
        _selectionHost.ClearValue(IsVisibleProperty);
        _selectionHost.ClearValue(WidthProperty);
        var analyze = this.FindControl<Grid>("AnalyzeTextsHost")!;
        analyze.Children.Remove(_selectionHost);
        analyze.ColumnDefinitions = new ColumnDefinitions("*");
        analyze.ColumnSpacing = 0;
        foreach (var child in analyze.Children) Grid.SetColumn(child, 0);
    }

    private ContentControl Host(string name) =>
        this.FindControl<ContentControl>(name)
        ?? throw new InvalidOperationException($"TextsPage.axaml has no element named '{name}'.");
}
