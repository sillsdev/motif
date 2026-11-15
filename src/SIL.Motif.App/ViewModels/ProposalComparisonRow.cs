using System.Globalization;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>One word the two compared Proposals treat differently, with what each does to it.</summary>
/// <param name="Word">The word's written form.</param>
/// <param name="FirstEffect">What the first Proposal does to the word.</param>
/// <param name="SecondEffect">What the second Proposal does to the word.</param>
/// <param name="ShowInTextsCommand">Opens the word on the Texts page.</param>
/// <param name="WritingSystemId">The writing system the word is shown in, when known.</param>
public sealed record ProposalComparisonRow(string Word, string FirstEffect, string SecondEffect,
    IRelayCommand ShowInTextsCommand, string? WritingSystemId = null)
{
    /// <summary>The button's name, which says which word it shows.</summary>
    public string ShowInTextsName => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.ProposalCompareShowInTextsFormat, Word);
}
