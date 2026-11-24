using System.Globalization;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;

namespace SIL.Motif.Commands.Records;

public sealed partial class RecordChangeSetLowerer
{
    private sealed partial class Run
    {
        private const int InputLimit = 16;
        private const int OutputLimit = 256;
        private readonly HashSet<int> _affixDone = [];
        private readonly List<(int Index, RecordLoweringRefusal Refusal)> _siblingRefusals = [];
        private readonly Dictionary<CanonicalId, int> _attributed = [];

        internal LcmCache Cache => cache;

        internal IReadOnlyList<RecordChange> Changes { get; init; } = [];

        private void Add(OperationEnvelope operation, int changeIndex)
        {
            _pending.Add(operation);
            _attributed[operation.OperationId] = changeIndex;
        }

        private void LowerAffixFormula(RecordChange change, string type, string key)
        {
            var binding = schema.FieldBinding(type, key)!.Value;
            var index = IndexOf(change);
            if (binding.DeclaringClass != "MoAffixProcess" || binding.Field is not ("Input" or "Output") || index < 0)
                throw Refuse(RefusalCodes.RuleNotationReadOnly, change,
                    "This field is shown as rule-formula text, which an import does not write yet; edit it in the project.");
            if (_affixDone.Contains(index))
                return;
            var group = Enumerable.Range(0, Changes.Count).Where(i => Changes[i].ObjectGuid == change.ObjectGuid &&
                Changes[i].ObjectType == type && Changes[i].Kind is RecordChangeKind.Set or RecordChangeKind.Clear &&
                schema.FieldBinding(type, Changes[i].Field) is { DeclaringClass: "MoAffixProcess", Field: "Input" or "Output" }).ToList();
            foreach (var member in group)
                _affixDone.Add(member);
            try
            {
                LowerAffixGroup(change, group.Select(i => (Index: i, Change: Changes[i])).ToList());
            }
            catch (Stop stop)
            {
                foreach (var member in group.Where(i => i != index))
                {
                    var other = Changes[member];
                    _siblingRefusals.Add((member, new(stop.Refusal.Code, other.ObjectType ?? "", other.Field, other.ObjectGuid, stop.Refusal.Reason)));
                }
                throw;
            }
        }

        private int IndexOf(RecordChange change)
        {
            for (var i = 0; i < Changes.Count; i++)
                if (ReferenceEquals(Changes[i], change))
                    return i;
            return -1;
        }

