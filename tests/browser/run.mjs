import assert from "node:assert/strict";
import { createHmac, randomBytes } from "node:crypto";
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { createServer } from "node:http";
import { createServer as createTcpServer } from "node:net";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import { chromium } from "@playwright/test";

// 测试专用：启动正式 Kestrel、真实签名 JWT 和隔离 SQLite，只有外部模型/目录/登录 SDK 被替代。
const root = process.cwd();
const temporary = await mkdtemp(path.join(tmpdir(), "ai-customer-browser-"));
const artifacts = path.join(root, "artifacts/browser");
await mkdir(artifacts, { recursive: true });
const key = randomBytes(48).toString("hex");
const errors = [];
const checks = [];
let log = "";
let browser;
let application;
const external = createServer(async (request, response) => {
    try {
        let text = "";
        for await (const chunk of request) text += chunk;
        const body = text ? JSON.parse(text) : {};
        response.setHeader("Content-Type", "application/json");
        let result;
        if (request.url.endsWith("/Info")) {
            result = {
                isSuccess: true,
                returnObj: { accountId: body.accountId, tenantId: "tenant-a", fullName: "浏览器客服" },
            };
        } else if (request.url.startsWith("/api/Organization/")) {
            result = { isSuccess: true, returnObj: [{ orgId: "org-a", status: 1 }] };
        } else if (request.url.endsWith("/list")) {
            result = {
                isSuccess: true,
                returnObj: [{ id: "directory-a", orgId: "org-a", name: "客服组" }],
                pager: { pageIndex: body.PageIndex, pageSize: body.PageSize, totalPage: 1, totalCount: 1 },
            };
        } else if (request.url.endsWith("/accountDetails")) {
            result = {
                isSuccess: true,
                returnObj: [{ id: "agent-a", fullName: "浏览器客服" }],
                pager: { pageIndex: body.PageIndex, pageSize: body.PageSize, totalPage: 1, totalCount: 1 },
            };
        } else if (request.url === "/v1/chat/completions" && body.stream) {
            response.setHeader("Content-Type", "text/event-stream");
            response.end(
                'data: {"id":"browser-chat","object":"chat.completion.chunk","created":1794441600,"model":"browser-test","choices":[{"index":0,"delta":{"role":"assistant","content":"请携带有效证件前往服务台办理。还有其他问题需要帮助吗？"},"finish_reason":"stop"}]}\n\ndata: [DONE]\n\n',
            );
            return;
        } else if (request.url === "/v1/chat/completions") {
            result = {
                id: "browser-classification",
                object: "chat.completion",
                created: 1794441600,
                model: "browser-test",
                choices: [
                    {
                        index: 0,
                        message: { role: "assistant", content: '{"category":"OTHER","confidence":0.9}' },
                        finish_reason: "stop",
                    },
                ],
            };
        } else if (request.url === "/notifications") {
            result = { accepted: true };
        } else {
            response.statusCode = 404;
            result = { error: "未知测试外部接口" };
        }
        response.end(JSON.stringify(result));
    } catch (error) {
        errors.push(String(error));
        response.statusCode = 500;
        response.end("{}");
    }
});

/** 为独立测试应用保留本机动态端口，不依赖用户开发服务。 */
async function freePort() {
    const socket = createTcpServer();
    await new Promise((resolve) => socket.listen(0, "127.0.0.1", resolve));
    const port = socket.address().port;
    await new Promise((resolve) => socket.close(resolve));
    return port;
}

/** 生成测试账号签名，生产应用仍执行发行者、受众、有效期和组织校验。 */
function token(account) {
    const encode = (value) => Buffer.from(JSON.stringify(value)).toString("base64url");
    const now = Math.floor(Date.now() / 1000);
    const unsigned =
        encode({ alg: "HS256", typ: "JWT" }) +
        "." +
        encode({
            iss: "browser-tests",
            aud: "browser-tests",
            exp: now + 1800,
            nbf: now - 1,
            tenantId: "tenant-a",
            accountId: account,
        });
    return unsigned + "." + createHmac("sha256", key).update(unsigned).digest("base64url");
}

