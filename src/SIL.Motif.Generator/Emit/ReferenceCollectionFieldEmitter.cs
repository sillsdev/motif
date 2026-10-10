using SIL.Motif.Generator.Model;

namespace SIL.Motif.Generator.Emit;

/// <summary>
/// Emits one <c>Operations/Generated2/{DeclaringClass}{FieldName}.g.cs</c> file for a <c>rel/col</c> or
/// <c>rel/seq</c> field's <c>addRef</c>/<c>removeRef</c> kinds, via
/// <see cref="Runner.Operations.ReferenceCollectionFieldLowering"/>.
/// </summary>
/// <remarks>
/// Only fields selected for ordered placement receive a <c>move</c> kind and placement-aware
/// <c>addRef</c> lowering. Other sequence fields keep their existing membership behavior.
/// </remarks>
public static class ReferenceCollectionFieldEmitter
{
    public static string Render(ReferenceCollectionFieldSpec spec)
    {
        var prefix = spec.SnapshotFieldConstant;
        var varName = spec.Construct;
        var snapshotRead = spec.Card == FieldCard.Seq
            ? "ReferenceSequenceFieldSnapshotting.ReadAlternatives"
            : "ReferenceCollectionFieldSnapshotting.ReadAlternatives";

        return Template
            .Replace("__ADDREFLOWERINGBLOCK__", spec.MoveKind is null
                ? LegacyAddRefLoweringBlock
                : PlacementAddRefLoweringBlock)
            .Replace("__MOVE_BLOCK__", spec.MoveKind is null ? string.Empty : "\n\n" + MoveBlock)
            .Replace("__PREFIX__", prefix)
            .Replace("__DECLARINGCLASS__", spec.DeclaringClass)
            .Replace("__FIELDNAME__", spec.FieldName)
            .Replace("__SIG__", spec.Sig)
            .Replace("__CARD__", spec.Card.ToString().ToLowerInvariant())
            .Replace("__NULLABLE_DIRECTIVE__", spec.MoveKind is null ? string.Empty : "#nullable enable\n")
            .Replace("__ADDKIND__", spec.AddRefKind)
            .Replace("__REMOVEKIND__", spec.RemoveRefKind)
            .Replace("__TARGETIFACE__", spec.TargetInterface)
            .Replace("__REFIFACE__", spec.RefInterface)
            .Replace("__ACCESSOR__", spec.AccessorPropertyName)
            .Replace("__VAR__", varName)
            .Replace("__SNAPSHOTFIELD__", "SnapshotFields." + prefix)
            .Replace("__READ__", snapshotRead)
            .Replace("__OPERATIONKINDSSUMMARY__", spec.MoveKind is null
                ? "Names the two"
                : "Names the generated ordered sequence")
            .Replace("__MOVE_STATUS__", spec.MoveKind is null
                ? "`move` deferred -- see ReferenceCollectionFieldEmitter.cs (SIL.Motif.Generator) remarks"
                : "`move` emitted with identity-relative placement")
            .Replace("__MOVE_KIND_BLOCK__", spec.MoveKind is null
                ? "\n    "
                : $"    public const string Move{spec.FieldName} = \"{spec.MoveKind}\";\n\n    ")
            .Replace("__MOVE_REGISTRATION__", spec.MoveKind is null
                ? string.Empty
                : $"        OperationKindRegistry.Register(Move{spec.FieldName});\n        OperationHandlerRegistry.Register(Move{spec.FieldName}, {prefix}MoveHandler.Instance);\n")
            .Replace("__REMOVE_PLACEMENT_CHECK__", spec.MoveKind is null
                ? "\n"
                : $"        if (operation.Placement is not null)\n            throw new InvalidOperationException($\"Operation '{{operation.OperationId.Value}}' of kind '{{{prefix}OperationKinds.RemoveRef{spec.FieldName}}}' cannot carry placement; use its move kind.\");\n\n")
            .Replace("__ADDREFCALL__", spec.MoveKind is null
                ? $"{prefix}AddRefLowering.Apply(cache, {varName}, memberId);"
                : $"{prefix}AddRefLowering.Apply(cache, {varName}, memberId, operation.Placement);");
    }

