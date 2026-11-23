import type { ReactNode } from "react";
import { Building2, ChevronDown, MessageCircle, ShieldCheck, LogIn } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import { AgentSidebar } from "@/app/layouts/AgentSidebar";
import type { AuthState } from "@/app/types";
import type { Assistant } from "@/shared/types/customer-service";

type AppLayoutProps = {
    authState: AuthState;
    assistants: Assistant[];
    assistantId: string;
    loginPending: boolean;
    onAssistantChange: (assistantId: string) => void;
    onLogin: () => void;
    children: ReactNode;
};

export function AppLayout({
    authState,
    assistants,
    assistantId,
    loginPending,
    onAssistantChange,
    onLogin,
    children,
}: AppLayoutProps) {
    const { pathname } = useLocation();
    const isAgent = pathname.startsWith("/b/");

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
                        {authState === "signed-in" && assistants.length > 0 && (
                            <label className="assistant-picker">
                                <Building2 size={15} />
                                <select
                                    aria-label="切换助手"
                                    value={assistantId}
                                    onChange={(event) => onAssistantChange(event.target.value)}
                                >
                                    {assistants.map((assistant) => {
                                        const id = assistant.assistantConfigId || assistant.configId || assistant.id;
                                        return (
                                            <option key={id} value={id}>
                                                {assistant.name}
                                            </option>
                                        );
                                    })}
                                </select>
                                <ChevronDown size={14} />
                            </label>
                        )}
                        {authState === "signed-in" ? (
                            <span className="secure-session">
                                <ShieldCheck size={15} /> 已登录
                            </span>
                        ) : (
                            <button
                                className="button button--quiet"
                                type="button"
                                onClick={onLogin}
                                disabled={loginPending}
                            >
                                <LogIn size={15} /> {loginPending ? "正在跳转" : "统一登录"}
                            </button>
                        )}
                    </div>
                </header>
                {children}
            </div>
        </div>
    );
}