try {
    await new Promise((resolve) => external.listen(0, "127.0.0.1", resolve));
    const dependency = `http://127.0.0.1:${external.address().port}`;
    const base = `http://127.0.0.1:${await freePort()}`;
    application = spawn(
        "dotnet",
        [path.join(root, "src/AI.Customer.Service.Web/bin/Debug/net10.0/AI.Customer.Service.dll"), "--urls", base],
        {
            cwd: path.join(root, "src/AI.Customer.Service.Web"),
            windowsHide: true,
            env: {
                ...process.env,
                ASPNETCORE_ENVIRONMENT: "Development",
                Database__Provider: "Sqlite",
                Database__ConnectionString: `Data Source=${path.join(temporary, "browser.db")}`,
                Authentication__Authority: "",
                Authentication__Issuer: "browser-tests",
                Authentication__Audience: "browser-tests",
                Authentication__SigningKey: key,
                Assistants__Items__0__Id: "assistant-a",
                Assistants__Items__0__TenantId: "tenant-a",
                Assistants__Items__0__TopOrgId: "org-a",
                UserCenter__BaseUrl: dependency,
                Model__BaseUrl: dependency + "/v1",
                Model__Model: "browser-test",
                CustomerService__Calendar__Opens: "00:00",
                CustomerService__Calendar__Closes: "23:59:59",
                Notifications__Enabled: "true",
                Notifications__Endpoint: dependency + "/notifications",
            },
            stdio: ["ignore", "pipe", "pipe"],
        },
    );
    application.stdout.on("data", (data) => {
        log += data;
    });
    application.stderr.on("data", (data) => {
        log += data;
    });
    for (let attempt = 0; attempt < 100; attempt++) {
        if (application.exitCode !== null) throw new Error("测试宿主启动失败");
        if (
            await fetch(base + "/health")
                .then((r) => r.ok)
                .catch(() => false)
        )
            break;
        await new Promise((resolve) => setTimeout(resolve, 100));
    }
    /** 使用真实 HTTP 配置边界为页面建立接待路由，不直接写数据库。 */
    async function api(url, method = "GET", body, account = "agent-a") {
        const response = await fetch(base + url, {
            method,
            headers: { Authorization: "Bearer " + token(account), "Content-Type": "application/json" },
            body: body === undefined ? undefined : JSON.stringify(body),
        });
        const json = await response.json();
        assert.equal(response.status, 200, JSON.stringify(json));
        return json.data ?? json.returnObj;
    }
    const config = await api("/api/customer-service/configuration?topOrgId=org-a");
    const groups = config.groups.map((group) => ({
        ...group,
        members: [{ accountId: "agent-a", displayName: "不可信页面名称" }],
    }));
    await api("/api/customer-service/configuration?topOrgId=org-a", "PUT", {
        version: config.version,
        groups,
        routes: [
            { assistantConfigId: "assistant-a", groups: Object.fromEntries(groups.map((g) => [g.category, g.id])) },
        ],
    });
    const today = new Intl.DateTimeFormat("en-CA", {
        timeZone: "Asia/Shanghai",
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
    }).format(new Date());
    await api("/api/customer-service/configuration/calendar?topOrgId=org-a", "PUT", {
        version: 0,
        calendarVersion: "browser-v1",
        zone: "Asia/Shanghai",
        opens: "00:00",
        closes: "23:59:59",
        overrides: { [today]: true },
    });
    browser = await chromium.launch({ headless: true, channel: process.env.PLAYWRIGHT_CHANNEL || undefined });
    /** 页面只替代外部登录 SDK，传给正式 API 的仍是可验证 JWT。 */
    async function pageFor(account) {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
        page.on("pageerror", (error) => errors.push(error.message));
        page.on("response", (response) => {
            if (response.status() >= 400) errors.push(`${response.status()} ${new URL(response.url()).pathname}`);
        });
        await page.route("**/api/auth/config", (route) =>
            route.fulfill({ json: { authority: "https://login.invalid", clientId: "test", scope: "openid" } }),
        );
        await page.route("**/auth/js/oidc-byt.min.js", (route) =>
            route.fulfill({
                contentType: "text/javascript",
                body: `window.oidcByt={OidcBytAuth:class {async getToken(){return ${JSON.stringify(token(account))}} async login(){}}};`,
            }),
        );
        return page;
    }
    const visitor = await pageFor("visitor");
    await visitor.goto(base + "/customer-service/visitor.html?assistantConfigId=assistant-a");
    await visitor.getByText("服务已就绪", { exact: true }).waitFor();
    await visitor.locator("#user-input").fill("请问如何办理参展证？");
    await visitor.locator("#send-message").click();
    await visitor
        .locator("#chat-messages")
        .getByText("请携带有效证件前往服务台办理。还有其他问题需要帮助吗？", { exact: true })
        .waitFor();
    checks.push("访客登录、可信助手配置及真实 SSE 文本渲染");
    await visitor.locator("#request-handoff").click();
    await visitor.getByText("正在等待客服接单", { exact: true }).waitFor();
    const id = await visitor.locator("#conversation-id").textContent();
    const agent = await pageFor("agent-a");
    await agent.goto(base + "/customer-service/agent.html?assistantConfigId=assistant-a");
    await agent.locator("#agent-queue-list button").first().click();
    await agent.locator("#agent-claim").click();
    await agent.locator("#agent-input").fill("您好，人工客服为您服务。");
    await agent.locator("#agent-send").click();
    await visitor.getByText("您好，人工客服为您服务。", { exact: true }).waitFor();
    await visitor.locator("#user-input").fill("请帮我核实办证时间。");
    await visitor.locator("#send-message").click();
    await agent.getByText("请帮我核实办证时间。", { exact: true }).waitFor();
    await agent.locator("#agent-resolve").click();
    await visitor.getByText("接待已结束，可继续向 AI 咨询", { exact: true }).waitFor();
    checks.push("离线默认客服直接认领、WebSocket 双向消息、首响及结束恢复 AI");
    const emailVisitor = await pageFor("email-visitor");
    await emailVisitor.goto(base + "/customer-service/visitor.html?assistantConfigId=assistant-a");
    await emailVisitor.getByText("服务已就绪", { exact: true }).waitFor();
    await emailVisitor.locator("#request-handoff").click();
    await emailVisitor.locator("#followup-email").fill("visitor@example.com");
    await emailVisitor.locator("#email-followup").click();
    await emailVisitor.getByText(/已创建跟进工单。/).waitFor();
    await agent.locator("#agent-followups").click();
    await agent.locator("#agent-followup-list button").first().click();
    await agent.locator("#claim-followup-form").click();
    await agent.locator("#followup-reply-subject").fill("咨询回复");
    await agent.locator("#followup-reply-content").fill("已核实，请于工作时间办理。");
    await agent.locator("#followup-reply-submit").click();
    for (let attempt = 0; attempt < 12; attempt++) {
        await agent.locator("#followup-reply-load").click();
        if (await agent.locator("#followup-reply-close").isEnabled()) break;
        await new Promise((resolve) => setTimeout(resolve, 500));
    }
    await agent.locator("#followup-reply-close").click();
    await agent.getByText("工单已结案", { exact: true }).waitFor();
    checks.push("首条咨询前直接申请、仅邮箱工单认领、最终回复经网关受理后显式结案");
    await visitor.screenshot({ path: path.join(artifacts, "visitor.png"), fullPage: true });
    await agent.screenshot({ path: path.join(artifacts, "agent.png"), fullPage: true });
    const routing = await pageFor("agent-a");
    await routing.goto(base + "/customer-service/routing.html?topOrgId=org-a&assistantConfigId=assistant-a");
    await routing.locator("#reload").click();
    await routing.locator("#groups input").first().waitFor();
    await routing.locator("#calendar-load").click();
    await routing.getByText("已读取工作时间，版本 1", { exact: true }).waitFor();
    await routing.screenshot({ path: path.join(artifacts, "routing.png"), fullPage: true });
    checks.push("分组与用户中心候选、持久营业日历页面读取");
    assert.ok(id.startsWith("session_"));
    assert.deepEqual(errors, []);
    console.log(JSON.stringify({ passed: checks.length, checks, browserErrors: errors }, null, 2));
} finally {
    await browser?.close();
    if (application && application.exitCode === null) {
        application.kill();
        await new Promise((resolve) => application.once("exit", resolve));
    }
    await new Promise((resolve) => external.close(resolve));
    await writeFile(path.join(artifacts, "server.log"), log);
    await writeFile(path.join(artifacts, "result.json"), JSON.stringify({ checks, errors }, null, 2));
    // 只移除本测试刚创建并保存的临时目录，绝不触碰项目数据和 Java 原库。
    assert.ok(path.resolve(temporary).startsWith(path.resolve(tmpdir()) + path.sep));
    await rm(temporary, { recursive: true, force: true });
}