    private const string LegacyAddRefLoweringBlock = """
        /// <summary>Lowers <see cref="__PREFIX__OperationKinds.AddRef__FIELDNAME__"/>: resolves the
        /// referenced __REFIFACE__ and adds it to <c>__VAR__.__ACCESSOR__</c> (a no-op if already
        /// present).</summary>
        public static class __PREFIX__AddRefLowering
        {
            public static void Apply(LcmCache cache, __TARGETIFACE__ __VAR__, CanonicalId memberId) =>
                ReferenceCollectionFieldLowering.ApplyAddRef<__REFIFACE__>(
                    cache, __VAR__.__ACCESSOR__, memberId, __PREFIX__OperationKinds.AddRef__FIELDNAME__);
        }
        """;

    private const string PlacementAddRefLoweringBlock = """
        /// <summary>Lowers <see cref="__PREFIX__OperationKinds.AddRef__FIELDNAME__"/> by inserting the
        /// referenced member at its declared identity-relative placement.</summary>
        public static class __PREFIX__AddRefLowering
        {
            public static void Apply(LcmCache cache, __TARGETIFACE__ __VAR__, CanonicalId memberId,
                Placement? placement) =>
                ReferenceSequenceFieldLowering.ApplyAddRef<__REFIFACE__>(
                    cache, __VAR__.__ACCESSOR__, memberId, placement,
                    __PREFIX__OperationKinds.AddRef__FIELDNAME__);
        }
        """;

    private const string MoveBlock = """
        /// <summary>Lowers the ordered sequence move by resolving the member id within the target sequence.</summary>
        public static class __PREFIX__MoveLowering
        {
            public static void Apply(__TARGETIFACE__ __VAR__, CanonicalId memberId, Placement placement) =>
                ReferenceSequenceFieldLowering.ApplyMove(
                    __VAR__.__ACCESSOR__, memberId, placement, __PREFIX__OperationKinds.Move__FIELDNAME__);
        }

        /// <summary>Resolves, snapshots, lowers, and re-snapshots one
        /// <see cref="__PREFIX__OperationKinds.Move__FIELDNAME__"/> operation.</summary>
        internal sealed class __PREFIX__MoveHandler : IOperationHandler
        {
            internal static readonly __PREFIX__MoveHandler Instance = new();
            private __PREFIX__MoveHandler() { }

            public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
            {
                if (operation.Target is not { })
                    throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{__PREFIX__OperationKinds.Move__FIELDNAME__}' requires 'target'.");
                if (operation.After is not { } after)
                    throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{__PREFIX__OperationKinds.Move__FIELDNAME__}' requires 'after'.");
                if (operation.Placement is not { } placement)
                    throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{__PREFIX__OperationKinds.Move__FIELDNAME__}' requires 'placement'.");

                var memberId = __PREFIX__MemberPayload.Parse(after, __PREFIX__OperationKinds.Move__FIELDNAME__);
                var (id, __VAR__) = TargetResolution.Resolve<__TARGETIFACE__>(cache, operation, __PREFIX__OperationKinds.Move__FIELDNAME__);
                touchedTargets.Add(id);
                var before = __READ__(__VAR__.__ACCESSOR__);
                __PREFIX__MoveLowering.Apply(__VAR__, memberId, placement);
                var afterValue = __READ__(__VAR__.__ACCESSOR__);
                return new ExpectedEffect(id, __SNAPSHOTFIELD__, before, afterValue);
            }

            public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
            {
                var (id, __VAR__) = TargetResolution.Resolve<__TARGETIFACE__>(cache, operation, __PREFIX__OperationKinds.Move__FIELDNAME__);
                var current = __READ__(__VAR__.__ACCESSOR__);
                return new ExpectedEffect(id, __SNAPSHOTFIELD__, current, current);
            }
        }
        """;

