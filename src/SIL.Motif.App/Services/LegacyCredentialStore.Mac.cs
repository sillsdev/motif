using System.Runtime.InteropServices;

namespace SIL.Motif.App.Services;

public sealed partial class LegacyCredentialStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private static class MacConstants
    {
        private static readonly IntPtr SecurityHandle = NativeLibrary.Load(Security);
        private static readonly IntPtr CoreHandle = NativeLibrary.Load(CoreFoundation);
        internal static IntPtr Get(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(SecurityHandle, name));
        internal static IntPtr True => Marshal.ReadIntPtr(NativeLibrary.GetExport(CoreHandle, "kCFBooleanTrue"));
    }

    private T WithMacQuery<T>(Func<IntPtr, T> action)
    {
        var query = DictionaryCreate(IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero);
        var service = StringCreate(IntPtr.Zero, Target, 0x08000100);
        var account = StringCreate(IntPtr.Zero, "OpenAI", 0x08000100);
        try
        {
            DictionarySet(query, MacConstants.Get("kSecClass"), MacConstants.Get("kSecClassGenericPassword"));
            DictionarySet(query, MacConstants.Get("kSecAttrService"), service);
            DictionarySet(query, MacConstants.Get("kSecAttrAccount"), account);
            return action(query);
        }
        finally
        {
            Release(query);
            Release(service);
            Release(account);
        }
    }

    private void DeleteMac() => WithMacQuery(query => SecItemDelete(query));

    [LibraryImport(Security, EntryPoint = "SecItemDelete")]
    private static partial int SecItemDelete(IntPtr query);
    [LibraryImport(CoreFoundation, EntryPoint = "CFDictionaryCreateMutable")]
    private static partial IntPtr DictionaryCreate(IntPtr allocator, nint capacity, IntPtr keys, IntPtr values);
    [LibraryImport(CoreFoundation, EntryPoint = "CFDictionarySetValue")]
    private static partial void DictionarySet(IntPtr dictionary, IntPtr key, IntPtr value);
    [LibraryImport(CoreFoundation, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr StringCreate(IntPtr allocator, string value, uint encoding);
    [LibraryImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static partial void Release(IntPtr value);
}
