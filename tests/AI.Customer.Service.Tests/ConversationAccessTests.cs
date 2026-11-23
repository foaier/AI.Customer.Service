using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI.Customer.Service.Domain.Models;
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
    /// <summary>
    /// 真实匿名 HTTP 和 WebSocket 只能访问签发会话，凭据到期/撤销立即失效。
    /// </summary>
    [TestMethod]
    public async Task AnonymousCredentialIsHashedScopedAndRevocable()
    {
        await application.ConfigureAsync();
        using var anonymous = application.CreateClient();
        var created = (await SendAsync(anonymous, HttpMethod.Post, "/api/agent/anonymous/session", new { assistantConfigId = "assistant-a" })).GetProperty("returnObj");
        var id = created.GetProperty("sessionId").GetString()!;
        var credential = created.GetProperty("credential").GetString()!;
        Assert.AreEqual(43, credential.Length);
        Assert.AreEqual(application.Clock.GetUtcNow().UtcDateTime.AddDays(7), created.GetProperty("expiresAt").GetDateTime());
        using var guest = application.CreateClient();
        guest.DefaultRequestHeaders.Add("X-Conversation-Credential", credential);
        await GetAsync(guest, "/api/agent/history_detail?sessionId=" + id);
        await SendAsync(guest, HttpMethod.Post, "/api/agent/anonymous/history_detail", new { sessionId = id, tempUserId = created.GetProperty("tempUserId").GetString(), assistantConfigId = "assistant-a" });
        await SendAsync(guest, HttpMethod.Post, "/api/agent/anonymous/chatStream", new { sessionId = id, tempUserId = "spoofed", userInput = "问题" }, HttpStatusCode.Forbidden);
        // 无 AI 首条消息也能直接申请人工。
        await RequestAsync(guest, id);
        using var owner = application.Client();
        var other = await SessionAsync(owner);
        await GetAsync(guest, "/api/agent/history_detail?sessionId=" + other, HttpStatusCode.Forbidden);
        await GetAsync(owner, "/api/agent/history_detail?sessionId=" + id, HttpStatusCode.Forbidden);
        await GetAsync(guest, "/api/customer-service/agents/queue?topOrgId=org-a&assistantConfigId=assistant-a", HttpStatusCode.Forbidden);
        await GetAsync(guest, ChatConfigPath, HttpStatusCode.Forbidden);
        await SendAsync(anonymous, HttpMethod.Post, "/api/agent/anonymous/session", new { assistantConfigId = "unknown" }, HttpStatusCode.Forbidden);
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
            var grant = await db.Set<AnonymousConversationAccess>().SingleAsync();
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential))), grant.CredentialHash);
            Assert.IsFalse(await db.Set<AiTurn>().AnyAsync());
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var socketClient = application.Server.CreateWebSocketClient();
        var prefix = "ws://localhost/api/customer-service/realtime/ws?credential=" + credential + "&agent=false&afterSeq=0&conversationId=";
        using var socket = await socketClient.ConnectAsync(new Uri(prefix + id), timeout.Token);
        var buffer = new byte[32768];
        var first = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
        Assert.AreEqual(WebSocketMessageType.Text, first.MessageType);
        StringAssert.Contains(Encoding.UTF8.GetString(buffer, 0, first.Count), "snapshot");
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
        using var denied = await socketClient.ConnectAsync(new Uri(prefix + other), timeout.Token);
        Assert.AreEqual(WebSocketMessageType.Close, (await denied.ReceiveAsync(buffer.AsMemory(), timeout.Token)).MessageType);
        Assert.AreEqual(WebSocketCloseStatus.PolicyViolation, denied.CloseStatus);
        guest.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;
        using (var mixed = await guest.GetAsync("/api/agent/history_detail?sessionId=" + id)) Assert.AreEqual(HttpStatusCode.Unauthorized, mixed.StatusCode);
        guest.DefaultRequestHeaders.Authorization = null;
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>();
            (await db.Set<AnonymousConversationAccess>().SingleAsync()).RevokedAt = application.Clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync();
        }
        using (var revoked = await guest.GetAsync("/api/agent/history_detail?sessionId=" + id)) Assert.AreEqual(HttpStatusCode.Unauthorized, revoked.StatusCode);
        var fresh = (await SendAsync(anonymous, HttpMethod.Post, "/api/agent/anonymous/session", new { assistantConfigId = "assistant-a" })).GetProperty("returnObj");
        guest.DefaultRequestHeaders.Remove("X-Conversation-Credential");
        guest.DefaultRequestHeaders.Add("X-Conversation-Credential", fresh.GetProperty("credential").GetString());
        application.Clock.Advance(TimeSpan.FromDays(7));
        using var expired = await guest.GetAsync("/api/agent/history_detail?sessionId=" + fresh.GetProperty("sessionId").GetString());
        Assert.AreEqual(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    /// <summary>
    /// 身份策略在创建时冻结；绑定只属于原访客，未绑定不写 AI 轮次，不能以 cookie 替代授权。
    /// </summary>
    [TestMethod]
    public async Task ExternalIdentityIsFirstWriteOnlyAndPolicyIsFrozen()
    {
        await using var app = new TestApplication { ExternalIdentityRequired = true };
        using var visitor = app.Client();
        var created = (await SendAsync(visitor, HttpMethod.Post, "/api/agent/session", new { assistantConfigId = "assistant-a" })).GetProperty("returnObj");
        var id = created.GetProperty("sessionId").GetString()!;
        Assert.IsTrue(created.GetProperty("externalIdentityRequired").GetBoolean());
        var options = app.Services.GetRequiredService<IOptions<AssistantOptions>>().Value;
        options.Items[0] = options.Items[0] with { ExternalIdentityRequired = false };
        using (var blocked = await visitor.PostAsJsonAsync("/api/agent/chatStream", new { sessionId = id, userInput = "问题" }))
        {
            Assert.AreEqual(HttpStatusCode.Conflict, blocked.StatusCode);
            StringAssert.Contains(await blocked.Content.ReadAsStringAsync(), "EXTERNAL_IDENTITY_REQUIRED");
        }
        await using (var scope = app.Services.CreateAsyncScope())
            Assert.IsFalse(await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<AiTurn>().AnyAsync());
        const string cookie = " opaque-cookie-value ";
        var path = "/api/agent/session/" + id + "/external-identity";
        using var other = app.Client("agent-a");
        await SendAsync(other, HttpMethod.Post, path, new { cookieId = cookie }, HttpStatusCode.Forbidden);
        using var otherTenant = app.Client("visitor", "tenant-b");
        await SendAsync(otherTenant, HttpMethod.Post, path, new { cookieId = cookie }, HttpStatusCode.Forbidden);
        var bound = await SendAsync(visitor, HttpMethod.Post, path, new { cookieId = cookie });
        Assert.IsFalse(bound.GetRawText().Contains(cookie, StringComparison.Ordinal));
        await SendAsync(visitor, HttpMethod.Post, path, new { cookieId = cookie });
        await SendAsync(visitor, HttpMethod.Post, path, new { cookieId = "different" }, HttpStatusCode.Conflict);
        await SendAsync(visitor, HttpMethod.Post, path, new { cookieId = new string('x', 513) }, HttpStatusCode.BadRequest);
        using (var chat = await visitor.PostAsJsonAsync("/api/agent/chatStream", new { sessionId = id, userInput = "问题" }))
            Assert.AreEqual(HttpStatusCode.OK, chat.StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
            Assert.AreEqual(cookie, (await scope.ServiceProvider.GetRequiredService<CustomerServiceDbContext>().Set<ConversationExternalIdentity>().SingleAsync()).ExternalAuthId);
        using var anonymous = app.CreateClient();
        var guestSession = (await SendAsync(anonymous, HttpMethod.Post, "/api/agent/anonymous/session", new { assistantConfigId = "assistant-a" })).GetProperty("returnObj");
        var guestId = guestSession.GetProperty("sessionId").GetString()!;
        anonymous.DefaultRequestHeaders.Add("X-Conversation-Credential", guestSession.GetProperty("credential").GetString());
        await SendAsync(anonymous, HttpMethod.Post, "/api/agent/anonymous/session/" + guestId + "/external-identity", new { cookieId = cookie });
        await SendAsync(anonymous, HttpMethod.Post, path, new { cookieId = cookie }, HttpStatusCode.Forbidden);
        using var anonymousChat = await anonymous.PostAsJsonAsync("/api/agent/anonymous/chatStream", new { sessionId = guestId, tempUserId = guestSession.GetProperty("tempUserId").GetString(), userInput = "问题" });
        Assert.AreEqual(HttpStatusCode.OK, anonymousChat.StatusCode);
    }
}
