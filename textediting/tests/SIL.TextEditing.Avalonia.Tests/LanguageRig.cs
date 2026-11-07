using System;
using System.Collections.Generic;
using Avalonia.Controls;
using SIL.TextEditing.Tests;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
        /// <summary>A field in a window with a service whose answers wait for the test: idle work is posted to a list the test runs.</summary>
        public sealed class LanguageRig
        {
            public readonly Window Window;
            public readonly RichTextField Field;
            public readonly FakeLanguageService Service;
            public readonly List<Action> Posts = new List<Action>();
            public readonly SimpleUndoStack Undo = new SimpleUndoStack();

            public LanguageRig(string text, FakeLanguageService? service = null, bool attach = true)
            {
                (Window, Field) = Fixtures.InWindow(OneParagraph(text));
                Service = service ?? new FakeLanguageService { Analysis = FakeLanguageService.Words("word") };
                Field.LanguageIdlePost = Posts.Add;
                Field.SingleLine = true;
                Undo.Attach(Field.Editor);
                if (attach) Field.LanguageService = Service;
            }

            /// <summary>Runs what is posted until nothing is left (what runs may post more).</summary>
            public void Idle()
            {
                for (int guard = 0; guard < 50 && Posts.Count > 0; guard++)
                {
                    var next = Posts[0];
                    Posts.RemoveAt(0);
                    next();
                }
                Headless.Pump();
            }

            public void Close() => Window.Close();
        }
}
