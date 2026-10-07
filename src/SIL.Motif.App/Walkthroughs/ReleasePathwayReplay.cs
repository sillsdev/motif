using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Composition;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.App.Walkthroughs;

internal static class ReleasePathwayReplay
{
    private const string SeedKind = "synthetic-motif-sample-v1";
    private static readonly string[] RequiredScripts =
    [
        "first-run-setup-parse",
        "explained-word-card",
        "try-word-typing",
        "review-apply-refresh-parse",
        "handoff-cancel-retry",
    ];

    public static int Run(string scriptFolder)
    {
        var resultPath = Environment.GetEnvironmentVariable("MOTIF_RELEASE_RESULT");
        var results = new List<PathwayResult>();
        try
        {
            var seed = ReadSeedManifest();
            var scripts = RequiredScripts.Select(id => ReadScript(scriptFolder, id)).ToArray();
            var missing = scripts.Select(script => script.Fixture).Distinct(StringComparer.Ordinal)
                .Where(fixture => !seed.Projects.ContainsKey(fixture)).ToArray();
            if (missing.Length > 0)
                throw new InvalidDataException($"The seed manifest is missing fixtures: {string.Join(", ", missing)}.");

            Environment.SetEnvironmentVariable("MOTIF_WORKER_ROOT", Path.Combine(seed.Root, "app-worker"));
            Environment.SetEnvironmentVariable("MOTIF_RUNNER_NAMESPACE", "release-pathway-replay");
            Environment.SetEnvironmentVariable("MOTIF_RUNNER_IDLE_SECONDS", "2");
            Environment.SetEnvironmentVariable("MOTIF_WRITING_SYSTEM_REPOSITORY_PATH",
                Path.Combine(seed.Root, "writing-systems"));
            Environment.SetEnvironmentVariable("MOTIF_TEST_SLDR_CACHE_PATH", Path.Combine(seed.Root, "sldr-cache"));

            AppBuilder.Configure<App>()
                .With(UiFontFamilies.CurrentOptions)
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

            foreach (var script in scripts)
            {
                var samplePath = seed.Projects[script.Fixture];
                var output = Path.Combine(seed.Root, "handoff", script.Id);
                var captureRoot = Path.Combine(seed.Root, "captures", script.Id);
                results.Add(Replay(script, samplePath, output, captureRoot));
            }

            WriteResult(resultPath, new ReleasePathwayResult(true, null, results));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            WriteResult(resultPath, new ReleasePathwayResult(false, exception.ToString(), results));
            return 1;
        }
    }