        private void LowerAffixGroup(RecordChange first, List<(int Index, RecordChange Change)> group)
        {
            var process = (IMoAffixProcess)cache.ServiceLocator.GetObject(first.ObjectGuid!.Value);
            var processId = CanonicalId.FromGuid(process.Guid);
            (int Index, RecordChange Change)? inChange = null, outChange = null;
            foreach (var item in group)
            {
                var field = schema.FieldBinding(item.Change.ObjectType!, item.Change.Field)!.Value.Field;
                if (field == "Input") inChange = item;
                else outChange = item;
                var ruleField = field == "Input" ? RuleField.AffixInput : RuleField.AffixOutput;
                string current;
                try { current = RuleNotation.Render(process, ruleField, cache); }
                catch (RuleNotationException exception) { throw Refuse(exception.Code, item.Change, exception.Message); }
                if (!string.Equals(current, item.Change.Old ?? "", StringComparison.Ordinal))
                    throw Refuse(RefusalCodes.RecordImportValueStale, item.Change, "The project's value is no longer the one the file started from.");
            }
            var oldInputs = process.InputOS.Select(item => item.Guid).ToList();
            var newInputs = oldInputs;
            RuleText? inputText = null;
            if (inChange is { } inputItem)
            {
                inputText = Parse(inputItem.Change, RuleField.AffixInput, null);
                if (inputText.Nodes.Count > InputLimit)
                    throw Refuse(RefusalCodes.RuleNotationLimitExceeded, inputItem.Change, $"An affix process has at most {InputLimit} input columns.");
                newInputs = Enumerable.Range(0, inputText.Nodes.Count).Select(_ => Guid.Empty).ToList();
            }
            RuleText? outputText = null;
            if (outChange is { } outputItem)
            {
                outputText = Parse(outputItem.Change, RuleField.AffixOutput, newInputs.Count);
                if (outputText.Nodes.Count > OutputLimit)
                    throw Refuse(RefusalCodes.RuleNotationLimitExceeded, outputItem.Change, $"An affix process has at most {OutputLimit} output mappings.");
            }

            var classes = new ClassResolver(this, process);
            var removedInputs = new List<Guid>();
            var replacedBy = new Dictionary<Guid, Guid>();
            if (inChange is { } inputChange)
            {
                var role = $"{inputChange.Index}:{inputChange.Change.Kind}";
                var oldTexts = Elements(inputChange.Change, () => RuleNotation.RenderElements(process, RuleField.AffixInput, cache));
                var newTexts = Elements(inputChange.Change, () => RuleNotation.RenderElements(inputText!, cache));
                var matches = Match(oldTexts, newTexts);
                var simulated = oldInputs.ToList();
                for (var n = 0; n < newTexts.Count; n++)
                {
                    if (matches.TryGetValue(n, out var kept))
                    {
                        newInputs[n] = oldInputs[kept];
                        continue;
                    }
                    var entity = Id($"{role}:input:{n}");
                    newInputs[n] = entity.ToGuid();
                    var node = inputText!.Nodes[n];
                    var created = Placed(simulated, n == 0 ? null : newInputs[n - 1], entity.ToGuid());
                    var className = node switch
                    {
                        RuleGap => "PhVariable",
                        RuleGroup => "PhSequenceContext",
                        RuleClass => "PhSimpleContextNC",
                        RuleUnit unit => unit.Unit is IPhPhoneme ? "PhSimpleContextSeg" : "PhSimpleContextBdry",
                        _ => throw Refuse(RefusalCodes.RuleNotationContextNotAllowed, inputChange.Change, "An input column is a gap, a unit, a class or a group."),
                    };
                    Add(Operation(Id($"{role}:create:{n}"), MoAffixProcessInputOperationKinds.Create, processId, new { @class = className },
                        created, entity), inputChange.Index);
                    FillInput(inputChange, role, n, entity, node, classes);
                }
                for (var i = 0; i < oldInputs.Count; i++)
                    if (!matches.ContainsValue(i))
                        removedInputs.Add(oldInputs[i]);
                foreach (var (gone, created) in Replacements(matches, oldInputs.Count, newTexts.Count))
                    replacedBy[oldInputs[gone]] = newInputs[created];
            }

            if (outChange is { } outputChange)
                LowerOutput(process, processId, outputChange, outputText!, newInputs, classes);
            else if (inChange is { } retargeting)
                foreach (var output in process.OutputOS)
                {
                    var pointed = output switch { IMoCopyFromInput copy => copy.ContentRA, IMoModifyFromInput modify => modify.ContentRA, _ => null };
                    if (pointed is null || newInputs.Contains(pointed.Guid))
                        continue;
                    if (!replacedBy.TryGetValue(pointed.Guid, out var successor))
                        throw Refuse(RefusalCodes.RuleNotationDanglingIndex, retargeting.Change,
                            "An output element still points at an input column this change removes; change the output in the same file.");
                    Add(Operation(Id($"{retargeting.Index}:{retargeting.Change.Kind}:retarget:{output.Guid:N}"),
                        output is IMoCopyFromInput ? MoCopyFromInputContentOperationKinds.SetContent : MoModifyFromInputContentOperationKinds.SetContent,
                        CanonicalId.FromGuid(output.Guid), new { @ref = CanonicalId.FromGuid(successor).Value }), retargeting.Index);
                }

            if (inChange is { } deleter)
                foreach (var gone in removedInputs)
                    Add(Operation(Id($"{deleter.Index}:{deleter.Change.Kind}:delete:{gone:N}"), AffixProcessMemberDeleteOperationKinds.DeleteInput,
                        CanonicalId.FromGuid(gone), new { }), deleter.Index);
        }

        private RuleText Parse(RecordChange change, RuleField field, int? columns)
        {
            var text = change.Kind == RecordChangeKind.Clear ? "" : change.New ?? "";
            try
            {
                var parsed = RuleNotation.Parse(text, field, cache, columns);
                if (parsed.Nodes.Count == 0)
                    throw Refuse(RuleNotation.Syntax, change, $"An affix process needs at least one {(field == RuleField.AffixInput ? "input column" : "output mapping")}.");
                return parsed;
            }
            catch (RuleNotationException exception) { throw Refuse(exception.Code, change, exception.Message); }
            catch (FeatureStructureNotationException exception) { throw Refuse(exception.Code, change, exception.Message); }
        }

        private IReadOnlyList<string> Elements(RecordChange change, Func<IReadOnlyList<string>> read)
        {
            try { return read(); }
            catch (RuleNotationException exception) { throw Refuse(exception.Code, change, exception.Message); }
            catch (FeatureStructureNotationException exception) { throw Refuse(exception.Code, change, exception.Message); }
        }

