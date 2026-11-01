using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Names the operation that deletes one grammatical info (morphosyntactic analysis) of an entry once nothing
/// refers to it. It targets the analysis itself and fails while a sense, rule or analysis still points at it.
/// </summary>
public static class MorphoSyntaxAnalysisDeleteOperationKinds
{
    /// <summary>Deletes an unreferenced grammatical info from its entry.</summary>
    public const string Delete = "lexical/lexEntry/deleteMorphoSyntaxAnalyses";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(Delete);
        OperationHandlerRegistry.Register(Delete, MorphoSyntaxAnalysisDeleteHandler.Instance);
        OperationSlotRegistry.RegisterEntityDeletion(Delete);
    }
#pragma warning restore CA2255
}

internal sealed class MorphoSyntaxAnalysisDeleteHandler : IOperationHandler
{
    internal static readonly MorphoSyntaxAnalysisDeleteHandler Instance = new();

    private const string Kind = MorphoSyntaxAnalysisDeleteOperationKinds.Delete;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        RequireEmptyPayload(operation);
        var id = operation.Target ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'target'.");
        touchedTargets.Add(id);
        var before = ReferenceFieldAlternatives.ToAlternatives(id);
        // LibLCM removes a grammatical info on its own when the last sense stops using it.
        if (!Exists(cache, id))
            return new ExpectedEffect(id, SnapshotFields.LexEntryMorphoSyntaxAnalyses, before,
                ReferenceFieldAlternatives.ToAlternatives(null));
        var (_, msa) = Resolve(cache, operation);
        var user = msa.ReferringObjects.FirstOrDefault(other => !ReferenceEquals(other, msa));
        if (user is not null)
            throw new InvalidOperationException(
                $"'{Kind}': '{id.Value}' is still used by a {user.ClassName} ({user.Guid:D}); relink that use first.");
        msa.Delete();
        return new ExpectedEffect(id, SnapshotFields.LexEntryMorphoSyntaxAnalyses, before,
            ReferenceFieldAlternatives.ToAlternatives(null));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var id = operation.Target ?? throw new InvalidOperationException(
            $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'target'.");
        var current = ReferenceFieldAlternatives.ToAlternatives(Exists(cache, id) ? id : null);
        return new ExpectedEffect(id, SnapshotFields.LexEntryMorphoSyntaxAnalyses, current, current);
    }

    private static bool Exists(LcmCache cache, CanonicalId id) =>
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(id.ToGuid(), out _);

    private static void RequireEmptyPayload(OperationEnvelope operation)
    {
        var after = OperationSlotRegistry.After(operation);
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, [], Kind);
    }

    private static (CanonicalId Id, IMoMorphSynAnalysis Msa) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var (id, msa) = TargetResolution.Resolve<IMoMorphSynAnalysis>(cache, operation, Kind);
        if (msa.Owner is not ILexEntry)
            throw new InvalidOperationException($"'{Kind}': '{id.Value}' is not the grammatical info of an entry.");
        return (id, msa);
    }
}
