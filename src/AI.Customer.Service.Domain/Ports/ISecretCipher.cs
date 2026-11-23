namespace AI.Customer.Service.Domain.Ports;

/// <summary>
/// 通知密钥保护端口，业务与配置接口均不持有加密主密钥。
/// </summary>
public interface ISecretCipher
{
    /// <summary>
    /// 保存前加密；主密钥未配置时拒绝写入。
    /// </summary>
    string Protect(string plaintext);
    /// <summary>
    /// 只在发送时解密；损坏密文使用安全失败分类。
    /// </summary>
    string Unprotect(string ciphertext);
}
