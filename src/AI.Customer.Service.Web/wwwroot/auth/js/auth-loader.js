/** 从独立服务读取公开 OIDC 配置；不复制 Java 环境地址及密钥。 */
(function () {
    "use strict";
    window.AUTH_SCRIPTS_READY = (async function () {
        const response = await fetch("/api/auth/config", { cache: "no-store" });
        if (!response.ok) throw new Error("无法读取登录配置");
        const config = await response.json();
        const script = document.createElement("script");
        script.src = "/auth/js/oidc-byt.min.js";
        await new Promise((resolve, reject) => {
            script.onload = resolve;
            script.onerror = reject;
            document.head.append(script);
        });
        window.OIDC_CONFIG = {
            getConfig() {
                if (!config.authority || !config.clientId) throw new Error("尚未配置统一登录");
                return {
                    client_id: config.clientId,
                    authority: config.authority,
                    scope: config.scope,
                    redirect_uri: location.origin + "/auth/callback.html",
                    silent_redirect_uri: location.origin + "/auth/silent.html",
                };
            },
        };
        window.AUTH_SCRIPTS_LOADED = true;
        document.dispatchEvent(new CustomEvent("auth-scripts-loaded"));
    })().catch(() => {
        document.dispatchEvent(new CustomEvent("auth-scripts-loaded", { detail: { error: true } }));
    });
})();
