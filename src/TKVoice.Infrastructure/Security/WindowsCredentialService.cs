using System.Runtime.InteropServices;
using System.Text;
using TKVoice.Core.Abstractions;
using TKVoice.Infrastructure.Native;

namespace TKVoice.Infrastructure.Security;

/// <summary>
/// Stores the OpenAI API key in the Windows Credential Manager (NFR-005), never in config files.
/// The key can also be stored manually: cmdkey /generic:TKVoice/OpenAI /user:openai /pass:&lt;key&gt;
/// </summary>
public sealed class WindowsCredentialService : ICredentialService
{
    public const string OpenAITarget = "TKVoice/OpenAI";

    public string? GetOpenAIApiKey()
    {
        if (!NativeMethods.CredRead(OpenAITarget, NativeMethods.CRED_TYPE_GENERIC, 0, out var credentialPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credentialPtr);
            if (credential.CredentialBlob == 0 || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.Unicode.GetString(blob).Trim();
        }
        finally
        {
            NativeMethods.CredFree(credentialPtr);
        }
    }

    public void SetOpenAIApiKey(string apiKey)
    {
        var blob = Encoding.Unicode.GetBytes(apiKey.Trim());
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new NativeMethods.CREDENTIAL
            {
                Type = NativeMethods.CRED_TYPE_GENERIC,
                TargetName = OpenAITarget,
                UserName = "openai",
                CredentialBlob = blobPtr,
                CredentialBlobSize = (uint)blob.Length,
                Persist = NativeMethods.CRED_PERSIST_LOCAL_MACHINE,
            };

            if (!NativeMethods.CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"API Key konnte nicht gespeichert werden (Win32-Fehler {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(blobPtr);
        }
    }
}
