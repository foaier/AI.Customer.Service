using System.Security.Cryptography;
using System.Text;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace AI.Customer.Service.Infrastructure.Adapters;

/// <summary>AES-256-GCM 保护雅式令牌，随机 nonce；主密钥由所有实例共享的 Secret 注入。</summary>
public sealed class NotificationTokenCipher(IOptions<NotificationOptions> options) : ISecretCipher
{
    /// <inheritdoc />
    public string Protect(string plaintext) => Transform(plaintext, encrypt: true);
    /// <inheritdoc />
    public string Unprotect(string ciphertext) => Transform(ciphertext, encrypt: false);

    /// <summary>版本前缀及认证标签共同保护密文，错误不包含令牌或密文。</summary>
    private string Transform(string value, bool encrypt)
    {
        byte[] key = [];
        try
        {
            key = Convert.FromBase64String(options.Value.EncryptionKey);
            if (key.Length != 32) throw new CryptographicException();
            using var cipher = new AesGcm(key, 16);
            if (encrypt)
            {
                var plain = Encoding.UTF8.GetBytes(value);
                var result = new byte[28 + plain.Length];
                RandomNumberGenerator.Fill(result.AsSpan(0, 12));
                try { cipher.Encrypt(result.AsSpan(0, 12), plain, result.AsSpan(28), result.AsSpan(12, 16)); }
                finally { CryptographicOperations.ZeroMemory(plain); }
                return "v1." + Convert.ToBase64String(result);
            }
            if (!value.StartsWith("v1.", StringComparison.Ordinal)) throw new CryptographicException();
            var bytes = Convert.FromBase64String(value[3..]);
            if (bytes.Length <= 28) throw new CryptographicException();
            var decrypted = new byte[bytes.Length - 28];
            try
            {
                cipher.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), decrypted);
                return Encoding.UTF8.GetString(decrypted);
            }
            finally { CryptographicOperations.ZeroMemory(decrypted); }
        }
        catch (Exception error) when (error is FormatException or CryptographicException or ArgumentException)
        {
            throw new BusinessException("CONFIG_SECRET_INVALID", "通知加密密钥未配置或密文无效", 503);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
