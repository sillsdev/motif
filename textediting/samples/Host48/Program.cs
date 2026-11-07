using System;
using System.Linq;
using System.Windows.Forms;
using Avalonia;
using Avalonia.Win32.Interoperability;
using SIL.TextEditing.Playground;
using SIL.TextEditing.Samples;

namespace SIL.TextEditing.Samples.Host48
{
    /// <summary>The usual way to host Avalonia in WinForms: a control host that claims the arrow keys while the
    /// Avalonia content has focus, so the form does not use them for navigation.</summary>
    sealed class KeyClaimingHost : WinFormsAvaloniaControlHost
    {
        static bool IsArrow(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The field's own claim on a key (ADR 0030): Enter, Tab and Esc while its completion popup is open, so they reach the field and not the form's
        /// default button, cancel button or Tab order; and Ctrl+Space. A host with a language service asks <c>RichTextField.WantsKey</c> in both overrides.</summary>
        bool FieldWants(Keys keyData)
        {
            var view = Content as PlaygroundView;
            if (view == null || !ContainsFocus) return false;
            var mods = global::Avalonia.Input.KeyModifiers.None;
            if ((keyData & Keys.Control) != 0) mods |= global::Avalonia.Input.KeyModifiers.Control;
            if ((keyData & Keys.Shift) != 0) mods |= global::Avalonia.Input.KeyModifiers.Shift;
            if ((keyData & Keys.Alt) != 0) mods |= global::Avalonia.Input.KeyModifiers.Alt;
            global::Avalonia.Input.Key key;
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Enter: key = global::Avalonia.Input.Key.Enter; break;
                case Keys.Tab: key = global::Avalonia.Input.Key.Tab; break;
                case Keys.Escape: key = global::Avalonia.Input.Key.Escape; break;
                case Keys.Space: key = global::Avalonia.Input.Key.Space; break;
                case Keys.PageUp: key = global::Avalonia.Input.Key.PageUp; break;
                case Keys.PageDown: key = global::Avalonia.Input.Key.PageDown; break;
                default: return false;
            }
            return view.Field.WantsKey(key, mods);
        }

        protected override bool IsInputKey(Keys keyData) => ((IsArrow(keyData) && ContainsFocus) || FieldWants(keyData)) || base.IsInputKey(keyData);

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => !((IsArrow(keyData) && ContainsFocus) || FieldWants(keyData)) && base.ProcessCmdKey(ref msg, keyData);
    }

    static class Program
    {
        /// <summary>FieldWorks' shape: a WinForms form, the playground embedded between two WinForms TextBoxes, to try Tab
        /// and key handoff and focus. Options: <c>--scenario "name"</c>, <c>--dev</c> (developer mode), <c>--minimized</c>.</summary>
        [STAThread]
        static void Main(string[] args)
        {
            AppBuilder.Configure<PlaygroundApp>().UsePlatformDetect().LogToTrace().SetupWithoutStarting();
            int s = Array.IndexOf(args, "--scenario");
            var view = new PlaygroundView(s >= 0 && s + 1 < args.Length ? args[s + 1] : Scenarios.MixedBidi, args.Contains("--dev"));
            var form = new Form
            {
                Text = "SIL.TextEditing playground in WinForms (net48, Avalonia " + PlaygroundApp.AvaloniaVersion + ")",
                ClientSize = new System.Drawing.Size(1100, 720),
            };
            var before = new TextBox { Dock = DockStyle.Top, Text = "WinForms TextBox (before)", TabIndex = 0 };
            var after = new TextBox { Dock = DockStyle.Bottom, Text = "WinForms TextBox (after)", TabIndex = 2 };
            var host = new KeyClaimingHost { Dock = DockStyle.Fill, TabStop = true, TabIndex = 1, Content = view };
            form.Controls.Add(host);
            form.Controls.Add(after);
            form.Controls.Add(before);
            if (args.Contains("--minimized")) form.WindowState = FormWindowState.Minimized;
            System.Windows.Forms.Application.Run(form);
        }
    }
}
