import { useCallback, useEffect, useMemo, useState } from "react";
import { Link, Navigate, Route, Routes, useLocation } from "react-router-dom";
import {
    ArrowRight,
    Bot,
    Building2,
    Check,
    ChevronDown,
    CircleHelp,
    Headset,
    LogIn,
    MessageCircle,
    Settings2,
    ShieldCheck,
    UsersRound,
} from "lucide-react";
import { beginLogin, getAccessToken } from "./auth";
import { request } from "./api";
import Visitor from "./Visitor";
import AgentInbox from "./AgentInbox";
import Settings from "./Settings";
import Followups from "./Followups";
import type { Assistant } from "./types";

type AuthState = "loading" | "signed-in" | "signed-out" | "error";

export default function App() {
    const location = useLocation();
    const [auth, setAuth] = useState<AuthState>("loading");
    const [authError, setAuthError] = useState("");
    const [assistants, setAssistants] = useState<Assistant[]>([]);
    const [assistantId, setAssistantId] = useState("");
    const [loginPending, setLoginPending] = useState(false);
    const isAgent = location.pathname.startsWith("/b/");
    const activeAssistant = useMemo(
        () =>
            assistants.find((item) => (item.assistantConfigId || item.configId || item.id) === assistantId) ||
            assistants[0],
        [assistants, assistantId],
    );

    const loadAssistants = useCallback(async () => {
        await getAccessToken();
        const result = await request<Assistant[]>("/assistant/config/list");
        const items = Array.isArray(result) ? result : [];
        setAssistants(items);
        const queryId = new URLSearchParams(window.location.search).get("assistantConfigId");
        const storedId = sessionStorage.getItem("cs.assistant");
        const preferred =
            queryId || storedId || (items[0] && (items[0].assistantConfigId || items[0].configId || items[0].id)) || "";
        setAssistantId(
            items.some((item) => (item.assistantConfigId || item.configId || item.id) === preferred) ? preferred : "",
        );
        setAuth("signed-in");
        setAuthError("");
    }, []);

    useEffect(() => {
        let alive = true;
        loadAssistants().catch((error: unknown) => {
            if (!alive) return;
            setAuth(
                error instanceof Error && /登录组件|OIDC|登录配置|统一登录/.test(error.message)
                    ? "error"
                    : "signed-out",
            );
            setAuthError(error instanceof Error ? error.message : "登录状态读取失败");
        });
        return () => {
            alive = false;
        };
    }, [loadAssistants]);

    const selectAssistant = (value: string) => {
        setAssistantId(value);
        sessionStorage.setItem("cs.assistant", value);
    };

    const login = async () => {
        setLoginPending(true);
        setAuthError("");
        try {
            await beginLogin();
            await loadAssistants();
        } catch (error) {
            setAuthError(error instanceof Error ? error.message : "登录失败");
            setLoginPending(false);
        }
    };

    return (
        <div className={isAgent ? "app-shell app-shell--agent" : "app-shell app-shell--visitor"}>
            {isAgent && <AgentSidebar />}
            <div className="app-main">
                <header className="topbar">
                    <Link to="/c/chat" className="brand-lockup" aria-label="AI 客服首页">
                        <span className="brand-mark">
                            <MessageCircle size={17} strokeWidth={2.4} />
                        </span>
                        <span className="brand-name">
                            Service<span>desk</span>
                        </span>
                    </Link>
                    <div className="topbar-context">
                        <span className="topbar-overline">
                            {isAgent ? "AI.CUSTOMER.SERVICE / WORKSPACE" : "AI.CUSTOMER.SERVICE"}
                        </span>
                        <span className="topbar-title">{isAgent ? "客服运营" : "在线咨询"}</span>
                    </div>
                    <div className="topbar-actions">
                        {auth === "signed-in" && assistants.length > 0 && (
                            <label className="assistant-picker">
                                <Building2 size={15} />
                                <select
                                    aria-label="切换助手"
                                    value={assistantId}
                                    onChange={(event) => selectAssistant(event.target.value)}
                                >
                                    {assistants.map((item) => {
                                        const id = item.assistantConfigId || item.configId || item.id;
                                        return (
                                            <option key={id} value={id}>
                                                {item.name}
                                            </option>
                                        );
                                    })}
                                </select>
                                <ChevronDown size={14} />
                            </label>
                        )}
                        {auth === "signed-in" ? (
                            <span className="secure-session">
                                <ShieldCheck size={15} /> 已登录
                            </span>
                        ) : (
                            <button
                                className="button button--quiet"
                                type="button"
                                onClick={login}
                                disabled={loginPending}
                            >
                                <LogIn size={15} /> {loginPending ? "正在跳转" : "统一登录"}
                            </button>
                        )}
                    </div>
                </header>

                {auth === "loading" ? (
                    <main className="loading-screen">
                        <span className="loader" />
                        <span>正在连接统一登录</span>
                    </main>
                ) : auth !== "signed-in" ? (
                    <main className="auth-screen">
                        <div className="auth-panel">
                            <div className="auth-icon">
                                <ShieldCheck size={23} />
                            </div>
                            <p className="eyebrow">SECURE WORKSPACE</p>
                            <h1>登录后继续</h1>
                            <p>使用组织统一账号登录。身份和组织权限由服务端校验。</p>
                            {authError && (
                                <p className="inline-error" role="alert">
                                    {authError}
                                </p>
                            )}
                            <button
                                className="button button--primary"
                                type="button"
                                onClick={login}
                                disabled={loginPending}
                            >
                                <LogIn size={16} /> 使用统一账号登录 <ArrowRight size={15} />
                            </button>
                        </div>
                    </main>
                ) : !activeAssistant ? (
                    <main className="auth-screen">
                        <div className="auth-panel">
                            <div className="auth-icon auth-icon--amber">
                                <CircleHelp size={23} />
                            </div>
                            <p className="eyebrow">ASSISTANT REGISTRY</p>
                            <h1>暂时没有可用助手</h1>
                            <p>请先在服务端助手注册表中为当前租户配置客服助手。</p>
                            <button
                                className="button button--quiet"
                                type="button"
                                onClick={() => void loadAssistants()}
                            >
                                <Check size={16} /> 重新检查
                            </button>
                        </div>
                    </main>
                ) : (
                    <Routes>
                        <Route path="/" element={<Navigate to="/c/chat" replace />} />
                        <Route path="/c/chat" element={<Visitor assistant={activeAssistant} />} />
                        <Route
                            path="/b/inbox"
                            element={
                                <AgentInbox
                                    key={activeAssistant.assistantConfigId || activeAssistant.configId || activeAssistant.id}
                                    assistant={activeAssistant}
                                />
                            }
                        />
                        <Route
                            path="/b/followups"
                            element={
                                <Followups
                                    key={activeAssistant.assistantConfigId || activeAssistant.configId || activeAssistant.id}
                                    assistant={activeAssistant}
                                />
                            }
                        />
                        <Route
                            path="/b/settings"
                            element={
                                <Settings
                                    key={activeAssistant.assistantConfigId || activeAssistant.configId || activeAssistant.id}
                                    assistant={activeAssistant}
                                />
                            }
                        />
                        <Route path="*" element={<Navigate to={isAgent ? "/b/inbox" : "/c/chat"} replace />} />
                    </Routes>
                )}
            </div>
        </div>
    );
}

