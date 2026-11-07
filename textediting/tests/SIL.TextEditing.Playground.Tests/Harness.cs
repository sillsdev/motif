using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.TextEditing.Avalonia;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SIL.TextEditing.Playground.Tests
{
    public class TestApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Light;
        }

        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>One headless Avalonia session for the whole test assembly. Every test body runs on its UI thread.</summary>
    public static class Headless
    {
        static readonly Lazy<HeadlessUnitTestSession> Session = new Lazy<HeadlessUnitTestSession>(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

        public static int AvaloniaMajor => typeof(Application).Assembly.GetName().Version!.Major;

        public static void Run(Action body) => Session.Value.Dispatch(() => { body(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();

        public static void Run(Func<Task> body) => Session.Value.Dispatch(async () => { await body(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();

        public static void Pump() => Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The playground in a headless window, driven the way a person drives it: keys and text input go to the
    /// focused field, buttons are clicked by name, the labels and the inspector are read back as text.</summary>
    public sealed class Session : IDisposable
    {
        readonly Window _window;
        public Window Window => _window;

        /// <summary>A playground in a headless window. The tests drive the developer-mode controls, so developer mode is on unless
        /// <paramref name="developerMode"/> says otherwise.</summary>
        public Session(string scenario = Scenarios.MixedBidi, double width = 1100, double height = 700, bool developerMode = true)
        {
            View = new PlaygroundView(scenario, developerMode);
            _window = new Window { Width = width, Height = height, Content = View };
            _window.Show();
            Headless.Pump();
            Field.Focus();
            Headless.Pump();
        }

        public PlaygroundView View { get; }
        public RichTextField Field => View.Field;
        public DocumentEditor Editor => Field.Editor;
        public string WsLabel => View.WsLabelText;
        public string Inspector => View.InspectorText;
        public string Log => string.Join("\n", View.EventLog);

        public void Dispose() => _window.Close();

        public T Find<T>(string name) where T : Control =>
            _window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name) ?? throw new InvalidOperationException("No " + typeof(T).Name + " named " + name);

        /// <summary>A control by name in the logical tree, which also holds what a popup shows (the selection bar) whether or not it is open.</summary>
        public T FindLogical<T>(string name) where T : Control =>
            View.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name) ?? throw new InvalidOperationException("No " + typeof(T).Name + " named " + name);

        public void ClickLogical(string buttonName)
        {
            FindLogical<Button>(buttonName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Headless.Pump();
        }

        public void Click(string buttonName)
        {
            Find<Button>(buttonName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Headless.Pump();
        }

        public void Pick(string scenario)
        {
            var picker = Find<ComboBox>(PlaygroundView.Names.ScenarioPicker);
            picker.SelectedItem = scenario;
            Headless.Pump();
            Field.Focus();
            Headless.Pump();
        }

        public void Type(string text)
        {
            Field.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = Field });
            Headless.Pump();
        }

        public void Press(Key key, KeyModifiers modifiers = KeyModifiers.None, int times = 1)
        {
            for (int i = 0; i < times; i++)
            {
                Field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = Field });
                Field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers, Source = Field });
            }
            Headless.Pump();
        }

        /// <summary>The middle of the character at a model index, in the coordinates of the window.</summary>
        public Point PointAt(int paragraph, int index)
        {
            var pl = Field.ParagraphLayoutFor(paragraph);
            var li = pl.Lines[pl.LineIndexOf(index)];
            double x0 = li.Line.GetDistanceFromCharacterHit(new CharacterHit(index)), x1 = li.Line.GetDistanceFromCharacterHit(new CharacterHit(index, 1));
            return Field.TranslatePoint(new Point(li.X + (x0 + x1) / 2, Field.ParagraphTop(paragraph) + li.TextY + li.Line.Height / 2), _window)!.Value;
        }

        /// <summary>Presses the left button <paramref name="clicks"/> times at a window point (a double click is 2), as a
        /// mouse does; an earlier click elsewhere never counts towards this one.</summary>
        public void MouseClick(Point at, int clicks = 1, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            ResetClicks();
            for (int i = 0; i < clicks; i++) { _window.MouseDown(at, MouseButton.Left, modifiers); _window.MouseUp(at, MouseButton.Left, modifiers); }
            Headless.Pump();
        }

        /// <summary>Presses and releases the right button at a window point, as a mouse does for a context menu.</summary>
        public void RightClick(Point at)
        {
            ResetClicks();
            _window.MouseDown(at, MouseButton.Right);
            _window.MouseUp(at, MouseButton.Right);
            Headless.Pump();
        }

        public void MouseDrag(Point from, Point to)
        {
            ResetClicks();
            _window.MouseDown(from, MouseButton.Left);
            _window.MouseMove(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), RawInputModifiers.LeftMouseButton);
            _window.MouseMove(to, RawInputModifiers.LeftMouseButton);
            _window.MouseUp(to, MouseButton.Left);
            Headless.Pump();
        }

        void ResetClicks()
        {
            var far = new Point(_window.Width + 400, _window.Height + 400);
            _window.MouseDown(far, MouseButton.Left);
            _window.MouseUp(far, MouseButton.Left);
        }

        public void Caret(int paragraph, int offset)
        {
            Editor.SetCaret(new TextPosition(paragraph, offset));
            Headless.Pump();
        }

        public void Select(int paragraph, int from, int to)
        {
            Editor.SetCaret(new TextPosition(paragraph, from));
            Editor.SetCaret(new TextPosition(paragraph, to), true);
            Headless.Pump();
        }

        public async Task WaitClipboard()
        {
            await View.ClipboardIdle;
            Headless.Pump();
        }

        public string ParagraphText(int index) => Editor.Document.Paragraphs[index].Text;
        public object? ParagraphKey(int index) => Editor.Document.Paragraphs[index].Key;
    }
}
