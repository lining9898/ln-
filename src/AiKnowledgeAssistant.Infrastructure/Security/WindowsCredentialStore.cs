using System.Runtime.InteropServices;
using System.Text;
using AiKnowledgeAssistant.Core.AI;

namespace AiKnowledgeAssistant.Infrastructure.Security;

public sealed class WindowsCredentialStore : ICredentialStore
{
    private const int CredentialTypeGeneric = 1;
    private const int PersistSession = 1;

    public void SaveSecret(string target, string secret)
    {
        Validate(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("仅 Windows 支持凭据管理器。");
        var bytes = Encoding.Unicode.GetBytes(secret);
        var credential = new NativeCredential
        {
            Type = CredentialTypeGeneric,
            TargetName = target,
            CredentialBlobSize = (uint)bytes.Length,
            CredentialBlob = Marshal.AllocHGlobal(bytes.Length),
            Persist = PersistSession,
            UserName = Environment.UserName
        };
        try
        {
            Marshal.Copy(bytes, 0, credential.CredentialBlob, bytes.Length);
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException("无法保存 Windows 凭据。错误代码：" + Marshal.GetLastWin32Error());
        }
        finally
        {
            if (credential.CredentialBlob != IntPtr.Zero) Marshal.FreeHGlobal(credential.CredentialBlob);
        }
    }

    public string? ReadSecret(string target)
    {
        Validate(target);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("仅 Windows 支持凭据管理器。");
        if (!CredRead(target, CredentialTypeGeneric, 0, out var credentialPtr)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return null;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        finally { CredFree(credentialPtr); }
    }

    public void DeleteSecret(string target)
    {
        Validate(target);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("仅 Windows 支持凭据管理器。");
        CredDelete(target, CredentialTypeGeneric, 0);
    }

    private static void Validate(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (target.Length > 256) throw new ArgumentException("凭据名称过长。");
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credentialPtr);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
