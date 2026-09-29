using System.Text.Json;

namespace SIL.Motif.Tests.App.Walkthrough;

internal enum WalkthroughStepKind { Click, Type, WaitFor, Highlight, Hold, Capture }

internal sealed record WalkthroughCallout(string AutomationId);

internal sealed record WalkthroughStep(
    string Id,
    WalkthroughStepKind Kind,
    string? AutomationId = null,
    string? Text = null,
    string? Condition = null,
    string? ExpectedText = null,
    int? TimeoutMs = null,
    int? DurationMs = null,
    IReadOnlyList<WalkthroughCallout>? Callouts = null,
    int? CropPadding = null);

internal sealed record WalkthroughScript(
    string Id, IReadOnlyList<WalkthroughStep> Steps, string Fixture = "fresh-project");

internal static class WalkthroughScriptLoader
{
    internal static WalkthroughScript Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            CheckProperties(root, "script", "id", "fixture", "steps");
            var id = ReadRequiredString(root, "id", "script");
            CheckId(id, "script id");
            var fixture = ReadRequiredString(root, "fixture", "script");
            CheckId(fixture, "script fixture");
            var items = Required(root, "steps", "script");
            if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                throw Invalid("script.steps must be a non-empty array");
            var steps = items.EnumerateArray().Select((step, index) => ReadStep(step, index)).ToArray();
            if (steps.Select(step => step.Id).Distinct(StringComparer.Ordinal).Count() != steps.Length)
                throw Invalid("step ids must be unique within a script");
            return new WalkthroughScript(id, steps, fixture);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Walkthrough script '{path}' is not valid JSON.", exception);
        }
    }

    internal static IReadOnlyList<string> Discover(string repositoryRoot) =>
        Directory.EnumerateFiles(Path.Combine(repositoryRoot, "walkthroughs"), "*.walkthrough.json")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static WalkthroughStep ReadStep(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid($"step {index} must be an object");
        var context = $"step {index}";
        var id = ReadRequiredString(element, "id", context);
        CheckId(id, $"step {index} id");
        context = $"step '{id}'";
        var kindText = ReadRequiredString(element, "kind", context);
        var kind = kindText switch
        {
            "click" => WalkthroughStepKind.Click,
            "type" => WalkthroughStepKind.Type,
            "waitFor" => WalkthroughStepKind.WaitFor,
            "highlight" => WalkthroughStepKind.Highlight,
            "hold" => WalkthroughStepKind.Hold,
            "capture" => WalkthroughStepKind.Capture,
            _ => throw Invalid($"{context} has unknown kind '{kindText}'"),
        };

        return kind switch
        {
            WalkthroughStepKind.Click => ReadTarget(element, id, kind),
            WalkthroughStepKind.Highlight => ReadTarget(element, id, kind),
            WalkthroughStepKind.Type => ReadType(element, id),
            WalkthroughStepKind.WaitFor => ReadWaitFor(element, id),
            WalkthroughStepKind.Hold => ReadHold(element, id),
            WalkthroughStepKind.Capture => ReadCapture(element, id),
            _ => throw Invalid($"{context} has unsupported kind '{kindText}'"),
        };
    }

    private static WalkthroughStep ReadTarget(JsonElement element, string id, WalkthroughStepKind kind)
    {
        var context = $"step '{id}'";
        if (kind == WalkthroughStepKind.Click)
        {
            CheckProperties(element, context, "id", "kind", "automationId");
            return new WalkthroughStep(id, kind, AutomationId: ReadAutomationId(element, context));
        }

        CheckProperties(element, context, "id", "kind", "automationId");
        return new WalkthroughStep(id, kind, AutomationId: ReadAutomationId(element, context));
    }

    private static WalkthroughStep ReadType(JsonElement element, string id)
    {
        CheckProperties(element, $"step '{id}'", "id", "kind", "automationId", "text");
        return new WalkthroughStep(id, WalkthroughStepKind.Type,
            AutomationId: ReadAutomationId(element, $"step '{id}'"),
            Text: ReadRequiredString(element, "text", $"step '{id}'"));
    }

    private static WalkthroughStep ReadWaitFor(JsonElement element, string id)
    {
        CheckProperties(element, $"step '{id}'", "id", "kind", "automationId", "condition", "expectedText", "timeoutMs");
        var condition = ReadRequiredString(element, "condition", $"step '{id}'");
        if (condition is not ("visible" or "hidden" or "enabled" or "text"))
            throw Invalid($"step '{id}' has unknown wait condition '{condition}'");
        var expected = element.TryGetProperty("expectedText", out var value)
            ? ReadString(value, $"step '{id}'.expectedText") : null;
        if ((condition == "text") != (expected is not null))
            throw Invalid($"step '{id}' needs expectedText exactly when condition is 'text'");
        return new WalkthroughStep(id, WalkthroughStepKind.WaitFor, ReadAutomationId(element, $"step '{id}'"),
            Condition: condition, ExpectedText: expected,
            TimeoutMs: ReadBoundedInteger(element, "timeoutMs", id, 1, 120000));
    }

    private static WalkthroughStep ReadHold(JsonElement element, string id)
    {
        CheckProperties(element, $"step '{id}'", "id", "kind", "durationMs");
        return new WalkthroughStep(id, WalkthroughStepKind.Hold,
            DurationMs: ReadBoundedInteger(element, "durationMs", id, 1, 60000));
    }

    private static WalkthroughStep ReadCapture(JsonElement element, string id)
    {
        CheckProperties(element, $"step '{id}'", "id", "kind", "durationMs", "callouts", "cropPadding");
        var items = Required(element, "callouts", $"step '{id}'");
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            throw Invalid($"step '{id}' callouts must be a non-empty array");
        var callouts = items.EnumerateArray().Select((item, index) =>
        {
            var context = $"step '{id}' callout {index}";
            CheckProperties(item, context, "automationId");
            return new WalkthroughCallout(ReadAutomationId(item, context));
        }).ToArray();
        if (callouts.Select(item => item.AutomationId).Distinct(StringComparer.Ordinal).Count() != callouts.Length)
            throw Invalid($"step '{id}' callout AutomationIds must be unique");
        return new WalkthroughStep(id, WalkthroughStepKind.Capture,
            DurationMs: ReadBoundedInteger(element, "durationMs", id, 100, 60000), Callouts: callouts,
            CropPadding: element.TryGetProperty("cropPadding", out _)
                ? ReadBoundedInteger(element, "cropPadding", id, 0, 256)
                : null);
    }

    private static string ReadAutomationId(JsonElement element, string context)
    {
        var value = ReadRequiredString(element, "automationId", context);
        return ValidateAutomationId(value, context);
    }

    private static string ValidateAutomationId(string value, string context)
    {
        if (value.Length == 0 || value[0] is < 'a' or > 'z' ||
            value.Any(character => !((character is >= 'a' and <= 'z') || char.IsAsciiDigit(character) || character == '-')))
            throw Invalid($"{context} has an invalid AutomationId '{value}'");
        return value;
    }

    private static int ReadBoundedInteger(JsonElement element, string name, string id, int minimum, int maximum)
    {
        var value = Required(element, name, $"step '{id}'");
        if (!value.TryGetInt32(out var result) || result < minimum || result > maximum)
            throw Invalid($"step '{id}' {name} must be an integer from {minimum} to {maximum}");
        return result;
    }

    private static string ReadRequiredString(JsonElement element, string name, string context) =>
        ReadString(Required(element, name, context), $"{context}.{name}");

    private static string ReadString(JsonElement value, string context)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Invalid($"{context} must be a non-empty string");
        return value.GetString()!;
    }

    private static JsonElement Required(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out var value)) throw Invalid($"{context} is missing '{name}'");
        return value;
    }

    private static void CheckProperties(JsonElement element, string context, params string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid($"{context} must be an object");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw Invalid($"{context} repeats '{property.Name}'");
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw Invalid($"{context} has unknown field '{property.Name}'");
        }
    }

    private static void CheckId(string value, string context)
    {
        if (value.Length == 0 || value[0] is < 'a' or > 'z' || value[^1] == '-' ||
            value.Contains("--", StringComparison.Ordinal) ||
            value.Any(character => !((character is >= 'a' and <= 'z') || char.IsAsciiDigit(character) || character == '-')))
            throw Invalid($"{context} must be lowercase kebab-case");
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
