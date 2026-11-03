using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Snapshotting;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the operation that sets how many times an iteration context repeats its member.</summary>
public static class PhIterationContextBoundsOperationKinds
{
    /// <summary>Writes the minimum and maximum repeat counts together, so they are never half set.</summary>
    public const string SetBounds = "grammar/phIterationContext/setBounds";

    /// <summary>The snapshot field that holds both counts.</summary>
    public const string BoundsField = "grammar/phIterationContext/bounds";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(SetBounds);
        OperationHandlerRegistry.Register(SetBounds, new PhIterationContextBoundsHandler());
        OperationSlotRegistry.RegisterScalar(SetBounds, BoundsField);
    }
#pragma warning restore CA2255
}

/// <summary>Reads and writes the repeat counts of one iteration context.</summary>
internal sealed class PhIterationContextBoundsHandler : IOperationHandler
{
    private const string Kind = PhIterationContextBoundsOperationKinds.SetBounds;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        var (minimum, maximum) = Parse(operation);
        var (id, context) = TargetResolution.Resolve<IPhIterationContext>(cache, operation, Kind);
        touchedTargets.Add(id);
        var before = Read(context);
        context.Minimum = minimum;
        context.Maximum = maximum;
        return new ExpectedEffect(id, PhIterationContextBoundsOperationKinds.BoundsField, before, Read(context));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, context) = TargetResolution.Resolve<IPhIterationContext>(cache, operation, Kind);
        var current = Read(context);
        return new ExpectedEffect(id, PhIterationContextBoundsOperationKinds.BoundsField, current, current);
    }

    internal static IReadOnlyDictionary<string, string> Read(IPhIterationContext context) =>
        new Dictionary<string, string>
        {
            ["minimum"] = context.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maximum"] = context.Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

    private static (int Minimum, int Maximum) Parse(OperationEnvelope operation)
    {
        if (operation.After is not { } after)
            throw new InvalidOperationException($"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'.");
        ClosedPayloadParsing.RequireObject(after, Kind);
        ClosedPayloadParsing.RejectUnknownProperties(after, new[] { "minimum", "maximum" }, Kind);
        var minimum = ClosedPayloadParsing.GetRequiredInteger(after, "minimum", Kind);
        var maximum = ClosedPayloadParsing.GetRequiredInteger(after, "maximum", Kind);
        if (minimum < 0)
            throw new ContractParseException($"'{Kind}': 'minimum' cannot be negative.");
        if (maximum != -1 && (maximum < 1 || maximum < minimum))
            throw new ContractParseException($"'{Kind}': 'maximum' must be -1 (unbounded) or at least 1 and at least 'minimum'.");
        return (minimum, maximum);
    }
}