    private static SeedManifest ReadSeedManifest()
    {
        var manifestPath = Environment.GetEnvironmentVariable("MOTIF_RELEASE_SEED_MANIFEST");
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new InvalidOperationException("MOTIF_RELEASE_SEED_MANIFEST is required for release replay.");
        manifestPath = Path.GetFullPath(manifestPath);
        var root = Path.GetDirectoryName(manifestPath)
            ?? throw new InvalidDataException("The seed manifest has no parent directory.");
        EnsureUnder(Path.GetFullPath(Path.GetTempPath()), root, "seed root");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("The release seed manifest is missing.", manifestPath);

        var manifest = JsonSerializer.Deserialize<SeedManifestFile>(File.ReadAllText(manifestPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The release seed manifest is empty.");
        if (manifest.Kind != SeedKind || manifest.Projects.Count != RequiredScripts.Length)
            throw new InvalidDataException("The release seed manifest does not describe the five synthetic fixtures.");
        var projects = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (fixture, candidate) in manifest.Projects)
        {
            if (!IsFixture(fixture)) throw new InvalidDataException($"Unknown release fixture '{fixture}'.");
            var fullPath = Path.GetFullPath(candidate);
            EnsureUnder(root, fullPath, $"fixture '{fixture}'");
            if (!File.Exists(fullPath) || !fullPath.EndsWith(".fwdata", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException($"The seeded project for '{fixture}' is missing.", fullPath);
            projects.Add(fixture, fullPath);
        }

        return new SeedManifest(root, projects);
    }

    private static bool IsFixture(string fixture) => fixture is
        "first-run-ready" or "explained-word-card" or "try-word-ready" or "apply-refresh-ready" or
        "handoff-cancel-ready";

    private static WalkthroughScript ReadScript(string scriptFolder, string id)
    {
        var root = Path.GetFullPath(scriptFolder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Walkthrough folder not found: {root}");
        // A walkthrough written for the tests' scripted parser has a release twin under release/ (ADR 0054).
        var releasePath = Path.GetFullPath(Path.Combine(root, "release", id + ".walkthrough.json"));
        var path = File.Exists(releasePath)
            ? releasePath
            : Path.GetFullPath(Path.Combine(root, id + ".walkthrough.json"));
        EnsureUnder(root, path, "walkthrough script");
        var script = WalkthroughScriptLoader.Load(path);
        if (!string.Equals(script.Id, id, StringComparison.Ordinal) || !IsFixture(script.Fixture))
            throw new InvalidDataException($"Walkthrough '{id}' has an unexpected id or fixture.");
        return script;
    }

    private static PathwayResult Replay(
        WalkthroughScript script, string projectPath, string handoffPath, string captureRoot)
    {
        Directory.CreateDirectory(handoffPath);
        Directory.CreateDirectory(captureRoot);
        var workerRoot = Path.Combine(Path.GetDirectoryName(captureRoot)!, "workers", script.Id);
        var writingSystems = Path.Combine(Path.GetDirectoryName(captureRoot)!, "writing-systems", script.Id);
        Environment.SetEnvironmentVariable("MOTIF_WORKER_ROOT", workerRoot);
        Environment.SetEnvironmentVariable("MOTIF_WRITING_SYSTEM_REPOSITORY_PATH", writingSystems);
        // The runner finds jobs through its own root's known projects; a CLI-seeded project was recorded elsewhere.
        var fullProject = Path.GetFullPath(projectPath);
        if (Worker.Store.KnownProjectRecorder.TryRecord(workerRoot, new Contract.Projects.ProjectLocator(
                fullProject, Path.GetFileNameWithoutExtension(fullProject))) is { } recordFailure)
            throw new InvalidOperationException($"Could not record '{fullProject}' for the replay's runner.", recordFailure);

        var commandSettings = CommandClientOptions.ForInstallation();
        if (string.IsNullOrWhiteSpace(commandSettings.ParserPath) || !File.Exists(commandSettings.ParserPath))
            throw new FileNotFoundException("The installed PanGloss executable could not be located.");
        var launchOptions = new JobRunnerLaunchOptions(workerRoot, commandSettings.ParserPath)
        {
            WorkerExecutable = ProcessRunnerLauncher.ConfiguredExecutable(),
            OwnerNamespace = "release-" + script.Id,
            IdleTimeout = TimeSpan.FromSeconds(2),
            Lease = TimeSpan.FromMinutes(2),
        };
        var composition = MotifAppComposition.Create(new MotifAppOptions(
            workerRoot,
            commandSettings.ParserPath,
            new ProcessRunnerLauncher(launchOptions),
            TimeProvider.System,
            new FixedProjectPicker(projectPath),
            new FixedFolderPicker(handoffPath),
            UserPreferencesStore: new MemoryUserPreferencesStore()));
        var window = composition.Window;
        window.Width = 1440;
        window.Height = 920;
        window.Show();
        window.ApplyTemplate();
        window.UpdateLayout();
        Pump();

        var captures = new List<string>();
        string? activeStep = null;
        try
        {
            foreach (var step in script.Steps)
            {
                activeStep = step.Id;
                switch (step.Kind)
                {
                    case WalkthroughStepKind.Click:
                        if (OperatingSystem.IsWindows() && script.Id == "review-apply-refresh-parse" &&
                            step.Id == "apply-change")
                            ClickAfterWindowsLockRefusal(window, composition.Workspace, projectPath,
                                step.AutomationId!);
                        else
                            Click(window, step.AutomationId!);
                        break;
                    case WalkthroughStepKind.Type:
                        Type(window, step.AutomationId!, step.Text!);
                        break;
                    case WalkthroughStepKind.WaitFor:
                        WaitFor(window, composition.Workspace, step);
                        break;
                    case WalkthroughStepKind.Highlight:
                        RequireVisible(Find(window, step.AutomationId!), step.AutomationId!);
                        break;
                    case WalkthroughStepKind.Hold:
                        Wait(TimeSpan.FromMilliseconds(step.DurationMs!.Value));
                        break;
                    case WalkthroughStepKind.Capture:
                        foreach (var callout in step.Callouts!)
                            RequireVisible(Find(window, callout.AutomationId), callout.AutomationId);
                        Wait(TimeSpan.FromMilliseconds(step.DurationMs!.Value));
                        var capture = Path.Combine(captureRoot, step.Id + ".png");
                        Capture(window, capture);
                        captures.Add(capture);
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported step kind '{step.Kind}'.");
                }
            }

            if (captures.Count == 0) throw new InvalidDataException($"Walkthrough '{script.Id}' has no captures.");
            var handoffFiles = Directory.Exists(handoffPath)
                ? Directory.EnumerateFiles(handoffPath, "*", SearchOption.AllDirectories).ToArray()
                : [];
            if (script.Id == "handoff-cancel-retry" && handoffFiles.Length == 0)
                throw new InvalidOperationException("The Handoff pathway finished without writing its files.");
            if (script.Id == "review-apply-refresh-parse" && composition.Workspace.Context.NeedsAssessment)
                throw new InvalidOperationException("The refreshed project did not publish its Assessment.");
            return new PathwayResult(script.Id, true, null, captures, handoffFiles);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Walkthrough '{script.Id}' failed at step '{activeStep}'.", exception);
        }
        finally
        {
            window.Close();
            Pump();
        }
    }

    private static void Click(Window window, string automationId)
    {
        var control = Find(window, automationId);
        if (!control.IsEffectivelyEnabled) throw new InvalidOperationException($"'{automationId}' is disabled.");
        var inputRoot = TopLevel.GetTopLevel(control) ?? window;
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), inputRoot)
            ?? throw new InvalidOperationException($"'{automationId}' is not positioned in its top level.");
        inputRoot.MouseMove(point);
        inputRoot.MouseDown(point, MouseButton.Left);
        inputRoot.MouseUp(point, MouseButton.Left);
        window.UpdateLayout();
        Pump();
    }

    private static void ClickAfterWindowsLockRefusal(
        Window window, WorkspaceShellViewModel workspace, string projectPath, string automationId)
    {
        var review = workspace.PageModel<ReviewPageModel>();
        using (new FileStream(projectPath + ".lock", FileMode.OpenOrCreate,
                   FileAccess.ReadWrite, FileShare.None))
        {
            Click(window, automationId);
            WaitUntil(() => review.ApplyRefusal is not null || review.Receipt is not null,
                TimeSpan.FromSeconds(30), "Apply did not answer while FieldWorks held the project");
            if (review.ApplyRefusal?.Code != "project.in-use")
                throw new InvalidOperationException("Apply did not report that the locked project is in use.");
        }

        // A refused Apply drops the check's results, so a person checks again before applying.
        WaitUntil(() => FindOptional(window, "motif-measure-changes") is { IsEffectivelyEnabled: true } || review.CanApply,
            TimeSpan.FromSeconds(30), "Neither Check nor Apply became available after the lock was released",
            () => review.ApplyBlockReason);
        if (!review.CanApply) Click(window, "motif-measure-changes");
        WaitUntil(() => review.CanApply, TimeSpan.FromMinutes(2),
            "Apply did not become available after the project lock was released",
            () => review.ApplyBlockReason);
        Click(window, automationId);
    }

    private static void Type(Window window, string automationId, string text)
    {
        var control = Find(window, automationId) as TextBox
            ?? throw new InvalidOperationException($"'{automationId}' is not a text box.");
        if (!control.Focus(NavigationMethod.Pointer))
            throw new InvalidOperationException($"'{automationId}' did not receive keyboard focus.");
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
        window.KeyTextInput(text);
        Pump();
        if (!string.Equals(control.Text, text, StringComparison.Ordinal))
            throw new InvalidOperationException($"'{automationId}' did not accept the scripted text.");
    }

    private static void WaitFor(MainWindow window, WorkspaceShellViewModel workspace, WalkthroughStep step)
    {
        var timeout = TimeSpan.FromMilliseconds(step.TimeoutMs!.Value);
        var deadline = DateTime.UtcNow + timeout;
        // Words and results arrive after the page opens, so the step waits for its control to exist too.
        while (FindOptional(window, step.AutomationId!) is null && DateTime.UtcNow < deadline)
        {
            Pump();
            Thread.Sleep(15);
        }
        var target = Find(window, step.AutomationId!);
        while (!Satisfies(target, workspace, step) && DateTime.UtcNow < deadline)
        {
            Pump();
            Thread.Sleep(15);
        }
        Pump();
        if (!Satisfies(target, workspace, step))
            throw new TimeoutException($"Step '{step.Id}' did not satisfy condition '{step.Condition}'.");
    }

    private static bool Satisfies(Control target, WorkspaceShellViewModel workspace, WalkthroughStep step) =>
        step.Condition switch
        {
            "visible" => IsVisible(target),
            "hidden" => !IsVisible(target),
            "enabled" => target.IsEffectivelyEnabled,
            "text" => string.Equals(ControlText(target), step.ExpectedText, StringComparison.Ordinal),
            "assessmentPublished" => IsVisible(target) && workspace.Assess.State == RunState.Completed &&
                workspace.Context.EvidencePublication.IsCompleted && !workspace.Context.NeedsAssessment &&
                workspace.PageModel<OverviewPageModel>().Overview is
                    { AssessmentId: not null, AssessedUtc: not null },
            "parseProgressVisibleOrCompleted" => IsVisible(target) || workspace.Assess.State == RunState.Completed,
            _ => throw new InvalidDataException($"Unknown wait condition '{step.Condition}'."),
        };

    private static bool IsVisible(Control control) => control.IsEffectivelyVisible &&
        control.Bounds.Width > 0 && control.Bounds.Height > 0;

    private static string? ControlText(Control control) => control switch
    {
        TextBlock text => text.Text,
        TextBox text => text.Text,
        ContentControl content => content.Content?.ToString(),
        _ => null,
    };

    private static void RequireVisible(Control control, string automationId)
    {
        if (!IsVisible(control)) throw new InvalidOperationException($"'{automationId}' is not visible.");
    }

    private static Control? FindOptional(Window window, string automationId) =>
        Controls(window).SingleOrDefault(control =>
            string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal));

    private static Control Find(Window window, string automationId)
    {
        var controls = Controls(window).Where(control =>
            string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal)).ToArray();
        return controls.Length switch
        {
            1 => controls[0],
            0 => throw new InvalidOperationException($"AutomationId '{automationId}' was not found. " +
                $"Similar ones on screen: {string.Join(", ", Similar(window, automationId))}."),
            _ => throw new InvalidOperationException($"AutomationId '{automationId}' is ambiguous."),
        };
    }

