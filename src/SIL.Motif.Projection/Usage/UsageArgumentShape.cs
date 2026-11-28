using System;
using System.Collections.Generic;

namespace SIL.Motif.Projection.Usage;

/// <summary>
/// One recorded call: which surface command ran, when, and the *shape* of its arguments —
/// never their values. See <see cref="UsageArgumentShape"/> for what "shape" means here.
/// </summary>
public sealed record UsageLogEntry(string TimestampUtc, string Command, IReadOnlyList<string> ArgumentShape);

/// <summary>
/// Builds argument-shape tokens that describe an argument's kind and, for a collection, its
/// cardinality — never the value itself. A canonical id, a gloss, and a project path all reduce to
/// <c>"name:text"</c>; only the parameter name and the fact that it was a single piece of text
/// survive.
/// </summary>
public static class UsageArgumentShape
{
    public static string Text(string name) => $"{name}:text";
    public static string Number(string name) => $"{name}:number";
    public static string Object(string name) => $"{name}:object";
    public static string List(string name, int count) => $"{name}:list({count})";
    public static string Flag(string name) => $"{name}:flag";
}
