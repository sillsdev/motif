using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Host.Store;
using SIL.Motif.Mcp;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Mcp;

[Trait("MotifTestLevel", "Unit")]
public sealed class ReadStoreAccessContractTests
{
    private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields()
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => code.Value);

    [Fact]
    public void EveryReadToolAndCatalogHandlerUsesOnlyExistingStores()
    {
        var roots = AgentTools.All.Where(tool => tool.Class == AgentClass.Read)
            .Select(tool => (Name: tool.Name, Method: (MethodBase)tool.Run.Method))
            .Concat(CommandCatalog.All.Where(command => command.Agent == AgentClass.Read)
                .SelectMany(command => Handlers(command).Select(method => (Name: command.Name, Method: (MethodBase)method))));
        var violations = roots.SelectMany(root => Violations(root.Method)
            .Select(path => root.Name + ": " + path)).ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
    }

    private static MethodInfo[] Handlers(CommandDescriptor command)
    {
        var methods = typeof(CommandCatalog).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public))
            .Where(method => method.GetParameters().FirstOrDefault()?.ParameterType == command.RequestType &&
                method.ReturnType == typeof(CommandOutcome<>).MakeGenericType(command.ResponseType)).ToArray();
        Assert.NotEmpty(methods);
        return methods;
    }

    internal static readonly IReadOnlyDictionary<string, (Type Type, string Method)> StoreWritingTools =
        new Dictionary<string, (Type, string)>
        {
            ["motif_compare"] = (typeof(CompareCommands), "ProduceStored"),
            ["motif_report"] = (typeof(ReportCommands), "ProduceStored"),
            ["motif_parsimony_view"] = (typeof(ParsimonyViewsCommand), "ReadWithArtifactLease"),
        };

    [Fact]
    public void StoreWritingReadToolsHaveAnExplicitWriterException()
    {
        foreach (var (name, method) in StoreWritingTools)
        {
            Assert.Equal(AgentClass.Read, AgentTools.All.Single(tool => tool.Name == name).Class);
            Assert.True(ExplicitWrites.ContainsKey(method));
        }
    }

    [Fact]
    public void ExplicitWriteExceptionsNameExistingPrivateWriterMethods()
    {
        foreach (var ((type, name), reason) in ExplicitWrites)
        {
            var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            Assert.True(method.IsPrivate);
            Assert.False(string.IsNullOrWhiteSpace(reason));
            Assert.NotEmpty(Violations(method, allowExplicitWrites: false));
        }
    }

    [Fact]
    public void GuardFollowsHelperDelegatesAndAsyncStateMachines()
    {
        Assert.Contains(Violations(typeof(ReadStoreAccessContractTests).GetMethod(nameof(IndirectCreatingRead),
            BindingFlags.Static | BindingFlags.NonPublic)!), path => path.Contains("ProjectStoreCommand.Run"));
        Assert.Empty(Violations(typeof(ReadStoreAccessContractTests).GetMethod(nameof(ExistingRead),
            BindingFlags.Static | BindingFlags.NonPublic)!));
    }

    private static async Task IndirectCreatingRead()
    {
        await Task.Yield();
        await Task.Run(() => CreatingHelper());
    }

    private static object CreatingHelper() => ProjectStoreCommand.Run<object>("unused", "1.0",
        (_, _) => CommandOutcome<object>.Success(new object()));

    private static object ExistingRead() => ProjectStoreCommand.ReadExisting("unused",
        CommandOutcome<object>.Success(new object()), (_, _) => CommandOutcome<object>.Success(new object()));

    private static IEnumerable<string> Violations(MethodBase root, bool allowExplicitWrites = true)
    {
        var seen = new HashSet<MethodBase>();
        var queue = new Queue<(MethodBase Method, string Path)>();
        queue.Enqueue((root, Label(root)));
        while (queue.TryDequeue(out var current))
        {
            var method = current.Method;
            if (!seen.Add(method)) continue;
            if (CreatesStore(method))
            {
                yield return current.Path;
                continue;
            }
            if (IsReadBoundary(method) || allowExplicitWrites && IsExplicitWrite(method)) continue;
            if (method.GetCustomAttribute<AsyncStateMachineAttribute>() is { } asyncState)
            {
                var move = asyncState.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!;
                queue.Enqueue((move, current.Path + " -> " + Label(move)));
            }
            foreach (var called in References(method).Where(IsMotifCode))
                queue.Enqueue((called, current.Path + " -> " + Label(called)));
        }
    }

    private static bool CreatesStore(MethodBase method) =>
        method.DeclaringType == typeof(ProjectStoreCommand) && method.Name is "Run" or "OpenSession" ||
        method.DeclaringType == typeof(ProjectDatabaseCatalog) && method.Name == "OpenOwned" ||
        method.DeclaringType == typeof(MotifDatabase) && method.Name == "OpenOwned";

    private static bool IsReadBoundary(MethodBase method) =>
        method.DeclaringType == typeof(ProjectStoreCommand) && method.Name is "ReadExisting" or "OpenExistingReadOnlySession" ||
        method.DeclaringType == typeof(MotifDatabase) && method.Name is "ReadExisting" or "OpenExistingReadOnly" ||
        method.DeclaringType == typeof(CatalogTools) && method.Name == "Invoke";

    private static readonly Dictionary<(Type Type, string Method), string> ExplicitWrites = new()
    {
        [(typeof(CompareCommands), "ProduceStored")] = "Persists a newly computed Difference Assessment.",
        [(typeof(ReportCommands), "ProduceStored")] = "Persists the generated report rendering.",
        [(typeof(ParsimonyViewsCommand), "ReadWithArtifactLease")] = "Maintains a persisted artifact reader lease.",
        [(typeof(ReadStateCommands), "Change")] = "Explicit IsRead requests change saved occurrence marks.",
        [(typeof(JobWait), "CancelJobAsync")] = "A cancelled wait explicitly cancels its durable job.",
        [(typeof(TrialReviewTools), "ReadTrace")] = "Explicit trace=true retains a new diagnostic trace.",
    };

    private static bool IsExplicitWrite(MethodBase method) =>
        method.DeclaringType is { } type && ExplicitWrites.ContainsKey((type, method.Name));

    private static bool IsMotifCode(MethodBase method) =>
        method.DeclaringType?.Assembly.GetName().Name?.StartsWith("SIL.Motif.", StringComparison.Ordinal) == true;

    private static string Label(MethodBase method) => method.DeclaringType?.Name + "." + method.Name;

    private static IEnumerable<MethodBase> References(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) yield break;
        for (var offset = 0; offset < il.Length;)
        {
            var code = il[offset] == 0xfe ? Codes[unchecked((short)(0xfe00 | il[offset + 1]))] : Codes[il[offset]];
            offset += code.Size;
            if (code.OperandType == OperandType.InlineMethod)
            {
                var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset),
                    method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
                if (called is not null) yield return called;
            }
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4,
            };
        }
    }
}
