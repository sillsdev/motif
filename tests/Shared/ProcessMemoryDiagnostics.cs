using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessMemoryDiagnostics
{
    public const string EnableVariable = "MOTIF_SCALE_DIAGNOSTICS";

    public static void WriteStartupCheckpoint(string stage)
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable(EnableVariable) != "1") return;
        Console.WriteLine(FormatMacCheckpoint(stage));
    }

    [SupportedOSPlatform("macos")]
    public static Sampler CreateSampler() => new();

    [SupportedOSPlatform("macos")]
    public static string FormatMacCheckpoint(string stage)
    {
        using var sampler = new Sampler();
        var memory = sampler.ReadSnapshot();
        var gc = GC.GetGCMemoryInfo();
        return string.Create(CultureInfo.InvariantCulture,
            $"SCALE CHECKPOINT | {stage} | {RuntimeInformation.ProcessArchitecture} | " +
            $"{GC.GetTotalMemory(false) / 1048576d:F1} MiB managed | " +
            $"heap {gc.HeapSizeBytes / 1048576d:F1} MiB, fragmented {gc.FragmentedBytes / 1048576d:F1} MiB | " +
            $"{memory.PhysicalFootprint / 1048576d:F1} MiB physical footprint | " +
            $"{memory.Resident / 1048576d:F1} MiB resident | " +
            $"{memory.Internal / 1048576d:F1} MiB internal | " +
            $"{memory.External / 1048576d:F1} MiB external | " +
            $"{memory.Device / 1048576d:F1} MiB device | " +
            $"{memory.Graphics / 1048576d:F1} MiB graphics | " +
            $"{sampler.TotalImageCount} dyld images; selected: {string.Join(", ", sampler.RelevantImages)}");
    }

    [SupportedOSPlatform("macos")]
    internal sealed class Sampler : IDisposable
    {
        private const int TaskVmInfoFlavor = 22;
        private const int TaskInfoCapacity = 1024;
        // TASK_VM_INFO uses Apple's 4-byte-packed task_vm_info layout.
        private const int DeviceOffset = 32;
        private const int InternalOffset = 48;
        private const int ExternalOffset = 64;
        private const int ResidentOffset = 16;
        private const int PhysicalFootprintOffset = 144;
        private const int GraphicsFootprintOffset = 272;
        private const uint MinimumPhysicalFootprintCount = 38;
        private static readonly IntPtr SystemLibrary = NativeLibrary.Load("libSystem.B.dylib");
        private static readonly Lazy<uint> CurrentTask = new(ReadCurrentTaskPort);
        private static readonly DyldImageCount ImageCount = Marshal.GetDelegateForFunctionPointer<DyldImageCount>(
            NativeLibrary.GetExport(SystemLibrary, "_dyld_image_count"));
        private static readonly DyldGetImageName ImageName = Marshal.GetDelegateForFunctionPointer<DyldGetImageName>(
            NativeLibrary.GetExport(SystemLibrary, "_dyld_get_image_name"));
        private readonly IntPtr _taskInfo = Marshal.AllocHGlobal(TaskInfoCapacity * sizeof(int));

        public uint TotalImageCount => ImageCount();

        public string[] RelevantImages
        {
            get
            {
                var images = new List<string>();
                for (uint index = 0; index < ImageCount(); index++)
                {
                    var image = Marshal.PtrToStringUTF8(ImageName(index));
                    if (image is null || !IsRelevantImage(image)) continue;
                    images.Add(Path.GetFileName(image));
                }
                return images.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            }
        }

        public TaskMemorySnapshot ReadSnapshot()
        {
            uint count = TaskInfoCapacity;
            var result = TaskInfo(CurrentTask.Value, TaskVmInfoFlavor, _taskInfo, ref count);
            if (result != 0 || count < MinimumPhysicalFootprintCount)
                throw new InvalidOperationException($"macOS task_info(TASK_VM_INFO) failed: {result}, count {count}.");
            return new TaskMemorySnapshot(
                Marshal.ReadInt64(_taskInfo, PhysicalFootprintOffset),
                Marshal.ReadInt64(_taskInfo, ResidentOffset),
                Marshal.ReadInt64(_taskInfo, InternalOffset),
                Marshal.ReadInt64(_taskInfo, ExternalOffset),
                Marshal.ReadInt64(_taskInfo, DeviceOffset),
                Marshal.ReadInt64(_taskInfo, GraphicsFootprintOffset));
        }

        private static bool IsRelevantImage(string image) =>
            image.Contains("icu", StringComparison.OrdinalIgnoreCase) ||
            image.Contains("skia", StringComparison.OrdinalIgnoreCase) ||
            image.Contains("metal", StringComparison.OrdinalIgnoreCase) ||
            image.Contains("appkit", StringComparison.OrdinalIgnoreCase) ||
            image.Contains("coregraphics", StringComparison.OrdinalIgnoreCase) ||
            image.Contains("iosurface", StringComparison.OrdinalIgnoreCase) ||
            image.Contains("opengl", StringComparison.OrdinalIgnoreCase) ||
            image.Contains("egl", StringComparison.OrdinalIgnoreCase) ||
            image.Contains("coreclr", StringComparison.OrdinalIgnoreCase) ||
            image.Contains("hostfxr", StringComparison.OrdinalIgnoreCase);

        private static uint ReadCurrentTaskPort()
        {
            var symbol = NativeLibrary.GetExport(SystemLibrary, "mach_task_self_");
            return unchecked((uint)Marshal.ReadInt32(symbol));
        }

        public void Dispose() => Marshal.FreeHGlobal(_taskInfo);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint DyldImageCount();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DyldGetImageName(uint index);

        [DllImport("libSystem.B.dylib", EntryPoint = "task_info")]
        private static extern int TaskInfo(uint targetTask, int flavor, IntPtr taskInfo, ref uint taskInfoCount);
    }

    internal readonly record struct TaskMemorySnapshot(
        long PhysicalFootprint, long Resident, long Internal, long External, long Device, long Graphics);
}
