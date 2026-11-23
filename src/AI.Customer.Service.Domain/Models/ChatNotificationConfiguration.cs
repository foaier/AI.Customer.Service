namespace AI.Customer.Service.Domain.Models;

/// <summary>
/// 组织聊天通知的统一持久配置；主加密密钥只由部署维护
/// </summary>
public sealed class ChatNotificationConfiguration : Aggregate
{
    /// <summary>
    /// 统一控制即时通知、旧提醒和邮件投递；默认关闭
    /// </summary>
    public bool Enabled { get; set; }
    /// <summary>
    /// ADSALE 或 WECOM_ROBOT；每笔新申请冻结一个通道
    /// </summary>
    public string ActiveProvider { get; set; } = "WECOM_ROBOT";
    /// <summary>
    /// 固定对账账号，不作为认证凭据
    /// </summary>
    public string AccountCode { get; set; } = "vendor_31";
    /// <summary>
    /// 固定官方雅式 API 根地址
    /// </summary>
    public string BaseUrl { get; set; } = "https://hub.adsaleinfo.com/open-api";
    /// <summary>
    /// 客服站点 HTTPS 根地址，通知链接不携带凭据
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";
    /// <summary>
    /// AES-GCM 密文；只由保存入口加密、通知适配器解密，禁止 API 回显
    /// </summary>
    public string? ApiTokenCiphertext { get; set; }
    /// <summary>
    /// SALES、VISITOR、FINANCE、EMP 对应的官方企微机器人 Hook ID
    /// </summary>
    public Dictionary<string, string> ChatGroup { get; set; } = [];

    /// <summary>
    /// 业务类别到通知群类别的固定映射
    /// </summary>
    public static string GroupKey(Category category) => category switch
    {
        Category.EXHIBITOR => "SALES",
        Category.VISITOR => "VISITOR",
        Category.FINANCE => "FINANCE",
        _ => "EMP"
    };
}
