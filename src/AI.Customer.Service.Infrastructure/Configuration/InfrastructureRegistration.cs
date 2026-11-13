using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Adapters;
using AI.Customer.Service.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AI.Customer.Service.Infrastructure.Configuration;

/// <summary>基础设施协议装配入口，数据库差异只存在于连接适配器。</summary>
public static class InfrastructureRegistration
{
    /// <summary>注册 ORM owner 仓储、用户中心和可靠通知协议。</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var database = configuration.GetSection("Database").Get<DatabaseOptions>() ?? new();
        services.AddSingleton(database);
        services.AddDbContext<CustomerServiceDbContext>(options =>
        {
            switch (database.Provider)
            {
                case "Sqlite":
                    options.UseSqlite(database.ConnectionString);
                    break;
                case "MySql":
                    options.UseMySQL(database.ConnectionString);
                    break;
                case "Kingbase":
                    options.UseNpgsql(database.ConnectionString);
                    break;
                default:
                    throw new InvalidOperationException("Database:Provider 只能为 Sqlite、MySql 或 Kingbase");
            }
        });
        services.AddScoped<IUnitOfWork, OrmUnitOfWork>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IReceptionRepository, ReceptionRepository>();
        services.AddScoped<IConfigurationRepository, ConfigurationRepository>();
        services.AddScoped<IFollowupRepository, FollowupRepository>();
        services.AddScoped<ISurveyRepository, SurveyRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IReceptionMetricsQuery, ReceptionMetricsQuery>();
        services.AddScoped<ISurveyMetricsQuery, SurveyMetricsQuery>();
        services.Configure<UserCenterOptions>(configuration.GetSection("UserCenter"));
        services.Configure<AssistantOptions>(configuration.GetSection("Assistants"));
        services.Configure<NotificationOptions>(configuration.GetSection("Notifications"));
        services.AddSingleton<IAssistantRegistry, ConfiguredAssistantRegistry>();
        services.AddHttpClient<IAccountDirectory, UserCenterAdapter>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(2) });
        services.AddHttpClient<INotificationPort, NotificationHttpAdapter>(client => client.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(2) });
        return services;
    }
}
