using System.Text.Json;
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
        private const int ContextLimit = 8;
        private readonly HashSet<int> _ruleDone = [];
        private ConstraintBook? _book;

        private sealed record RuleItem(int Index, RecordChange Change, RuleField Field, ICmObject Owner, RuleText Text);

        // Editor shapes: RegRuleFormulaControl.InsertContext, CreateSeqCtxt and SetContextOccurrence.
        private void LowerRuleFormula(RecordChange change, string type)
        {
            var index = IndexOf(change);
            if (type == "PhMetathesisRule" || index < 0 || RuleOf(change) is not IPhRegularRule rule)
                throw Refuse(RefusalCodes.RuleNotationReadOnly, change,
                    "A metathesis rule's formula is written by compose-author-metathesis-rule, not by a record file.");
            if (_ruleDone.Contains(index))
                return;
            var group = Enumerable.Range(0, Changes.Count).Where(i => Changes[i].Kind is RecordChangeKind.Set or RecordChangeKind.Clear &&
                Changes[i].ObjectType is not null && Changes[i].ObjectGuid is not null && IsRuleField(Changes[i]) &&
                RuleOf(Changes[i])?.Guid == rule.Guid).ToList();
            foreach (var member in group)
                _ruleDone.Add(member);
            try
            {
                LowerRuleGroup(rule, group);
            }
            catch (Stop stop)
            {
                foreach (var member in group.Where(i => i != index))
                {
                    var other = Changes[member];
                    _siblingRefusals.Add((member, new(stop.Refusal.Code, other.ObjectType ?? "", other.Field, other.ObjectGuid, stop.Refusal.Reason)));
                }
                throw Refuse(stop.Refusal.Code, change, stop.Refusal.Reason);
            }
        }

        private bool IsRuleField(RecordChange change) =>
            schema.FieldBinding(change.ObjectType!, change.Field) is { IsCustom: false } binding &&
            RuleNotation.TryFieldOf(binding.DeclaringClass, binding.Field, out var field) &&
            field is RuleField.StrucDesc or RuleField.StrucChange or RuleField.LeftContext or RuleField.RightContext;

        private IPhSegmentRule? RuleOf(RecordChange change)
        {
            if (change.ObjectGuid is not { } guid || !cache.ServiceLocator.ObjectRepository.TryGetObject(guid, out var owner))
                return null;
            return owner as IPhSegmentRule ?? (owner as IPhSegRuleRHS)?.Owner as IPhSegmentRule;
        }

        private void LowerRuleGroup(IPhRegularRule rule, List<int> group)
        {
            var items = new List<RuleItem>();
            foreach (var i in group)
            {
                var change = Changes[i];
                var binding = schema.FieldBinding(change.ObjectType!, change.Field)!.Value;
                RuleNotation.TryFieldOf(binding.DeclaringClass, binding.Field, out var field);
                var owner = cache.ServiceLocator.GetObject(change.ObjectGuid!.Value);
                string current;
                try { current = RuleNotation.Render(owner, field, cache); }
                catch (RuleNotationException exception) { throw Refuse(exception.Code, change, exception.Message); }
                if (!string.Equals(current, change.Old ?? "", StringComparison.Ordinal))
                    throw Refuse(RefusalCodes.RecordImportValueStale, change, "The project's value is no longer the one the file started from.");
                items.Add(new(i, change, field, owner, ParseRule(change, field)));
            }
            CheckAlphaAcrossRule(rule, items);
            var tree = RuleTree(rule);
            var classes = new ClassResolver(this, rule.Name.BestAnalysisAlternative?.Text ?? "");
            _book = new ConstraintBook(this, rule);
            try
            {
                LowerRuleItems(rule, items, tree, classes);
                _book.Collect(items.Min(entry => entry.Index));
            }
            finally
            {
                _book = null;
            }
        }

        /// <summary>Checks alpha letters over the rule as it will stand.</summary>
        private void CheckAlphaAcrossRule(IPhRegularRule rule, List<RuleItem> items)
        {
            var texts = items.Select(item => item.Text).ToList();
            var changed = items.Select(item => (item.Owner.Guid, item.Field)).ToHashSet();
            void Other(ICmObject owner, RuleField field)
            {
                if (changed.Contains((owner.Guid, field)))
                    return;
                try { texts.Add(RuleNotation.Parse(RuleNotation.Render(owner, field, cache), field, cache)); }
                catch (Exception exception) when (exception is RuleNotationException or FeatureStructureNotationException) { }
            }
            Other(rule, RuleField.StrucDesc);
            foreach (var rhs in rule.RightHandSidesOS)
            {
                Other(rhs, RuleField.StrucChange);
                Other(rhs, RuleField.LeftContext);
                Other(rhs, RuleField.RightContext);
            }
            try { RuleNotation.CheckAlpha(texts); }
            catch (RuleNotationException exception) { throw Refuse(exception.Code, items[0].Change, exception.Message); }
        }

        private void LowerRuleItems(IPhRegularRule rule, List<RuleItem> items, HashSet<Guid> tree, ClassResolver classes)
        {
            foreach (var item in items.OrderBy(entry => entry.Field))
            {
                var pair = (item.Index, item.Change);
                var role = $"{item.Index}:{item.Change.Kind}";
                switch (item.Field)
                {
                    case RuleField.StrucDesc:
                        FlatList(pair, role, rule, rule.StrucDescOS.Cast<IPhPhonContext>().ToList(), item.Text,
                            PhSegmentRuleStrucDescOperationKinds.Create, tree, classes);
                        break;
                    case RuleField.StrucChange:
                        FlatList(pair, role, item.Owner, ((IPhSegRuleRHS)item.Owner).StrucChangeOS.Cast<IPhPhonContext>().ToList(), item.Text,
                            PhSegRuleRHSStrucChangeOperationKinds.Create, tree, classes);
                        break;
                    default:
                        var rhs = (IPhSegRuleRHS)item.Owner;
                        var left = item.Field == RuleField.LeftContext;
                        Side(pair, role, rhs, left ? rhs.LeftContextOA : rhs.RightContextOA, item.Text,
                            left ? PhSegRuleRHSLeftContextOperationKinds.Create : PhSegRuleRHSRightContextOperationKinds.Create, tree, classes);
                        break;
                }
            }
        }

        private RuleText ParseRule(RecordChange change, RuleField field)
        {
            var text = change.Kind == RecordChangeKind.Clear ? "" : change.New ?? "";
            RuleText parsed;
            try { parsed = RuleNotation.Parse(text, field, cache); }
            catch (RuleNotationException exception) { throw Refuse(exception.Code, change, exception.Message); }
            catch (FeatureStructureNotationException exception) { throw Refuse(exception.Code, change, exception.Message); }
            var sides = field is RuleField.LeftContext or RuleField.RightContext;
            if (sides && parsed.Nodes.Count > ContextLimit)
                throw Refuse(RefusalCodes.RuleNotationLimitExceeded, change, $"A rule context has at most {ContextLimit} members.");
            foreach (var node in parsed.Nodes)
            {
                if (!sides && node is RuleUnit { Unit.Guid: var marker } && marker == LangProjectTags.kguidPhRuleWordBdry)
                    throw Refuse(RefusalCodes.RuleNotationContextNotAllowed, change, "The word boundary # is only at the outer edge of a left or right context.");
                if (node is RuleRepeat { Member: RuleUnit { Unit: IPhBdryMarker } })
                    throw Refuse(RefusalCodes.RuleNotationContextNotAllowed, change, "A boundary cannot be repeated or made optional.");
            }
            return parsed;
        }

        private static IEnumerable<IPhPhonContext> Descendants(IPhPhonContext context)
        {
            yield return context;
            var below = context switch
            {
                IPhSequenceContext sequence => sequence.MembersRS.Cast<IPhPhonContext>().ToList(),
                IPhIterationContext { MemberRA: { } member } => [member],
                _ => [],
            };
            foreach (var member in below.SelectMany(Descendants))
                yield return member;
        }

        private static HashSet<Guid> RuleTree(IPhRegularRule rule)
        {
            var tree = new HashSet<Guid> { rule.Guid };
            foreach (var context in rule.StrucDescOS.Cast<IPhPhonContext>().SelectMany(Descendants))
                tree.Add(context.Guid);
            foreach (var rhs in rule.RightHandSidesOS)
            {
                tree.Add(rhs.Guid);
                var roots = rhs.StrucChangeOS.Cast<IPhPhonContext>().Concat(new[] { rhs.LeftContextOA, rhs.RightContextOA }.OfType<IPhPhonContext>());
                foreach (var context in roots.SelectMany(Descendants))
                    tree.Add(context.Guid);
            }
            return tree;
        }

        private static string CtxKind(ICmObject context) => context switch
        {
            IPhSimpleContextSeg => "seg",
            IPhSimpleContextNC => "nc",
            IPhSimpleContextBdry => "bdry",
            IPhIterationContext => "iter",
            IPhSequenceContext => "seq",
            _ => "other:" + context.ClassName,
        };

        private static string NodeKind(RuleNode node) => node switch
        {
            RuleUnit unit => unit.Unit is IPhPhoneme ? "seg" : "bdry",
            RuleClass => "nc",
            RuleRepeat => "iter",
            _ => "other",
        };

        private static string ClassOf(string kind) => kind switch
        {
            "seg" => "PhSimpleContextSeg",
            "nc" => "PhSimpleContextNC",
            "bdry" => "PhSimpleContextBdry",
            "iter" => "PhIterationContext",
            _ => "PhSequenceContext",
        };

        /// <summary>Stops on a shared context, or a deleted one that holds a name or description.</summary>
        private void Guard((int Index, RecordChange Change) item, IPhPhonContext context, HashSet<Guid> tree, bool deleting)
        {
            foreach (var part in Descendants(context))
            {
                if (part.ReferringObjects.Any(user => !tree.Contains(user.Guid)))
                    throw Refuse(RefusalCodes.RuleNotationSharedClass, item.Change,
                        "Another object refers to a context of this rule, so the rule's text cannot change it.");
                if (deleting && (HasText(part.Name) || part.DescriptionOA is { ParagraphsOS: { } paragraphs } && paragraphs.OfType<IStTxtPara>().Any(paragraph => paragraph.Contents.Length > 0)))
                    throw Refuse(RefusalCodes.RuleNotationContextHasData, item.Change,
                        "A context this edit would delete has a name or description the file cannot show.");
            }
        }

        private static bool HasText(IMultiAccessorBase text) =>
            text.AvailableWritingSystemIds.Any(ws => text.get_String(ws) is { Length: > 0 });

        private static string Ctx(ICmObject context) => context.Guid.ToString("N");

        private void Delete((int Index, RecordChange Change) item, string role, ICmObject context, bool owned)
        {
            if (context is IPhPhonContext phonContext)
                _book?.Retire(phonContext);
            Add(Operation(Id($"{role}:delete:{Ctx(context)}"),
                owned ? PhonologicalRuleDeleteOperationKinds.DeleteContexts : AffixProcessMemberDeleteOperationKinds.DeleteContext,
                CanonicalId.FromGuid(context.Guid), new { }), item.Index);
        }

        /// <summary>Deletes a pooled context nothing else refers to; its repeated context goes with it.</summary>
        private void DeletePooled((int Index, RecordChange Change) item, string role, IPhPhonContext context, ICmObject referrer)
        {
            if (context.Owner != cache.LangProject.PhonologicalDataOA || context.ReferringObjects.Any(user => user != referrer))
                return;
            Delete(item, role, context, owned: false);
        }

        private void DeleteTree((int Index, RecordChange Change) item, string role, IPhPhonContext owned, HashSet<Guid> tree)
        {
            Guard(item, owned, tree, deleting: true);
            Delete(item, role, owned, owned: true);
        }

        /// <summary>Aligns a rule-owned flat list of simple contexts by position.</summary>
        private void FlatList((int Index, RecordChange Change) item, string role, ICmObject owner, List<IPhPhonContext> old, RuleText text,
            string createKind, HashSet<Guid> tree, ClassResolver classes)
        {
            var nodes = text.Nodes;
            var kept = Enumerable.Range(0, nodes.Count).Select(n => n < old.Count && CtxKind(old[n]) == NodeKind(nodes[n])).ToArray();
            var simulated = old.Select(context => context.Guid).ToList();
            for (var n = 0; n < old.Count; n++)
            {
                if (n < kept.Length && kept[n])
                    continue;
                Guard(item, old[n], tree, deleting: true);
                Delete(item, role, old[n], owned: true);
                simulated.Remove(old[n].Guid);
            }
            var final = new Guid[nodes.Count];
            for (var n = 0; n < nodes.Count; n++)
            {
                if (kept[n])
                {
                    final[n] = old[n].Guid;
                    Guard(item, old[n], tree, deleting: false);
                    InputInPlace(item, role, n, old[n], nodes[n], classes);
                    continue;
                }
                var entity = Id($"{role}:context:{n}");
                final[n] = entity.ToGuid();
                Add(Operation(Id($"{role}:create:{n}"), createKind, CanonicalId.FromGuid(owner.Guid), new { @class = ClassOf(NodeKind(nodes[n])) },
                    Placed(simulated, n == 0 ? null : final[n - 1], entity.ToGuid()), entity), item.Index);
                FillInput(item, role, n, entity, nodes[n], classes);
            }
        }

        /// <summary>Aligns a left or right context: none, one simple context, one repeat, or a sequence.</summary>
        private void Side((int Index, RecordChange Change) item, string role, IPhSegRuleRHS rhs, IPhPhonContext? held, RuleText text,
            string createKind, HashSet<Guid> tree, ClassResolver classes)
        {
            var nodes = text.Nodes;
            var shape = nodes.Count == 0 ? "none" : nodes.Count > 1 ? "seq" : NodeKind(nodes[0]);
            if (held is not null && CtxKind(held) == shape)
            {
                Guard(item, held, tree, deleting: false);
                switch (held)
                {
                    case IPhIterationContext iteration:
                        Iteration(item, role, iteration, (RuleRepeat)nodes[0], tree, classes);
                        break;
                    case IPhSequenceContext sequence:
                        Members(item, role, sequence, nodes, tree, classes);
                        break;
                    default:
                        InputInPlace(item, role, 0, held, nodes[0], classes);
                        break;
                }
                return;
            }
            if (held is not null)
                DeleteTree(item, role, held, tree);
            if (shape == "none")
                return;
            var rhsId = CanonicalId.FromGuid(rhs.Guid);
            var entity = Id($"{role}:context");
            Add(Operation(Id($"{role}:create"), createKind, rhsId, new { @class = ClassOf(shape) }, entity: entity), item.Index);
            switch (shape)
            {
                case "iter":
                    FillIteration(item, role, entity, (RuleRepeat)nodes[0], classes);
                    break;
                case "seq":
                    CanonicalId? previous = null;
                    for (var k = 0; k < nodes.Count; k++)
                    {
                        var member = CreatePooled(item, role, k, nodes[k], classes);
                        Add(Operation(Id($"{role}:m:{k}:add"), PhSequenceContextMembersOperationKinds.AddRefMembers, entity,
                            new { member = member.Value }, previous is { } prior ? new Placement(prior, null) : null), item.Index);
                        previous = member;
                    }
                    break;
                default:
                    FillInput(item, role, 0, entity, nodes[0], classes);
                    break;
            }
        }

        private void FillIteration((int Index, RecordChange Change) item, string role, CanonicalId iteration, RuleRepeat repeat, ClassResolver classes)
        {
            SetBounds(item, role, iteration, repeat);
            var inner = CreateMember(item, role + ":in", 0, 0, repeat.Member, classes);
            Add(Operation(Id($"{role}:member"), PhIterationContextMemberOperationKinds.SetMember, iteration, new { @ref = inner.Value }), item.Index);
        }

        private void SetBounds((int Index, RecordChange Change) item, string role, CanonicalId iteration, RuleRepeat repeat) =>
            Add(Operation(Id($"{role}:bounds"), PhIterationContextBoundsOperationKinds.SetBounds, iteration,
                new { minimum = repeat.Minimum, maximum = repeat.Maximum }), item.Index);

        /// <summary>A pooled sequence member: a simple context or a repeat.</summary>
        private CanonicalId CreatePooled((int Index, RecordChange Change) item, string role, int k, RuleNode node, ClassResolver classes)
        {
            if (node is not RuleRepeat repeat)
                return CreateMember(item, role, 0, k, node, classes);
            var iteration = Id($"{role}:iteration:{k}");
            Add(Operation(Id($"{role}:m:{k}:create"), PhPhonDataContextsOperationKinds.Create, CanonicalId.FromGuid(cache.LangProject.PhonologicalDataOA.Guid),
                new { @class = "PhIterationContext" }, entity: iteration), item.Index);
            SetBounds(item, $"{role}:m:{k}", iteration, repeat);
            var inner = CreateMember(item, $"{role}:m:{k}:in", 0, 0, repeat.Member, classes);
            Add(Operation(Id($"{role}:m:{k}:member"), PhIterationContextMemberOperationKinds.SetMember, iteration, new { @ref = inner.Value }), item.Index);
            return iteration;
        }

        /// <summary>Keeps a repeat, sets its counts when they changed, and aligns the one context it repeats.</summary>
        private void Iteration((int Index, RecordChange Change) item, string role, IPhIterationContext iteration, RuleRepeat repeat,
            HashSet<Guid> tree, ClassResolver classes)
        {
            var id = CanonicalId.FromGuid(iteration.Guid);
            if (iteration.Minimum != repeat.Minimum || iteration.Maximum != repeat.Maximum)
                SetBounds(item, role, id, repeat);
            var held = iteration.MemberRA;
            if (held is not null && CtxKind(held) == NodeKind(repeat.Member))
            {
                Guard(item, held, tree, deleting: false);
                InputInPlace(item, role + ":in", 0, held, repeat.Member, classes);
                return;
            }
            if (held is not null)
                Guard(item, held, tree, deleting: true);
            var inner = CreateMember(item, role + ":in", 0, 0, repeat.Member, classes);
            Add(Operation(Id($"{role}:member"), PhIterationContextMemberOperationKinds.SetMember, id, new { @ref = inner.Value }), item.Index);
            if (held is not null)
                DeletePooled(item, role, held, iteration);
        }

        /// <summary>Aligns a sequence's members by position and drops only unused pooled contexts.</summary>
        private void Members((int Index, RecordChange Change) item, string role, IPhSequenceContext sequence, IReadOnlyList<RuleNode> nodes,
            HashSet<Guid> tree, ClassResolver classes)
        {
            var sequenceId = CanonicalId.FromGuid(sequence.Guid);
            var old = sequence.MembersRS.Cast<IPhPhonContext>().ToList();
            var simulated = old.Select(member => member.Guid).ToList();
            var final = new Guid[nodes.Count];
            var fresh = new List<int>();
            var dropped = new List<IPhPhonContext>();
            for (var k = 0; k < nodes.Count; k++)
            {
                if (k < old.Count && CtxKind(old[k]) == NodeKind(nodes[k]))
                {
                    final[k] = old[k].Guid;
                    Guard(item, old[k], tree, deleting: false);
                    if (old[k] is IPhIterationContext iteration)
                        Iteration(item, $"{role}:m:{k}", iteration, (RuleRepeat)nodes[k], tree, classes);
                    else
                        InputInPlace(item, $"{role}:m:{k}", k, old[k], nodes[k], classes);
                    continue;
                }
                fresh.Add(k);
                if (k < old.Count)
                    dropped.Add(old[k]);
            }
            for (var k = nodes.Count; k < old.Count; k++)
                dropped.Add(old[k]);
            foreach (var member in dropped)
            {
                Guard(item, member, tree, deleting: true);
                Add(Operation(Id($"{role}:member-remove:{Ctx(member)}"), PhSequenceContextMembersOperationKinds.RemoveRefMembers, sequenceId,
                    new { member = CanonicalId.FromGuid(member.Guid).Value }), item.Index);
                DeletePooled(item, role, member, sequence);
                simulated.Remove(member.Guid);
            }
            foreach (var k in fresh)
            {
                var member = CreatePooled(item, role, k, nodes[k], classes);
                final[k] = member.ToGuid();
                Add(Operation(Id($"{role}:m:{k}:add"), PhSequenceContextMembersOperationKinds.AddRefMembers, sequenceId,
                    new { member = member.Value }, Placed(simulated, k == 0 ? null : final[k - 1], member.ToGuid())), item.Index);
            }
        }

        // Editor shape: PhonologicalFeatureChooserDlg.UpdateFeatureConstraints.
        private sealed class ConstraintBook
        {
            private readonly Run _run;
            private readonly IPhRegularRule _rule;
            private readonly Dictionary<Guid, IPhFeatureConstraint> _held = [];
            private readonly Dictionary<Guid, CanonicalId> _made = [];
            private readonly HashSet<Guid> _retired = [];
            private readonly List<Guid> _retiredOrder = [];

            internal ConstraintBook(Run run, IPhRegularRule rule)
            {
                _run = run;
                _rule = rule;
                foreach (var constraint in rule.FeatureConstraints)
                    if (constraint.FeatureRA is { } feature)
                        _held.TryAdd(feature.Guid, constraint);
            }

            internal void Retire(IPhPhonContext context)
            {
                foreach (var part in Descendants(context).OfType<IPhSimpleContextNC>())
                    foreach (var constraint in part.PlusConstrRS.Concat(part.MinusConstrRS))
                        Note(constraint);
            }

            private void Note(IPhFeatureConstraint constraint)
            {
                if (_retired.Add(constraint.Guid))
                    _retiredOrder.Add(constraint.Guid);
            }

            private CanonicalId Resolve((int Index, RecordChange Change) item, IFsClosedFeature feature)
            {
                if (_held.TryGetValue(feature.Guid, out var held))
                    return CanonicalId.FromGuid(held.Guid);
                if (_made.TryGetValue(feature.Guid, out var made))
                    return made;
                var entity = _run.Id($"alpha:{_rule.Guid:N}:{feature.Guid:N}");
                var data = CanonicalId.FromGuid(_run.Cache.LangProject.PhonologicalDataOA.Guid);
                _run.Add(Operation(_run.Id($"alpha:{_rule.Guid:N}:{feature.Guid:N}:create"), PhPhonDataFeatConstraintsOperationKinds.Create, data,
                    new { }, entity: entity), item.Index);
                _run.Add(Operation(_run.Id($"alpha:{_rule.Guid:N}:{feature.Guid:N}:feature"), PhFeatureConstraintFeatureOperationKinds.SetFeature, entity,
                    new { @ref = CanonicalId.FromGuid(feature.Guid).Value }), item.Index);
                _made[feature.Guid] = entity;
                return entity;
            }

            /// <summary>Makes a class context's constraints the bracket's alpha variables; a null <paramref name="held"/> is a context just created.</summary>
            internal void Reconcile((int Index, RecordChange Change) item, string role, IPhSimpleContextNC? held, CanonicalId context, RuleClass bracket)
            {
                var desired = bracket.Alphas.ToDictionary(alpha => alpha.Feature.Guid, alpha => alpha);
                var plus = held?.PlusConstrRS.ToList() ?? [];
                var minus = held?.MinusConstrRS.ToList() ?? [];
                foreach (var alpha in bracket.Alphas)
                {
                    var current = alpha.Disagree ? minus : plus;
                    if (current.Any(constraint => constraint.FeatureRA?.Guid == alpha.Feature.Guid))
                        continue;
                    _run.Add(Operation(_run.Id($"{role}:alpha:{alpha.Feature.Guid:N}:add"),
                        alpha.Disagree ? PhSimpleContextNCMinusConstrOperationKinds.AddRefMinusConstr : PhSimpleContextNCPlusConstrOperationKinds.AddRefPlusConstr,
                        context, new { member = Resolve(item, alpha.Feature).Value }), item.Index);
                }
                foreach (var constraint in plus.Where(constraint => !Wanted(desired, constraint, disagree: false)))
                {
                    Note(constraint);
                    _run.Add(Operation(_run.Id($"{role}:alpha:{constraint.Guid:N}:remove"), PhSimpleContextNCPlusConstrOperationKinds.RemoveRefPlusConstr,
                        context, new { member = CanonicalId.FromGuid(constraint.Guid).Value }), item.Index);
                }
                foreach (var constraint in minus.Where(constraint => !Wanted(desired, constraint, disagree: true)))
                {
                    Note(constraint);
                    _run.Add(Operation(_run.Id($"{role}:alpha:{constraint.Guid:N}:remove"), PhSimpleContextNCMinusConstrOperationKinds.RemoveRefMinusConstr,
                        context, new { member = CanonicalId.FromGuid(constraint.Guid).Value }), item.Index);
                }
            }

            private static bool Wanted(Dictionary<Guid, RuleAlpha> desired, IPhFeatureConstraint constraint, bool disagree) =>
                constraint.FeatureRA is { } feature && desired.TryGetValue(feature.Guid, out var alpha) && alpha.Disagree == disagree;

            /// <summary>Offers every constraint the group stopped using for collection, after every other operation of the group.</summary>
            internal void Collect(int changeIndex)
            {
                if (_retiredOrder.Count == 0)
                    return;
                var before = _run._pending.Select(operation => new OperationDependency(operation.OperationId)).ToArray();
                var id = _run.Id($"alpha:{_rule.Guid:N}:collect");
                _run.Add(new OperationEnvelope(id, PhonologicalRuleDeleteOperationKinds.CollectUnusedFeatConstraints, target: CanonicalId.FromGuid(_rule.Guid),
                    after: JsonSerializer.SerializeToElement(new { constraints = _retiredOrder.Select(guid => CanonicalId.FromGuid(guid).Value).ToArray() }),
                    dependsOn: before), changeIndex);
            }
        }
    }
}
