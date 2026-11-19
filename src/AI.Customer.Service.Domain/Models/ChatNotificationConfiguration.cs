namespace AI.Customer.Service.Domain.Models;

/// <summary>组织群通知路由；认证凭据和通知总开关仍由部署配置维护。</summary>
public sealed class ChatNotificationConfiguration : Aggregate
{
    /// <summary>SALES、VISITOR、FINANCE、EMP 对应的官方企微机器人 Hook ID。</summary>
    public Dictionary<string, string> ChatGroup { get; set; } = [];

    /// <summary>兼容既有 MEDIA 分类的财务群映射，不改变组 ID 或历史引用。</summary>
    public static string GroupKey(Category category) => category switch
    {
        Category.EXHIBITOR => "SALES",
        Category.VISITOR => "VISITOR",
        Category.MEDIA => "FINANCE",
        _ => "EMP"
    };
}