function AgentSidebar() {
    return (
        <aside className="agent-sidebar">
            <div className="sidebar-heading">
                <span className="sidebar-orbit">
                    <Headset size={16} />
                </span>
                <span>客服工作台</span>
            </div>
            <div className="sidebar-section-label">接待中心</div>
            <nav className="sidebar-nav" aria-label="客服工作台导航">
                <Link to="/b/inbox" className={window.location.pathname === "/b/inbox" ? "active" : ""}>
                    <UsersRound size={17} />
                    <span>在线接待</span>
                    <span className="nav-live-dot" />
                </Link>
                <Link to="/b/followups" className={window.location.pathname === "/b/followups" ? "active" : ""}>
                    <MessageCircle size={17} />
                    <span>邮件跟进</span>
                </Link>
            </nav>
            <div className="sidebar-section-label sidebar-section-label--spaced">管理</div>
            <nav className="sidebar-nav">
                <Link to="/b/settings" className={window.location.pathname === "/b/settings" ? "active" : ""}>
                    <Settings2 size={17} />
                    <span>分组与营业时间</span>
                </Link>
            </nav>
            <div className="sidebar-bottom">
                <div className="sidebar-status">
                    <span className="status-pip" />
                    <span>客服服务已连接</span>
                </div>
                <Link to="/c/chat" className="switch-role">
                    <Bot size={16} />
                    <span>访客咨询端</span>
                    <ArrowRight size={14} />
                </Link>
            </div>
        </aside>
    );
}
