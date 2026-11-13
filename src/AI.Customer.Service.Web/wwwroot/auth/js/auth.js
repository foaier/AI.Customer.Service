/** 复用原 OIDC SDK 和登录 JWT，页面不签发客服身份、不使用测试身份头。 */
class AuthManager {
    /** 初始化只创建客户端，未登录时保留明确登录按钮。 */
    async init() {
        await window.AUTH_SCRIPTS_READY;
        if (!this.oidcAuth) this.oidcAuth = new oidcByt.OidcBytAuth(OIDC_CONFIG.getConfig());
        this.isInitialized = true;
    }

    /** 令牌只交给 Authorization 或实时认证首帧，不输出到日志。 */
    async getToken() {
        await this.init();
        return this.oidcAuth.getToken();
    }

    /** 规范化已有 access token，空值不能当作登录成功。 */
    getAuthorizationHeader(token) {
        const value = typeof token === "string" ? token.trim().replace(/^Bearer\s+/i, "") : "";
        return value ? "Bearer " + value : null;
    }

    /** 发起原统一登录，不维护第二套账号。 */
    async login() {
        await this.init();
        await this.oidcAuth.login();
    }
}
const authManager = new AuthManager();
