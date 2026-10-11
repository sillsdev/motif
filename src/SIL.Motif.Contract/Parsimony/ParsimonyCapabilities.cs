using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SIL.Motif.Contract.Parsimony;

/// <summary>The closed names a Measure or a fixed view may require: evidence needs, fact sections and update capabilities. Wire spellings never change.</summary>
public static class ParsimonyCapabilities
{
    /// <summary>The wire spelling <c>adhoc-signature</c>.</summary>
    public const string AdhocSignature = "adhoc-signature";

    /// <summary>The wire spelling <c>adhoc_prohibitions</c>.</summary>
    public const string AdhocProhibitions = "adhoc_prohibitions";

    /// <summary>The wire spelling <c>affix-slot-membership</c>.</summary>
    public const string AffixSlotMembership = "affix-slot-membership";

    /// <summary>The wire spelling <c>affix_processes</c>.</summary>
    public const string AffixProcesses = "affix_processes";

    /// <summary>The wire spelling <c>allomorph-context</c>.</summary>
    public const string AllomorphContext = "allomorph-context";

    /// <summary>The wire spelling <c>allomorph-environments</c>.</summary>
    public const string AllomorphEnvironments = "allomorph-environments";

    /// <summary>The wire spelling <c>allomorph-forms</c>.</summary>
    public const string AllomorphForms = "allomorph-forms";

    /// <summary>The wire spelling <c>allomorphs</c>.</summary>
    public const string Allomorphs = "allomorphs";

    /// <summary>The wire spelling <c>approved-signatures</c>.</summary>
    public const string ApprovedSignatures = "approved-signatures";

    /// <summary>The wire spelling <c>author-affix-slot</c>.</summary>
    public const string AuthorAffixSlot = "author-affix-slot";

    /// <summary>The wire spelling <c>author-affix-template</c>.</summary>
    public const string AuthorAffixTemplate = "author-affix-template";

    /// <summary>The wire spelling <c>author-environment</c>.</summary>
    public const string AuthorEnvironment = "author-environment";

    /// <summary>The wire spelling <c>author-natural-class</c>.</summary>
    public const string AuthorNaturalClass = "author-natural-class";

    /// <summary>The wire spelling <c>author-phonological-rule</c>.</summary>
    public const string AuthorPhonologicalRule = "author-phonological-rule";

    /// <summary>The wire spelling <c>case-outcomes</c>.</summary>
    public const string CaseOutcomes = "case-outcomes";

    /// <summary>The wire spelling <c>categories</c>.</summary>
    public const string Categories = "categories";

    /// <summary>The wire spelling <c>class-extension</c>.</summary>
    public const string ClassExtension = "class-extension";

    /// <summary>The wire spelling <c>compiled_mappings</c>.</summary>
    public const string CompiledMappings = "compiled_mappings";

    /// <summary>The wire spelling <c>compound_rules</c>.</summary>
    public const string CompoundRules = "compound_rules";

    /// <summary>The wire spelling <c>context-universe</c>.</summary>
    public const string ContextUniverse = "context-universe";

    /// <summary>The wire spelling <c>disapproved-opinions</c>.</summary>
    public const string DisapprovedOpinions = "disapproved-opinions";

    /// <summary>The wire spelling <c>edit-adhoc-disabled</c>.</summary>
    public const string EditAdhocDisabled = "edit-adhoc-disabled";

    /// <summary>The wire spelling <c>edit-affix-slot</c>.</summary>
    public const string EditAffixSlot = "edit-affix-slot";

    /// <summary>The wire spelling <c>edit-affix-template</c>.</summary>
    public const string EditAffixTemplate = "edit-affix-template";

    /// <summary>The wire spelling <c>edit-allomorph-condition</c>.</summary>
    public const string EditAllomorphCondition = "edit-allomorph-condition";

    /// <summary>The wire spelling <c>edit-inflectional-affix</c>.</summary>
    public const string EditInflectionalAffix = "edit-inflectional-affix";

    /// <summary>The wire spelling <c>edit-natural-class</c>.</summary>
    public const string EditNaturalClass = "edit-natural-class";

    /// <summary>The wire spelling <c>effective-allomorph-order</c>.</summary>
    public const string EffectiveAllomorphOrder = "effective-allomorph-order";

    /// <summary>The wire spelling <c>effective-context-matches</c>.</summary>
    public const string EffectiveContextMatches = "effective-context-matches";

    /// <summary>The wire spelling <c>effective-objects</c>.</summary>
    public const string EffectiveObjects = "effective-objects";

    /// <summary>The wire spelling <c>entries</c>.</summary>
    public const string Entries = "entries";

    /// <summary>The wire spelling <c>entry-identities</c>.</summary>
    public const string EntryIdentities = "entry-identities";

    /// <summary>The wire spelling <c>environments</c>.</summary>
    public const string Environments = "environments";

