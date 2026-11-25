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
                var simulated = oldInputs.ToList();
                var contexts = process.InputOS.ToList();
                for (var n = 0; n < inputText!.Nodes.Count; n++)
                {
                    var node = inputText.Nodes[n];
                    var kind = InputKind(node, inputChange.Change);
                    if (n < contexts.Count && InputKind(contexts[n]) == kind)
                    {
                        newInputs[n] = oldInputs[n];
                        InputInPlace(inputChange, role, n, contexts[n], node, classes);
                        continue;
                    }
                    var entity = Id($"{role}:input:{n}");
                    newInputs[n] = entity.ToGuid();
                    var created = Placed(simulated, n == 0 ? null : newInputs[n - 1], entity.ToGuid());
                    var className = kind switch { "gap" => "PhVariable", "seq" => "PhSequenceContext", "nc" => "PhSimpleContextNC", "seg" => "PhSimpleContextSeg", _ => "PhSimpleContextBdry" };
                    Add(Operation(Id($"{role}:create:{n}"), MoAffixProcessInputOperationKinds.Create, processId, new { @class = className },
                        created, entity), inputChange.Index);
                    FillInput(inputChange, role, n, entity, node, classes);
                    if (n < oldInputs.Count)
                    {
                        removedInputs.Add(oldInputs[n]);
                        replacedBy[oldInputs[n]] = newInputs[n];
                    }
                }
                for (var n = inputText.Nodes.Count; n < oldInputs.Count; n++)
                    removedInputs.Add(oldInputs[n]);
            }

            var gaps = new HashSet<Guid>();
            for (var n = 0; n < newInputs.Count; n++)
                if (inputText is not null ? inputText.Nodes[n] is RuleGap : process.InputOS[n] is IPhVariable)
                    gaps.Add(newInputs[n]);
            if (outChange is { } outputChange)
                LowerOutput(process, processId, outputChange, outputText!, newInputs, gaps, classes);
            else if (inChange is { } retargeting)
                foreach (var output in process.OutputOS)
                {
                    var pointed = output switch { IMoCopyFromInput copy => copy.ContentRA, IMoModifyFromInput modify => modify.ContentRA, _ => null };
                    if (pointed is null || newInputs.Contains(pointed.Guid))
                        continue;
                    if (!replacedBy.TryGetValue(pointed.Guid, out var successor))
                        throw Refuse(RefusalCodes.RuleNotationDanglingIndex, retargeting.Change,
                            "An output element still points at an input column this change removes; change the output in the same file.");
                    if (output is IMoModifyFromInput && gaps.Contains(successor))
                        throw Refuse(RefusalCodes.RuleNotationContextNotAllowed, retargeting.Change, StemModifyReason);
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

        private string InputKind(RuleNode node, RecordChange change) => node switch
        {
            RuleGap => "gap",
            RuleGroup => "seq",
            RuleClass => "nc",
            RuleUnit unit => unit.Unit is IPhPhoneme ? "seg" : "bdry",
            _ => throw Refuse(RefusalCodes.RuleNotationContextNotAllowed, change, "An input column is a gap, a unit, a class or a group."),
        };

        private static string InputKind(ICmObject context) => context switch
        {
            IPhVariable => "gap",
            IPhSequenceContext => "seq",
            IPhSimpleContextNC => "nc",
            IPhSimpleContextSeg => "seg",
            IPhSimpleContextBdry => "bdry",
            _ => "other:" + context.ClassName,
        };

        private string OutputKind(RuleNode node, RecordChange change) => node switch
        {
            RuleCopy => "copy",
            RuleModify => "modify",
            RuleClass => "class",
            RuleInsertUnits => "units",
            _ => throw Refuse(RefusalCodes.RuleNotationContextNotAllowed, change, "An output holds copies, classes and inserted units."),
        };

        private static string OutputKind(ICmObject mapping) => mapping switch
        {
            IMoCopyFromInput => "copy",
            IMoModifyFromInput => "modify",
            IMoInsertNC => "class",
            IMoInsertPhones => "units",
            _ => "other:" + mapping.ClassName,
        };

        /// <summary>Whether a stored class is the one the bracket names.</summary>
        private static bool SameClass(IPhNaturalClass? held, RuleClass bracket)
        {
            if (held is null)
                return false;
            if (bracket.NamedClass is { } named)
                return held.Guid == named.Guid;
            if (held is not IPhNCFeatures { FeaturesOA: { } structure })
                return false;
            var abbreviation = held.Abbreviation.BestAnalysisAlternative.Text;
            if (!string.IsNullOrEmpty(abbreviation) && !abbreviation.StartsWith("***", StringComparison.Ordinal))
                return false;
            var stored = structure.FeatureSpecsOC.OfType<IFsClosedValue>().Select(spec => (spec.FeatureRA?.Guid, spec.ValueRA?.Guid)).ToList();
            return stored.Count == structure.FeatureSpecsOC.Count && stored.Count == bracket.Features.Count &&
                stored.ToHashSet().SetEquals(bracket.Features.Select(f => ((Guid?)f.Feature.Guid, (Guid?)f.Value.Guid)));
        }

        /// <summary>Keeps a column's object and sets only the fields its new text changes.</summary>
        private void InputInPlace((int Index, RecordChange Change) item, string role, int n, ICmObject context, RuleNode node, ClassResolver classes)
        {
            var id = CanonicalId.FromGuid(context.Guid);
            switch (node)
            {
                case RuleUnit unit:
                    var held = context is IPhSimpleContextSeg segment ? segment.FeatureStructureRA?.Guid : ((IPhSimpleContextBdry)context).FeatureStructureRA?.Guid;
                    if (held != unit.Unit.Guid)
                        SetUnit(item, $"{role}:unit:{n}", id, unit);
                    break;
                case RuleClass naturalClass:
                    if (!SameClass(((IPhSimpleContextNC)context).FeatureStructureRA, naturalClass))
                        Add(Operation(Id($"{role}:class:{n}"), PhSimpleContextNCFeatureStructureOperationKinds.SetFeatureStructure, id,
                            new { @ref = classes.Resolve(item, naturalClass, role + ":in" + n).Value }), item.Index);
                    break;
                case RuleGroup group:
                    SequenceInPlace(item, role, n, (IPhSequenceContext)context, group, classes);
                    break;
            }
        }

        /// <summary>Aligns a sequence's members by position.</summary>
        private void SequenceInPlace((int Index, RecordChange Change) item, string role, int n, IPhSequenceContext sequence, RuleGroup group, ClassResolver classes)
        {
            var data = cache.LangProject.PhonologicalDataOA;
            var sequenceId = CanonicalId.FromGuid(sequence.Guid);
            var old = sequence.MembersRS.ToList();
            var simulated = old.Select(member => member.Guid).ToList();
            var final = new Guid[group.Members.Count];
            var fresh = new List<int>();
            var dropped = new List<ICmObject>();
            for (var k = 0; k < group.Members.Count; k++)
            {
                if (k < old.Count && InputKind(old[k]) == InputKind(group.Members[k], item.Change))
                {
                    final[k] = old[k].Guid;
                    InputInPlace(item, $"{role}:m{n}:{k}", k, old[k], group.Members[k], classes);
                    continue;
                }
                fresh.Add(k);
                if (k < old.Count)
                    dropped.Add(old[k]);
            }
            for (var k = group.Members.Count; k < old.Count; k++)
                dropped.Add(old[k]);
            foreach (var member in dropped)
            {
                var memberId = CanonicalId.FromGuid(member.Guid);
                Add(Operation(Id($"{role}:member-remove:{n}:{member.Guid:N}"), PhSequenceContextMembersOperationKinds.RemoveRefMembers, sequenceId,
                    new { member = memberId.Value }), item.Index);
                if (member.Owner == data && member.ReferringObjects.All(user => user == sequence))
                    Add(Operation(Id($"{role}:member-delete:{n}:{member.Guid:N}"), AffixProcessMemberDeleteOperationKinds.DeleteContext, memberId, new { }), item.Index);
                simulated.Remove(member.Guid);
            }
            foreach (var k in fresh)
            {
                var member = CreateMember(item, role, n, k, group.Members[k], classes);
                final[k] = member.ToGuid();
                Add(Operation(Id($"{role}:m{n}:{k}:add"), PhSequenceContextMembersOperationKinds.AddRefMembers, sequenceId,
                    new { member = member.Value }, Placed(simulated, k == 0 ? null : final[k - 1], member.ToGuid())), item.Index);
            }
        }

        private CanonicalId CreateMember((int Index, RecordChange Change) item, string role, int n, int k, RuleNode node, ClassResolver classes)
        {
            var data = CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.Guid);
            var member = Id($"{role}:member:{n}:{k}");
            var memberRole = $"{role}:m{n}:{k}";
            if (node is RuleUnit memberUnit)
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
                    new { @ref = classes.Resolve(item, (RuleClass)node, memberRole).Value }), item.Index);
            }
            return member;
        }

        /// <summary>The placement that puts a new member right after its predecessor.</summary>
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
                    CanonicalId? previous = null;
                    for (var k = 0; k < group.Members.Count; k++)
                    {
                        var member = CreateMember(item, role, n, k, group.Members[k], classes);
                        Add(Operation(Id($"{role}:m{n}:{k}:add"), PhSequenceContextMembersOperationKinds.AddRefMembers, entity,
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

        // Editor shape: AffixRuleFormulaControl.IsIndexCurrent.
        private const string StemModifyReason = "A stem variable (*) can be copied but not modified; FieldWorks's affix editor does not offer it.";

        private void LowerOutput(IMoAffixProcess process, CanonicalId processId, (int Index, RecordChange Change) item, RuleText text,
            List<Guid> inputs, HashSet<Guid> gaps, ClassResolver classes)
        {
            var role = $"{item.Index}:{item.Change.Kind}";
            var oldOutputs = process.OutputOS.ToList();
            var kinds = text.Nodes.Select(node => OutputKind(node, item.Change)).ToList();
            var simulated = oldOutputs.Select(output => output.Guid).ToList();
            for (var i = 0; i < oldOutputs.Count; i++)
            {
                if (i < kinds.Count && OutputKind(oldOutputs[i]) == kinds[i])
                    continue;
                Add(Operation(Id($"{role}:delete:{oldOutputs[i].Guid:N}"), AffixProcessMemberDeleteOperationKinds.DeleteOutput,
                    CanonicalId.FromGuid(oldOutputs[i].Guid), new { }), item.Index);
                simulated.Remove(oldOutputs[i].Guid);
            }
            var finalOrder = new Guid[kinds.Count];
            for (var n = 0; n < kinds.Count; n++)
            {
                var node = text.Nodes[n];
                if (node is RuleModify stemModify && gaps.Contains(inputs[stemModify.Index - 1]))
                    throw Refuse(RefusalCodes.RuleNotationContextNotAllowed, item.Change, StemModifyReason);
                if (node is RuleInsertUnits units && units.Units.Select(unit => unit.Unit.Guid).Distinct().Count() != units.Units.Count)
                    throw Refuse(RefusalCodes.RuleNotationUnsupportedClass, item.Change,
                        "An inserted group names one unit twice; the content list holds each unit once.");
                if (n < oldOutputs.Count && OutputKind(oldOutputs[n]) == kinds[n])
                {
                    finalOrder[n] = oldOutputs[n].Guid;
                    OutputInPlace(item, role, n, oldOutputs[n], node, inputs, classes);
                    continue;
                }
                var entity = Id($"{role}:output:{n}");
                finalOrder[n] = entity.ToGuid();
                var className = kinds[n] switch { "copy" => "MoCopyFromInput", "modify" => "MoModifyFromInput", "class" => "MoInsertNC", _ => "MoInsertPhones" };
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
                        for (var k = 0; k < insert.Units.Count; k++)
                            Add(Operation(Id($"{role}:unit:{n}:{k}"), MoInsertPhonesContentOperationKinds.AddRefContent, entity,
                                new { member = CanonicalId.FromGuid(insert.Units[k].Unit.Guid).Value }), item.Index);
                        break;
                }
            }
        }

        /// <summary>Keeps an output mapping's object and sets only the fields its new text changes.</summary>
        private void OutputInPlace((int Index, RecordChange Change) item, string role, int n, ICmObject mapping, RuleNode node,
            List<Guid> inputs, ClassResolver classes)
        {
            var id = CanonicalId.FromGuid(mapping.Guid);
            switch (node)
            {
                case RuleCopy copy:
                    if (((IMoCopyFromInput)mapping).ContentRA?.Guid != inputs[copy.Index - 1])
                        SetContent(item, $"{role}:content:{n}", node, id, inputs[copy.Index - 1]);
                    break;
                case RuleModify modify:
                    var held = (IMoModifyFromInput)mapping;
                    if (held.ContentRA?.Guid != inputs[modify.Index - 1])
                        SetContent(item, $"{role}:content:{n}", node, id, inputs[modify.Index - 1]);
                    if (!SameClass(held.ModificationRA, modify.Modification))
                        Add(Operation(Id($"{role}:modification:{n}"), MoModifyFromInputModificationOperationKinds.SetModification, id,
                            new { @ref = classes.Resolve(item, modify.Modification, $"{role}:out{n}").Value }), item.Index);
                    break;
                case RuleClass insertClass:
                    if (!SameClass(((IMoInsertNC)mapping).ContentRA, insertClass))
                        Add(Operation(Id($"{role}:insert-class:{n}"), MoInsertNCContentOperationKinds.SetContent, id,
                            new { @ref = classes.Resolve(item, insertClass, $"{role}:out{n}").Value }), item.Index);
                    break;
                case RuleInsertUnits insert:
                    var stored = ((IMoInsertPhones)mapping).ContentRS.Select(unit => unit.Guid).ToList();
                    var common = 0;
                    while (common < stored.Count && common < insert.Units.Count && stored[common] == insert.Units[common].Unit.Guid)
                        common++;
                    for (var k = common; k < stored.Count; k++)
                        Add(Operation(Id($"{role}:unit-remove:{n}:{stored[k]:N}"), MoInsertPhonesContentOperationKinds.RemoveRefContent, id,
                            new { member = CanonicalId.FromGuid(stored[k]).Value }), item.Index);
                    for (var k = common; k < insert.Units.Count; k++)
                        Add(Operation(Id($"{role}:unit:{n}:{k}"), MoInsertPhonesContentOperationKinds.AddRefContent, id,
                            new { member = CanonicalId.FromGuid(insert.Units[k].Unit.Guid).Value }), item.Index);
                    break;
            }
        }

        private void SetContent((int Index, RecordChange Change) item, string role, RuleNode node, CanonicalId output, Guid input)
        {
            var kind = node is RuleCopy ? MoCopyFromInputContentOperationKinds.SetContent : MoModifyFromInputContentOperationKinds.SetContent;
            Add(Operation(Id(role), kind, output, new { @ref = CanonicalId.FromGuid(input).Value }), item.Index);
        }

        /// <summary>A named class, or a new private class per bracket (SetMappingFeatures).</summary>
        private sealed class ClassResolver(Run run, IMoAffixProcess process)
        {
            private readonly List<Guid> _classes = run.Cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Select(item => item.Guid).ToList();

            internal CanonicalId Resolve((int Index, RecordChange Change) item, RuleClass bracket, string role)
            {
                if (bracket.NamedClass is { } named)
                    return CanonicalId.FromGuid(named.Guid);
                if (bracket.Features.Count == 0)
                    throw Refuse(RuleNotation.Syntax, item.Change, "An empty class [] names no feature.");
                var signature = string.Join(",", bracket.Features.OrderBy(s => s.Feature.Guid).Select(s => s.Feature.Guid.ToString("N") + "=" + s.Value.Guid.ToString("N")));
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
                return id;
            }

            private static Stop Refuse(string code, RecordChange change, string reason) =>
                new(new(code, change.ObjectType ?? "", change.Field, change.ObjectGuid, reason));
        }
    }
}
