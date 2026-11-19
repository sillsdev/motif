using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Review;
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
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var violations = roots.SelectMany(root => Violations(root.Method, reached: reached)
            .Select(path => root.Name + ": " + path)).ToArray();
        Assert.True(violations.Length == 0, string.Join(Environment.NewLine, violations));
        // An exception no Read root reaches any more is dead weight that would silently exempt a future caller.
        var unreached = ExplicitWrites.Keys.Where(identity => !reached.Contains(identity)).ToArray();
        Assert.True(unreached.Length == 0, "Unreached exceptions: " + string.Join(", ", unreached));
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

    /// <summary>Read-class tools whose action may write, with the one method that holds the writing open.</summary>
    internal static readonly IReadOnlyDictionary<string, string> StoreWritingTools =
        new Dictionary<string, string>
        {
            ["motif_compare"] = Identity(typeof(CompareCommands), "ProduceStored", typeof(ProduceComparisonRequest)),
            ["motif_report"] = Identity(typeof(ReportCommands), "ProduceStored", typeof(ProduceReportRequest)),
            ["motif_parsimony_view"] = Identity(typeof(ParsimonyViewsCommand), "ReadWithArtifactLease",
                typeof(ReadParsimonyViewRequest), typeof(CancellationToken)),
        };

    private static readonly Dictionary<string, string> ExplicitWrites = new()
    {
        [StoreWritingTools["motif_compare"]] = "Persists a newly computed Difference Assessment.",
        [StoreWritingTools["motif_report"]] = "Persists the generated report rendering.",
        [StoreWritingTools["motif_parsimony_view"]] = "Maintains a persisted artifact reader lease.",
        [Identity(typeof(ReadStateCommands), "Change", typeof(WordReadStateRequest))] =
            "Explicit IsRead requests change saved occurrence marks.",
        [Identity(typeof(JobWait), "CancelJob", typeof(string), typeof(string), typeof(string),
            typeof(CancellationToken), typeof(long?))] =
            "A cancelled wait explicitly cancels its durable job.",
        [Identity(typeof(TrialReviewTools), "ReadTrace", typeof(ServerContext), typeof(ReviewReadRequest))] =
            "Explicit trace=true retains a new diagnostic trace.",
    };

    internal static string Identity(Type type, string name, params Type[] parameters) =>
        type.FullName + "." + name + "(" + string.Join(",", parameters.Select(parameter => parameter.FullName)) + ")";

    private static string Identity(MethodBase method) => method.DeclaringType!.FullName + "." + method.Name + "(" +
        string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName)) + ")";

    [Fact]
    public void StoreWritingReadToolsHaveAnExplicitWriterException()
    {
        foreach (var (name, identity) in StoreWritingTools)
        {
            Assert.Equal(AgentClass.Read, AgentTools.All.Single(tool => tool.Name == name).Class);
            Assert.True(ExplicitWrites.ContainsKey(identity));
        }
    }

    [Fact]
    public void ExplicitWriteExceptionsNameExistingPrivateWritersThatPrecheckTheStore()
    {
        foreach (var (identity, reason) in ExplicitWrites)
        {
            var method = Writers().SingleOrDefault(candidate => Identity(candidate) == identity);
            Assert.True(method is not null, identity + " names no private static method.");
            Assert.True(method.IsPrivate);
            Assert.False(string.IsNullOrWhiteSpace(reason));
            var reachable = Walk(method, allowExplicitWrites: false, reached: null).Select(step => step.Method).ToArray();
            Assert.Contains(reachable, CreatesStore);
            // The writing open is valid only behind the existing-store pre-check.
            Assert.True(reachable.Any(called => called.DeclaringType == typeof(ProjectStoreCommand) &&
                called.Name == nameof(ProjectStoreCommand.RefuseUnlessStoreIsCurrent)),
                identity + " opens a writing store without ProjectStoreCommand.RefuseUnlessStoreIsCurrent.");
        }
    }

    private static IEnumerable<MethodInfo> Writers() => new[]
        {
            typeof(CompareCommands), typeof(ReportCommands), typeof(ParsimonyViewsCommand), typeof(ReadStateCommands),
            typeof(JobWait), typeof(TrialReviewTools),
        }.SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));

    [Fact]
    public void ExceptionsAreKeyedByFullMethodIdentityNotByName()
    {
        // A second overload with the same name must not inherit the exemption.
        Assert.NotEqual(Identity(typeof(ReadStateCommands), "Change", typeof(WordReadStateRequest)),
            Identity(typeof(ReadStateCommands), "Change", typeof(WordReadStateRequest), typeof(int)));
    }

    [Fact]
    public void GuardFollowsHelperDelegatesAsyncStateMachinesAndInterfaceImplementations()
    {
        Assert.Contains(Violations(typeof(ReadStoreAccessContractTests).GetMethod(nameof(IndirectCreatingRead),
            BindingFlags.Static | BindingFlags.NonPublic)!), path => path.Contains("ProjectStoreCommand.Run"));
        Assert.Contains(Violations(typeof(ReadStoreAccessContractTests).GetMethod(nameof(ThroughInterface),
            BindingFlags.Static | BindingFlags.NonPublic)!), path => path.Contains("CreatingImplementation"));
        Assert.Empty(Violations(typeof(ReadStoreAccessContractTests).GetMethod(nameof(ExistingRead),
            BindingFlags.Static | BindingFlags.NonPublic)!));
    }

    private interface IProbeReader { object Read(); }

    private sealed class CreatingImplementation : IProbeReader
    {
        public object Read() => CreatingHelper();
    }

    private static object ThroughInterface(IProbeReader reader) => reader.Read();

    private static async Task IndirectCreatingRead()
    {
        await Task.Yield();
        await Task.Run(() => CreatingHelper());
    }

    private static object CreatingHelper() => ProjectStoreCommand.Run<object>("unused", "1.0",
        (_, _) => CommandOutcome<object>.Success(new object()));

    private static object ExistingRead() => ProjectStoreCommand.ReadExisting("unused",
        CommandOutcome<object>.Success(new object()), (_, _) => CommandOutcome<object>.Success(new object()));

    private static IEnumerable<string> Violations(MethodBase root, bool allowExplicitWrites = true,
        HashSet<string>? reached = null) =>
        Walk(root, allowExplicitWrites, reached).Where(step => CreatesStore(step.Method)).Select(step => step.Path);

    private static IEnumerable<(MethodBase Method, string Path)> Walk(MethodBase root, bool allowExplicitWrites,
        HashSet<string>? reached)
    {
        var seen = new HashSet<MethodBase>();
        var queue = new Queue<(MethodBase Method, string Path)>();
        queue.Enqueue((root, Label(root)));
        while (queue.TryDequeue(out var current))
        {
            var method = current.Method;
            if (!seen.Add(method)) continue;
            yield return current;
            if (CreatesStore(method)) continue;
            if (allowExplicitWrites && ExplicitWrites.ContainsKey(Identity(method)))
            {
                reached?.Add(Identity(method));
                continue;
            }
            if (IsReadBoundary(method)) continue;
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
                if (called is null) continue;
                yield return called;
                foreach (var implementation in Implementations(called)) yield return implementation;
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

    private static readonly Dictionary<MethodBase, MethodBase[]> ImplementationCache = new();

    /// <summary>Motif implementations a call through an interface or virtual member can dispatch to.</summary>
    private static MethodBase[] Implementations(MethodBase declared)
    {
        if (declared is not MethodInfo { DeclaringType: { } owner } method || !IsMotifCode(method) ||
            !owner.IsInterface && !(method.IsVirtual && !method.IsFinal) || method.IsStatic)
            return [];
        lock (ImplementationCache)
        {
            if (ImplementationCache.TryGetValue(declared, out var cached)) return cached;
            var found = new List<MethodBase>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()
                .Where(candidate => candidate.GetName().Name?.StartsWith("SIL.Motif.", StringComparison.Ordinal) == true))
            foreach (var type in Types(assembly).Where(type => !type.IsAbstract && !type.IsInterface && owner.IsAssignableFrom(type)))
            {
                if (owner.IsInterface)
                {
                    var map = type.GetInterfaceMap(owner);
                    var index = Array.IndexOf(map.InterfaceMethods, method);
                    if (index >= 0 && map.TargetMethods[index].DeclaringType?.Assembly == assembly)
                        found.Add(map.TargetMethods[index]);
                }
                else if (type.GetMethod(method.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly, null, method.GetParameters().Select(p => p.ParameterType).ToArray(), null)
                    is { } overriding && overriding.GetBaseDefinition() == method.GetBaseDefinition())
                    found.Add(overriding);
            }
            return ImplementationCache[declared] = found.Distinct().ToArray();
        }
    }

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception) { return exception.Types.OfType<Type>(); }
    }
}