    /// <summary>The wire spelling <c>evidence</c>.</summary>
    public const string Evidence = "evidence";

    /// <summary>The wire spelling <c>feature-assignments</c>.</summary>
    public const string FeatureAssignments = "feature-assignments";

    /// <summary>The wire spelling <c>feature-contributions</c>.</summary>
    public const string FeatureContributions = "feature-contributions";

    /// <summary>The wire spelling <c>features</c>.</summary>
    public const string Features = "features";

    /// <summary>The wire spelling <c>grouped-adhoc-load</c>.</summary>
    public const string GroupedAdhocLoad = "grouped-adhoc-load";

    /// <summary>The wire spelling <c>human-judgments</c>.</summary>
    public const string HumanJudgments = "human-judgments";

    /// <summary>The wire spelling <c>incoming-references</c>.</summary>
    public const string IncomingReferences = "incoming-references";

    /// <summary>The wire spelling <c>load-facts</c>.</summary>
    public const string LoadFacts = "load-facts";

    /// <summary>The wire spelling <c>load_accounting</c>.</summary>
    public const string LoadAccounting = "load_accounting";

    /// <summary>The wire spelling <c>loaded-zero-realizations</c>.</summary>
    public const string LoadedZeroRealizations = "loaded-zero-realizations";

    /// <summary>The wire spelling <c>morph-type</c>.</summary>
    public const string MorphType = "morph-type";

    /// <summary>The wire spelling <c>msas</c>.</summary>
    public const string Msas = "msas";

    /// <summary>The wire spelling <c>observed-context</c>.</summary>
    public const string ObservedContext = "observed-context";

    /// <summary>The wire spelling <c>order-allomorphs</c>.</summary>
    public const string OrderAllomorphs = "order-allomorphs";

    /// <summary>The wire spelling <c>ordered-adhoc-targets</c>.</summary>
    public const string OrderedAdhocTargets = "ordered-adhoc-targets";

    /// <summary>The wire spelling <c>paired-verification</c>.</summary>
    public const string PairedVerification = "paired-verification";

    /// <summary>The wire spelling <c>parser-cases</c>.</summary>
    public const string ParserCases = "parser-cases";

    /// <summary>The wire spelling <c>parser-overlay</c>.</summary>
    public const string ParserOverlay = "parser-overlay";

    /// <summary>The wire spelling <c>parser-signatures</c>.</summary>
    public const string ParserSignatures = "parser-signatures";

    /// <summary>The wire spelling <c>patterns</c>.</summary>
    public const string Patterns = "patterns";

    /// <summary>The wire spelling <c>phoneme-tokenization</c>.</summary>
    public const string PhonemeTokenization = "phoneme-tokenization";

    /// <summary>The wire spelling <c>phonology</c>.</summary>
    public const string Phonology = "phonology";

    /// <summary>The wire spelling <c>relink-natural-class</c>.</summary>
    public const string RelinkNaturalClass = "relink-natural-class";

    /// <summary>The wire spelling <c>report-findings</c>.</summary>
    public const string ReportFindings = "report-findings";

    /// <summary>The wire spelling <c>retire-allomorph-intent</c>.</summary>
    public const string RetireAllomorphIntent = "retire-allomorph-intent";

    /// <summary>The wire spelling <c>retire-redundant-zero-affix-intent</c>.</summary>
    public const string RetireRedundantZeroAffixIntent = "retire-redundant-zero-affix-intent";

    /// <summary>The wire spelling <c>retire-statement</c>.</summary>
    public const string RetireStatement = "retire-statement";

    /// <summary>The wire spelling <c>reviewed-negative-expectations</c>.</summary>
    public const string ReviewedNegativeExpectations = "reviewed-negative-expectations";

    /// <summary>The wire spelling <c>rooted-morph</c>.</summary>
    public const string RootedMorph = "rooted-morph";

    /// <summary>The wire spelling <c>slot-users</c>.</summary>
    public const string SlotUsers = "slot-users";

    /// <summary>The wire spelling <c>statement-load-facts</c>.</summary>
    public const string StatementLoadFacts = "statement-load-facts";

    /// <summary>The wire spelling <c>statement-reference</c>.</summary>
    public const string StatementReference = "statement-reference";

    /// <summary>The wire spelling <c>stats</c>.</summary>
    public const string Stats = "stats";

    /// <summary>The wire spelling <c>template-embeddings</c>.</summary>
    public const string TemplateEmbeddings = "template-embeddings";

    /// <summary>The wire spelling <c>templates</c>.</summary>
    public const string Templates = "templates";

    /// <summary>The wire spelling <c>zero-morphology</c>.</summary>
    public const string ZeroMorphology = "zero-morphology";

    /// <summary>Every capability name, for validating a catalog entry.</summary>
    public static IReadOnlySet<string> All { get; } = typeof(ParsimonyCapabilities)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(field => field.IsLiteral)
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);
}
