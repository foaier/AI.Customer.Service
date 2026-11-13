using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Configuration;
using AI.Customer.Service.Infrastructure.Persistence;
using AI.Customer.Service.Service.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AI.Customer.Service.Tests;

/// <summary>真实 MVC、JWT、SQLite 事务和 WebSocket 的隔离宿主，外部账号、模型和网关明确替身。</summary>
public sealed class TestApplication : WebApplicationFactory<Program>
{
    /// <summary>测试专用临时数据库目录，绝不使用应用默认数据库。</summary>
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ai-customer-service-tests", Guid.NewGuid().ToString("N"));
    /// <summary>只供测试签发和验证的密钥，不进入运行配置。</summary>
    private const string SigningKey = "tests-only-012345678901234567890123456789";
    /// <summary>可控时钟从营业时间开始，不修改系统时钟。</summary>
    public TestClock Clock { get; } = new();
    /// <summary>明确的模型替身，用于真实 SSE 管线及围栏测试。</summary>
    public TestModel Model { get; } = new();
    /// <summary>框架端到端场景可注入真实 MAF 端口，仅替代外部 HTTP 传输；缺省使用可控模型替身。</summary>
    public IChatModel? ModelPort { get; init; }
    /// <summary>可模拟账号禁用的外部用户中心替身。</summary>
    public TestDirectory DirectoryPort { get; } = new();
    /// <summary>可模拟拒绝和受理的通知网关替身。</summary>
    public TestNotification Notification { get; } = new();

    /// <summary>替换外部依赖和数据源，其余正式应用代码保持真实装配。</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(directory);
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Data Source=" + Path.Combine(directory, "test.db"),
            ["Authentication:Issuer"] = "tests",
            ["Authentication:Audience"] = "tests",
            ["Authentication:SigningKey"] = SigningKey,
            ["Assistants:Items:0:Id"] = "assistant-a",
            ["Assistants:Items:0:TenantId"] = "tenant-a",
            ["Assistants:Items:0:TopOrgId"] = "org-a",
            ["Assistants:Items:0:Name"] = "测试助手",
            ["CustomerService:Capacity"] = "1"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<CustomerServiceDbContext>>();
            services.AddDbContext<CustomerServiceDbContext>(options => options.UseSqlite("Data Source=" + Path.Combine(directory, "test.db") + ";Default Timeout=5"));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IAccountDirectory>();
            services.AddSingleton<IAccountDirectory>(DirectoryPort);
            services.RemoveAll<IChatModel>();
            services.AddSingleton<IChatModel>(ModelPort ?? Model);
            services.RemoveAll<INotificationPort>();
            services.AddSingleton<INotificationPort>(Notification);
            var workers = services.Where(x => x.ServiceType == typeof(IHostedService) &&
                (x.ImplementationType == typeof(ReliableTaskWorker) || x.ImplementationType == typeof(SurveyRecoveryWorker))).ToList();
            foreach (var worker in workers)
            {
                services.Remove(worker);
            }
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.ConfigurationManager = null;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "tests",
                    ValidateAudience = true,
                    ValidAudience = "tests",
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey))
                };
            });
        });
    }

    /// <summary>创建携带正式签名 JWT 的 HTTP 客户端，账号不会从测试身份头注入。</summary>
    public HttpClient Client(string account = "visitor", string tenant = "tenant-a")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(account, tenant));
        return client;
    }

    /// <summary>按正式规则生成测试 JWT，支持租户和过期验证。</summary>
    public static string Token(string account, string tenant = "tenant-a", bool expired = false) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "tests",
            Audience = "tests",
            IssuedAt = DateTime.UtcNow.AddMinutes(-2),
            NotBefore = DateTime.UtcNow.AddMinutes(-2),
            Expires = DateTime.UtcNow.AddMinutes(expired ? -1 : 10),
            Subject = new ClaimsIdentity([new Claim("accountId", account), new Claim("tenantId", tenant)]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256)
        });

    /// <summary>保存四类本地组并关联两个正式账号替身，未上线仍具备手动接单资格。</summary>
    public async Task ConfigureAsync()
    {
        using var manager = Client("agent-a");
        var current = await GetAsync(manager, "/api/customer-service/configuration?topOrgId=org-a");
        var groups = current.GetProperty("groups").EnumerateArray().Select(x => new
        {
            id = x.GetProperty("id").GetString(),
            category = x.GetProperty("category").GetString(),
            name = x.GetProperty("name").GetString(),
            members = new[] { new { accountId = "agent-a", displayName = "客户端名不授予权限" }, new { accountId = "agent-b", displayName = "客服乙" } }
        }).ToArray();
        await SendAsync(manager, HttpMethod.Put, "/api/customer-service/configuration?topOrgId=org-a", new
        {
            version = current.GetProperty("version").GetInt64(),
            groups,
            routes = new[] { new { assistantConfigId = "assistant-a", groups = groups.ToDictionary(g => g.category!, g => g.id) } }
        });
    }

    /// <summary>通过真实会话 HTTP 入口创建共享 conversation。</summary>
    public static async Task<string> SessionAsync(HttpClient visitor)
    {
        using var response = await visitor.GetAsync("/api/agent/sessionId?assistantConfigId=assistant-a");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("returnObj").GetProperty("sessionId").GetString()!;
    }

    /// <summary>发送严格 JSON 命令并校验业务响应状态，失败输出只供测试诊断。</summary>
    public static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string path, object input, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(input, options: JsonDefaults.Options) };
        using var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.TryGetProperty("data", out var data) ? data.Clone() : json.RootElement.Clone();
    }

    /// <summary>读取真实 HTTP 安全业务投影。</summary>
    public static async Task<JsonElement> GetAsync(HttpClient client, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, content);
        using var json = JsonDocument.Parse(content);
        return json.RootElement.TryGetProperty("data", out var data) ? data.Clone() : json.RootElement.Clone();
    }

    /// <summary>测试后只删除已校验的本测试临时目录，不访问工作区数据。</summary>
    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteClearPools();
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ai-customer-service-tests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>关闭测试 SQLite 连接池以释放 Windows 文件句柄。</summary>
    private static void SqliteClearPools() => SqliteConnection.ClearAllPools();
}

