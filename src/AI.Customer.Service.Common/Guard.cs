using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace AI.Customer.Service.Common;

/// <summary>跨上下文复用的有界文本、幂等摘要和版本校验。</summary>
public static class Guard
{
    /// <summary>校验有业务含义的必填纯文本，不回显原文。</summary>
    public static string Text(string? value, string field, int max = 128)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Contains('\0'))
        {
            throw BusinessException.Invalid($"{field}为空或长度超限");
        }
        return value;
    }

    /// <summary>严格校验单个邮箱，拒绝显示名称及邮件头注入。</summary>
    public static string Email(string? value)
    {
        Text(value, "邮箱", 254);
        if (!MailAddress.TryCreate(value, out var address) || address.Address != value || value!.Any(char.IsControl))
        {
            throw BusinessException.Invalid("邮箱格式无效");
        }
        return value;
    }

    /// <summary>比较客户端首次使用的聚合版本。</summary>
    public static void Version(long expected, long actual)
    {
        if (expected < 1 || expected != actual)
        {
            throw BusinessException.Conflict("版本已变化，请恢复最新状态");
        }
    }

    /// <summary>为稳定命令内容生成摘要；摘要只做同键异内容检测。</summary>
    public static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>生成带业务前缀的服务端引用，不接受模型指定标识。</summary>
    public static string Id(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
