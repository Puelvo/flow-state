using System.Security.Cryptography;
using System.Text;

namespace FlowState;

/// <summary>Windows: oturum anahtarını kullanıcı hesabına bağlı DPAPI ile şifreler (mevcut sync.json ile uyumlu).</summary>
public sealed class WindowsSecretCodec : ISecretCodec
{
    public string Protect(string plain) => Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public string Unprotect(string stored) => Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
}
