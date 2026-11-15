using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>A supported next action, with a complete accessible sentence and an ordinary activation command.</summary>
/// <param name="Category">The destination's visible category.</param>
/// <param name="Text">The fact that makes this action useful.</param>
/// <param name="AutomationId">The stable automation identity of this action.</param>
/// <param name="OpenCommand">Opens the destination only while this row's read is current.</param>
public sealed record ProgressNextRow(string Category, string Text, string AutomationId, IRelayCommand OpenCommand);