        /// <summary>Pairs new positions with old ones by longest common subsequence, taking the leftmost match on a tie.</summary>
        internal static Dictionary<int, int> Match(IReadOnlyList<string> old, IReadOnlyList<string> wanted)
        {
            var table = new int[old.Count + 1, wanted.Count + 1];
            for (var i = old.Count - 1; i >= 0; i--)
                for (var j = wanted.Count - 1; j >= 0; j--)
                    table[i, j] = old[i] == wanted[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            var pairs = new Dictionary<int, int>();
            for (int i = 0, j = 0; i < old.Count && j < wanted.Count;)
            {
                if (old[i] == wanted[j] && table[i, j] == table[i + 1, j + 1] + 1) pairs[j++] = i++;
                else if (table[i + 1, j] >= table[i, j + 1]) i++;
                else j++;
            }
            return pairs;
        }

        /// <summary>Within each gap between matched elements, the removed old elements and the new ones taken in order: the i-th removed is replaced by the i-th new.</summary>
        private static IEnumerable<(int Old, int New)> Replacements(Dictionary<int, int> matches, int oldCount, int newCount)
        {
            var anchors = matches.OrderBy(pair => pair.Key).Select(pair => (New: pair.Key, Old: pair.Value))
                .Append((New: newCount, Old: oldCount)).ToList();
            int previousNew = -1, previousOld = -1;
            foreach (var (matchedNew, matchedOld) in anchors)
            {
                var removed = Enumerable.Range(previousOld + 1, matchedOld - previousOld - 1).ToList();
                var added = Enumerable.Range(previousNew + 1, matchedNew - previousNew - 1).ToList();
                for (var k = 0; k < Math.Min(removed.Count, added.Count); k++)
                    yield return (removed[k], added[k]);
                previousNew = matchedNew;
                previousOld = matchedOld;
            }
        }

        /// <summary>The placement that puts a new member right after its predecessor in the list as it stands, which then gains the member.</summary>
        private static Placement? Placed(List<Guid> simulated, Guid? predecessor, Guid entity)
        {
            var at = predecessor is { } before ? simulated.IndexOf(before) + 1 : 0;
            var placement = simulated.Count == 0 ? null : new Placement(predecessor is { } p ? CanonicalId.FromGuid(p) : null,
                at < simulated.Count ? CanonicalId.FromGuid(simulated[at]) : null);
            simulated.Insert(at, entity);
            return placement;
        }

        private void FillInput(( int Index, RecordChange Change) item, string role, int n, CanonicalId entity, RuleNode node, ClassResolver classes)
        {
            switch (node)
            {
                case RuleUnit unit:
                    SetUnit(item, $"{role}:unit:{n}", entity, unit);
                    break;
                case RuleClass naturalClass:
                    Add(Operation(Id($"{role}:class:{n}"), PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure, entity,
                        new { @ref = classes.Resolve(item, naturalClass, role + ":in" + n).Value }), item.Index);
                    break;
                case RuleGroup group:
                    var data = CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.Guid);
                    CanonicalId? previous = null;
                    for (var k = 0; k < group.Members.Count; k++)
                    {
                        var member = Id($"{role}:member:{n}:{k}");
                        var memberRole = $"{role}:m{n}:{k}";
                        if (group.Members[k] is RuleUnit memberUnit)
                        {
                            Add(Operation(Id(memberRole + ":create"), PhPhonDataContextsOperationKinds.Create, data,
                                new { @class = memberUnit.Unit is IPhPhoneme ? "PhSimpleContextSeg" : "PhSimpleContextBdry" }, entity: member), item.Index);
                            SetUnit(item, memberRole, member, memberUnit);
                        }
                        else
                        {
                            Add(Operation(Id(memberRole + ":create"), PhPhonDataContextsOperationKinds.Create, data,
                                new { @class = "PhSimpleContextNC" }, entity: member), item.Index);
                            Add(Operation(Id(memberRole + ":class"), PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure, member,
                                new { @ref = classes.Resolve(item, (RuleClass)group.Members[k], memberRole).Value }), item.Index);
                        }
                        Add(Operation(Id(memberRole + ":add"), PhSequenceContextMembersOperationKinds.AddRefMembers, entity,
                            new { member = member.Value }, previous is { } prior ? new Placement(prior, null) : null), item.Index);
                        previous = member;
                    }
                    break;
            }
        }

        private void SetUnit((int Index, RecordChange Change) item, string role, CanonicalId context, RuleUnit unit)
        {
            var kind = unit.Unit is IPhPhoneme ? PhSimpleContextSegFeatureStructureOperationKinds.SetFeatureStructure
                : PhSimpleContextBdryFeatureStructureOperationKinds.SetFeatureStructure;
            Add(Operation(Id(role + ":unit"), kind, context, new { @ref = CanonicalId.FromGuid(unit.Unit.Guid).Value }), item.Index);
        }

