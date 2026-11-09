using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SIL.TextEditing.Avalonia
{
    public partial class RichTextField
    {
        /// <summary>Entering the field is the library's job. When the host toolkit hands keyboard focus to the control that holds the field (Tab into a WinForms
        /// control host, say), Avalonia has no focused element and a key down is raised on the top level itself (a character is dropped). The field that last had
        /// focus in that top level takes focus on that key down, so the character after it and the keys that follow reach it, so a host does not have to forward focus.</summary>
        sealed class HostFocusRelay
        {
            static readonly ConditionalWeakTable<TopLevel, HostFocusRelay> Relays = new ConditionalWeakTable<TopLevel, HostFocusRelay>();
            readonly TopLevel _top;
            WeakReference<RichTextField>? _last;

            HostFocusRelay(TopLevel top)
            {
                _top = top;
                top.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble, handledEventsToo: false);
            }

            public static void Remember(RichTextField field)
            {
                if (TopLevel.GetTopLevel(field) is not { } top) return;
                var relay = Relays.GetValue(top, t => new HostFocusRelay(t));
                relay._last = new WeakReference<RichTextField>(field);
            }

            // An event raised on the top level may have been routed before the field took focus, so the field already holding focus counts too.
            RichTextField? Target()
            {
                var focused = _top.FocusManager?.GetFocusedElement();
                return _last != null && _last.TryGetTarget(out var f) && f.IsEffectivelyVisible && ReferenceEquals(TopLevel.GetTopLevel(f), _top)
                    && (focused == null || ReferenceEquals(focused, f)) ? f : null;
            }

            void OnKeyDown(object? sender, KeyEventArgs e)
            {
                if (!ReferenceEquals(e.Source, _top) || Target() is not { } field) return;
                field.Focus();
                // Not marked handled: Avalonia drops the character that follows a handled key down.
                field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = e.Key, KeyModifiers = e.KeyModifiers, PhysicalKey = e.PhysicalKey, KeySymbol = e.KeySymbol, Source = field });
            }
        }

        /// <summary>Called from <see cref="OnAttachedToVisualTree"/>: until a field has had focus, the first one attached is the one to focus.</summary>
        void WatchHostFocus()
        {
            if (TopLevel.GetTopLevel(this) is { } top && top.FocusManager?.GetFocusedElement() == null) HostFocusRelay.Remember(this);
        }

#if NET10_0_OR_GREATER
        protected override void OnGotFocus(FocusChangedEventArgs e)
#else
        protected override void OnGotFocus(GotFocusEventArgs e)
#endif
        {
            HostFocusRelay.Remember(this);
            base.OnGotFocus(e);
        }
    }
}
