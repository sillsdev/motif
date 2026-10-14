using CommunityToolkit.Mvvm.ComponentModel;

namespace SIL.Motif.App.ViewModels;

/// <summary>A selectable Warnings area with the count currently shown in that area.</summary>
public sealed class WarningsAreaFilterOption : ObservableObject
{
    private int _count;
    private bool _isActive;

    /// <summary>Creates an area option; a null <paramref name="area"/> represents all areas.</summary>
    public WarningsAreaFilterOption(string? area, string label, int count, bool isActive)
    {
        Area = area;
        Label = label;
        _count = count;
        _isActive = isActive;
    }

    /// <summary>The area key, or null for all areas.</summary>
    public string? Area { get; }

    /// <summary>The label shown to the person.</summary>
    public string Label { get; }

    /// <summary>The number of findings currently shown in the area.</summary>
    public int Count
    {
        get => _count;
        private set => SetProperty(ref _count, value);
    }

    /// <summary>Whether choosing this option would leave the current filter unchanged.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetProperty(ref _isActive, value);
    }

    internal void Update(int count, bool isActive)
    {
        Count = count;
        IsActive = isActive;
    }
}
