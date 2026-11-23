type OidcAuthClient = {
    getToken(): Promise<string | null>;
    login(): Promise<void>;
};

declare global {
    interface Window {
        AUTH_SCRIPTS_READY?: Promise<void>;
        AUTH_SCRIPTS_LOADED?: boolean;
        OIDC_CONFIG?: { getConfig(): Record<string, string> };
        oidcByt?: { OidcBytAuth: new (config: Record<string, string>) => OidcAuthClient };
    }
}

let client: OidcAuthClient | null = null;
let initializing: Promise<OidcAuthClient> | null = null;

async function authClient(): Promise<OidcAuthClient> {
    if (client) return client;
    if (!initializing) {
        initializing = (async () => {
            if (!window.AUTH_SCRIPTS_READY) {
                await new Promise<void>((resolve, reject) => {
                    const script = document.createElement("script");
                    script.src = "/auth/js/auth-loader.js";
                    script.onload = () => resolve();
                    script.onerror = () => reject(new Error("登录组件加载失败，请确认服务地址和登录配置。"));
                    document.head.appendChild(script);
                });
            }
            await window.AUTH_SCRIPTS_READY;
            if (!window.oidcByt || !window.OIDC_CONFIG) throw new Error("统一登录尚未配置。");
            client = new window.oidcByt.OidcBytAuth(window.OIDC_CONFIG.getConfig());
            return client;
        })().catch((error) => {
            initializing = null;
            throw error;
        });
    }
    return initializing;
}

export async function getAccessToken(): Promise<string> {
    const token = await (await authClient()).getToken();
    if (!token) throw new Error("请先登录后继续。");
    return token.replace(/^Bearer\s+/i, "").trim();
}

export async function beginLogin(): Promise<void> {
    await (await authClient()).login();
}

export async function hasSession(): Promise<boolean> {
    try {
        return Boolean(await getAccessToken());
    } catch {
        return false;
    }
}
