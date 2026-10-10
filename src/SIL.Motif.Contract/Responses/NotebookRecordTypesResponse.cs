using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>One project Notebook record type, identified by its portable LibLCM GUID.</summary>
public sealed record NotebookRecordType(string Id, string Name);

/// <summary>The saved Notebook record types offered for an authored project judgment.</summary>
public sealed record NotebookRecordTypesResponse(IReadOnlyList<NotebookRecordType> RecordTypes);
