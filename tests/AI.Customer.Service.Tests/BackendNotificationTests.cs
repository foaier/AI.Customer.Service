using System.Net;
using System.Text;
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
    private static Dictionary<string, string> NotificationGroups() => new()
    {
        ["SALES"] = "sales-hook-123",
        ["VISITOR"] = "visitor-hook-123",
        ["FINANCE"] = "finance-hook-123",
        ["EMP"] = "other-hook-123"
    };

    /// <summary>
    /// 即时登记一次；关闭不耗次数；永久失败可按原周期人工恢复；取消后旧命令不能重放。
    /// </summary>
    [TestMethod]
    public async Task ImmediateNotificationIsIdempotentPausedAndManuallyRecoverable()
    {
        await application.ConfigureAsync();
        application.Notification.UseConfiguredChatGroups = true;
        using var agent = application.Client("agent-a");
        await SendAsync(agent, HttpMethod.Put, ChatConfigPath, new { version = 0, enabled = true, publicBaseUrl = "https://support.example.com", chatGroup = NotificationGroups() });
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        var first = await RequestAsync(visitor, id, "first");
        await RequestAsync(visitor, id, "first");
        application.Clock.Advance(TimeSpan.FromMinutes(5));
        await GetAsync(visitor, VisitorPath(id) + "/waiting");
        await SendAsync(agent, HttpMethod.Put, ChatConfigPath, new { version = 1, enabled = false, chatGroup = NotificationGroups() });
        await RunWorkerAsync();
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
            var task = await db.Set<ReliableTask>().SingleAsync(x => x.Kind == "HANDOFF_NOTIFY");
            Assert.AreEqual(0, task.Attempts);
            Assert.AreEqual(DeliveryState.PENDING, task.State);
            Assert.IsFalse(await db.Set<ReliableTask>().AnyAsync(x => x.Kind == "INTERNAL"));
        }
        await SendAsync(agent, HttpMethod.Put, ChatConfigPath, new { version = 2, enabled = true, chatGroup = NotificationGroups() });
        application.Notification.Failure = new("CONFIG_PROVIDER_MISMATCH", false);
        await RunWorkerAsync();
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var task = await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<ReliableTask>().SingleAsync(x => x.Kind == "HANDOFF_NOTIFY");
            Assert.AreEqual(DeliveryState.DEAD, task.State);
            Assert.AreEqual(1, task.Attempts);
        }
        var retryPath = AgentPath(id) + "/notifications/retry";
        await SendAsync(agent, HttpMethod.Post, retryPath, new { });
        application.Notification.Failure = new("ADSALE_HTTP_REJECTED", true);
        await RunWorkerAsync();
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var task = await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<ReliableTask>().SingleAsync(x => x.Kind == "HANDOFF_NOTIFY");
            Assert.AreEqual(DeliveryState.PENDING, task.State);
            Assert.IsTrue(task.DueAt > application.Clock.GetUtcNow().UtcDateTime);
        }
        application.Clock.Advance(TimeSpan.FromSeconds(5));
        application.Notification.Failure = null;
        await RunWorkerAsync();
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/reception/cancel", new { requestId = "cancel", expectedVersion = first.GetProperty("version").GetInt64() });
        await SendAsync(visitor, HttpMethod.Post, VisitorPath(id) + "/handoffs", new { requestId = "first", email = "visitor@example.com" }, HttpStatusCode.Conflict);
        var next = await RequestAsync(visitor, id, "second");
        Assert.AreNotEqual(first.GetProperty("episodeId").GetString(), next.GetProperty("episodeId").GetString());
        await using var finalScope = application.Services.CreateAsyncScope();
        Assert.AreEqual(2, await finalScope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<ReliableTask>().CountAsync(x => x.Kind == "HANDOFF_NOTIFY"));
    }

    /// <summary>
    /// 真实配置接口保护密钥，省略保留、显式清除；雅式遵循固定地址、认证头和严格回执。
    /// </summary>
    [TestMethod]
    public async Task AdsaleTokenAndReceiptFollowProviderContract()
    {
        await application.ConfigureAsync();
        using var agent = application.Client("agent-a");
        const string token = "test-secret-token";
        var saved = await SendAsync(agent, HttpMethod.Put, ChatConfigPath, new
        {
            version = 0,
            enabled = true,
            activeProvider = "ADSALE",
            publicBaseUrl = "https://support.example.com",
            apiToken = token,
            chatGroup = NotificationGroups()
        });
        Assert.IsTrue(saved.GetProperty("apiTokenConfigured").GetBoolean());
        Assert.IsFalse(saved.GetRawText().Contains(token, StringComparison.Ordinal));
        await SendAsync(agent, HttpMethod.Put, ChatConfigPath, new { version = 1, chatGroup = NotificationGroups() });
        using var visitor = application.Client();
        var id = await SessionAsync(visitor);
        await RequestAsync(visitor, id);
        await using var scope = application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
        var settings = await db.Set<ChatNotificationConfiguration>().SingleAsync();
        Assert.IsNotNull(settings.ApiTokenCiphertext);
        Assert.IsFalse(settings.ApiTokenCiphertext.Contains(token, StringComparison.Ordinal));
        var cipher = scope.ServiceProvider.GetRequiredService<ISecretCipher>();
        Assert.AreEqual(token, cipher.Unprotect(settings.ApiTokenCiphertext));
        var wrongCipher = new NotificationTokenCipher(Options.Create(new NotificationOptions { EncryptionKey = Convert.ToBase64String(new byte[32]) }));
        Assert.ThrowsExactly<BusinessException>(() => wrongCipher.Unprotect(settings.ApiTokenCiphertext));
        var task = await db.Set<ReliableTask>().SingleAsync(x => x.Kind == "HANDOFF_NOTIFY");
        var snapshot = JsonSerializer.Deserialize<HandoffNotificationSnapshot>(task.Payload, JsonDefaults.Options)!;
        settings.ChatGroup[snapshot.NotificationCategory] = "changed-after-request";
        await db.SaveChangesAsync();
        var receipt = "{\"success\":true,\"data\":{\"request_id\":\"request_123:ok\"}}";
        var status = HttpStatusCode.OK;
        var sends = 0;
        using var http = new HttpClient(new ChatHandler(async request =>
        {
            sends++;
            Assert.AreEqual("https://hub.adsaleinfo.com/open-api/messages/send", request.RequestUri!.AbsoluteUri);
            Assert.AreEqual(token, request.Headers.GetValues("X-API-Key").Single());
            Assert.IsNull(request.Headers.Authorization);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.AreEqual("group", body.RootElement.GetProperty("recipient_type").GetString());
            Assert.AreEqual("text", body.RootElement.GetProperty("msg_type").GetString());
            Assert.AreEqual(snapshot.ChatId, body.RootElement.GetProperty("chat_id").GetString());
            StringAssert.Contains(body.RootElement.GetProperty("content").GetProperty("content").GetString(), "/b/inbox?");
            return new HttpResponseMessage(status) { Content = new StringContent(receipt, Encoding.UTF8, "application/json") };
        }));
        var adapter = new NotificationHttpAdapter(http, Options.Create(new NotificationOptions()),
            scope.ServiceProvider.GetRequiredService<IConfigurationRepository>(), scope.ServiceProvider.GetRequiredService<IReceptionRepository>(), cipher);
        await adapter.SendAsync(task, default);
        Assert.AreEqual("request_123:ok", task.ProviderRequestId);
        foreach (var bad in new[] { "{}", "{\"success\":true}", "{\"success\":false,\"request_id\":\"ok\"}", "{\"success\":true,\"request_id\":\"bad id\"}" })
        {
            receipt = bad;
            Assert.IsFalse((await Assert.ThrowsExactlyAsync<NotificationDeliveryException>(() => adapter.SendAsync(task, default))).Retryable);
        }
        status = HttpStatusCode.ServiceUnavailable;
        Assert.IsTrue((await Assert.ThrowsExactlyAsync<NotificationDeliveryException>(() => adapter.SendAsync(task, default))).Retryable);
        status = HttpStatusCode.Unauthorized;
        Assert.IsFalse((await Assert.ThrowsExactlyAsync<NotificationDeliveryException>(() => adapter.SendAsync(task, default))).Retryable);
        settings.ActiveProvider = "WECOM_ROBOT";
        var sentBefore = sends;
        Assert.AreEqual("CONFIG_PROVIDER_MISMATCH", (await Assert.ThrowsExactlyAsync<NotificationDeliveryException>(() => adapter.SendAsync(task, default))).Code);
        Assert.AreEqual(sentBefore, sends);
        // 使用另一个请求作用域读取已保存版本；禁用后才能清空雅式令牌。
        await SendAsync(agent, HttpMethod.Put, ChatConfigPath, new { version = 2, enabled = false, clearApiToken = true, chatGroup = NotificationGroups() });
        Assert.IsFalse((await GetAsync(agent, ChatConfigPath)).GetProperty("apiTokenConfigured").GetBoolean());
    }

    /// <summary>
    /// 中文和 emoji 摘要裁剪遵守 UTF-8 字节上限，完整链接始终保留。
    /// </summary>
    [TestMethod]
    public void NotificationTextRetainsCompleteLinksAndUnicode()
    {
        var task = new ReliableTask { AssistantConfigId = "assistant-a", ConversationId = "conversation-a" };
        var text = HandoffNotificationText.Render(task, new("WECOM_ROBOT", "FINANCE", null,
            "https://hub.adsaleinfo.com/open-api", "https://support.example.com", DateTime.UtcNow.AddMinutes(-3), "zh-CN",
            string.Concat(Enumerable.Repeat("发票😀", 1000)), new string('答', 2000)));
        Assert.IsFalse(text.Contains("已等待"));
        Assert.IsTrue(Encoding.UTF8.GetByteCount(text) <= 2048);
        Assert.IsFalse(text.Contains('\uFFFD'));
        StringAssert.Contains(text, "FINANCE");
        StringAssert.Contains(text, "😀");
        Assert.AreEqual(2, text.Split("https://support.example.com/b/inbox?assistantConfigId=assistant-a&conversationId=conversation-a").Length - 1);
    }

    /// <summary>
    /// EMP 分类、已填写联系邮箱和企微链接按安全文本展示。
    /// </summary>
    [TestMethod]
    public void NotificationTextShowsContactAndEscapesMarkdown()
    {
        var task = new ReliableTask { AssistantConfigId = "assistant-a", ConversationId = "conversation-a" };
        var snapshot = new HandoffNotificationSnapshot("WECOM_ROBOT", "EMP", null,
            "https://hub.adsaleinfo.com/open-api", "https://support.example.com", DateTime.UtcNow, "zh-CN", "问题摘要", null,
            "person_[a]@example.com");

        var text = HandoffNotificationText.Render(task, snapshot);

        StringAssert.Contains(text, "其他客服（EMP）");
        StringAssert.Contains(text, "person\\_\\[a\\]@example.com");
        StringAssert.Contains(text, "[接手对话](https://support.example.com/b/inbox?");
        Assert.IsFalse(snapshot.ToString().Contains("person_[a]@example.com", StringComparison.Ordinal));
    }

    /// <summary>
    /// 财务是正式枚举，拒绝旧类别写回；默认组引用保持历史散列不变。
    /// </summary>
    [TestMethod]
    public async Task FinanceIsCanonicalAndOldCategoryIsRejected()
    {
        using var agent = application.Client("agent-a");
        var value = await GetAsync(agent, "/api/customer-service/configuration");
        Assert.IsTrue(value.GetProperty("groups").EnumerateArray().Any(x => x.GetProperty("category").GetString() == "FINANCE"));
        var stale = value.GetRawText().Replace("FINANCE", "MEDIA", StringComparison.Ordinal);
        using var body = JsonDocument.Parse(stale);
        await SendAsync(agent, HttpMethod.Put, "/api/customer-service/configuration", body.RootElement, HttpStatusCode.BadRequest);
    }
}
