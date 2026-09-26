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
    [InlineData("SIL.Motif.App")]
    [InlineData("SIL.Motif.Worker")]
    [InlineData("motif")]
    public void EveryExecutableSuppressesCrashDialogsAtStartup(string executable)
    {
        var assembly = ProductContext.LoadFromAssemblyPath(Path.Combine(BuildOutput.ProductDirectory, executable + ".dll"));
        var entryPoint = assembly.EntryPoint ?? throw new InvalidOperationException(executable + " has no entry point.");

        var first = FirstCall(UserCode(entryPoint));

        Assert.True(first is { Name: nameof(CrashDialogs.Suppress) } && first.DeclaringType == typeof(CrashDialogs),
            $"{executable}'s entry point first calls {first?.DeclaringType?.FullName}.{first?.Name}, not CrashDialogs.Suppress.");
    }

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

    private static MethodBase? FirstCall(MethodBase method)
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
                if (called?.DeclaringType?.IsDefined(typeof(CompilerGeneratedAttribute)) != true) return called;
            }
            offset += OperandSize(code, il, offset);
        }
        return null;
    }

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
