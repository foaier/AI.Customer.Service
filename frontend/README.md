# AI.Customer.Service 前端

独立 React 前端包含 C 端访客咨询和 B 端客服工作台，连接同级 .NET 10 API。Agent、模型流式对话和工具调用由后端 Microsoft Agent Framework 执行；浏览器不承担模型编排，也不依赖或修改原 Java、Vue 前端。

## 本地运行

需要 Node.js 20.19+ 或 22.12+。在此目录安装依赖并启动：

```powershell
npm install
npm run dev
```

默认地址为 `http://localhost:5173`。`.env.example` 中的 `VITE_BACKEND_URL` 指向本地 API，默认 `http://localhost:5180`。Vite 将 `/api` 和 `/auth` 转发到 API，并代理人工客服 WebSocket。

## 页面入口

- C 端访客：`/c/chat`，支持 `assistantConfigId`、`conversationId` 查询参数。
- B 端工作台：`/b/inbox`。
- B 端邮件跟进：`/b/followups`。
- B 端分组和营业时间：`/b/settings`。

登录使用部署提供的 `/auth/js/auth-loader.js` 同源统一登录组件。生产环境需由同源网关转发 `/api`、`/auth` 和 `/api/customer-service/realtime/ws` 到 .NET API；身份、组织和客服权限仍由服务端校验。

## 构建

```powershell
npm run build
```
