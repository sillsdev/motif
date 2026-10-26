using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace SIL.Motif.App.Views;

/// <summary>Builds a Review row from the template for its kind, so a word change carries no grammar-only controls and a grammar change carries no word-only ones.</summary>
public sealed class ReviewRowTemplate : IDataTemplate
{
    /// <summary>The row of a grammar change.</summary>
    public IDataTemplate? Grammar { get; set; }

    /// <summary>The row of a word change.</summary>
    public IDataTemplate? Word { get; set; }

    public bool Match(object? data) => data is ViewModels.ReviewListEntryViewModel { Review: not null };

    public Control? Build(object? data) =>
        data is ViewModels.ReviewListEntryViewModel { Review: { } review }
            ? (review.IsGrammar ? Grammar : Word)?.Build(data)
            : null;
}
