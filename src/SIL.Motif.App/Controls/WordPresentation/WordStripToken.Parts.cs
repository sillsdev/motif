using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Controls.WordPresentation;

public sealed partial class WordStripToken
{
    private T Part<T>(ref T? field, string name) where T : Control
    {
        if (field is null)
        {
            field = BuildPart<T>(name, Data);
            _owned.Add(field);
        }
        return field;
    }

    private void SyncOptionalControls()
    {
        var data = Data;
        var overlay = Overlay;
        var active = ReferenceEquals(overlay.Owner, this) && data is not null;
        var line = new List<Control>(8);
        if (ShowsSelectionCheckBox)
            line.Add(active ? overlay.SelectionBox : Part(ref _selectionGlyph, "SelectionGlyph"));
        if (data?.ShowUnread == true) line.Add(Part(ref _unreadMark, "UnreadMark"));
        line.Add(_drawnWord is { } drawn ? drawn : _wordText);
        line.Add(_verdictGlyph);
        if (data?.HasNamedWarning == true) line.Add(Part(ref _namedWarning, "NamedWarning"));
        if (active) line.Add(overlay.Buttons);
        else
        {
            line.Add(_markFace);
            if (data?.Disposition.HasReadings == true) line.Add(Part(ref _addFace, "AddFace"));
        }
        if (data?.HasDisapprovedReading == true) line.Add(Part(ref _disapprovedMark, "DisapprovedChip"));
        Reconcile(_wordLinePanel, line);

        var fieldWorks = new List<Control>(3);
        if (_drawnWord is null && data?.HasFieldWorksAnalyses == true)
        {
            var created = _fieldWorksMorphology is null;
            fieldWorks.Add(Part(ref _fieldWorksMorphology, "FieldWorksMorphology"));
            if (created) ApplyOverflowRequests();
        }
        if (data?.HasMoreFieldWorksAnalyses == true) fieldWorks.Add(Part(ref _moreFieldWorks, "MoreFieldWorks"));
        if (data?.HasNothingStored == true) fieldWorks.Add(Part(ref _nothingStored, "NothingStored"));
        FillRow(_fieldWorksLine, fieldWorks, ref _fieldWorksColumns);

        var rows = new List<Control>(3) { _wordLine };
        if (_drawnWord is null || fieldWorks.Count > 0) rows.Add(_fieldWorksLine);
        if (data is { Verdict: not OccurrenceVerdict.Matches } &&
            (_drawnWord is null || data.HasMorePanGlossReadings || data.ShowsPanGlossNote))
        {
            var panGlossLine = Part(ref _panGlossLine, "PanGlossLine");
            var panGloss = new List<Control>(3);
            if (_drawnWord is null && data.ShowsPanGlossReading)
            {
                var created = _panGlossMorphology is null;
                panGloss.Add(Part(ref _panGlossMorphology, "PanGlossMorphology"));
                if (created) ApplyOverflowRequests();
            }
            if (data.HasMorePanGlossReadings) panGloss.Add(Part(ref _morePanGloss, "MorePanGloss"));
            if (data.ShowsPanGlossNote) panGloss.Add(Part(ref _panGlossNote, "PanGlossNote"));
            FillRow(panGlossLine, panGloss, ref _panGlossColumns);
            rows.Add(panGlossLine);
        }
        Reconcile(_stripStack, rows);

        var columns = new List<Control>(3) { _stripStack };
        if (data is { ShowsActions: true })
        {
            SetChild(_actions, data.HasPrimaryAction ? Part(ref _actionRow, "ActionRow") : null);
            columns.Add(_actions);
        }
        else SetChild(_actions, null);
        if (data?.HasStagedChanges == true) columns.Add(Part(ref _stagedChanges, "Staged"));
        Reconcile(_stripContent, columns);
        Park();
    }

    private void FillRow(Border row, List<Control> items, ref Control? columnsField)
    {
        if (items.Count == 0)
        {
            row.Child = null;
            return;
        }
        if (items.Count == 1)
        {
            SetChild(row, items[0]);
            return;
        }
        var columns = (Grid)Part(ref columnsField, "Row");
        SetChild(row, columns);
        Reconcile(columns, items);
    }

    private void Reconcile(Panel panel, List<Control> desired)
    {
        for (var index = 0; index < desired.Count; index++)
        {
            var child = desired[index];
            if (panel is Grid) Grid.SetColumn(child, ColumnOf(panel, child, index));
            if (index < panel.Children.Count && ReferenceEquals(panel.Children[index], child)) continue;
            Detach(child);
            panel.Children.Insert(Math.Min(index, panel.Children.Count), child);
        }
        for (var index = panel.Children.Count - 1; index >= desired.Count; index--) panel.Children.RemoveAt(index);
        for (var index = panel.Children.Count - 1; index >= 0; index--)
            if (!desired.Contains(panel.Children[index])) panel.Children.RemoveAt(index);
    }

    private int ColumnOf(Panel panel, Control child, int index)
    {
        if (!ReferenceEquals(panel, _stripContent)) return index;
        return ReferenceEquals(child, _stripStack) ? 0 : ReferenceEquals(child, _actions) ? 1 : 2;
    }

    private void Detach(Control child)
    {
        if (ReferenceEquals(child.Parent, this)) LogicalChildren.Remove(child);
        switch (child.Parent)
        {
            case Panel panel: panel.Children.Remove(child); break;
            case Decorator decorator when ReferenceEquals(decorator.Child, child): decorator.Child = null; break;
            case ContentControl content when ReferenceEquals(content.Content, child): content.Content = null; break;
        }
    }

    private void SetChild(Border parent, Control? child)
    {
        if (ReferenceEquals(parent.Child, child)) return;
        parent.Child = null;
        if (child is null) return;
        Detach(child);
        parent.Child = child;
    }

    private void Park()
    {
        foreach (var part in _owned)
        {
            if (part.GetVisualParent() is null)
            {
                if (!LogicalChildren.Contains(part)) LogicalChildren.Add(part);
            }
            else if (ReferenceEquals(part.Parent, this)) LogicalChildren.Remove(part);
        }
    }
}
