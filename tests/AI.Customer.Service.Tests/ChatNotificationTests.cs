using System.Net;
using System.Text.Json;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Domain.Ports;
using AI.Customer.Service.Infrastructure.Adapters;
using AI.Customer.Service.Infrastructure.Configuration;
using AI.Customer.Service.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static AI.Customer.Service.Tests.TestApplication;

namespace AI.Customer.Service.Tests;

public sealed partial class WorkflowTests
{
    private const string ChatConfigPath = "/api/customer-service/configuration/chat-notifications";

    /// <summary>
    /// 正式 HTTP 覆盖首次保存、兼容键、版本冲突及非法地址，读取不泄露认证参数。
    /// </summary>
    [TestMethod]
    public async Task ChatNotificationConfigurationIsScopedAndVersioned()
    {
        using var manager = application.Client("agent-a");
        var initial = await GetAsync(manager, ChatConfigPath);
        Assert.AreEqual(0L, initial.GetProperty("version").GetInt64());
        Assert.IsFalse(initial.GetProperty("enabled").GetBoolean());
        var groups = new Dictionary<string, string>
        {
            ["EXHIBITOR"] = "sales-hook-123",
            ["VISITOR"] = "visitor-hook-123",
            ["MEDIA"] = "finance-hook-123",
            ["OTHER"] = "other-hook-123"
        };
        var saved = await SendAsync(manager, HttpMethod.Put, ChatConfigPath, new { version = 0, chatGroup = groups });
        Assert.AreEqual("finance-hook-123", saved.GetProperty("chatGroup").GetProperty("FINANCE").GetString());
        Assert.IsFalse(saved.GetProperty("chatGroup").TryGetProperty("MEDIA", out _));
        Assert.IsFalse(saved.TryGetProperty("authorization", out _));
        await SendAsync(manager, HttpMethod.Put, ChatConfigPath, new { version = 0, chatGroup = groups }, HttpStatusCode.Conflict);
        groups["MEDIA"] = "https://example.invalid/?key=secret";
        await SendAsync(manager, HttpMethod.Put, ChatConfigPath, new { version = 1, chatGroup = groups }, HttpStatusCode.BadRequest);
        var reloaded = await GetAsync(manager, ChatConfigPath);
        Assert.AreEqual(saved.GetRawText(), reloaded.GetRawText());
        using var sameAccount = application.Client("agent-a");
        await GetAsync(sameAccount, ChatConfigPath);
        application.DirectoryPort.Disabled.Add("agent-a");
        await SendAsync(manager, HttpMethod.Put, ChatConfigPath, new { version = 1, chatGroup = groups }, HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// 保存的财务群真正用于 INTERNAL 投递；总开关、回执和租户边界不能绕过。
    /// </summary>
    [TestMethod]
    public async Task SavedChatGroupRoutesNotificationAndRequiresWeComReceipt()
    {
        await application.ConfigureAsync();
        using var manager = application.Client("agent-a");
        await SendAsync(manager, HttpMethod.Put, ChatConfigPath, new
        {
            version = 0,
            enabled = true,
            publicBaseUrl = "https://support.example.com",
            chatGroup = new Dictionary<string, string> { ["SALES"] = "sales-hook-123", ["VISITOR"] = "visitor-hook-123", ["FINANCE"] = "finance-hook-123", ["EMP"] = "other-hook-123" }
        });
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        var episode = await db.Set<Episode>().SingleAsync();
        episode.Category = Category.FINANCE;
        await db.SaveChangesAsync();
        var task = new ReliableTask
        {
            Id = "test-chat-task",
            PrincipalId = episode.PrincipalId,
            ConversationId = id,
            AssistantConfigId = episode.AssistantConfigId,
            BventId = episode.BventId,
            EpisodeId = episode.Id,
            Kind = "INTERNAL",
            EventKey = "remind:" + episode.Id,
            Payload = "{}"
        };
        db.Add(task);
        await db.SaveChangesAsync();
        db.Entry(task).State = EntityState.Detached;
        var tasks = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        Assert.AreEqual(0, (await tasks.DueAsync([], DateTime.UtcNow, 100, default)).Count);
        Assert.AreEqual(2, (await tasks.DueAsync([], DateTime.UtcNow, 100, default, includeConfiguredChatGroups: true)).Count);
        var accepted = false;
        var sent = 0;
        using var http = new HttpClient(new ChatHandler(async request =>
        {
            sent++;
            Assert.AreEqual("https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=finance-hook-123", request.RequestUri!.AbsoluteUri);
            Assert.IsNull(request.Headers.Authorization);
            Assert.AreEqual(task.EventKey, request.Headers.GetValues("Idempotency-Key").Single());
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            StringAssert.Contains(payload.RootElement.GetProperty("text").GetProperty("content").GetString(), id);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(accepted ? "{\"errcode\":0}" : "{\"errcode\":40014}") };
        }));
        var options = new NotificationOptions { Authorization = "must-not-leak" };
        var adapter = new NotificationHttpAdapter(http, Options.Create(options),
            scope.ServiceProvider.GetRequiredService<IConfigurationRepository>(), scope.ServiceProvider.GetRequiredService<IReceptionRepository>(), scope.ServiceProvider.GetRequiredService<ISecretCipher>(), application.Clock);
        await Assert.ThrowsExactlyAsync<NotificationDeliveryException>(() => adapter.SendAsync(task, default));
        accepted = true;
        await adapter.SendAsync(task, default);
        var settings = await db.Set<ChatNotificationConfiguration>().SingleAsync();
        settings.Enabled = false;
        await db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<NotificationDeliveryException>(() => adapter.SendAsync(task, default));
        settings.Enabled = true;
        await db.SaveChangesAsync();
        Assert.AreEqual(2, sent);
        db.Remove(await db.Set<ChatNotificationConfiguration>().SingleAsync());
        await db.SaveChangesAsync();
        Assert.AreEqual(0, (await tasks.DueAsync([], DateTime.UtcNow, 100, default, includeConfiguredChatGroups: true)).Count);
    }

    private sealed class ChatHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
