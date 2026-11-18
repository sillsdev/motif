using System;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.HandAnalyses;

/// <summary>Eligibility is explicit; Ctrl+E retains every existing analysis or gloss attachment.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "policy")]
[JsonDerivedType(typeof(HandAnalysisGeneralElsewhereScope), "SameWordformAndGuess")]
[JsonDerivedType(typeof(HandAnalysisBareElsewhereScope), "CurrentChapterOrTextBareEitherCase")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record HandAnalysisElsewhereScope;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisGeneralElsewhereScope(HandAnalysisGeneralPopulation Population) : HandAnalysisElsewhereScope;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisBareElsewhereScope(HandAnalysisBarePopulation Population) : HandAnalysisElsewhereScope;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HandAnalysisCorpusPopulation), "corpus")]
[JsonDerivedType(typeof(HandAnalysisGeneralTextPopulation), "text")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record HandAnalysisGeneralPopulation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisCorpusPopulation(Guid CorpusId) : HandAnalysisGeneralPopulation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisGeneralTextPopulation(Guid TextId) : HandAnalysisGeneralPopulation;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HandAnalysisChapterPopulation), "chapter")]
[JsonDerivedType(typeof(HandAnalysisBareTextPopulation), "text")]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public abstract record HandAnalysisBarePopulation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisChapterPopulation(Guid SourceId, string Versification, string Book, int Chapter) : HandAnalysisBarePopulation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HandAnalysisBareTextPopulation(Guid TextId) : HandAnalysisBarePopulation;
