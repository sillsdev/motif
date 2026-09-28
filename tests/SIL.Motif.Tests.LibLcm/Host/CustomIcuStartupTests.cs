using Icu.Normalization;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.Motif.Host.LcmUtils;
using Xunit;

namespace SIL.Motif.Tests.LibLcm.Host;

public sealed class CustomIcuStartupTests
{
    [Fact]
    public void StartupLoadsFieldWorksNormalizationData()
    {
        var customDataDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "IcuData", "icudt70l"));
        FwDataProjectLoader.Init();

        Assert.True(CustomIcu.HaveCustomIcuLibrary);
        Assert.StartsWith("70.", Icu.Wrapper.IcuVersion);
        Assert.NotEqual(customDataDirectory, Environment.GetEnvironmentVariable("ICU_DATA"));

        var fieldWorks = CustomIcu.GetIcuNormalizer(FwNormalizationMode.knmNFC);
        var stock = Normalizer2.GetInstance(null, "nfc", Normalizer2.Mode.COMPOSE);
        const string input = "\uF170\u0327";

        Assert.Equal("\uF170\u0327", stock.Normalize(input));
        Assert.Equal("\u0327\uF170", fieldWorks.Normalize(input));
    }
}
