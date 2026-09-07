using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;
using SIL.LCModel;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// Lowers one <see cref="AuthorFeatureValueIntent"/> into the two closed-schema Layer-0 operations
/// that realize it -- the grammar counterpart to <see cref="AuthorLexemeFormComposer"/>'s multi-operation
/// shape. One authored construct becomes <c>grammar/fsFeatStruc/createFeatureSpecs</c>, then
/// <c>grammar/fsFeatureSpecification/setFeature</c> targeting the entity the first operation creates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Composer, not primitive.</b> <see cref="Build"/> takes <c>(cache, intent)</c> because it must
/// resolve both references and refuse before authoring anything -- the same split
/// <see cref="AuthorFeatureStructureComposer"/> follows. Two refusals only the composer can make,
/// because the raw <c>create</c> operation (ADR 0022 §4) enforces neither:
/// </para>
/// <list type="bullet">
/// <item><description><b>Duplicate.</b> Two specifications for the same feature on one structure is a
/// contradiction Motif chooses not to author, even though LibLCM's own <c>FeatureSpecsOC.Add</c> would
/// accept it silently (<c>FsFeatStruc.GetOrCreateValue</c> in <c>sillsdev/liblcm</c> is the one caller
/// that already avoids this, for the closed-feature case only).</description></item>
/// <item><description><b>Type membership.</b> When <c>FeatStruc.TypeRA</c> is set, its
/// <c>FsFeatStrucType.FeaturesRS</c> is the model's own declared feature list for structures of that
/// type; a feature outside it is refused. When <c>TypeRA</c> is null (every structure
/// <see cref="AuthorFeatureStructureComposer"/> creates today), nothing constrains which feature may be
/// named -- LibLCM's own <c>ReferenceTargetCandidates</c> falls back to the whole
/// <c>LangProject.MsFeatureSystemOA.FeaturesOC</c> in exactly that case.</description></item>
/// </list>
/// <para>
/// <b>Ordering is declared, not positional.</b> The <c>setFeature</c> operation carries an explicit
/// <see cref="OperationDependency"/> on the create operation's id, because its target is the entity the
/// create operation's <c>entityId</c> proposes -- the same reasoning
/// <see cref="AuthorLexemeFormComposer"/>'s remarks give for its own <c>setIsAbstract</c> step.
/// </para>
/// <para>
/// <b>Scope.</b> This construct authors which feature a specification is for, not the chosen value:
/// <c>FsClosedValue.Value</c> has no generated <c>set</c>/<c>clear</c> kind yet (see
/// <see cref="FsFeatStrucFeatureSpecsOperationKinds"/>), so choosing e.g. Number=Singular over
/// Number=Plural is not yet authorable through Motif.
/// </para>
/// </remarks>
public static class AuthorFeatureValueComposer
{
    private const string ConstructName = "AuthorFeatureValue";

    /// <param name="cache">The project to resolve <paramref name="intent"/>'s references against.</param>
    /// <param name="intent">The authored construct.</param>
    /// <param name="mintId">
    /// Overrides id minting; defaults to <see cref="CanonicalId.Mint(string)"/>. Tests supply a
    /// deterministic source to pin the composer's lowering as reproducible.
    /// </param>
    public static IReadOnlyList<OperationEnvelope> Build(
        LcmCache cache, AuthorFeatureValueIntent intent, Func<CanonicalId>? mintId = null)
    {
        if (cache is null) throw new ArgumentNullException(nameof(cache));
        if (intent is null) throw new ArgumentNullException(nameof(intent));

        var mint = mintId ?? (() => CanonicalId.Mint());

        var featStruc = ReferenceFieldLowering.Resolve<IFsFeatStruc>(cache, intent.FeatStruc, ConstructName);
        var feature = ReferenceFieldLowering.Resolve<IFsFeatDefn>(cache, intent.Feature, ConstructName);

        if (featStruc.FeatureSpecsOC.Any(spec => spec.FeatureRA is { } existing && existing.Guid == feature.Guid))
        {
            throw new InvalidOperationException(
                $"'{ConstructName}': FsFeatStruc '{intent.FeatStruc.Value}' already has a feature " +
                $"specification for feature '{intent.Feature.Value}'; this construct adds one, it does " +
                "not replace one.");
        }

        if (featStruc.TypeRA is { } type && !type.FeaturesRS.Contains(feature))
        {
            throw new InvalidOperationException(
                $"'{ConstructName}': feature '{intent.Feature.Value}' does not belong to FsFeatStruc " +
                $"'{intent.FeatStruc.Value}''s type ('{type.Guid}').");
        }

        var createOpId = mint();
        var newSpecId = mint();

        return new[]
        {
            new OperationEnvelope(
                operationId: createOpId,
                kind: FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
                entityId: newSpecId,
                target: intent.FeatStruc,
                after: EmptyAfter(),
                rationale: Rationale),
            new OperationEnvelope(
                operationId: mint(),
                kind: FsFeatureSpecificationFeatureOperationKinds.SetFeature,
                target: newSpecId,
                after: BuildSetFeatureAfter(intent.Feature),
                dependsOn: new[] { new OperationDependency(createOpId) },
                rationale: Rationale),
        };
    }

    private const string Rationale = "Authored by the AuthorFeatureValue composer.";

    private static JsonElement EmptyAfter()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static JsonElement BuildSetFeatureAfter(CanonicalId feature)
    {
        var json = JsonSerializer.Serialize(new { @ref = feature.Value });
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
