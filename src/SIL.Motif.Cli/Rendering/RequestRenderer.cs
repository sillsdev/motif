using System.Text;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Cli.Rendering;

internal static class RequestRenderer
{
    public static string RenderList(RequestListResponse response)
    {
        if (response.Requests.Count == 0)
            return $"No Requests.{Environment.NewLine}";
        var text = new StringBuilder();
        text.AppendLine($"Requests {response.Offset + 1}-{response.Offset + response.Requests.Count} of {response.Total} " +
            $"({response.OpenCount} open):");
        foreach (var request in response.Requests)
        {
            text.AppendLine($"  {request.RequestId}  {request.Status}  {request.TargetKind}  {request.TargetLabel}");
            if (request.Note is { } note) text.AppendLine($"    The linguist wrote: \"{note}\"");
        }
        return text.ToString();
    }
}
