using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace SIL.TextEditing.Markdown.Records
{
    internal enum YamlFrameKind { Scalar, Mapping, Sequence, Alias }

    /// <summary>One node of the YAML a record file's front matter holds, as YamlDotNet's parser reports it, with its place in the file. Nothing is
    /// resolved: a scalar is its text and its style, and the record schema decides what it means (ADR 0032).</summary>
    internal sealed class YamlFrameNode
    {
        public YamlFrameKind Kind;
        /// <summary>[Start, End) in the file, as UTF-16 offsets.</summary>
        public int Start, End;
        /// <summary>A scalar's value (after YAML's own unescaping and folding), or an alias's name.</summary>
        public string Value = string.Empty;
        public ScalarStyle Style;
        public bool Flow;
        public string? Anchor, Tag;
        public readonly List<KeyValuePair<YamlFrameNode, YamlFrameNode>> Pairs = new List<KeyValuePair<YamlFrameNode, YamlFrameNode>>();
        public readonly List<YamlFrameNode> Items = new List<YamlFrameNode>();

        public bool IsScalar => Kind == YamlFrameKind.Scalar;

        public override string ToString() => Kind + " " + Start + "-" + End + (Kind == YamlFrameKind.Scalar ? " \"" + Value + "\"" : string.Empty);
    }

    /// <summary>What YamlDotNet's parser made of one front matter: the first document's root, the start of any later document, and the syntax error
    /// that stopped it, if one did.</summary>
    internal sealed class YamlParse
    {
        public YamlFrameNode? Root;
        public readonly List<int> LaterDocuments = new List<int>();
        /// <summary>Why the parse stopped, or null when it did not: a syntax error, whatever YamlDotNet threw it as.</summary>
        public string? Error;
        /// <summary>Where it stopped: YamlDotNet's place for a <see cref="YamlException"/>, otherwise the end of the last event it gave.</summary>
        public int ErrorAt;
        /// <summary>The nodes nested deeper than <see cref="YamlTree.MaxDepth"/> were not built.</summary>
        public int? TooDeepAt;
    }

    /// <summary>For the tests: how much work the record reader did, counted and not timed (as <c>WriterWork</c> is for the Markdown writer). Per thread.</summary>
    internal static class RecordWork
    {
        [ThreadStatic] public static long Steps;
    }

    /// <summary>Builds <see cref="YamlFrameNode"/> trees with YamlDotNet's event parser (its parser layer only, never the serializer: ADR 0032).</summary>
    internal static class YamlTree
    {
        /// <summary>The most tokens or events one front matter of <paramref name="length"/> characters can give: every one but a few takes a character. A
        /// scanner that keeps returning without reading on (YamlDotNet's scanner does, after some syntax errors) is stopped here.</summary>
        static long Limit(int length) => 4L * length + 64;

        /// <summary>How deep the tree may nest; a record is never near it, and it keeps a hostile file from exhausting the stack.</summary>
        public const int MaxDepth = 64;

        /// <summary>Parses <paramref name="text"/>. Offsets are offsets of <paramref name="text"/>, so the caller passes the whole file cut at the
        /// closing fence, with the opening fence blanked.</summary>
        public static YamlParse Parse(string text)
        {
            var result = new YamlParse();
            var parser = new Parser(new StringReader(text));
            var stack = new List<YamlFrameNode>();
            int documents = 0;
            int skipDepth = 0;
            long events = 0, limit = Limit(text.Length);
            int lastEnd = 0;
            try
            {
                while (parser.MoveNext())
                {
                    RecordWork.Steps++;
                    if (++events > limit) throw new YamlException("The YAML parser did not read on.");
                    var e = parser.Current!;
                    lastEnd = (int)e.End.Index;
                    switch (e)
                    {
                        case DocumentStart start:
                            documents++;
                            if (documents > 1) result.LaterDocuments.Add((int)start.Start.Index);
                            break;
                        case Scalar s:
                            if (documents > 1 || skipDepth > 0) break;
                            Add(result, stack, new YamlFrameNode
                            {
                                Kind = YamlFrameKind.Scalar, Start = (int)s.Start.Index, End = (int)s.End.Index, Value = s.Value, Style = s.Style,
                                Anchor = s.Anchor.IsEmpty ? null : s.Anchor.Value, Tag = s.Tag.IsEmpty ? null : s.Tag.Value,
                            });
                            break;
                        case AnchorAlias a:
                            if (documents > 1 || skipDepth > 0) break;
                            Add(result, stack, new YamlFrameNode { Kind = YamlFrameKind.Alias, Start = (int)a.Start.Index, End = (int)a.End.Index, Value = a.Value.Value });
                            break;
                        case MappingStart m:
                        case SequenceStart q:
                        {
                            if (documents > 1) break;
                            if (skipDepth > 0 || stack.Count >= MaxDepth)
                            {
                                if (skipDepth == 0) result.TooDeepAt = (int)e.Start.Index;
                                skipDepth++;
                                break;
                            }
                            var node = new YamlFrameNode { Start = (int)e.Start.Index, End = (int)e.End.Index };
                            if (e is MappingStart ms)
                            {
                                node.Kind = YamlFrameKind.Mapping;
                                node.Flow = ms.Style == MappingStyle.Flow;
                                node.Anchor = ms.Anchor.IsEmpty ? null : ms.Anchor.Value;
                                node.Tag = ms.Tag.IsEmpty ? null : ms.Tag.Value;
                            }
                            else
                            {
                                var ss = (SequenceStart)e;
                                node.Kind = YamlFrameKind.Sequence;
                                node.Flow = ss.Style == SequenceStyle.Flow;
                                node.Anchor = ss.Anchor.IsEmpty ? null : ss.Anchor.Value;
                                node.Tag = ss.Tag.IsEmpty ? null : ss.Tag.Value;
                            }
                            Add(result, stack, node);
                            stack.Add(node);
                            break;
                        }
                        case MappingEnd _:
                        case SequenceEnd _:
                            if (documents > 1) break;
                            if (skipDepth > 0) { skipDepth--; break; }
                            if (stack.Count > 0)
                            {
                                var done = stack[stack.Count - 1];
                                stack.RemoveAt(stack.Count - 1);
                                done.End = Math.Max(done.End, LastEnd(done));
                                // A flow collection ends after its closing bracket; a block one ends with its last value.
                                if (done.Flow) done.End = Math.Max(done.End, (int)e.End.Index);
                            }
                            break;
                    }
                }
            }
            catch (YamlException error)
            {
                result.Error = error.Message;
                result.ErrorAt = error.Start.Index > 0 || lastEnd == 0 ? (int)error.Start.Index : lastEnd;
            }
            catch (Exception error) when (!(error is OutOfMemoryException) && !(error is StackOverflowException))
            {
                // YamlDotNet's scanner throws other exceptions on some syntax errors (an unclosed [ or { before more lines throws InvalidOperationException
                // from ScanPlainScalar, review of PR 147, P2-3). Whatever it throws is a syntax error of the file, not a failure of the reader.
                result.Error = "the parser stopped here (an unclosed bracket, or another error it could not place: " + error.Message + ").";
                result.ErrorAt = lastEnd;
            }
            return result;
        }

        static int LastEnd(YamlFrameNode node)
        {
            if (node.Kind == YamlFrameKind.Mapping && node.Pairs.Count > 0) return node.Pairs[node.Pairs.Count - 1].Value.End;
            if (node.Kind == YamlFrameKind.Sequence && node.Items.Count > 0) return node.Items[node.Items.Count - 1].End;
            return node.End;
        }

        static void Add(YamlParse result, List<YamlFrameNode> stack, YamlFrameNode node)
        {
            if (stack.Count == 0)
            {
                if (result.Root == null) result.Root = node;
                return;
            }
            var parent = stack[stack.Count - 1];
            if (parent.Kind == YamlFrameKind.Sequence) parent.Items.Add(node);
            else
            {
                // A mapping's events alternate key, value; a key waits in the parent's last pair until its value comes.
                int n = parent.Pairs.Count;
                if (n > 0 && ReferenceEquals(parent.Pairs[n - 1].Value, Pending)) parent.Pairs[n - 1] = new KeyValuePair<YamlFrameNode, YamlFrameNode>(parent.Pairs[n - 1].Key, node);
                else parent.Pairs.Add(new KeyValuePair<YamlFrameNode, YamlFrameNode>(node, Pending));
            }
        }

        /// <summary>The value of a key whose value has not come yet.</summary>
        static readonly YamlFrameNode Pending = new YamlFrameNode();

        /// <summary>The comments in <paramref name="text"/>, as [start, end) ranges from the <c>#</c> to the end of the comment. Found by a scan of their own
        /// with comments on, so the tree is built with YamlDotNet's default (comments skipped). The scan stops at the first error token (after some syntax
        /// errors YamlDotNet's scanner returns error tokens for ever, review of PR 147) and after <see cref="Limit"/> tokens; the reader asks only for
        /// text that parsed.</summary>
        public static List<int[]> Comments(string text)
        {
            var found = new List<int[]>();
            long tokens = 0, limit = Limit(text.Length);
            try
            {
                var scanner = new Scanner(new StringReader(text), skipComments: false);
                while (scanner.MoveNext())
                {
                    RecordWork.Steps++;
                    if (scanner.Current is YamlDotNet.Core.Tokens.Error || ++tokens > limit) break;
                    if (scanner.Current is YamlDotNet.Core.Tokens.Comment c) found.Add(new[] { (int)c.Start.Index, (int)c.End.Index });
                }
            }
            catch (Exception error) when (!(error is OutOfMemoryException) && !(error is StackOverflowException))
            {
                _ = error;
                // The parse reports the syntax error; comments found before it are still reported.
            }
            return found;
        }
    }
}
