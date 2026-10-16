using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SIL.Motif.FakePanGloss;

internal static class PhonologyExamples
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4) return 64;
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "_phonology-behaviour.json");
        var settings = File.Exists(settingsPath) ? JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject() : new JsonObject();
        var mode = settings["mode"]?.GetValue<string>() ?? "succeed";
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "_phonology-argv.json"), JsonSerializer.Serialize(args));
        File.Copy(args[2], Path.Combine(AppContext.BaseDirectory, "_phonology-request.json"), true);
        if (settings["startedPath"] is { } started) File.WriteAllText(started.GetValue<string>(), "started");
        if (mode == "hang") Thread.Sleep(Timeout.Infinite);
        if (mode == "noResult") return 0;
        if (mode == "oversized")
        {
            using var file = File.Create(args[3]);
            file.SetLength(32 * 1024 * 1024 + 1);
            return 0;
        }
        var requestBytes = File.ReadAllBytes(args[2]);
        var request = JsonNode.Parse(requestBytes)!.AsObject();
        var examples = request["examples"]!.AsArray();
        var limits = request["limits"]!;
        var step = limits["stepCap"]!.GetValue<ulong>();
        var work = limits["workCap"]!.GetValue<ulong>();
        var time = limits["timeoutMs"]!.GetValue<uint>();
        var grammar = JsonNode.Parse(File.ReadAllText(args[1]))!;
        var surfaces = grammar["surfaces"]!.AsObject();
        var rows = new JsonArray();
        var agree = 0;
        for (var i = 0; i < examples.Count; i++)
        {
            var example = examples[i]!;
            var surface = surfaces[example["underlying"]!.GetValue<string>()]!.GetValue<string>().Normalize(NormalizationForm.FormD);
            var expected = example["expected"]!.AsArray();
            var flags = expected.Select(e => e!.GetValue<string>().Normalize(NormalizationForm.FormD) == surface).ToArray();
            if (flags.All(b => b)) agree++;
            rows.Add(new JsonObject
            {
                ["id"] = example["id"]!.DeepClone(), ["requestedContext"] = example["context"]!.DeepClone(),
                ["resolvedContext"] = example["context"]!.DeepClone(), ["underlying"] = example["underlying"]!.DeepClone(),
                ["expected"] = expected.DeepClone(), ["effectiveLimits"] = new JsonObject
                {
                    ["stepCap"] = Allocation(step, examples.Count, i), ["workCap"] = Allocation(work, examples.Count, i),
                    ["timeoutMs"] = time - (uint)Math.Min(i, time)
                },
                ["disposition"] = flags.All(b => b) ? "agree" : "disagree", ["completion"] = "complete",
                ["expectedObserved"] = JsonSerializer.SerializeToNode(flags), ["observed"] = surface, ["reason"] = null,
                ["steps"] = 1, ["workSteps"] = 3, ["capped"] = false, ["timedOut"] = false,
                ["orderedRuleIds"] = new JsonArray("first", "unused"), ["visitedRuleIds"] = new JsonArray("first"),
                ["appliedRuleIds"] = new JsonArray("first")
            });
        }
        var dependencies = new JsonArray();
        var store = Path.Combine(Path.GetDirectoryName(args[1])!, "WritingSystemStore");
        if (string.Equals(Path.GetExtension(args[1]), ".fwdata", StringComparison.OrdinalIgnoreCase) && Directory.Exists(store))
            foreach (var path in Directory.EnumerateFiles(store, "*.ldml").Where(p => Path.GetExtension(p) == ".ldml").OrderBy(p => Convert.ToHexString(Encoding.UTF8.GetBytes(Path.GetFileName(p))), StringComparer.Ordinal))
                dependencies.Add(new JsonObject { ["path"] = "WritingSystemStore/" + Path.GetFileName(path), ["sha256"] = Hash(File.ReadAllBytes(path)) });
        var response = new JsonObject
        {
            ["version"] = 1, ["grammarSha256"] = Hash(File.ReadAllBytes(args[1])), ["grammarDependencies"] = dependencies,
            ["requestSha256"] = new string('a', 64), ["requestFileSha256"] = Hash(requestBytes),
            ["build"] = "fake-phonology/v1", ["limits"] = new JsonObject
            {
                ["scope"] = "orderedPhonologyFolds", ["stepCap"] = step, ["workCap"] = work, ["timeoutMs"] = time,
                ["stepAllocation"] = "balancedByRequestOrder", ["timeAllocation"] = "sharedMonotonicDeadline"
            },
            ["measuredEntryCount"] = 7, ["rows"] = rows,
            ["counts"] = new JsonObject { ["agree"] = agree, ["disagree"] = examples.Count - agree, ["unavailable"] = 0, ["total"] = examples.Count },
            ["completionCounts"] = new JsonObject { ["complete"] = examples.Count, ["incomplete"] = 0, ["notStarted"] = 0 }
        };
        var first = rows[0]!;
        if (mode == "incompleteTrueFlags")
        {
            first["completion"] = "incomplete"; first["disposition"] = "unavailable";
            first["reason"] = "stepCap"; first["capped"] = true;
            response["counts"]!["agree"] = agree - 1; response["counts"]!["unavailable"] = 1;
            response["completionCounts"]!["complete"] = examples.Count - 1; response["completionCounts"]!["incomplete"] = 1;
        }
        if (mode == "zeroTimeout")
        {
            foreach (var row in rows)
            {
                row!["completion"] = "incomplete"; row["disposition"] = "unavailable";
                row["reason"] = "timeout"; row["timedOut"] = true; row["effectiveLimits"]!["timeoutMs"] = 0;
            }
            response["counts"] = new JsonObject { ["agree"] = 0, ["disagree"] = 0, ["unavailable"] = examples.Count, ["total"] = examples.Count };
            response["completionCounts"] = new JsonObject { ["complete"] = 0, ["incomplete"] = examples.Count, ["notStarted"] = 0 };
        }
        if (mode == "mutateOriginal") File.AppendAllText(settings["originalPath"]!.GetValue<string>(), " ");
        if (mode == "mutateGrammar") File.AppendAllText(args[1], " ");
        if (mode == "mutateRequest") File.AppendAllText(args[2], " ");
        if (mode == "mutateDependency") File.AppendAllText(Directory.EnumerateFiles(store).First(), " ");
        if (mode == "changeExecutable")
        {
            var executable = Environment.ProcessPath!;
            File.Move(executable, executable + ".old");
            File.Copy(executable + ".old", executable);
            File.AppendAllText(executable, "changed");
        }
        if (settings["set"] is JsonObject set)
            foreach (var pair in set)
            {
                var parts = pair.Key.Split('/');
                JsonNode parent = response;
                foreach (var part in parts[..^1]) parent = parent is JsonArray array ? array[int.Parse(part)]! : parent[part]!;
                if (parent is JsonArray lastArray) lastArray[int.Parse(parts[^1])] = pair.Value?.DeepClone();
                else parent[parts[^1]] = pair.Value?.DeepClone();
            }
        if (settings["remove"] is JsonArray remove)
            foreach (var field in remove) first.AsObject().Remove(field!.GetValue<string>());
        var json = response.ToJsonString();
        if (mode == "duplicate") json = json.Replace("\"steps\":1", "\"steps\":1,\"steps\":1", StringComparison.Ordinal);
        if (mode == "malformed") json = "{";
        File.WriteAllText(args[3], json);
        File.Copy(args[3], Path.Combine(AppContext.BaseDirectory, "_phonology-result.json"), true);
        return 0;
    }

    private static ulong Allocation(ulong total, int count, int index) => total / (ulong)count + ((ulong)index < total % (ulong)count ? 1UL : 0UL);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