        private void LowerOutput(IMoAffixProcess process, CanonicalId processId, (int Index, RecordChange Change) item, RuleText text,
            List<Guid> inputs, ClassResolver classes)
        {
            var role = $"{item.Index}:{item.Change.Kind}";
            var oldOutputs = process.OutputOS.ToList();
            var oldTexts = Elements(item.Change, () => RuleNotation.RenderElements(process, RuleField.AffixOutput, cache));
            var newTexts = Elements(item.Change, () => RuleNotation.RenderElements(text, cache));
            var matches = Match(oldTexts, newTexts);
            var simulated = oldOutputs.Select(output => output.Guid).ToList();
            for (var i = 0; i < oldOutputs.Count; i++)
            {
                if (matches.ContainsValue(i))
                    continue;
                Add(Operation(Id($"{role}:delete:{oldOutputs[i].Guid:N}"), AffixProcessMemberDeleteOperationKinds.DeleteOutput,
                    CanonicalId.FromGuid(oldOutputs[i].Guid), new { }), item.Index);
                simulated.Remove(oldOutputs[i].Guid);
            }
            var finalOrder = new Guid[newTexts.Count];
            for (var n = 0; n < newTexts.Count; n++)
            {
                var node = text.Nodes[n];
                CanonicalId entity;
                if (matches.TryGetValue(n, out var kept))
                {
                    entity = CanonicalId.FromGuid(oldOutputs[kept].Guid);
                    finalOrder[n] = oldOutputs[kept].Guid;
                    var held = oldOutputs[kept] switch
                    {
                        IMoCopyFromInput copy => copy.ContentRA?.Guid,
                        IMoModifyFromInput modify => modify.ContentRA?.Guid,
                        _ => null,
                    };
                    var wanted = node switch { RuleCopy c => inputs[c.Index - 1], RuleModify m => inputs[m.Index - 1], _ => (Guid?)null };
                    if (wanted is { } target && held != target)
                        SetContent(item, $"{role}:content:{n}", node, entity, target);
                    continue;
                }
                entity = Id($"{role}:output:{n}");
                finalOrder[n] = entity.ToGuid();
                var className = node switch
                {
                    RuleCopy => "MoCopyFromInput",
                    RuleModify => "MoModifyFromInput",
                    RuleClass => "MoInsertNC",
                    RuleInsertUnits => "MoInsertPhones",
                    _ => throw Refuse(RefusalCodes.RuleNotationContextNotAllowed, item.Change, "An output holds copies, classes and inserted units."),
                };
                Add(Operation(Id($"{role}:create:{n}"), MoAffixProcessOutputOperationKinds.Create, processId, new { @class = className },
                    Placed(simulated, n == 0 ? null : finalOrder[n - 1], entity.ToGuid()), entity), item.Index);
                switch (node)
                {
                    case RuleCopy copy:
                        SetContent(item, $"{role}:content:{n}", node, entity, inputs[copy.Index - 1]);
                        break;
                    case RuleModify modify:
                        SetContent(item, $"{role}:content:{n}", node, entity, inputs[modify.Index - 1]);
                        Add(Operation(Id($"{role}:modification:{n}"), MoModifyFromInputModificationOperationKinds.SetModification, entity,
                            new { @ref = classes.Resolve(item, modify.Modification, $"{role}:out{n}").Value }), item.Index);
                        break;
                    case RuleClass insertClass:
                        Add(Operation(Id($"{role}:insert-class:{n}"), MoInsertNCContentOperationKinds.SetContent, entity,
                            new { @ref = classes.Resolve(item, insertClass, $"{role}:out{n}").Value }), item.Index);
                        break;
                    case RuleInsertUnits insert:
                        if (insert.Units.Select(unit => unit.Unit.Guid).Distinct().Count() != insert.Units.Count)
                            throw Refuse(RefusalCodes.RuleNotationUnsupportedClass, item.Change,
                                "An inserted group names one unit twice; the content list holds each unit once.");
                        for (var k = 0; k < insert.Units.Count; k++)
                            Add(Operation(Id($"{role}:unit:{n}:{k}"), MoInsertPhonesContentOperationKinds.AddRefContent, entity,
                                new { member = CanonicalId.FromGuid(insert.Units[k].Unit.Guid).Value }), item.Index);
                        break;
                }
            }
        }

