using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SIL.TextEditing.Avalonia.Tests
{
    public class TestApp : Application
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>One headless Avalonia session for the whole test assembly. Every test body runs on its UI thread.</summary>
    public static class Headless
    {
        static readonly Lazy<HeadlessUnitTestSession> Session = new Lazy<HeadlessUnitTestSession>(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

        /// <summary>Which Avalonia generation the tests run against: 11 (netstandard2.0 build) or 12 (net10.0 build).</summary>
        public static int AvaloniaMajor => typeof(Application).Assembly.GetName().Version!.Major;

        public static void Run(Action body) => Session.Value.Dispatch(() => { body(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();

        public static void Run(Func<Task> body) => Session.Value.Dispatch(async () => { await body(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();

        public static void Pump() => Dispatcher.UIThread.RunJobs();
    }

    public static class InputExtensions
    {
        public static void Press(this InputElement target, Key key, KeyModifiers modifiers = KeyModifiers.None)
        {
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = target });
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers, Source = target });
        }

        public static void Type(this InputElement target, string text)
        {
            target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = target });
        }
    }
}
