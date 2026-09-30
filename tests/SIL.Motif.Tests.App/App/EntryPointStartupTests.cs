using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Reads each built executable's entry point and requires the first thing it calls, past compiler-generated
/// plumbing, to be <see cref="CrashDialogs.Suppress"/>.
/// </summary>
/// <remarks>
/// A behavioural test cannot show this: every test process already suppresses the dialog at module load, and
/// the error mode it sets is inherited by any child a test starts. So the entry point's own code is read instead.
/// </remarks>
public sealed class EntryPointStartupTests
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    // The built executables, beside this suite's copies; Host resolves to the suite's, so types compare equal.
    private static readonly AssemblyLoadContext ProductContext = ProductLoadContext();

    [Theory]
    [InlineData("SIL.Motif.Worker")]
    [InlineData("motif")]
    public void NonAppExecutablesSuppressCrashDialogsAtStartup(string executable)
    {
        var assembly = ProductContext.LoadFromAssemblyPath(Path.Combine(BuildOutput.ProductDirectory, executable + ".dll"));
        var entryPoint = assembly.EntryPoint ?? throw new InvalidOperationException(executable + " has no entry point.");

        var first = FirstCall(UserCode(entryPoint));

        Assert.True(first is { Name: nameof(CrashDialogs.Suppress) } && first.DeclaringType == typeof(CrashDialogs),
            $"{executable}'s entry point first calls {first?.DeclaringType?.FullName}.{first?.Name}, not CrashDialogs.Suppress.");
    }

    [Fact]
    public void AppMainHandlesVelopackHooksBeforeNormalStartup()
    {
        var assembly = LoadAppAssembly();
        var entryPoint = assembly.EntryPoint ?? throw new InvalidOperationException("SIL.Motif.App has no entry point.");
        var calls = CalledMethods(UserCode(entryPoint)).ToArray();
        var build = Array.FindIndex(calls, IsVelopackBuild);
        var run = Array.FindIndex(calls, IsVelopackRun);
        var fastExit = Array.FindIndex(calls, IsVelopackFastExitHook);
        var normalStartup = Array.FindIndex(calls, IsAvaloniaStartup);
        var crashDialogs = Array.FindIndex(calls, IsCrashDialogSuppression);

        Assert.True(build == 0, "SIL.Motif.App must start Velopack before any other startup work.");
        Assert.True(run > build, "SIL.Motif.App's entry point must call VelopackApp.Run after Build.");
        Assert.True(fastExit > run && fastExit < normalStartup,
            "SIL.Motif.App must return for Velopack fast-exit hooks before starting Avalonia.");
        Assert.True(crashDialogs > fastExit,
            "Crash-dialog setup must not run before Velopack has handled its fast-exit hooks.");
    }

    [Theory]
    [InlineData("--veloapp-install")]
    [InlineData("--veloapp-updated")]
    [InlineData("--veloapp-obsolete")]
    [InlineData("--veloapp-uninstall")]
    public void AppRecognizesVelopackFastExitArguments(string hookArgument)
    {
        var program = LoadAppAssembly().GetType("SIL.Motif.App.Program", throwOnError: true)!;
        var method = program.GetMethod("IsVelopackFastExitHook", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(true, method!.Invoke(null, [new[] { hookArgument, "1.2.3" }]));
    }

    [Fact]
    public void AppDoesNotTreatNormalArgumentsAsVelopackHooks()
    {
        var program = LoadAppAssembly().GetType("SIL.Motif.App.Program", throwOnError: true)!;
        var method = program.GetMethod("IsVelopackFastExitHook", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(false, method!.Invoke(null, [new[] { "--smoke" }]));
    }

    [Fact]
    public async Task TheBuiltAppsSmokeRunRendersAWindowAndExitsCleanly()
    {
        var app = Path.Combine(BuildOutput.ProductDirectory,
            OperatingSystem.IsWindows() ? "SIL.Motif.App.exe" : "SIL.Motif.App");
        var start = new System.Diagnostics.ProcessStartInfo(app, "--smoke")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment.Remove("ICU_DATA");
        using var process = System.Diagnostics.Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(limit.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The app's --smoke run did not exit within two minutes.");
        }

        Assert.True(process.ExitCode == 0,
            $"The app's --smoke run exited {process.ExitCode}. stderr: {await error}");
    }

    [Fact]
    public void PackageSmokeScriptsUseTheSampleProjectBuilderOutput()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(BuildOutput.ProductDirectory, "..", ".."));
        foreach (var relativePath in new[] { "tools/package-smoke.ps1", "tools/package-smoke-unix.sh" })
        {
            var script = File.ReadAllText(Path.Combine(repositoryRoot, relativePath));

            Assert.Contains("SIL.Motif.SampleProjects", script, StringComparison.Ordinal);
            Assert.Contains("projectPath", script, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("deep-optional-affix-nesting", script, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static Assembly LoadAppAssembly() =>
        ProductContext.LoadFromAssemblyPath(Path.Combine(BuildOutput.ProductDirectory, "SIL.Motif.App.dll"));

    private static AssemblyLoadContext ProductLoadContext()
    {
        var context = new AssemblyLoadContext("Motif's built executables");
        context.Resolving += (loading, name) =>
            Path.Combine(BuildOutput.ProductDirectory, name.Name + ".dll") is var path && File.Exists(path)
                ? loading.LoadFromAssemblyPath(path)
                : null;
        return context;
    }

    // An async Main is reached through a generated <Main> stub, and its statements live in a state machine.
    private static MethodBase UserCode(MethodInfo entryPoint)
    {
        var main = entryPoint.Name == "<Main>"
            ? entryPoint.DeclaringType!.GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                entryPoint.GetParameters().Select(parameter => parameter.ParameterType).ToArray())!
            : entryPoint;
        return main.GetCustomAttribute<AsyncStateMachineAttribute>() is { } async
            ? async.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!
            : main;
    }

    private static MethodBase? FirstCall(MethodBase method) => CalledMethods(method).FirstOrDefault();

    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (var offset = 0; offset < il.Length;)
        {
            var code = il[offset] == 0xFE
                ? OpCodesByValue[unchecked((short)(0xFE00 | il[offset + 1]))]
                : OpCodesByValue[il[offset]];
            offset += code.Size;
            if (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj)
            {
                var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset),
                    method.DeclaringType?.GetGenericArguments(), null);
                // Top-level statements open by allocating their closure, which runs none of Motif's code.
                if (called is not null && called.DeclaringType?.IsDefined(typeof(CompilerGeneratedAttribute)) != true)
                    yield return called;
            }
            offset += OperandSize(code, il, offset);
        }
    }

    private static bool IsVelopackBuild(MethodBase method) =>
        method.DeclaringType?.FullName == "Velopack.VelopackApp" && method.Name == "Build";

    private static bool IsVelopackRun(MethodBase method) =>
        method.DeclaringType?.FullName == "Velopack.VelopackApp" && method.Name == "Run";

    private static bool IsVelopackFastExitHook(MethodBase method) =>
        method.DeclaringType?.FullName == "SIL.Motif.App.Program" && method.Name == "IsVelopackFastExitHook";

    private static bool IsAvaloniaStartup(MethodBase method) =>
        method.DeclaringType?.FullName == "SIL.Motif.App.Program" && method.Name == "BuildAvaloniaApp";

    private static bool IsCrashDialogSuppression(MethodBase method) =>
        method.DeclaringType == typeof(CrashDialogs) && method.Name == nameof(CrashDialogs.Suppress);

    private static int OperandSize(OpCode code, byte[] il, int offset) => code.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
        _ => 4,
    };
}