        private void SetContent((int Index, RecordChange Change) item, string role, RuleNode node, CanonicalId output, Guid input)
        {
            var kind = node is RuleCopy ? MoCopyFromInputContentOperationKinds.SetContent : MoModifyFromInputContentOperationKinds.SetContent;
            Add(Operation(Id(role), kind, output, new { @ref = CanonicalId.FromGuid(input).Value }), item.Index);
        }

        /// <summary>Finds or creates the feature class a bracket names; a created class is auto-named as FieldWorks names the class of a modification.</summary>
        private sealed class ClassResolver(Run run, IMoAffixProcess process)
        {
            private readonly Dictionary<string, CanonicalId> _created = [];
            private readonly List<Guid> _classes = run.Cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Select(item => item.Guid).ToList();

            internal CanonicalId Resolve((int Index, RecordChange Change) item, RuleClass bracket, string role)
            {
                if (bracket.NamedClass is { } named)
                    return CanonicalId.FromGuid(named.Guid);
                if (bracket.Features.Count == 0)
                    throw Refuse(RuleNotation.Syntax, item.Change, "An empty class [] names no feature.");
                var signature = string.Join(",", bracket.Features.OrderBy(s => s.Feature.Guid).Select(s => s.Feature.Guid.ToString("N") + "=" + s.Value.Guid.ToString("N")));
                if (_created.TryGetValue(signature, out var again))
                    return again;
                var wanted = bracket.Features.ToDictionary(s => s.Feature.Guid, s => s.Value.Guid);
                foreach (var existing in run.Cache.LangProject.PhonologicalDataOA.NaturalClassesOS.OfType<IPhNCFeatures>())
                {
                    if (existing.Abbreviation.StringCount > 0 && existing.Abbreviation.BestAnalysisAlternative.Text is { Length: > 0 })
                        continue;
                    var held = existing.FeaturesOA?.FeatureSpecsOC.OfType<IFsClosedValue>()
                        .Where(spec => spec.FeatureRA is not null && spec.ValueRA is not null)
                        .ToDictionary(spec => spec.FeatureRA.Guid, spec => spec.ValueRA.Guid);
                    if (held is not null && held.Count == wanted.Count && wanted.All(pair => held.TryGetValue(pair.Key, out var value) && value == pair.Value))
                        return _created[signature] = CanonicalId.FromGuid(existing.Guid);
                }
                var id = run.Id(role + ":featureclass:" + signature);
                var data = CanonicalId.FromGuid(run.Cache.LangProject.PhonologicalDataOA.Guid);
                var form = process.Form.BestVernacularAlternative?.Text ?? "";
                run.Add(Operation(run.Id(role + ":featureclass-create:" + signature), PhPhonDataNaturalClassesOperationKinds.Create, data,
                    new { @class = "PhNCFeatures" }, Placed(_classes, _classes.Count == 0 ? null : _classes[^1], id.ToGuid()), id), item.Index);
                run.Add(Operation(run.Id(role + ":featureclass-name:" + signature), PhNaturalClassNameOperationKinds.SetName, id,
                    new { ws = run.Cache.WritingSystemFactory.GetStrFromWs(run.Cache.DefaultAnalWs), text = $"Created automatically for rule \"{form}\"" }), item.Index);
                var structure = run.Id(role + ":featureclass-structure:" + signature);
                run.Add(Operation(run.Id(role + ":featureclass-features:" + signature), PhNCFeaturesFeaturesOperationKinds.Create, id,
                    new { }, entity: structure), item.Index);
                foreach (var selection in bracket.Features.OrderBy(s => s.Feature.Guid))
                {
                    var feature = selection.Feature.Guid.ToString("N");
                    var spec = run.Id(role + ":featureclass-spec:" + signature + ":" + feature);
                    run.Add(Operation(run.Id(role + ":featureclass-spec-create:" + signature + ":" + feature),
                        FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs, structure, new { }, entity: spec), item.Index);
                    run.Add(Operation(run.Id(role + ":featureclass-spec-feature:" + signature + ":" + feature),
                        FsFeatureSpecificationFeatureOperationKinds.SetFeature, spec, new { @ref = CanonicalId.FromGuid(selection.Feature.Guid).Value }), item.Index);
                    run.Add(Operation(run.Id(role + ":featureclass-spec-value:" + signature + ":" + feature),
                        FsClosedValueValueOperationKinds.SetValue, spec, new { @ref = CanonicalId.FromGuid(selection.Value.Guid).Value }), item.Index);
                }
                return _created[signature] = id;
            }

            private static Stop Refuse(string code, RecordChange change, string reason) =>
                new(new(code, change.ObjectType ?? "", change.Field, change.ObjectGuid, reason));
        }
    }
}
