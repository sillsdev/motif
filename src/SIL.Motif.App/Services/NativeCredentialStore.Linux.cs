using System.Runtime.InteropServices;

namespace SIL.Motif.App.Services;

public sealed partial class NativeCredentialStore
{
    private const string SecretLibrary = "libsecret-1.so.0";
    private const string GlibLibrary = "libglib-2.0.so.0";

    private T WithLinuxAttributes<T>(Func<IntPtr, T> action)
    {
        if (!NativeLibrary.TryLoad(SecretLibrary, out var library)) throw new InvalidOperationException();
        var key = Marshal.StringToCoTaskMemUTF8("application");
        var value = Marshal.StringToCoTaskMemUTF8(_target);
        var table = IntPtr.Zero;
        try
        {
            table = HashTableNew(IntPtr.Zero, IntPtr.Zero);
            HashTableInsert(table, key, value);
            return action(table);
        }
        finally
        {
            if (table != IntPtr.Zero) HashTableUnref(table);
            Marshal.FreeCoTaskMem(key);
            Marshal.FreeCoTaskMem(value);
            NativeLibrary.Free(library);
        }
    }

    private bool WriteLinux(string credential) => WithLinuxAttributes(attributes =>
    {
        var success = PasswordStore(IntPtr.Zero, attributes, IntPtr.Zero, "Motif OpenAI", credential, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero) ErrorFree(error);
        return success != 0;
    });

    private string? ReadLinux() => WithLinuxAttributes(attributes =>
    {
        var password = PasswordLookup(IntPtr.Zero, attributes, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero)
        {
            ErrorFree(error);
            throw new InvalidOperationException();
        }
        if (password == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(password); }
        finally { PasswordFree(password); }
    });

    private void DeleteLinux() => WithLinuxAttributes(attributes =>
    {
        PasswordClear(IntPtr.Zero, attributes, IntPtr.Zero, out var error);
        if (error != IntPtr.Zero) ErrorFree(error);
        return 0;
    });

    [LibraryImport(GlibLibrary, EntryPoint = "g_hash_table_new")]
    private static partial IntPtr HashTableNew(IntPtr hash, IntPtr equal);
    [LibraryImport(GlibLibrary, EntryPoint = "g_hash_table_insert")]
    private static partial int HashTableInsert(IntPtr table, IntPtr key, IntPtr value);
    [LibraryImport(GlibLibrary, EntryPoint = "g_hash_table_unref")]
    private static partial void HashTableUnref(IntPtr table);
    [LibraryImport(GlibLibrary, EntryPoint = "g_error_free")]
    private static partial void ErrorFree(IntPtr error);
    [LibraryImport(SecretLibrary, EntryPoint = "secret_password_storev_sync", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int PasswordStore(IntPtr schema, IntPtr attributes, IntPtr collection, string label,
        string password, IntPtr cancellable, out IntPtr error);
    [LibraryImport(SecretLibrary, EntryPoint = "secret_password_lookupv_sync")]
    private static partial IntPtr PasswordLookup(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
    [LibraryImport(SecretLibrary, EntryPoint = "secret_password_clearv_sync")]
    private static partial int PasswordClear(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);
    [LibraryImport(SecretLibrary, EntryPoint = "secret_password_free")]
    private static partial void PasswordFree(IntPtr password);
}
