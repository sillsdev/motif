using System;
using System.IO;
using System.Reflection;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.Utils;
using SIL.WritingSystems;

namespace SIL.TextEditing.Project.Tests
{
    sealed class ProjectFixture : IDisposable
    {
        static readonly string ProcessRoot = Initialize();
        readonly string _root = Path.Combine(ProcessRoot, Guid.NewGuid().ToString("N"));
        public LcmCache Cache { get; }

        public ProjectFixture()
        {
            Directory.CreateDirectory(_root);
            Cache = LcmCache.CreateCacheWithNewBlankLangProj(new MemoryProject(), "en", "en", "en",
                new SilentLcmUI(new SingleThreadedSynchronizeInvoke()), new Directories(_root), new LcmSettings());
            NonUndoableUnitOfWorkHelper.Do(Cache.ActionHandlerAccessor, () =>
            {
                Cache.ServiceLocator.WritingSystemManager.GetOrSet("ar", out var arabic);
                arabic.RightToLeftScript = true;
                Cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Add(arabic);
                Cache.ServiceLocator.WritingSystems.DefaultVernacularWritingSystem = arabic;
            });
        }

        static string Initialize()
        {
            var root = Path.Combine(Path.GetTempPath(), "textediting-project-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            _ = string.Compare("a", "b", StringComparison.CurrentCulture);
            string? stage = Environment.GetEnvironmentVariable("TEXTEDITING_SIL_ICU_STAGE");
            Icu.Wrapper.SetPreferredIcu4cDirectory(string.IsNullOrEmpty(stage)
                ? Path.Combine(AppContext.BaseDirectory, "lib", "win-x64") : stage);
            Icu.Wrapper.ConfineIcuVersions(70, 70);
            Icu.Wrapper.DataDirectory = Path.Combine(AppContext.BaseDirectory, "IcuData", "icudt70l");
            string? previous = Environment.GetEnvironmentVariable("ICU_DATA");
            try
            {
                Environment.SetEnvironmentVariable("ICU_DATA", Icu.Wrapper.DataDirectory);
                CustomIcu.InitIcuDataDir();
            }
            finally { Environment.SetEnvironmentVariable("ICU_DATA", previous); }
            // The public SLDR initializer shares a cache; a private one keeps concurrent test processes isolated.
            var initialize = typeof(Sldr).GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(bool), typeof(string) }, null)
                ?? throw new InvalidOperationException("The pinned SLDR initializer no longer accepts a private cache.");
            initialize.Invoke(null, new object[] { true, Path.Combine(root, "sldr") });
            return root;
        }

        public void Dispose()
        {
            Cache.Dispose();
            Directory.Delete(_root, true);
        }

        public void Change(Action action) => NonUndoableUnitOfWorkHelper.Do(Cache.ActionHandlerAccessor, action);

        public IStStyle Style(string name, StyleType type, IStStyle? parent, Action<ITsPropsBldr> rules)
        {
            IStStyle? style = null;
            Change(() =>
            {
                style = Cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
                Cache.LangProject.StylesOC.Add(style);
                style.Name = name;
                style.Type = type;
                style.BasedOnRA = parent;
                var builder = TsStringUtils.MakePropsBldr();
                rules(builder);
                style.Rules = builder.GetTextProps();
            });
            return style!;
        }

        sealed class MemoryProject : IProjectIdentifier
        {
            public string Name => "TextEditingTests";
            public string UiName => Name;
            public string Path { get => string.Empty; set { } }
            public string ProjectFolder => string.Empty;
            public string Handle => Name;
            public string PipeHandle => Name;
            public BackendProviderType Type => BackendProviderType.kMemoryOnly;
        }

        sealed class Directories : ILcmDirectories
        {
            public Directories(string root) { ProjectsDirectory = root; TemplateDirectory = root; }
            public string ProjectsDirectory { get; }
            public string TemplateDirectory { get; }
        }
    }
}
