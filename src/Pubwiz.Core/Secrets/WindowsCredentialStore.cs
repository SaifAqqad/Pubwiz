using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Pubwiz.Core.Secrets;

/// <summary>
/// Windows: generic credentials in the Windows Credential Manager (advapi32 Cred* functions).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ISecretStore
{
    private const int CredentialTypeGeneric = 1;
    private const int PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string Name => "Windows Credential Manager";

    public bool IsAvailable => true;

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!NativeMethods.CredRead(TargetName(key), CredentialTypeGeneric, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound
                ? Task.FromResult<string?>(null)
                : throw new SecretStoreException("Failed to read from the Credential Manager.", new Win32Exception(error));
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.Credential>(credentialPointer);
            var secret = credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / sizeof(char));

            return Task.FromResult<string?>(secret);
        }
        finally
        {
            NativeMethods.CredFree(credentialPointer);
        }
    }

    public Task SetAsync(string key, string label, string secret, CancellationToken cancellationToken = default)
    {
        var blob = Encoding.Unicode.GetBytes(secret);
        var blobPointer = Marshal.AllocHGlobal(blob.Length);

        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new NativeMethods.Credential
            {
                Type = CredentialTypeGeneric,
                TargetName = TargetName(key),
                Comment = label,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = PersistLocalMachine,
                UserName = Environment.UserName
            };

            if (!NativeMethods.CredWrite(ref credential, 0))
            {
                throw new SecretStoreException("Failed to write to the Credential Manager.", new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }
        finally
        {
            // Clear the plain-text copy before freeing it.
            Marshal.Copy(new byte[blob.Length], 0, blobPointer, blob.Length);
            Marshal.FreeHGlobal(blobPointer);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        NativeMethods.CredDelete(TargetName(key), CredentialTypeGeneric, 0);
        return Task.CompletedTask;
    }

    private static string TargetName(string key) => $"{SecretKeys.Service}:{key}";

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct Credential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredWrite(ref Credential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        public static extern void CredFree(IntPtr buffer);
    }
}
