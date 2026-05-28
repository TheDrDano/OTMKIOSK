using System.Security.Cryptography;

namespace Otm.Kiosk.Shared.Security;

public static class RecoveryKeyGenerator
{
    public static string CreateRecoveryKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(18);
        return Convert.ToBase64String(bytes).Replace("+", "A").Replace("/", "B").TrimEnd('=');
    }
}