    private const string Template = """
        // <auto-generated>
        //   Produced by SIL.Motif.Generator's `emit` command.
        //   Do NOT hand-edit. Rerun `dotnet run --project src/SIL.Motif.Generator -- emit` after a
        //   manifest or MasterLCModel.xml change, and check in the result.
        //
        //   Source: __DECLARINGCLASS__.__FIELDNAME__ (rel/__CARD__ __SIG__, addRef|removeRef emitted;
        //   __MOVE_STATUS__).
        // </auto-generated>
        __NULLABLE_DIRECTIVE__using System;
        using System.Collections.Generic;
        using System.Runtime.CompilerServices;
        using System.Text.Json;
        using SIL.Motif.Contract.Ids;
        using SIL.Motif.Contract.Model;
        using SIL.Motif.Contract.Parsing;
        using SIL.Motif.Model.Effects;
        using SIL.Motif.Model.Snapshot;
        using SIL.Motif.Runner.Snapshotting;
        using SIL.LCModel;

        namespace SIL.Motif.Runner.Operations;

        /// <summary>__OPERATIONKINDSSUMMARY__ <c>__DECLARINGCLASS__.__FIELDNAME__</c> operation kinds.</summary>
        public static class __PREFIX__OperationKinds
        {
            public const string AddRef__FIELDNAME__ = "__ADDKIND__";
            public const string RemoveRef__FIELDNAME__ = "__REMOVEKIND__";
        __MOVE_KIND_BLOCK__[ModuleInitializer]
            internal static void Register()
            {
                OperationKindRegistry.Register(AddRef__FIELDNAME__);
                OperationKindRegistry.Register(RemoveRef__FIELDNAME__);
                OperationHandlerRegistry.Register(AddRef__FIELDNAME__, __PREFIX__AddRefHandler.Instance);
                OperationHandlerRegistry.Register(RemoveRef__FIELDNAME__, __PREFIX__RemoveRefHandler.Instance);
        __MOVE_REGISTRATION__    }
        }

        /// <summary>The <c>after</c> payload for both <see cref="__PREFIX__OperationKinds.AddRef__FIELDNAME__"/>
        /// and <see cref="__PREFIX__OperationKinds.RemoveRef__FIELDNAME__"/>:
        /// <c>{ "member": "&lt;canonicalId&gt;" }</c>. Closed: any other property is rejected.</summary>
        public static class __PREFIX__MemberPayload
        {
            private static readonly string[] AllowedProperties = { "member" };

            public static CanonicalId Parse(JsonElement after, string kind)
            {
                ClosedPayloadParsing.RequireObject(after, kind);
                ClosedPayloadParsing.RejectUnknownProperties(after, AllowedProperties, kind);

                return ClosedPayloadParsing.GetRequiredCanonicalId(after, "member", kind);
            }
        }

        __ADDREFLOWERINGBLOCK__

        /// <summary>Lowers <see cref="__PREFIX__OperationKinds.RemoveRef__FIELDNAME__"/>: resolves the
        /// referenced __REFIFACE__ and removes it from <c>__VAR__.__ACCESSOR__</c> (a no-op if
        /// absent).</summary>
        public static class __PREFIX__RemoveRefLowering
        {
            public static void Apply(LcmCache cache, __TARGETIFACE__ __VAR__, CanonicalId memberId) =>
                ReferenceCollectionFieldLowering.ApplyRemoveRef<__REFIFACE__>(
                    cache, __VAR__.__ACCESSOR__, memberId, __PREFIX__OperationKinds.RemoveRef__FIELDNAME__);
        }

        /// <summary>Resolves, snapshots, lowers, and re-snapshots one
        /// <see cref="__PREFIX__OperationKinds.AddRef__FIELDNAME__"/> operation.</summary>
        internal sealed class __PREFIX__AddRefHandler : IOperationHandler
        {
            internal static readonly __PREFIX__AddRefHandler Instance = new();
            private __PREFIX__AddRefHandler() { }

            public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
            {
                if (operation.Target is not { })
                {
                    throw new InvalidOperationException(
                        $"Operation '{operation.OperationId.Value}' of kind '{__PREFIX__OperationKinds.AddRef__FIELDNAME__}' requires 'target'.");
                }

                if (operation.After is not { } after)
                {
                    throw new InvalidOperationException(
                        $"Operation '{operation.OperationId.Value}' of kind '{__PREFIX__OperationKinds.AddRef__FIELDNAME__}' requires 'after'.");
                }

                var memberId = __PREFIX__MemberPayload.Parse(after, __PREFIX__OperationKinds.AddRef__FIELDNAME__);

                var (id, __VAR__) = TargetResolution.Resolve<__TARGETIFACE__>(cache, operation, __PREFIX__OperationKinds.AddRef__FIELDNAME__);
                touchedTargets.Add(id);

                var before = __READ__(__VAR__.__ACCESSOR__);
                __ADDREFCALL__
                var afterValue = __READ__(__VAR__.__ACCESSOR__);

                return new ExpectedEffect(id, __SNAPSHOTFIELD__, before, afterValue);
            }

            public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
            {
                var (id, __VAR__) = TargetResolution.Resolve<__TARGETIFACE__>(cache, operation, __PREFIX__OperationKinds.AddRef__FIELDNAME__);
                var current = __READ__(__VAR__.__ACCESSOR__);
                return new ExpectedEffect(id, __SNAPSHOTFIELD__, current, current);
            }
        }

        /// <summary>The <see cref="__PREFIX__OperationKinds.RemoveRef__FIELDNAME__"/> counterpart to
        /// <see cref="__PREFIX__AddRefHandler"/>.</summary>
        internal sealed class __PREFIX__RemoveRefHandler : IOperationHandler
        {
            internal static readonly __PREFIX__RemoveRefHandler Instance = new();
            private __PREFIX__RemoveRefHandler() { }

            public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
            {
                if (operation.Target is not { })
                {
                    throw new InvalidOperationException(
                        $"Operation '{operation.OperationId.Value}' of kind '{__PREFIX__OperationKinds.RemoveRef__FIELDNAME__}' requires 'target'.");
                }

                if (operation.After is not { } after)
                {
                    throw new InvalidOperationException(
                        $"Operation '{operation.OperationId.Value}' of kind '{__PREFIX__OperationKinds.RemoveRef__FIELDNAME__}' requires 'after'.");
                }

                var memberId = __PREFIX__MemberPayload.Parse(after, __PREFIX__OperationKinds.RemoveRef__FIELDNAME__);
        __REMOVE_PLACEMENT_CHECK__        var (id, __VAR__) = TargetResolution.Resolve<__TARGETIFACE__>(cache, operation, __PREFIX__OperationKinds.RemoveRef__FIELDNAME__);
                touchedTargets.Add(id);

                var before = __READ__(__VAR__.__ACCESSOR__);
                __PREFIX__RemoveRefLowering.Apply(cache, __VAR__, memberId);
                var afterValue = __READ__(__VAR__.__ACCESSOR__);

                return new ExpectedEffect(id, __SNAPSHOTFIELD__, before, afterValue);
            }

            public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
            {
                var (id, __VAR__) = TargetResolution.Resolve<__TARGETIFACE__>(cache, operation, __PREFIX__OperationKinds.RemoveRef__FIELDNAME__);
                var current = __READ__(__VAR__.__ACCESSOR__);
                return new ExpectedEffect(id, __SNAPSHOTFIELD__, current, current);
            }
        }__MOVE_BLOCK__

        """;
}
