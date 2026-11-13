# AI.Customer.Service

从 `byt-cloud-agent` 的智能客服模块独立迁移的 **.NET 10 + Microsoft Agent Framework 1.28.0** 项目，按原 Java 的 Common / Api / Domain / Infrastructure / Core / Service / Web 拆分。源码位于本目录，不依赖 Java 工程构建，也不修改 Java 代码或原数据库。

已迁移 AI 文本咨询、转人工建议与确认、接单/首响/消息/结束、WebSocket、分组路由、营业日历、等待及邮箱跟进、留言认领、最终邮件回复/结案、满意度、可靠任务和分页导出。完整边界及 Java 对照见 [迁移说明](docs/migration.md)。

AI 对话、流式输出、语义分类和受限工具执行均通过 Microsoft Agent Framework 的 `ChatClientAgent` 运行，使用官方 OpenAI provider 连接兼容模型端点。框架负责工具调用与结果续轮；业务状态由原有领域规则和成功轮次围栏控制。接入细节见 [Agent Framework 实现](docs/agent-framework.md)。

## 本机运行

需要稳定版 .NET SDK **10.0.302**（允许同补丁系列更新）。运行后无需 Java、Consul、Redis 或 Node.js；Node.js 只用于前端检查。

```powershell
cd D:\MyCode\AI.Customer.Service
dotnet restore AI.Customer.Service.sln
dotnet run --project src/AI.Customer.Service.Web
```

默认地址：<http://localhost:5180>。也可执行 `./scripts/start.ps1 -Port 5180`。

默认用独立 SQLite 数据库，文件在 `src/AI.Customer.Service.Web/data/runtime/customer-service.db`，仅对空库初始化。`/health` 为进程存活检查，不代表外部依赖已配置或可用。开发模式 OpenAPI：`/openapi/v1.json`。

模板不含真实地址、账号和密钥。完成登录、模型、用户中心及助手配置后才能实际咨询和接单：将 `config/appsettings.Local.example.json` 复制为 `src/AI.Customer.Service.Web/appsettings.Local.json`，替换示例值。该本机文件已排除 Git。全部配置规则见 [配置说明](docs/configuration.md)。

入口：

- `/customer-service/debug.html`：选择助手后进入各业务页面。
- `/customer-service/visitor.html?assistantConfigId=助手ID`：访客聊天。
- `/customer-service/agent.html?assistantConfigId=助手ID`：客服工作台。
- `/customer-service/routing.html?topOrgId=组织ID&assistantConfigId=助手ID`：分组、路由、营业时间和聊天导出。

页面复用统一 OIDC 登录；在登录平台登记 `/auth/callback.html` 和 `/auth/silent.html` 回调。首次由具备组织启用关系的账号保存四类分组、成员和助手映射后，关联客服可直接手动接单；显示在线只用于在线展示及接收同事改派。

## 项目结构

| 项目                                 | 职责                                       |
| ------------------------------------ | ------------------------------------------ |
| `AI.Customer.Service.Common`         | 业务异常、参数规则、JSON                   |
| `AI.Customer.Service.Api`            | 严格 HTTP 请求契约                         |
| `AI.Customer.Service.Domain`         | 聚合、状态规则、Repository 和外部领域端口  |
| `AI.Customer.Service.Infrastructure` | EF Core 仓储、事务、用户中心和通知 Adapter |
| `AI.Customer.Service.Core`           | 会话回复权、轮次围栏、MAF Agent 和工具     |
| `AI.Customer.Service.Service`        | 客服应用编排与恢复 Worker                  |
| `AI.Customer.Service.Web`            | JWT、Controller、SSE、WebSocket 和静态页面 |
| `AI.Customer.Service.Tests`          | HTTP、数据库、并发、WebSocket 及协议回归   |
| `AI.Customer.Service.Schema`         | 不连接数据库的离线 DDL 生成工具            |

## 数据库

运行时仅使用 EF Core 类型化 ORM。提供 SQLite、MySQL 和 Kingbase（PostgreSQL 兼容模式）适配，选择 `Database:Provider`，连接串见配置文档。

```powershell
# 只生成脚本和字段字典，不连接或修改任何数据库
dotnet run --project tools/AI.Customer.Service.Schema -- data/sql
node scripts/format-sql.mjs
```

新库脚本：`data/sql/{sqlite,mysql,kingbase}/001_initial.sql`；字段和 owner 说明：[数据库字典](docs/schema.md)。生产部署在全新独立库执行对应脚本，再设置 `Database:Initialize=false`。已有库升级需要单独维护增量迁移，`EnsureCreated` 不升级旧表。

`data/sql/reference` 和 `docs/java-baseline` 是原项目只读参考快照，不能用于创建此 .NET 数据库，也不能将其中 Java 的历史验收结论当成本项目结果。业务历史数据未自动搬迁，需要另行设计 ETL 和切换窗口。

## 验证

```powershell
dotnet test AI.Customer.Service.sln --logger "trx;LogFileName=customer-service.trx" --results-directory artifacts/tests
dotnet format AI.Customer.Service.sln --no-restore --verify-no-changes
dotnet publish src/AI.Customer.Service.Web -c Release -o artifacts/publish

# 以下仅供开发/验证；服务运行不需要 npm
npm ci
npx playwright install chromium
npm run test:browser
# Windows 已安装 Edge 时也可用：$env:PLAYWRIGHT_CHANNEL='msedge'
npm run format:check
```

测试为独立 SQLite、签名 JWT、正式 Kestrel/HTTP/WebSocket；外部用户中心、OIDC SDK、模型和邮件网关在测试目录中替代，不向真实账号发信。测试脚本不会注册生产身份后门。验证范围及仍需部署联调的内容见 [验证记录](docs/verification.md)。

## 发布

主入口程序集为 `AI.Customer.Service.dll`，发布目录包含静态页面。发布后设置实际外部服务及独立连接串：

```powershell
cd artifacts/publish
dotnet AI.Customer.Service.dll --urls http://localhost:5180
```

也提供 .NET 10 多阶段 Dockerfile，容器监听 8080。SQLite 目录挂载 `/app/data/runtime`；密钥通过环境变量或 Secret 注入，不能打进镜像。HTTPS、反向代理路径和统一登录回调需与实际部署域名一致；WebSocket 需要代理支持 Upgrade。

保留目标仓库原有 MIT LICENSE。本次迁移未自动提交或推送。
