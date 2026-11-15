using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

/// <summary>A classification cell in stored Assessment evidence.</summary>
public sealed record TextsListCell(WordProjectStatus Row, CompareColumnKind Column);
