using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    public class ProjectApiTests
    {
        [Fact]
        public void A_public_stylesheet_reads_inherited_properties_from_a_memory_only_project()
        {
            using (var project = new ProjectFixture())
            {
                var cache = project.Cache;
                IStStyle? child = null;
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                {
                    var parent = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
                    cache.LangProject.StylesOC.Add(parent);
                    parent.Name = "Test paragraph";
                    var b = TsStringUtils.MakePropsBldr();
                    b.SetStrPropValue(1, "Charis SIL");
                    b.SetIntPropValues(6, 1, 18000);
                    parent.Rules = b.GetTextProps();
                    child = cache.ServiceLocator.GetInstance<IStStyleFactory>().Create();
                    cache.LangProject.StylesOC.Add(child);
                    child.Name = "Test child";
                    child.BasedOnRA = parent;
                    b = TsStringUtils.MakePropsBldr();
                    b.SetIntPropValues(3, 3, 1);
                    child.Rules = b.GetTextProps();
                });
                var sheet = new LcmStyleSheet();
                sheet.Init(cache, cache.LangProject.Hvo, LangProjectTags.kflidStyles);
                var properties = sheet.GetStyleRgch(child!.Name.Length, child.Name);
                Assert.Equal("Charis SIL", properties.GetStrPropValue(1));
                Assert.Equal(18000, properties.GetIntPropValues(6, out _));
                Assert.Equal(1, properties.GetIntPropValues(3, out _));
            }
        }
    }
}
