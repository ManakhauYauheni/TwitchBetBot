using System;
using System.Security.Cryptography;
using System.Text;
namespace TwitchBetBot.Utils
{
    /// <summary>
    /// Шифрование секретов через Windows DPAPI (CurrentUser).
    /// Значение можно расшифровать только под той же учётной записью Windows,
    /// поэтому config.json безопасно переносить нельзя — токены придётся ввести заново.
    /// </summary>
    public static class SecureStorage
    {
        // Дополнительная энтропия — чуть усложняет расшифровку чужим процессом того же пользователя
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TwitchBetBot.v1");
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return "";
            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encrypted = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DPAPI Protect error: {ex.Message}");
                return "";
            }
        }
        public static string Unprotect(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return "";
            byte[] encryptedBytes;
            try
            {
                encryptedBytes = Convert.FromBase64String(encryptedText);
            }
            catch
            {
                return "";
            }
            // Сначала пробуем с энтропией (новый формат)
            try
            {
                byte[] plain = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                // Fallback: старый формат без энтропии (конфиги предыдущих версий)
                try
                {
                    byte[] plain = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plain);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"DPAPI Unprotect error: {ex.Message}");
                    return "";
                }
            }
        }
        public static bool SelfTest()
        {
            const string probe = "dpapi_self_test_123";
            return Unprotect(Protect(probe)) == probe;
        }
    }
}
