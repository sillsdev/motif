using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class DeclareDependenciesIndexTests
{
    /// <summary>The original algorithm: every step scans the whole staged list.</summary>
    private static OperationEnvelope[] Reference(JsonElement input, IReadOnlyList<OperationEnvelope> operations,
        IReadOnlyList<OperationEnvelope> staged)
    {
        var authoredReferences = new HashSet<Guid>();
        ProposalCommands.CollectIds(input, authoredReferences);
        var positions = new Dictionary<CanonicalId, int>();
        for (var index = 0; index < staged.Count; index++) positions[staged[index].OperationId] = index;
        return operations.Select(operation =>
        {
            var references = new HashSet<Guid>(authoredReferences);
            if (operation.Target is { } target) references.Add(target.ToGuid());
            if (operation.Placement?.After is { } left) references.Add(left.ToGuid());
            if (operation.Placement?.Before is { } right) references.Add(right.ToGuid());
            if (operation.After is { } after) ProposalCommands.CollectIds(after, references);
            var creators = staged.Where(prior => prior.EntityId is { } created && references.Contains(created.ToGuid()))
                .Select(prior => prior.OperationId).ToArray();
            var editors = staged.Where(prior => prior.Target is { } modified && references.Contains(modified.ToGuid()))
                .Select(prior => prior.OperationId).Except(creators).ToArray();
            var dependencies = operation.DependsOn.Select(dependency => dependency.OperationId)
                .Concat(creators)
                .Concat(ProposalCommands.WithoutImplied(creators.Concat(editors).ToArray(), editors, staged, positions))
                .Distinct().Select(id => new OperationDependency(id)).ToArray();
            return new OperationEnvelope(operation.OperationId, operation.Kind, operation.EntityId,
                operation.Target, operation.After, operation.Placement, dependencies, rationale: operation.Rationale);
        }).ToArray();
    }

    [Fact]
    public void AnIndexKeptAcrossStepsDeclaresTheDependenciesAFullRebuildDeclares()
    {
        var pool = Enumerable.Range(0, 8).Select(_ => CanonicalId.Mint()).ToArray();
        var random = new Random(20261021);
        CanonicalId Pick() => pool[random.Next(pool.Length)];
        var retained = Enumerable.Range(0, 5).Select(_ => new OperationEnvelope(CanonicalId.Mint(),
            LexicalSenseOperationKinds.SetGloss, target: Pick(),
            after: JsonSerializer.SerializeToElement(new { ws = "en", text = "t" }))).ToList();
        var staged = new List<OperationEnvelope>(retained);
        var index = new ProposalCommands.StagedDependencyIndex(retained);

        var withDependencies = 0;
        for (var step = 0; step < 60; step++)
        {
            var input = JsonSerializer.SerializeToElement(new { target = Pick().Value, other = Pick().Value });
            var operations = Enumerable.Range(0, random.Next(1, 4)).Select(_ =>
            {
                var created = random.Next(3) == 0 ? Pick() : (CanonicalId?)null;
                var priorId = staged.Count > 0 && random.Next(4) == 0
                    ? new[] { new OperationDependency(staged[random.Next(staged.Count)].OperationId) }
                    : [];
                return new OperationEnvelope(CanonicalId.Mint(), LexicalSenseOperationKinds.SetGloss,
                    entityId: created, target: Pick(),
                    after: JsonSerializer.SerializeToElement(new { ws = "en", text = "t", ref1 = Pick().Value }),
                    placement: random.Next(3) == 0 ? new Placement(Pick(), Pick()) : null, dependsOn: priorId);
            }).ToArray();

            var expected = Reference(input, operations, staged);
            var actual = ProposalCommands.DeclareDependencies(input, operations, index);

            Assert.Equal(expected.Select(Describe), actual.Select(Describe));
            withDependencies += actual.Count(operation => operation.DependsOn.Count > 0);
            staged.AddRange(actual);
            index.Add(actual);
        }
        Assert.True(withDependencies > 60, $"Only {withDependencies} operations declared dependencies.");
    }

    private static string Describe(OperationEnvelope operation) => operation.OperationId.Value + ":" +
        string.Join(",", operation.DependsOn.Select(dependency => dependency.OperationId.Value));
}
