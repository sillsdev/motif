namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Replies recorded from a real <c>pangloss serve</c> <c>generate</c> request with <c>trace:true</c>, byte for byte. They
/// were taken from the serve-generate-trace build (54cac0e86) on the public conformance fixture
/// <c>docs/divergences/evidence/071/kat-fixture-with-k.fwdata</c>: root 00000000-0000-0000-0000-000000000040 alone, with
/// the suffix 00000000-0000-0000-0000-000000000050, and with <c>wordTimeoutMs:0</c> and <c>maxWork:1</c>. An affix is a
/// rule here: role <c>rule</c>, a <c>ruleName</c>, and no shape, allomorph id or form guid.
/// </summary>
internal static class RecordedGenerate
{
    internal const string RootGuid = "00000000-0000-0000-0000-000000000040";
    internal const string RootMsa = "00000000-0000-0000-0000-000000000042";
    internal const string AffixGuid = "00000000-0000-0000-0000-000000000050";
    internal const string AffixMsa = "00000000-0000-0000-0000-000000000052";

    /// <summary>The root with its suffix: one seed, the surface <c>rannas</c>, one morph step.</summary>
    internal const string RootAffix = """
        {"complete":true,"id":"root-affix","limits":{"generationCap":50000000,"maxSeeds":10000,"maxTraceSteps":50000},"op":"generate","seedChoices":1,"seeds":[{"allomorphs":[{"allomorphId":1,"entryGuid":"00000000-0000-0000-0000-000000000040","formGuid":"00000000-0000-0000-0000-000000000041","msaGuid":"00000000-0000-0000-0000-000000000042","role":"root","ruleName":null,"shape":"ranna"},{"allomorphId":null,"entryGuid":"00000000-0000-0000-0000-000000000050","formGuid":null,"msaGuid":"00000000-0000-0000-0000-000000000052","role":"rule","ruleName":"-s","shape":null}],"seedKey":"r0/p0","surfaces":[{"alternatives":[],"derivations":1,"steps":[{"input":"ranna","output":"ranna+s","rule":{"guid":"00000000-0000-0000-0000-000000000052","id":"00000000-0000-0000-0000-000000000052","kind":"morph","name":"-s"},"stratum":"Morphology","subrule":0}],"stepsUnresolved":false,"surface":"rannas"}],"traceTruncated":false}],"stopReason":null,"traceSteps":12,"type":"result","workUnits":90}
        """;

    /// <summary>The root alone: complete, and no surface.</summary>
    internal const string RootAlone = """
        {"complete":true,"id":"root-alone","limits":{"generationCap":50000000,"maxSeeds":10000,"maxTraceSteps":50000},"op":"generate","seedChoices":1,"seeds":[{"allomorphs":[{"allomorphId":1,"entryGuid":"00000000-0000-0000-0000-000000000040","formGuid":"00000000-0000-0000-0000-000000000041","msaGuid":"00000000-0000-0000-0000-000000000042","role":"root","ruleName":null,"shape":"ranna"}],"seedKey":"r0/p0","surfaces":[],"traceTruncated":false}],"stopReason":null,"traceSteps":6,"type":"result","workUnits":14}
        """;

    /// <summary>Stopped by a zero deadline: incomplete, no seeds.</summary>
    internal const string Deadline = """
        {"complete":false,"id":"deadline","limits":{"generationCap":50000000,"maxSeeds":10000,"maxTraceSteps":50000},"op":"generate","seedChoices":0,"seeds":[],"stopReason":"deadline","traceSteps":0,"type":"result","workUnits":0}
        """;

    /// <summary>Stopped by a work cap of one: incomplete, the seed's allomorphs but no surface.</summary>
    internal const string WorkLimit = """
        {"complete":false,"id":"work-limit","limits":{"generationCap":1,"maxSeeds":10000,"maxTraceSteps":50000},"op":"generate","seedChoices":1,"seeds":[{"allomorphs":[{"allomorphId":1,"entryGuid":"00000000-0000-0000-0000-000000000040","formGuid":"00000000-0000-0000-0000-000000000041","msaGuid":"00000000-0000-0000-0000-000000000042","role":"root","ruleName":null,"shape":"ranna"},{"allomorphId":null,"entryGuid":"00000000-0000-0000-0000-000000000050","formGuid":null,"msaGuid":"00000000-0000-0000-0000-000000000052","role":"rule","ruleName":"-s","shape":null}],"seedKey":"r0/p0","surfaces":[],"traceTruncated":false}],"stopReason":"work_limit","traceSteps":1,"type":"result","workUnits":1}
        """;
}
