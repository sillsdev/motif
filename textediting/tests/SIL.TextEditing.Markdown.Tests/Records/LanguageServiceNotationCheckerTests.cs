using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Records
{
    public class LanguageServiceNotationCheckerTests
    {
        static LanguageServiceNotationChecker Checker(ITextLanguageService service)
            => new LanguageServiceNotationChecker(new Dictionary<string, ITextLanguageService> { ["environment"] = service });

        [Fact]
        public void ChecksActualTextAndPreservesDiagnosticsRangesAndFixes()
        {
            var service = new Immediate();
            var checker = Checker(service);
            var findings = checker.Check("environment", "invalid", "senses/1/environment");
            Assert.Equal("invalid", service.Text);
            Assert.Same(service.Diagnostics, findings);
            Assert.Equal(new LanguageRange(1, 3), findings[0].Range);
            Assert.NotNull(findings[0].Fix);
            Assert.Equal("notation.invalid", findings[0].Code);
        }

        [Fact]
        public void UnknownNotationIsNotAnalyzedAndNamesAreOrdinal()
        {
            var service = new Immediate();
            Assert.Empty(Checker(service).Check("Environment", "text", "field"));
            Assert.Null(service.Text);
        }

        [Fact]
        public void MappingIsCopiedAtConstruction()
        {
            var services = new Dictionary<string, ITextLanguageService> { ["environment"] = new Immediate() };
            var checker = new LanguageServiceNotationChecker(services);
            services.Clear();
            Assert.Single(checker.Check("environment", "text", "field"));
        }

        [Fact]
        public void PendingAnalysisIsRefusedWithoutWaiting()
        {
            var service = new Deferred();
            var error = Assert.Throws<ArgumentException>(() => Checker(service).Check("environment", "text", "field"));
            Assert.Contains("synchronously", error.Message);
            Assert.False(service.Pending.Task.IsCompleted);
            service.Pending.SetResult(LanguageAnalysis.Empty);
        }

        [Fact]
        public void CompletionIsCheckedOnEveryCall()
        {
            var service = new Deferred { Immediate = true };
            var checker = Checker(service);
            Assert.Empty(checker.Check("environment", "first", "field"));
            service.Immediate = false;
            Assert.Throws<ArgumentException>(() => checker.Check("environment", "second", "field"));
            service.Pending.SetResult(LanguageAnalysis.Empty);
        }

        [Fact]
        public void CompletedFailuresPropagate()
        {
            var service = new Deferred();
            var failure = new InvalidOperationException("service failure");
            service.Pending.SetException(failure);
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Checker(service).Check("environment", "text", "field")));
        }

        [Fact]
        public void CompletedCancellationPropagates()
        {
            var service = new Deferred();
            service.Pending.SetCanceled();
            Assert.ThrowsAny<OperationCanceledException>(() => Checker(service).Check("environment", "text", "field"));
        }

        [Fact]
        public void InvalidMappingsAreRefused()
        {
            Assert.Throws<ArgumentNullException>(() => new LanguageServiceNotationChecker(null!));
            Assert.Throws<ArgumentException>(() => new LanguageServiceNotationChecker(
                new Dictionary<string, ITextLanguageService> { [""] = new Immediate() }));
            Assert.Throws<ArgumentException>(() => new LanguageServiceNotationChecker(
                new Dictionary<string, ITextLanguageService> { ["x"] = null! }));
        }

        sealed class Immediate : TextLanguageService
        {
            public string? Text;
            public readonly IReadOnlyList<LanguageDiagnostic> Diagnostics = new[]
            {
                new LanguageDiagnostic(new LanguageRange(1, 3), DiagnosticSeverity.Error, "invalid", "notation.invalid",
                    fix: new LanguageFix("replace", new[] { new LanguageTextEdit(new LanguageRange(1, 3), "ok") }))
            };
            protected override LanguageAnalysis Analyze(string text)
            {
                Text = text;
                return new LanguageAnalysis(Array.Empty<LanguageToken>(), Diagnostics);
            }
        }

        sealed class Deferred : TextLanguageService
        {
            public bool Immediate;
            public readonly TaskCompletionSource<LanguageAnalysis> Pending = new TaskCompletionSource<LanguageAnalysis>();
            protected override LanguageAnalysis Analyze(string text) => LanguageAnalysis.Empty;
            public override Task<LanguageAnalysis> AnalyzeAsync(string text, CancellationToken cancellation)
                => Immediate ? Task.FromResult(LanguageAnalysis.Empty) : Pending.Task;
        }
    }
}
