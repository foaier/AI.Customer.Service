using AI.Customer.Service.Domain.Ports;

namespace AI.Customer.Service.Infrastructure.Configuration;

/// <summary>独立数据源配置，禁止指向仍由 Java 写入的原业务库。</summary>
public sealed class DatabaseOptions
{
    /// <summary>Sqlite、MySql 或 Kingbase，默认 Sqlite；Kingbase 使用 PostgreSQL 兼容协议。</summary>
    public string Provider { get; set; } = "Sqlite";
    /// <summary>独立数据库连接串；Sqlite 默认保存在应用 data/runtime。</summary>
    public string ConnectionString { get; set; } = "Data Source=data/runtime/customer-service.db";
    /// <summary>仅空数据库允许 ORM 建表，默认 true；已有库不执行破坏性升级。</summary>
    public bool Initialize { get; set; } = true;
}

/// <summary>用户中心只读协议适配配置，地址及认证通过部署注入。</summary>
public sealed class UserCenterOptions
{
    /// <summary>用户中心 HTTP(S) 根地址，默认空；未配置时明确返回依赖不可用。</summary>
    public string BaseUrl { get; set; } = "";
    /// <summary>服务间 Authorization 头，默认空，不回显或记录。</summary>
    public string Authorization { get; set; } = "";
}

/// <summary>可信助手运行注册表，不从访客或模型请求推导组织。</summary>
public sealed class AssistantOptions
{
    /// <summary>服务端维护的助手清单，默认空；未知助手一律拒绝。</summary>
    public List<AssistantDefinition> Items { get; set; } = [];
}

/// <summary>可靠通知及可选事件转发网关，所有外发缺省关闭。</summary>
public sealed class NotificationOptions
{
    /// <summary>外部通知总开关，默认 false；关闭时不领取 EMAIL/INTERNAL。</summary>
    public bool Enabled { get; set; }
    /// <summary>需持久 eventKey 去重并返回 accepted=true 的 HTTP(S) 网关，默认空。</summary>
    public string Endpoint { get; set; } = "";
    /// <summary>服务间认证头，默认空，不写入任务、日志或响应。</summary>
    public string Authorization { get; set; } = "";
    /// <summary>可选官方企微机器人地址，只发送 INTERNAL；默认空。</summary>
    public string WeComWebhook { get; set; } = "";
    /// <summary>审计事件对外转发开关，默认 false；本地接待不依赖外部转发。</summary>
    public bool EventsEnabled { get; set; }
    /// <summary>可选持久事件网关，默认空，需按 eventKey 幂等受理。</summary>
    public string EventsEndpoint { get; set; } = "";
}
