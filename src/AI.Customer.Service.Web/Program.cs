using System.Text;
using System.Text.Json.Serialization;
using AI.Customer.Service.Common.Converter;
using AI.Customer.Service.Core;
using AI.Customer.Service.Core.Adapters;
using AI.Customer.Service.Core.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Configuration;
using AI.Customer.Service.Infrastructure.Persistence;
using AI.Customer.Service.Service;
using AI.Customer.Service.Service.Configuration;
using AI.Customer.Service.Service.Workers;
using AI.Customer.Service.Web.Middleware;
using AI.Customer.Service.Web.Realtime;
using AI.Customer.Service.Web.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using AuthenticationOptions = AI.Customer.Service.Web.Security.AuthenticationOptions;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
var authentication = builder.Configuration.GetSection("Authentication").Get<AuthenticationOptions>() ?? new();
builder.Services.Configure<AuthenticationOptions>(builder.Configuration.GetSection("Authentication"));
builder.Services.AddAuthentication("VisitorOrBearer")
    .AddPolicyScheme("VisitorOrBearer", "访客凭据或正式登录", options =>
        options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey(ConversationCredentialHandler.Header)
            ? ConversationCredentialHandler.SchemeName : JwtBearerDefaults.AuthenticationScheme)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ConversationCredentialHandler>(ConversationCredentialHandler.SchemeName, _ => { })
    .AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.SaveToken = false;
    options.IncludeErrorDetails = false;
    if (!string.IsNullOrWhiteSpace(authentication.Authority))
    {
        options.Authority = authentication.Authority;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    }
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = authentication.Issuer,
        ValidateAudience = true,
        ValidAudience = authentication.Audience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero,
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true
    };
    if (!string.IsNullOrWhiteSpace(authentication.SigningKey))
    {
        if (Encoding.UTF8.GetByteCount(authentication.SigningKey) < 32)
        {
            throw new InvalidOperationException("Authentication:SigningKey 至少需要 32 字节");
        }
        options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authentication.SigningKey));
    }
});
builder.Services.AddAuthorization();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(new { code = "INVALID_ARGUMENT", message = "请求字段或格式无效" });
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
});
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<RequestAccess>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOptions<ModelOptions>().Bind(builder.Configuration.GetSection("Model"))
    .Validate(x => x.TimeoutSeconds is >= 5 and <= 240, "模型超时必须为 5 至 240 秒").ValidateOnStart();
builder.Services.AddHttpClient<IChatModel, AgentFrameworkChatModel>(client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5) });
builder.Services.AddOptions<CustomerServiceOptions>().Bind(builder.Configuration.GetSection("CustomerService"))
    .Validate(x => x.Capacity is >= 1 and <= 100 && x.ProposalTtlMinutes is >= 1 and <= 1440 &&
        x.SurveyTtlMinutes is >= 1 and <= 1440, "客服容量或有效期配置超出合法范围").ValidateOnStart();
builder.Services.AddScoped<ConversationRuntime>();
builder.Services.AddScoped<ConversationAccessService>();
builder.Services.AddScoped<AccessService>();
builder.Services.AddScoped<ConfigurationService>();
builder.Services.AddScoped<ChatNotificationService>();
builder.Services.AddScoped<CommandJournal>();
builder.Services.AddScoped<HandoffService>();
builder.Services.AddScoped<HandoffNotificationService>();
builder.Services.AddScoped<ReceptionService>();
builder.Services.AddScoped<MessagingService>();
builder.Services.AddScoped<WaitingService>();
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<FollowupService>();
builder.Services.AddScoped<InboxService>();
builder.Services.AddScoped<SurveyService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<ReliableTaskProcessor>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<MetricsService>();
builder.Services.AddHostedService<ReliableTaskWorker>();
builder.Services.AddHostedService<SurveyRecoveryWorker>();
builder.Services.AddHostedService<AutoCloseWorker>();
builder.Services.AddSingleton<RealtimeConnectionLimiter>();

var app = builder.Build();
// 数据库初始化只对空库建表。
await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<DatabaseOptions>();
    var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
    if (database.Provider == "Sqlite")
    {
        var directory = Path.Combine(app.Environment.ContentRootPath, "data", "runtime");
        Directory.CreateDirectory(directory);
    }
    if (database.Initialize)
    {
        await db.Database.EnsureCreatedAsync();
    }
}
app.UseMiddleware<BusinessExceptionMiddleware>();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => new { status = "Healthy", application = "AI.Customer.Service", framework = ".NET 10" }).AllowAnonymous();
app.MapGet("/api/auth/config", () => new
{
    authority = authentication.Authority,
    clientId = authentication.ClientId,
    scope = authentication.Scope
}).AllowAnonymous();
app.Map("/api/customer-service/realtime/ws", RealtimeEndpoint.HandleAsync).AllowAnonymous();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
await app.RunAsync();

/// <summary>
/// WebApplicationFactory 的公开应用入口，生产启动由顶层语句负责。
/// </summary>
public partial class Program;
