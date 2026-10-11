namespace SIL.Motif.Host.Parsimony;

public sealed partial class ParsimonyQuerySession
{
    internal MeasureContext ReadMeasureContext(string bundleId) =>
        new(bundleId, MeasureDigestContext.From(_inputs), DescribeObject);

    internal DuplicateProhibitionInput ReadDuplicateProhibitionInput()
    {
        var prohibitions = ReadAdhocProhibitions();
        return new DuplicateProhibitionInput(prohibitions, GroupedAdhocFactsAvailable);
    }

    internal UnslottedAffixInput ReadUnslottedAffixInput()
    {
        var rows = ReadUnslottedAffixes();
        return new UnslottedAffixInput(rows, CountLoadedInflectionalAffixes());
    }

    internal StatementUsageInput ReadStatementUsageInput()
    {
        var statements = ReadStatementUsage();
        var referrers = new Dictionary<(string StatementKind, string StatementGuid),
            IReadOnlyList<(string ReferrerKind, string ReferrerGuid, string ParserEffect)>>();
        foreach (var item in statements.Where(item => MeasureRunner.IsUnusedStatement(item)))
            referrers[(item.StatementKind, item.StatementGuid)] =
                ReadStatementReferrers(item.StatementKind, item.StatementGuid);
        return new StatementUsageInput(statements, referrers);
    }

    internal AdhocSlotOrderInput ReadAdhocSlotOrderInput()
    {
        var rules = ReadAdhocProhibitions();
        var facts = ReadTemplateSequenceFacts();
        var compiledMorphRuleMsas = ReadCompiledMorphRuleMsas();
        var loaderReasons = ReadAdhocProhibitionLoaderReasons();
        return new AdhocSlotOrderInput(rules, facts, compiledMorphRuleMsas, loaderReasons,
            GroupedAdhocFactsAvailable);
    }
}
