import { useCallback, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { AppRoutes } from "@/app/AppRoutes";
import { AuthPanel } from "@/app/components/AuthPanel";
import { AppLayout } from "@/app/layouts/AppLayout";
import type { AuthState } from "@/app/types";
import { request } from "@/shared/api/client";
import { beginLogin, getAccessToken } from "@/shared/auth/client";
import { getAssistantId } from "@/shared/types/customer-service";
import type { Assistant } from "@/shared/types/customer-service";

export default function App() {
    const [auth, setAuth] = useState<AuthState>("loading");
    const [authError, setAuthError] = useState("");
    const [assistants, setAssistants] = useState<Assistant[]>([]);
    const [assistantId, setAssistantId] = useState("");
    const [loginPending, setLoginPending] = useState(false);

    const activeAssistant = useMemo(
        () => assistants.find((assistant) => getAssistantId(assistant) === assistantId) || assistants[0],
        [assistants, assistantId],
    );

    const loadAssistants = useCallback(async () => {
        await getAccessToken();
        const result = await request<Assistant[]>("/assistant/config/list");
        const items = Array.isArray(result) ? result : [];
        const queryId = new URLSearchParams(window.location.search).get("assistantConfigId");
        const storedId = sessionStorage.getItem("cs.assistant");
        const preferredId = queryId || storedId || (items[0] ? getAssistantId(items[0]) : "");

        setAssistants(items);
        setAssistantId(items.some((assistant) => getAssistantId(assistant) === preferredId) ? preferredId : "");
        setAuth("signed-in");
        setAuthError("");
    }, []);

    useEffect(() => {
        let mounted = true;

        loadAssistants().catch((error: unknown) => {
            if (!mounted) return;

            const message = error instanceof Error ? error.message : "登录状态读取失败";
            setAuth(/登录组件|OIDC|登录配置|统一登录/.test(message) ? "error" : "signed-out");
            setAuthError(message);
        });

        return () => {
            mounted = false;
        };
    }, [loadAssistants]);

    const handleAssistantChange = (value: string) => {
        setAssistantId(value);
        sessionStorage.setItem("cs.assistant", value);
    };

    const handleLogin = async () => {
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

    let page: ReactNode;
    if (auth === "loading") {
        page = (
            <main className="loading-screen" aria-live="polite">
                <span className="loader" />
                <span>正在连接统一登录</span>
            </main>
        );
    } else if (auth !== "signed-in") {
        page = <AuthPanel kind="sign-in" message={authError} onRetry={handleLogin} />;
    } else if (!activeAssistant) {
        page = <AuthPanel kind="no-assistant" onRetry={() => void loadAssistants()} />;
    } else {
        page = <AppRoutes assistant={activeAssistant} />;
    }

    return (
        <AppLayout
            authState={auth}
            assistants={assistants}
            assistantId={assistantId}
            loginPending={loginPending}
            onAssistantChange={handleAssistantChange}
            onLogin={() => void handleLogin()}
        >
            {page}
        </AppLayout>
    );
}
