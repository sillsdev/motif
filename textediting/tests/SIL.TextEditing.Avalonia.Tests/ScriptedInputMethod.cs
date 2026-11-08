using Avalonia.Input;
using Avalonia.Input.TextInput;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>
    /// A scripted input method for headless field tests. It requests the field's real Avalonia input-method client,
    /// sends preedit and selection changes through that client, and delivers commits and backspaces as routed input
    /// events. Scripts can inspect surrounding text and selection between actions just as an input method can; they
    /// model the platform's output, not keyboard conversion, candidate windows, or operating-system integration.
    /// </summary>
    internal sealed class ScriptedInputMethod
    {
        readonly RichTextField _field;
        readonly TextInputMethodClient _client;

        public ScriptedInputMethod(RichTextField field)
        {
            _field = field;
            var request = new TextInputMethodClientRequestedEventArgs
            {
                RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
                Source = field
            };
            field.RaiseEvent(request);
            _client = request.Client!;
        }

        public string SurroundingText => _client.SurroundingText;
        public TextSelection Selection { get => _client.Selection; set => _client.Selection = value; }
        public void Preedit(string? text, int? cursor = null) => _client.SetPreeditText(text, cursor);
        public void Commit(string text) => _field.Type(text);
        public void Cancel() => _client.SetPreeditText(null);
        public void Backspace() => _field.Press(Key.Back);
    }
}
