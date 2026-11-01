using System.Text;
using System.Text.Json;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli;

/// <summary>
/// Writes every decision evidence record as one JSON document, a page at a time, so the export holds one page of
/// rows in memory however many records the store holds.
/// </summary>
internal static class EvidenceExportCli
{
    internal const int DefaultPageSize = 500;

    /// <summary>Streams the document to <paramref name="outPath"/>, or to <paramref name="standardOutput"/> when none is given.</summary>
    internal static CommandResult Run(string project, string productVersion, string? tier, int pageSize,
        string? outPath, TextWriter standardOutput, bool asJson, Func<string, Stream>? createTemporary = null)
    {
        var readingPage = false;
        CommandOutcome<DecisionEvidenceExportResponse> ReadPage(string? after)
        {
            readingPage = true;
            var page = ProposalCommands.EvidenceExport(new EvidenceExportRequest(project, productVersion, tier, after, pageSize));
            readingPage = false;
            return page;
        }

        if (outPath is null)
        {
            var streamed = Write(ReadPage, new TextWriterStream(standardOutput));
            return streamed.Succeeded ? new CommandResult(0, string.Empty) : ProposalCommandRenderer.Render(streamed, asJson);
        }

        string? temporary = null;
        var moved = false;
        try
        {
            var fullPath = Path.GetFullPath(outPath);
            temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            CommandOutcome<DecisionEvidenceExportSummary> outcome;
            using (var file = createTemporary?.Invoke(temporary) ??
                              new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                outcome = Write(ReadPage, file);
                if (outcome.Succeeded && file is FileStream disk) disk.Flush(flushToDisk: true);
            }
            if (!outcome.Succeeded) return ProposalCommandRenderer.Render(outcome, asJson);
            File.Move(temporary, fullPath, overwrite: true);
            moved = true;
            return ProposalCommandRenderer.Render(
                CommandOutcome<DecisionEvidenceExportSummary>.Success(outcome.Value! with { Path = fullPath }), asJson);
        }
        // A page read leaves readingPage set when it throws, so only file operations are refused as unwritable.
        catch (Exception exception) when (!readingPage && exception is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return ProposalCommandRenderer.Render(CommandOutcome<DecisionEvidenceExportSummary>.Refused(new Refusal(
                RefusalCodes.EvidenceOutUnwritable, FailureReason.InvalidArgument,
                $"--out '{outPath}' could not be written: {exception.Message}")), asJson);
        }
        finally
        {
            if (!moved && temporary is not null) Discard(temporary);
        }
    }

    private static CommandOutcome<DecisionEvidenceExportSummary> Write(
        Func<string?, CommandOutcome<DecisionEvidenceExportResponse>> readPage, Stream sink)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        using var json = new Utf8JsonWriter(sink, new JsonWriterOptions { Indented = true, NewLine = "\n" });
        json.WriteStartObject();
        json.WriteString("format", DecisionEvidenceExport.Format);
        json.WriteStartArray("rows");
        string? after = null;
        do
        {
            var page = readPage(after);
            if (!page.Succeeded)
                return CommandOutcome<DecisionEvidenceExportSummary>.Refused(page.Refusal!);
            foreach (var row in page.Value!.Rows)
            {
                json.WriteRawValue(ProjectionJson.Serialize(row), skipInputValidation: true);
                counts[row.Tier] = counts.GetValueOrDefault(row.Tier) + 1;
                total++;
            }
            json.Flush();
            after = page.Value.Next;
        } while (after is not null);
        json.WriteEndArray();
        json.WriteNumber("total", total);
        json.WriteStartObject("tierCounts");
        foreach (var (name, count) in counts) json.WriteNumber(name, count);
        json.WriteEndObject();
        json.WriteEndObject();
        json.Flush();
        sink.Write("\n"u8);
        sink.Flush();
        return CommandOutcome<DecisionEvidenceExportSummary>.Success(
            new DecisionEvidenceExportSummary(string.Empty, total, counts));
    }

    private static void Discard(string temporary)
    {
        try { File.Delete(temporary); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Lets the JSON writer's UTF-8 bytes reach a text writer, such as standard output.</summary>
    private sealed class TextWriterStream(TextWriter target) : Stream
    {
        private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var chars = new char[_decoder.GetCharCount(buffer, flush: false)];
            var written = _decoder.GetChars(buffer, chars, flush: false);
            target.Write(chars, 0, written);
        }

        public override void Flush() => target.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
