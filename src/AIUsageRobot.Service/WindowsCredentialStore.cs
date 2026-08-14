using System.Runtime.InteropServices;
using System.Text;

namespace AIUsageRobot.Service;

public interface ICredentialStore
{
    Task<string?> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(string secret, CancellationToken cancellationToken);
    Task DeleteAsync(CancellationToken cancellationToken);
}

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const string Target = "AIUsageRobot/DeepSeekApiKey";
    private const int GenericCredential = 1;
    private const int LocalMachinePersistence = 2;

    public Task<string?> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CredRead(Target, GenericCredential, 0, out var pointer)) return Task.FromResult<string?>(null);
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
                return Task.FromResult<string?>(null);
            var value = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
            return Task.FromResult<string?>(value);
        }
        finally { CredFree(pointer); }
    }

    public Task SaveAsync(string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = Target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = LocalMachinePersistence,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException($"无法写入 Windows 凭据管理器，错误码 {Marshal.GetLastWin32Error()}。");
            return Task.CompletedTask;
        }
        finally { Marshal.FreeCoTaskMem(blob); }
    }

    public Task DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CredDelete(Target, GenericCredential, 0);
        return Task.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
}
