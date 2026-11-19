import assert from "node:assert/strict";
import { fileURLToPath } from "node:url";
import { chromium, expect } from "@playwright/test";
import { createServer } from "../../frontend/node_modules/vite/dist/node/index.js";

// 运行真实 React 页面；仅替代外部登录、API 和实时传输。API 的持久化与权限另由 .NET 集成测试覆盖。
const root = fileURLToPath(new URL("../../frontend", import.meta.url));
const server = await createServer({
    root,
    server: { host: "127.0.0.1", port: 0, strictPort: false },
    logLevel: "error",
});
let browser;
try {
    await server.listen();
    const base = `http://127.0.0.1:${server.httpServer.address().port}`;
    browser = await chromium.launch({ headless: true, channel: process.env.PLAYWRIGHT_CHANNEL || undefined });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = [];
    page.on("pageerror", (error) => errors.push(error.message));
    await page.addInitScript(() => {
        window.AUTH_SCRIPTS_READY = Promise.resolve();
        window.OIDC_CONFIG = { getConfig: () => ({}) };
        window.oidcByt = {
            OidcBytAuth: class {
                async getToken() {
                    return "frontend-test-only";
                }
            },
        };
    });
    const episodes = ["read", "unread"].map((id, index) => ({
        episodeId: id,
        scope: { conversationId: id, assistantConfigId: "assistant-a" },
        status: "ACTIVE",
        version: 1,
        principalId: `访客-${id}`,
        requestedAt: new Date().toISOString(),
        unreadCount: index,
        lastVisitorSeq: 1,
        readSeq: 1 - index,
    }));
    const groups = ["EXHIBITOR", "VISITOR", "MEDIA", "OTHER"].map((category) => ({
        id: category,
        category,
        name: category === "MEDIA" ? "媒体组" : category,
        members: [],
    }));
    let chat = { version: 0, enabled: false, activeProvider: "WECOM_ROBOT", chatGroup: {} };
    let configMissing = false;
    let conflict = false;
    let puts = 0;
    let queueReads = 0;
    const receipts = [];
    const messageReads = [];
    let inboxSocket;
    let notificationToken = "initial";
    const summary = () => ({
        unreadMessageCount: 1,
        unreadConversationCount: 1,
        newFollowupCount: 0,
        pendingReceptionCount: 0,
        changeToken: notificationToken,
    });
    await page.routeWebSocket(/\/api\/customer-service\/realtime\/ws/, (socket) => {
        const url = new URL(socket.url());
        assert.equal(url.pathname, "/api/customer-service/realtime/ws", "React 路由不应进入 WebSocket API 路径");
        if (!url.searchParams.has("conversationId")) {
            inboxSocket = socket;
            socket.send(
                JSON.stringify({
                    type: "snapshot",
                    data: { queue: url.searchParams.get("assistantConfigId") === "assistant-b" ? [] : episodes },
                }),
            );
            socket.send(JSON.stringify({ type: "inbox.notifications", data: summary() }));
        }
    });
    let heldHistory;
    let holdHistory = false;
    await page.route("**/api/**", async (route) => {
        const url = new URL(route.request().url());
        const path = url.pathname;
        let data;
        let status = 200;
        if (path === "/api/assistant/config/list")
            data = ["a", "b"].map((id) => ({
                id: `assistant-${id}`,
                name: `助手${id}`,
                topOrgId: "org-a",
            }));
        else if (path.endsWith("/agents/queue")) {
            queueReads++;
            data = { episodes: url.searchParams.get("assistantConfigId") === "assistant-b" ? [] : episodes };
        } else if (path.endsWith("/availability")) data = { status: "AWAY", version: 0 };
        else if (path.endsWith("/inbox/summary")) data = summary();
        else if (path.endsWith("/messages")) {
            const id = path.split("/").at(-2);
            messageReads.push(id);
            data = {
                messages: [
                    {
                        messageId: `message-${id}`,
                        seq: 1,
                        senderRole: "VISITOR",
                        content: `${id} 最新留言`,
                        createdAt: new Date().toISOString(),
                    },
                ],
            };
        } else if (path.endsWith("/history")) {
            if (holdHistory && path.includes("/read/")) {
                heldHistory = route;
                return;
            }
            data = [];
        } else if (path.endsWith("/receipts")) {
            receipts.push(route.request().postDataJSON());
            data = {};
        } else if (path.endsWith("/configuration/chat-notifications")) {
            if (configMissing) {
                status = 404;
                data = { message: "未初始化" };
            } else if (route.request().method() === "PUT") {
                puts++;
                const body = route.request().postDataJSON();
                assert.deepEqual(Object.keys(body).sort(), ["chatGroup", "version"]);
                if (conflict) {
                    status = 409;
                    data = { message: "群通知配置版本已变化，请重新加载" };
                } else {
                    chat = { ...chat, ...body, version: chat.version + 1 };
                    data = chat;
                }
            } else data = { ...chat, apiTokenConfigured: true };
        } else if (path.endsWith("/configuration/calendar"))
            data = {
                version: 0,
                calendarVersion: "v1",
                zone: "Asia/Shanghai",
                opens: "09:00",
                closes: "18:00",
                overrides: {},
            };
        else if (path.endsWith("/configuration")) data = { version: 1, groups, routes: [] };
        else if (path.endsWith("/agents/followups"))
            data = [
                {
                    scope: { conversationId: "seen" },
                    episodeId: "seen",
                    ticketId: "已查看工单",
                    state: "OPEN",
                    hasUnseen: false,
                },
                {
                    scope: { conversationId: "fresh" },
                    episodeId: "fresh",
                    ticketId: "新留言工单",
                    state: "OPEN",
                    hasUnseen: true,
                },
            ];
        else throw new Error(`未预期接口 ${path}`);
        await route.fulfill({ status, json: status === 200 ? { code: "OK", data } : data });
    });

    await page.goto(base + "/b/inbox");
    await expect(page.locator(".queue-item").first()).toContainText("访客-unread");
    await expect(page.locator(".queue-preview").first()).toHaveText("unread 最新留言");
    await expect(page.locator(".queue-preview").last()).toHaveText("read 最新留言");
    await expect(page.locator(".metric-status")).toHaveText("暂离");
    assert.equal(receipts.length, 0, "后台预览不能标记已读");
    const previewsLoaded = messageReads.length;
    inboxSocket.send(JSON.stringify({ type: "snapshot", data: { queue: episodes } }));
    await expect(page.locator(".queue-item")).toHaveCount(2);
    const originalReads = queueReads;
    notificationToken = "changed";
    for (let i = 0; i < 5; i++) inboxSocket.send(JSON.stringify({ type: "inbox.notifications", data: summary() }));
    await expect.poll(() => queueReads).toBe(originalReads + 1);
    assert.equal(messageReads.length, previewsLoaded, "相同水位的预览应复用");
    holdHistory = true;
    await page.locator(".queue-item").filter({ hasText: "访客-read" }).click();
    await expect.poll(() => Boolean(heldHistory)).toBe(true);
    await page.locator(".queue-item").filter({ hasText: "访客-unread" }).click();
    await expect.poll(() => receipts.length).toBe(1);
    await heldHistory.fulfill({
        json: { code: "OK", data: [{ id: "late", role: "assistant", content: "迟到的旧会话" }] },
    });
    await expect(page.locator(".conversation-panel")).not.toContainText("迟到的旧会话");
    await page.getByLabel("切换助手").selectOption("assistant-b");
    await expect(page.locator(".queue-item")).toHaveCount(0);
    await expect(page.getByText("选择一条接待记录", { exact: true })).toBeVisible();
    console.log("PASS 会话预览、未读排序、预览无 READ、实时去重、旧响应隔离、助手切换");

    await page.getByRole("link", { name: "分组与营业时间" }).click();
    const form = page.locator(".chat-notification-section");
    await expect(form.getByRole("button", { name: "保存群通知" })).toBeDisabled();
    for (const [label, value] of [
        ["展商客服", "sales-hook-123"],
        ["观众客服", "visitor-hook-123"],
        ["财务客服", "finance-hook-123"],
        ["其他客服", "other-hook-123"],
    ]) {
        await form.getByLabel(label, { exact: true }).fill(value);
    }
    await form.getByRole("button", { name: "保存群通知" }).click();
    await expect(form.getByRole("status")).toHaveText("企业微信群配置已保存。");
    assert.equal(puts, 1);
    await expect(form.getByRole("button", { name: "保存群通知" })).toBeDisabled();
    const reloadResponse = page.waitForResponse(
        (response) => response.url().includes("chat-notifications") && response.request().method() === "GET",
    );
    await form.getByRole("button", { name: "重新读取" }).click();
    await reloadResponse;
    await expect(form.getByRole("button", { name: "重新读取" })).toBeEnabled();
    await expect(form.getByLabel("财务客服", { exact: true })).toHaveValue("finance-hook-123");
    conflict = true;
    await form.getByLabel("财务客服", { exact: true }).fill("finance-hook-new");
    await form.getByRole("button", { name: "保存群通知" }).click();
    await expect(form.getByRole("alert")).toContainText("版本已变化");
    await expect(form.getByLabel("财务客服", { exact: true })).toHaveValue("finance-hook-new");
    await page.setViewportSize({ width: 390, height: 844 });
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
    configMissing = true;
    await form.getByRole("button", { name: "重新读取" }).click();
    await expect(form.getByRole("alert")).toContainText("暂时不能保存");
    await expect(form.getByLabel("财务客服", { exact: true })).toBeDisabled();
    console.log("PASS 群配置保存/重读、无变化禁用、冲突保留草稿、404 禁用、移动端布局");
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.getByRole("link", { name: "邮件跟进" }).click();
    await expect(page.locator(".followup-row").first()).toContainText("新留言工单");
    assert.deepEqual(errors, []);
    console.log("PASS 留言未查看优先；无浏览器运行错误");
} finally {
    await browser?.close();
    await server.close();
}
