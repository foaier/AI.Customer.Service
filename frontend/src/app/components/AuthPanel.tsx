import { ArrowRight, Check, CircleHelp, LogIn, ShieldCheck } from "lucide-react";

type AuthPanelProps = {
    kind: "sign-in" | "no-assistant";
    message?: string;
    onRetry: () => void;
};

export function AuthPanel({ kind, message, onRetry }: AuthPanelProps) {
    const needsSignIn = kind === "sign-in";

    return (
        <main className="auth-screen">
            <div className="auth-panel">
                <div className={needsSignIn ? "auth-icon" : "auth-icon auth-icon--amber"}>
                    {needsSignIn ? <ShieldCheck size={23} /> : <CircleHelp size={23} />}
                </div>
                <p className="eyebrow">{needsSignIn ? "SECURE WORKSPACE" : "ASSISTANT REGISTRY"}</p>
                <h1>{needsSignIn ? "登录后继续" : "暂时没有可用助手"}</h1>
                <p>
                    {needsSignIn
                        ? "使用组织统一账号登录。身份和组织权限由服务端校验。"
                        : "请先在服务端助手注册表中为当前租户配置客服助手。"}
                </p>
                {message && (
                    <p className="inline-error" role="alert">
                        {message}
                    </p>
                )}
                <button
                    className={needsSignIn ? "button button--primary" : "button button--quiet"}
                    type="button"
                    onClick={onRetry}
                >
                    {needsSignIn ? <LogIn size={16} /> : <Check size={16} />}
                    {needsSignIn ? "使用统一账号登录" : "重新检查"}
                    {needsSignIn && <ArrowRight size={15} />}
                </button>
            </div>
        </main>
    );
}
