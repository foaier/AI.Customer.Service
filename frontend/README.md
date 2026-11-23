# AI.Customer.Service 前端

独立 React 前端包含 C 端访客咨询和 B 端客服工作台，连接同级 .NET 10 API。Agent、模型流式对话和工具调用由后端 Microsoft Agent Framework 执行。

## 本地运行

需要 Node.js 20.19+ 或 22.12+。在此目录安装依赖并启动：

```powershell
npm install
npm run dev
```

默认地址为 `http://localhost:5173`。`.env.example` 中的 `VITE_BACKEND_URL` 指向本地 API，默认 `http://localhost:5180`。Vite 将 `/api` 和 `/auth` 转发到 API，并代理人工客服 WebSocket。

## 工程结构

```text
src/
├── app/                 # 应用根组件、路由、布局、认证状态和错误边界
├── features/            # 按业务域拆分的页面与组件
│   ├── agent-inbox/
│   ├── followups/
│   ├── settings/
│   └── visitor/
├── shared/              # API、认证、环境配置、共享类型和全局样式
└── test/                # 测试环境初始化
```

应用入口是 `src/main.tsx`，路由和页面按需加载。`@/` 指向 `src/`，Vite 与 TypeScript 使用同一别名。新增页面放入对应业务域，跨业务复用的能力放入 `shared/`。

## 页面入口

- C 端访客：`/c/chat`，支持 `assistantConfigId`、`conversationId` 查询参数。
- B 端工作台：`/b/inbox`。
- B 端邮件跟进：`/b/followups`。
- B 端分组和营业时间：`/b/settings`。

登录使用部署提供的 `/auth/js/auth-loader.js` 同源统一登录组件。生产环境需由同源网关转发 `/api`、`/auth` 和 `/api/customer-service/realtime/ws` 到 .NET API；身份、组织和客服权限仍由服务端校验。

## 构建

```powershell
npm run typecheck
npm run lint
npm run format:check
npm test
npm run build
npm run test:frontend
```

Vitest 和 Testing Library 用于快速组件及路由测试；`test:frontend` 运行真实浏览器回归。`npm run dev` 启动开发服务器，`npm run preview` 预览生产构建。
