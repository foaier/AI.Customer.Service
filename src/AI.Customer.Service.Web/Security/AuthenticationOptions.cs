namespace AI.Customer.Service.Web.Security;

/// <summary>复用既有登录 JWT 的验证配置，不签发新的客服身份或票据。</summary>
public sealed class AuthenticationOptions
{
    /// <summary>OIDC 权威地址，默认空；有值时从发现端点加载签名密钥。</summary>
    public string Authority { get; set; } = "";
    /// <summary>接受的 JWT 签发者；使用本地签名验证时必填。</summary>
    public string Issuer { get; set; } = "";
    /// <summary>接受的 audience；部署必须与原登录系统一致。</summary>
    public string Audience { get; set; } = "";
    /// <summary>可选 HS256 服务端密钥，至少 32 字节；默认空，仅 Secret 注入。</summary>
    public string SigningKey { get; set; } = "";
    /// <summary>JWT 中租户声明名，默认 tenantId。</summary>
    public string TenantClaim { get; set; } = "tenantId";
    /// <summary>JWT 中账号声明名，默认 accountId。</summary>
    public string AccountClaim { get; set; } = "accountId";
    /// <summary>OIDC 浏览器客户端 ID，默认空；需注册本项目回调地址。</summary>
    public string ClientId { get; set; } = "";
    /// <summary>OIDC scope，默认 openid profile。</summary>
    public string Scope { get; set; } = "openid profile";
}