    // Names a few on-screen ids with the same prefix, so a script author can see which one to use.
    private static IEnumerable<string> Similar(Window window, string automationId)
    {
        var second = automationId.IndexOf('-', automationId.IndexOf('-') + 1);
        var prefix = second > 0 ? automationId[..(second + 1)] : automationId;
        var ids = Controls(window).Select(AutomationProperties.GetAutomationId).OfType<string>()
            .Distinct(StringComparer.Ordinal).ToArray();
        var similar = ids.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).Take(8).ToArray();
        return similar.Length > 0 ? similar : ids.Where(id => id.Contains("word", StringComparison.Ordinal)).Take(20);
    }

    private static IEnumerable<Control> Controls(Window window) => AllControls(window).Distinct();

    private static IEnumerable<Control> AllControls(Window window)
    {
        yield return window;
        // Rows a virtualizing list realizes from a template are visual children only, not logical ones.
        foreach (var control in window.GetLogicalDescendants().OfType<Control>()
                     .Concat(window.GetVisualDescendants().OfType<Control>()))
            yield return control;
        if (window.FindControl<Button>("ProjectMenuButton")?.Flyout is Flyout flyout &&
            flyout.Content is Control content)
        {
            yield return content;
            foreach (var control in content.GetLogicalDescendants().OfType<Control>()) yield return control;
        }
        foreach (var owned in window.OwnedWindows)
        {
            yield return owned;
            foreach (var control in owned.GetLogicalDescendants().OfType<Control>()) yield return control;
        }
    }

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var size = window.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) throw new InvalidOperationException("The window has no renderable size.");
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Round(size.Width), (int)Math.Round(size.Height)), new Vector(96, 96));
        bitmap.Render(window);
        using var stream = File.Create(path);
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private static void Wait(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            Pump();
            Thread.Sleep(15);
        }
        Pump();
    }

    private static void WaitUntil(Func<bool> predicate, TimeSpan timeout, string failure, Func<string>? detail = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            Pump();
            Thread.Sleep(15);
        }
        Pump();
        if (!predicate()) throw new TimeoutException(detail is null ? failure : $"{failure}: {detail()}");
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void EnsureUnder(string root, string candidate, string description)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullCandidate = Path.GetFullPath(candidate);
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullCandidate.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            !string.Equals(fullCandidate, fullRoot, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException($"The {description} must stay below '{fullRoot}'.");
    }

    private static void WriteResult(string? path, ReleasePathwayResult result)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (!result.Succeeded) Console.Error.WriteLine("MOTIF_RELEASE_RESULT was not set; result file not written.");
            return;
        }
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class FixedProjectPicker(string projectPath) : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(projectPath);
    }

    private sealed class FixedFolderPicker(string folderPath) : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(folderPath);
    }

    private sealed record SeedManifestFile(string Kind, Dictionary<string, string> Projects);
    private sealed record SeedManifest(string Root, Dictionary<string, string> Projects);
    private sealed record ReleasePathwayResult(bool Succeeded, string? Error, IReadOnlyList<PathwayResult> Pathways);
    private sealed record PathwayResult(
        string Id, bool Succeeded, string? Error, IReadOnlyList<string> Screenshots, IReadOnlyList<string> HandoffFiles);
}
