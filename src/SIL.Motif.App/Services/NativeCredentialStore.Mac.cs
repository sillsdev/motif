using System.Runtime.InteropServices;
using System.Text;

namespace SIL.Motif.App.Services;

public sealed partial class NativeCredentialStore
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
        var service = StringCreate(IntPtr.Zero, _target, 0x08000100);
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

    private bool WriteMac(string credential) => WithMacQuery(query =>
    {
        var bytes = Encoding.UTF8.GetBytes(credential);
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        var data = IntPtr.Zero;
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            data = DataCreate(IntPtr.Zero, buffer, bytes.Length);
            SecItemDelete(query);
            DictionarySet(query, MacConstants.Get("kSecValueData"), data);
            return SecItemAdd(query, IntPtr.Zero) == 0;
        }
        finally
        {
            Array.Clear(bytes);
            for (var i = 0; i < bytes.Length; i++) Marshal.WriteByte(buffer, i, 0);
            Marshal.FreeHGlobal(buffer);
            if (data != IntPtr.Zero) Release(data);
        }
    });

    private string? ReadMac() => WithMacQuery(query =>
    {
        DictionarySet(query, MacConstants.Get("kSecReturnData"), MacConstants.True);
        if (SecItemCopyMatching(query, out var data) != 0) return null;
        try
        {
            var size = checked((int)DataGetLength(data));
            var bytes = new byte[size];
            Marshal.Copy(DataGetBytePtr(data), bytes, 0, size);
            try { return Encoding.UTF8.GetString(bytes); }
            finally { Array.Clear(bytes); }
        }
        finally { Release(data); }
    });

    private void DeleteMac() => WithMacQuery(query => SecItemDelete(query));

    [LibraryImport(Security, EntryPoint = "SecItemAdd")]
    private static partial int SecItemAdd(IntPtr attributes, IntPtr result);
    [LibraryImport(Security, EntryPoint = "SecItemCopyMatching")]
    private static partial int SecItemCopyMatching(IntPtr query, out IntPtr result);
    [LibraryImport(Security, EntryPoint = "SecItemDelete")]
    private static partial int SecItemDelete(IntPtr query);
    [LibraryImport(CoreFoundation, EntryPoint = "CFDictionaryCreateMutable")]
    private static partial IntPtr DictionaryCreate(IntPtr allocator, nint capacity, IntPtr keys, IntPtr values);
    [LibraryImport(CoreFoundation, EntryPoint = "CFDictionarySetValue")]
    private static partial void DictionarySet(IntPtr dictionary, IntPtr key, IntPtr value);
    [LibraryImport(CoreFoundation, EntryPoint = "CFStringCreateWithCString", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr StringCreate(IntPtr allocator, string value, uint encoding);
    [LibraryImport(CoreFoundation, EntryPoint = "CFDataCreate")]
    private static partial IntPtr DataCreate(IntPtr allocator, IntPtr data, nint length);
    [LibraryImport(CoreFoundation, EntryPoint = "CFDataGetLength")]
    private static partial nint DataGetLength(IntPtr data);
    [LibraryImport(CoreFoundation, EntryPoint = "CFDataGetBytePtr")]
    private static partial IntPtr DataGetBytePtr(IntPtr data);
    [LibraryImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static partial void Release(IntPtr value);
}
