using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Builds the full word strip for a word and a single hidden element for punctuation, which never shows.</summary>
public sealed class WordStripTokenTemplate : IDataTemplate
{
    [Content]
    public IDataTemplate? Word { get; set; }

    public bool Match(object? data) => Word?.Match(data) ?? false;

    public Control? Build(object? data) => IsPunctuation(data) ? new Border { IsVisible = false } : Word?.Build(data);

    internal bool CanRecycle(object? model, Control existing) => IsPunctuation(model) != existing is WordStripToken;

    private static bool IsPunctuation(object? model) => model is ResultsTokenViewModel { IsWord: false };
}
