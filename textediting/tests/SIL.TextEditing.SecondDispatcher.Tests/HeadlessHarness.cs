using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SIL.TextEditing.SecondDispatcher.Tests
{
    public class TestApp : Application
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>One headless Avalonia session for this test assembly. Every test body that asks for it runs on its UI thread.</summary>
    public static class Headless
    {
        static readonly Lazy<HeadlessUnitTestSession> Session = new Lazy<HeadlessUnitTestSession>(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

        public static void Run(Action body) => Session.Value.Dispatch(() => { body(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();

        public static void Run(Func<Task> body) => Session.Value.Dispatch(async () => { await body(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();
    }
}
