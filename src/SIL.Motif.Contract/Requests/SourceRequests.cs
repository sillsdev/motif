using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Requests;

/// <summary>Lists the Motif Sources of the current Baseline.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="All">Whether to list Removed Sources too; they are hidden otherwise.</param>
/// <param name="ExpectedBaseline">The required Baseline identity; null accepts the current Baseline.</param>
public sealed record SourceListRequest(string ProjectPath, bool All = false, BaselineToken? ExpectedBaseline = null);

/// <summary>Reads one Source: its Context as book and chapter ranges, its folder on this machine and its status.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Id">The Source id, such as <c>WEB</c>; matched ignoring case.</param>
/// <param name="ExpectedBaseline">The required Baseline identity; null accepts the current Baseline.</param>
public sealed record SourceShowRequest(string ProjectPath, string Id, BaselineToken? ExpectedBaseline = null);

/// <summary>Says where a chapter sits in each Source: in its Context, the person's Current chapter, or later.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Ref">The chapter, such as <c>MAT 5</c>.</param>
/// <param name="Source">One Source id; null asks every Active Source.</param>
/// <param name="ExpectedBaseline">The required Baseline identity; null accepts the current Baseline.</param>
public sealed record SourceWhereRequest(string ProjectPath, string Ref, string? Source = null, BaselineToken? ExpectedBaseline = null);

/// <summary>Reads the Current chapter of each Source from this machine's local file.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Source">One Source id; null reads every Source that has a Current chapter.</param>
/// <param name="ExpectedBaseline">The required Baseline identity; null accepts the current Baseline.</param>
public sealed record CurrentGetRequest(string ProjectPath, string? Source = null, BaselineToken? ExpectedBaseline = null);

/// <summary>Records the folder of a Source on this machine. Local and instant: no Draft.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Source">The Source id; it must be in the project's Source list.</param>
/// <param name="Path">The existing Paratext project folder.</param>
public sealed record SourceSetFolderRequest(string ProjectPath, string Source, string Path);

/// <summary>Forgets the folder of a Source on this machine.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Source">The Source id; it must be in the project's Source list.</param>
public sealed record SourceClearFolderRequest(string ProjectPath, string Source);

/// <summary>Makes a chapter the Current chapter of a Source on this machine.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Source">The Source id; it must be in the project's Source list.</param>
/// <param name="Ref">The chapter, such as <c>MAT 5</c>.</param>
public sealed record CurrentSetRequest(string ProjectPath, string Source, string Ref);

/// <summary>Forgets the Current chapter of a Source on this machine.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="Source">The Source id; it must be in the project's Source list.</param>
public sealed record CurrentClearRequest(string ProjectPath, string Source);
