namespace SIL.Motif.App.ViewModels;

/// <summary>The stage of the work the window serves (ADR 0100); each page says which stages it shows in.</summary>
public enum MotifStage
{
    /// <summary>Growing the grammar: the lean set of pages for getting analyses right.</summary>
    Grow,

    /// <summary>Polishing the grammar: every page, including the measurement and tuning ones.</summary>
    Polish,
}
