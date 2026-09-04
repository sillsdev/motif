using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>The <c>report</c> verb: computing, storing and rendering one report kind over one Assessment.</summary>
/// <remarks>
/// <para>
/// <see cref="Catalog"/> is the whole registry: adding a kind means registering another
/// <see cref="IReportProducer"/> there, never adding a case here or a new verb (ADR 0042's Reports
/// amendment). <see cref="Assessors"/> deliberately registers no <see cref="SIL.Motif.Host.Parser.PanGlossParser"/>-backed
/// Assessor — every kind registered so far renders from an Assessment's own stored rows, and needs none.
/// </para>
/// <para>
/// A computed report is stored the moment it is produced (<see cref="ReportRecord.RenderedText"/>), so a
/// later reader of that row never has to ask an Assessor — whose binary may by then be gone — to read
/// yesterday's evidence again.
/// </para>
/// </remarks>
public static class ReportCommands
{
    /// <summary>Every registered report kind. Extend by adding an <see cref="IReportProducer"/> here.</summary>
    public static readonly ReportCatalog Catalog = new(new IReportProducer[]
    {
        new CoverageReportProducer(),
        new CorrectnessReportProducer(),
        new DifferenceReportProducer(),
    });

    /// <summary>Every report kind that may be asked for, as <c>report --list-kinds</c> prints it.</summary>
    public static CommandOutcome<ReportKindListResponse> ListKinds(ListReportKindsRequest request) =>
        CommandOutcome<ReportKindListResponse>.Success(new ReportKindListResponse(
            Catalog.All.Select(producer => new ReportKindResponse(producer.Kind, producer.Description)).ToArray()));

    /// <summary>
    /// Computes one report kind over one Assessment, stores the rendering, and returns it. Refuses, naming
    /// the reason, when the kind is unregistered (pinned by `AskingForAKindOutsideTheRegistry_Refuses`) or
    /// the Assessment was not collected in a way the kind can report from (pinned by
    /// `ACorrectnessReportOverAParseTimeAssessment_RefusesNamingTheReason`).
    /// </summary>
    public static CommandOutcome<ReportResponse> Produce(ProduceReportRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            AssessmentRecord record;
            try
            {
                record = new AssessmentRepository(database).Get(request.AssessmentId);
            }
            catch (KeyNotFoundException exception)
            {
                return CommandOutcome<ReportResponse>.Refused(new Refusal(
                    "report.assessment-not-found", FailureReason.NotFound, exception.Message,
                    Fact(("assessmentId", request.AssessmentId))));
            }

            IReportProducer producer;
            try
            {
                producer = Catalog.Resolve(request.Kind);
            }
            catch (KeyNotFoundException exception)
            {
                return CommandOutcome<ReportResponse>.Refused(new Refusal(
                    "report.invalid-kind", FailureReason.InvalidArgument, exception.Message,
                    Fact(("kind", request.Kind))));
            }

            RenderedReport rendered;
            try
            {
                rendered = producer.Produce(
                    record.ToReportable(), new ReportQuery(request.Word, request.Text), AssessorCatalog.Empty);
            }
            catch (ReportRefusalException exception)
            {
                return CommandOutcome<ReportResponse>.Refused(new Refusal(
                    "report.refused", FailureReason.Refused, exception.Message,
                    Fact(("assessmentId", request.AssessmentId), ("kind", request.Kind))));
            }

            var reportId = CanonicalId.Mint("report/").Value;
            var reportJson = JsonSerializer.Serialize(
                new { kind = rendered.Kind, assessmentId = request.AssessmentId, text = rendered.Text },
                MotifJson.CreateOptions());
            var evidenceJson = JsonSerializer.Serialize(new
            {
                assessmentId = request.AssessmentId,
                selectionSha256 = record.Selection.Sha256,
                grammarSourceSha256 = record.GrammarSourceSha256,
            }, MotifJson.CreateOptions());
            new ReportRepository(database).Save(new ReportRecord(
                reportId, record.ProposalId, request.AssessmentId, reportJson, evidenceJson, rendered.Kind,
                rendered.Text));

            return CommandOutcome<ReportResponse>.Success(
                new ReportResponse(reportId, request.AssessmentId, rendered.Kind, rendered.Text));
        });
    }

    private static Dictionary<string, string> Fact(params (string Key, string? Value)[] entries)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not null) facts[key] = value;
        }
        return facts;
    }
}
