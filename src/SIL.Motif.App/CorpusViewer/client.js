import { EditorState } from '@codemirror/state';
import { EditorView, Decoration } from '@codemirror/view';
import { basicSetup } from 'codemirror';

let view;
window.clearCorpusDocument = () => {
  if (view) view.destroy();
  view = undefined;
};
window.showCorpusDocument = ({ text, spans, sourceName, phrases }) => {
  window.clearCorpusDocument();
  const decorations = Decoration.set(spans.map(span =>
    Decoration.mark({ class: 'corpus-word' }).range(span.utf16Start, span.utf16Start + span.utf16Length)), true);
  view = new EditorView({
    parent: document.getElementById('document'),
    state: EditorState.create({ doc: text, extensions: [basicSetup, EditorState.lineSeparator.of('\n'),
      EditorState.readOnly.of(true), EditorView.editable.of(false), EditorState.phrases.of(phrases),
      EditorView.contentAttributes.of({ tabindex: '0', role: 'textbox', 'aria-readonly': 'true', 'aria-label': sourceName }),
      EditorView.decorations.of(decorations)] })
  });
};
window.showCorpusOccurrence = (start, length) => {
  if (!view) return;
  view.dispatch({ selection: { anchor: start, head: start + length },
    effects: EditorView.scrollIntoView(start, { y: 'center' }) });
  view.focus();
};