/// <summary>可推进的业务时钟，仅影响领域期限，不改变 JWT 的系统时钟。</summary>
public sealed class TestClock : TimeProvider
{
    /// <summary>初始为周一中国标准时间十一点，满足缺省营业区间。</summary>
    private DateTimeOffset now = DateTimeOffset.Parse("2026-11-09T03:00:00Z");
    /// <summary>取得测试 UTC 当前时刻。</summary>
    public override DateTimeOffset GetUtcNow() => now;
    /// <summary>推进业务期限以测试五分钟、十分钟及租约恢复。</summary>
    public void Advance(TimeSpan interval) => now += interval;
}

/// <summary>外部账号系统替身，不注册任何本地生产测试后门。</summary>
public sealed class TestDirectory : IAccountDirectory
{
    /// <summary>模拟外部账号禁用名单。</summary>
    public HashSet<string> Disabled { get; } = [];
    /// <inheritdoc />
    public Task<Member> RequireAsync(string tenant, string org, string account, CancellationToken ct) =>
        tenant == "tenant-a" && org == "org-a" && !Disabled.Contains(account) && account is "agent-a" or "agent-b" or "visitor"
            ? Task.FromResult(new Member(account, "权威名称-" + account))
            : throw BusinessException.Forbidden("外部账号未启用");
    /// <inheritdoc />
    public Task<DirectoryPage<DirectoryGroup>> GroupsAsync(string tenant, string org, string? keyword, string? cursor, int limit, CancellationToken ct) =>
        Task.FromResult(new DirectoryPage<DirectoryGroup>("READY", [new("external-group", "用户中心组")], null, false));
    /// <inheritdoc />
    public Task<DirectoryPage<Member>> AccountsAsync(string tenant, string org, string group, string? keyword, string? cursor, int limit, CancellationToken ct) =>
        Task.FromResult(new DirectoryPage<Member>("READY", [new("agent-a", "客服甲"), new("agent-b", "客服乙")], null, false));
}

/// <summary>明确的流式模型替身，可控制并发轮次暂停和最终失败。</summary>
public sealed class TestModel : IChatModel
{
    /// <summary>模拟生成失败，禁止被视作真实模型验收。</summary>
    public bool Fail { get; set; }
    /// <summary>可控回复正文。</summary>
    public string Reply { get; set; } = "请在展馆服务台办理。还有其他问题需要帮助吗？";
    /// <summary>可控受限工具结果。</summary>
    public ModelDelta? Tool { get; set; }
    /// <inheritdoc />
    public async IAsyncEnumerable<ModelDelta> StreamAsync(ModelRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        if (Fail)
        {
            throw BusinessException.Unavailable("测试模型不可用");
        }
        yield return new(Reply);
        if (Tool is not null)
        {
            yield return Tool;
        }
    }
    /// <inheritdoc />
    public Task<Classification> ClassifyAsync(Scope scope, string context, CancellationToken ct) =>
        Task.FromResult(new Classification(Category.OTHER, 0, "TEST_FALLBACK"));
}

/// <summary>可靠网关替身，记录受理键用于验证 Worker 幂等和重试。</summary>
public sealed class TestNotification : INotificationPort
{
    /// <summary>模拟网关拒绝，任务应退避且最终 DEAD。</summary>
    public bool Reject { get; set; }
    /// <summary>按稳定事件键记录受理事实。</summary>
    public HashSet<string> Accepted { get; } = [];
    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedKinds { get; set; } = ["EMAIL", "EMAIL_REPLY", "INTERNAL"];
    /// <inheritdoc />
    public Task SendAsync(ReliableTask task, CancellationToken ct)
    {
        if (Reject)
        {
            throw BusinessException.Unavailable("测试网关拒绝");
        }
        Accepted.Add(task.EventKey);
        return Task.CompletedTask;
    }
}
