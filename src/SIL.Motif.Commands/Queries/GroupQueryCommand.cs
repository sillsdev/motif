using SIL.LCModel;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.LCModel.DomainServices;

namespace SIL.Motif.Commands.Queries;

/// <summary>Groups the words of the project's Texts by candidate stem. It reads a saved copy of the project and never writes.</summary>
public static class GroupQueryCommand
{
    private const int IncorrectSpelling = 2;

    /// <summary>Reads the Texts and the lexicon and groups the words the request selects.</summary>
    public static CommandOutcome<GroupResponse> Run(GroupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var asOf = string.IsNullOrWhiteSpace(request.AsOf) ? null : request.AsOf.Trim();
        if (asOf is not null && asOf != "baseline" && !CanonicalId.TryParse(asOf, out _))
            return Refuse(RefusalCodes.GroupAsOfUnavailable, FailureReason.Refused,
                $"'{asOf}' is neither 'baseline' nor a Proposal id. Revision digests cannot be resolved yet.");
        if (request.CursorOffset() < 0)
            return Refuse(RefusalCodes.PagingInvalid, FailureReason.InvalidArgument, "The cursor must be the nextCursor a previous page returned.");
        var spec = new GroupSpec(request.Kind, request.Kind == WordGrouping.Suffix ? request.Ending
            : request.Kind == WordGrouping.Prefix ? request.Start : null, request.Pattern, request.ParseState,
            request.Limit, request.Cursor, request.MinStem);
        if (request.Kind == WordGrouping.Suffix && request.Start is not null ||
            request.Kind == WordGrouping.Prefix && request.Ending is not null)
            return Refuse(RefusalCodes.GroupRequestInvalid, FailureReason.InvalidArgument,
                "Use --ending with group suffix and --start with group prefix.");
        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<GroupResponse>.Refused(located.Refusal!);
        try
        {
            if (asOf is null)
                return ProjectStoreCommand.RunOperation(request.ProjectPath, () =>
                    ProjectReadCache.ReadSaved(located.Value!, (_, cache) => Group(cache, located.Value!, request, spec, "saved project")));
            return ProjectStoreCommand.ReadExisting(request.ProjectPath, Refuse(RefusalCodes.GroupAsOfUnavailable,
                FailureReason.Refused, "The project has no Motif store, so there is no Baseline or Trial to read as of."),
                (database, project) => ReadAsOf(database, project, request, spec, asOf));
        }
        catch (QueryRefusalException refused)
        {
            return CommandOutcome<GroupResponse>.Refused(refused.Refusal);
        }
        catch (ArgumentException error)
        {
            return Refuse(RefusalCodes.GroupRequestInvalid, FailureReason.InvalidArgument, error.Message);
        }
    }

    private static CommandOutcome<GroupResponse> ReadAsOf(SIL.Motif.Host.Store.MotifDatabase database, ProjectLocator project,
        GroupRequest request, GroupSpec spec, string asOf)
    {
        var key = ProjectWorkspaceKey.Compute(project);
        string path;
        string label;
        if (asOf == "baseline")
        {
            var baseline = new BaselineRepository(database).GetCurrentPublished(key);
            if (baseline is null)
                return Refuse(RefusalCodes.GroupAsOfUnavailable, FailureReason.Refused,
                    "The project has no current Baseline. Run 'motif baseline capture --project <fwdata>' first.");
            path = baseline.FwDataPath;
            label = "baseline";
        }
        else
        {
            var trial = Trials.LatestFor(database, project, asOf);
            if (trial is null)
                return Refuse(RefusalCodes.GroupAsOfUnavailable, FailureReason.Refused, "No Trial is recorded for this Proposal.");
            var scratch = TrialScratchPath.ProjectFile(TrialScratchPath.ForJob(trial.JobId));
            if (scratch is null)
                return Refuse(RefusalCodes.GroupAsOfUnavailable, FailureReason.Refused,
                    "The Trial's details were cleared. Run the Trial again to bring them back.");
            path = scratch;
            label = "proposal " + asOf;
        }
        try
        {
            using var reader = BaselineReadCache.Open(path);
            return Group(reader.Cache, project, request, spec, label);
        }
        catch (QueryRefusalException refused)
        {
            return CommandOutcome<GroupResponse>.Refused(refused.Refusal);
        }
        catch (ArgumentException error)
        {
            return Refuse(RefusalCodes.GroupRequestInvalid, FailureReason.InvalidArgument, error.Message);
        }
    }

