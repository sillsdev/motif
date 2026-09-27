using System;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

[JsonConverter(typeof(JsonStringEnumConverter<GrammarDiagnosticLevel>))]
public enum GrammarDiagnosticLevel
{
    [JsonStringEnumMemberName("error")]
    Error,
    [JsonStringEnumMemberName("warning")]
    Warning,
    [JsonStringEnumMemberName("info")]
    Information,
}

[JsonConverter(typeof(JsonStringEnumConverter<GrammarFindingOrigin>))]
public enum GrammarFindingOrigin
{
    [JsonStringEnumMemberName("check")]
    Check,
    [JsonStringEnumMemberName("import")]
    Import,
}

[JsonConverter(typeof(JsonStringEnumConverter<GrammarWarningPartRole>))]
public enum GrammarWarningPartRole
{
    [JsonStringEnumMemberName("text")]
    Text,
    [JsonStringEnumMemberName("value")]
    Value,
    [JsonStringEnumMemberName("object")]
    Object,
    [JsonStringEnumMemberName("missing")]
    Missing,
}

[JsonConverter(typeof(JsonStringEnumConverter<FieldWorksLinkStatus>))]
public enum FieldWorksLinkStatus
{
    [JsonStringEnumMemberName("available")]
    Available,
    [JsonStringEnumMemberName("unavailable")]
    Unavailable,
}

[JsonConverter(typeof(JsonStringEnumConverter<FieldWorksLinkReason>))]
public enum FieldWorksLinkReason
{
    [JsonStringEnumMemberName("missing_project")]
    MissingProject,
    [JsonStringEnumMemberName("guid_not_recorded")]
    GuidNotRecorded,
    [JsonStringEnumMemberName("invalid_guid")]
    InvalidGuid,
    [JsonStringEnumMemberName("unsupported_kind")]
    UnsupportedKind,
}

[JsonConverter(typeof(JsonStringEnumConverter<GrammarFieldWorksProjectSource>))]
public enum GrammarFieldWorksProjectSource
{
    [JsonStringEnumMemberName("argument")]
    Argument,
    [JsonStringEnumMemberName("fwdata_path")]
    FwdataPath,
}

public static class GrammarDiagnosticLevelExtensions
{
    public static string ToWireValue(this GrammarDiagnosticLevel level) => level switch
    {
        GrammarDiagnosticLevel.Error => "error",
        GrammarDiagnosticLevel.Warning => "warning",
        GrammarDiagnosticLevel.Information => "info",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