    private static CommandOutcome<GroupResponse> Group(LcmCache cache, ProjectLocator project, GroupRequest request,
        GroupSpec spec, string readAt)
    {
        var foldSpec = string.IsNullOrWhiteSpace(request.Fold) ? QueryFold.Default : request.Fold;
        var resolved = QueryFold.ResolveProject(foldSpec, FormsQueryCommand.ProjectFold(project));
        var fold = QueryFold.Compile(resolved);
        var corpus = ReadCorpus(cache, out var notes);
        var response = WordGrouping.Group(corpus, ReadLexiconStems(cache), spec, fold, foldSpec, readAt, notes);
        return CommandOutcome<GroupResponse>.Success(response);
    }

    private static List<CorpusWord> ReadCorpus(LcmCache cache, out List<string> notes)
    {
        var counts = new Dictionary<Guid, int>();
        var forms = new Dictionary<Guid, (string Form, string State)>();
        var staleParagraphs = 0;
        var misspelled = 0;
        foreach (var text in cache.ServiceLocator.GetInstance<ITextRepository>().AllInstances())
            foreach (var paragraph in text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>() ?? Enumerable.Empty<IStTxtPara>())
            {
                if (!paragraph.ParseIsCurrent) staleParagraphs++;
                foreach (var occurrence in SegmentServices.GetAnalysisOccurrences(paragraph))
                {
                    var wordform = occurrence.Analysis switch
                    {
                        IWfiGloss gloss => (IWfiWordform)gloss.Owner.Owner,
                        IWfiAnalysis analysis => (IWfiWordform)analysis.Owner,
                        IWfiWordform bare => bare,
                        _ => null,
                    };
                    if (wordform is null) continue;
                    counts[wordform.Guid] = counts.GetValueOrDefault(wordform.Guid) + 1;
                    if (forms.ContainsKey(wordform.Guid)) continue;
                    var form = WritingSystemTextReader.First(cache, wordform.Form).Text;
                    if (wordform.SpellingStatus == IncorrectSpelling) { misspelled++; form = string.Empty; }
                    forms[wordform.Guid] = (form, StateOf(wordform));
                }
            }
        notes = [];
        if (staleParagraphs > 0)
            notes.Add($"{staleParagraphs} paragraphs have no current word analysis, so their words are not in the corpus.");
        if (misspelled > 0) notes.Add($"{misspelled} wordforms marked incorrectly spelled are left out.");
        return [.. forms.Where(item => item.Value.Form.Length > 0)
            .Select(item => new CorpusWord(item.Value.Form, item.Value.State, counts[item.Key]))];
    }

    private static string StateOf(IWfiWordform wordform)
    {
        var approved = wordform.HumanApprovedAnalyses.ToHashSet();
        if (approved.Count > 0) return WordGrouping.Approved;
        var disapproved = wordform.HumanDisapprovedParses.ToHashSet();
        return wordform.AnalysesOC.Any(analysis => !disapproved.Contains(analysis)) ? WordGrouping.Parsed : WordGrouping.Unparsed;
    }

    private static IEnumerable<string> ReadLexiconStems(LcmCache cache)
    {
        var stems = new List<string>();
        foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances())
            foreach (var form in entry.AlternateFormsOS.Concat(entry.LexemeFormOA is { } lexeme ? [lexeme] : []).OfType<IMoStemAllomorph>())
                foreach (var ws in form.Form.AvailableWritingSystemIds)
                    if (form.Form.get_String(ws)?.Text is { Length: > 0 } text) stems.Add(text);
        return stems;
    }

    private static CommandOutcome<GroupResponse> Refuse(string code, FailureReason reason, string message) =>
        CommandOutcome<GroupResponse>.Refused(new Refusal(code, reason, message));
}
