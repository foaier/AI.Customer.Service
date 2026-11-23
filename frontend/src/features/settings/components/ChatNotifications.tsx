import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { BellRing, RefreshCw, Save } from "lucide-react";
import { ApiError, put, request } from "@/shared/api/client";

type ChatConfiguration = {
    version: number;
    enabled: boolean;
    activeProvider: "WECOM_ROBOT" | "ADSALE";
    publicBaseUrl: string;
    apiTokenConfigured: boolean;
    chatGroup: Record<string, string>;
};
const groups = [
    { key: "SALES", legacy: "EXHIBITOR", label: "展商客服" },
    { key: "VISITOR", legacy: "VISITOR", label: "观众客服" },
    { key: "FINANCE", legacy: "MEDIA", label: "财务客服" },
    { key: "EMP", legacy: "OTHER", label: "其他客服" },
];
function readGroups(value: ChatConfiguration) {
    return Object.fromEntries(
        groups.map(({ key, legacy }) => [key, value.chatGroup[key] || value.chatGroup[legacy] || ""]),
    );
}

export default function ChatNotifications() {
    const path = `/customer-service/configuration/chat-notifications`;
    const [saved, setSaved] = useState<ChatConfiguration | null>(null);
    const [draft, setDraft] = useState<Record<string, string>>({});
    const [enabled, setEnabled] = useState(false);
    const [provider, setProvider] = useState<ChatConfiguration["activeProvider"]>("WECOM_ROBOT");
    const [publicBaseUrl, setPublicBaseUrl] = useState("");
    const [apiToken, setApiToken] = useState("");
    const [clearApiToken, setClearApiToken] = useState(false);
    function accept(value: ChatConfiguration) {
        setSaved(value);
        setDraft(readGroups(value));
        setEnabled(value.enabled);
        setProvider(value.activeProvider);
        setPublicBaseUrl(value.publicBaseUrl || "");
        setApiToken("");
        setClearApiToken(false);
    }
    const [revision, setRevision] = useState(0);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState("");
    const [notice, setNotice] = useState("");
    useEffect(() => {
        const controller = new AbortController();
        setLoading(true);
        setSaved(null);
        setNotice("");
        request<ChatConfiguration>(path, { signal: controller.signal })
            .then((value) => {
                if (controller.signal.aborted) return;
                accept(value);
                setError("");
            })
            .catch((reason) => {
                if (controller.signal.aborted) return;
                setError(
                    reason instanceof ApiError && reason.status === 404
                        ? "群通知配置接口尚未启用，暂时不能保存。"
                        : reason instanceof Error
                          ? reason.message
                          : "群通知配置读取失败",
                );
            })
            .finally(() => {
                if (!controller.signal.aborted) setLoading(false);
            });
        return () => controller.abort();
    }, [path, revision]);
    const changed =
        saved &&
        (groups.some(({ key }) => (draft[key] || "").trim() !== readGroups(saved)[key]) ||
            enabled !== saved.enabled ||
            provider !== saved.activeProvider ||
            publicBaseUrl.trim() !== (saved.publicBaseUrl || "") ||
            apiToken.length > 0 ||
            clearApiToken);
    const save = async (event: FormEvent) => {
        event.preventDefault();
        if (!saved || !changed || saving) return;
        setSaving(true);
        setError("");
        setNotice("");
        try {
            const value = await put<ChatConfiguration>(path, {
                version: saved.version,
                enabled,
                activeProvider: provider,
                publicBaseUrl: publicBaseUrl.trim(),
                ...(apiToken ? { apiToken } : {}),
                clearApiToken,
                chatGroup: Object.fromEntries(groups.map(({ key }) => [key, (draft[key] || "").trim()])),
            });
            accept(value);
            setNotice("群通知配置已保存。");
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "群通知保存失败");
        } finally {
            setSaving(false);
        }
    };
    return (
        <form className="settings-section chat-notification-section" onSubmit={save}>
            <div className="settings-section-heading">
                <span className="settings-heading-icon settings-heading-icon--teal">
                    <BellRing size={18} />
                </span>
                <div>
                    <h2>企业微信群通知</h2>
                    <span>新转人工申请即时通知对应群，可共用同一个群</span>
                </div>
            </div>
            <fieldset disabled={loading || saving || !saved} className="chat-notification-options">
                <label className="chat-notification-field">
                    <span>启用通知</span>
                    <input type="checkbox" checked={enabled} onChange={(event) => setEnabled(event.target.checked)} />
                </label>
                <p className="settings-footnote">统一控制群通知和邮件通知。关闭后待发送任务暂停，重新启用后继续。</p>
                <label className="chat-notification-field">
                    <span>通知通道</span>
                    <select
                        aria-label="通知通道"
                        value={provider}
                        onChange={(event) => setProvider(event.target.value as ChatConfiguration["activeProvider"])}
                    >
                        <option value="WECOM_ROBOT">企业微信机器人</option>
                        <option value="ADSALE">雅式企微</option>
                    </select>
                </label>
                <label className="chat-notification-field">
                    <span>客服站点地址</span>
                    <input
                        type="url"
                        value={publicBaseUrl}
                        onChange={(event) => setPublicBaseUrl(event.target.value)}
                        required={enabled}
                        maxLength={256}
                        placeholder="https://support.example.com"
                    />
                </label>
                {provider === "ADSALE" && (
                    <>
                        <label className="chat-notification-field">
                            <span>API Token{saved?.apiTokenConfigured ? "（已配置）" : "（未配置）"}</span>
                            <input
                                type="password"
                                value={apiToken}
                                onChange={(event) => {
                                    setApiToken(event.target.value);
                                    setClearApiToken(false);
                                }}
                                maxLength={2048}
                                autoComplete="new-password"
                                placeholder="留空保留原令牌"
                            />
                        </label>
                        <label className="chat-notification-field">
                            <span>清除已保存令牌</span>
                            <input
                                type="checkbox"
                                checked={clearApiToken}
                                onChange={(event) => {
                                    setClearApiToken(event.target.checked);
                                    setApiToken("");
                                }}
                            />
                        </label>
                    </>
                )}
            </fieldset>
            <p className="settings-footnote">
                {provider === "WECOM_ROBOT"
                    ? "填写群机器人 Webhook 中 key= 后的 Hook ID，无需填写完整地址。"
                    : "填写雅式群 ID。切换通道后，旧通道任务需管理员处理。"}
            </p>
            {groups.map(({ key, label }) => (
                <label className="chat-notification-field" key={key}>
                    <span>{label}</span>
                    <input
                        value={draft[key] || ""}
                        onChange={(event) => setDraft((value) => ({ ...value, [key]: event.target.value }))}
                        disabled={loading || saving || !saved}
                        required={enabled}
                        pattern={provider === "WECOM_ROBOT" ? "[A-Za-z0-9_\\-]{10,200}" : undefined}
                        maxLength={provider === "WECOM_ROBOT" ? 200 : 128}
                        autoComplete="off"
                        placeholder={provider === "WECOM_ROBOT" ? "群机器人 Hook ID" : "雅式群 ID"}
                    />
                </label>
            ))}
            {error && (
                <p className="inline-error" role="alert">
                    {error}
                </p>
            )}
            {notice && (
                <p className="page-success" role="status">
                    {notice}
                </p>
            )}
            <div className="settings-form-footer">
                <button
                    type="button"
                    className="button button--quiet"
                    disabled={loading || saving}
                    onClick={() => {
                        setLoading(true);
                        setRevision((value) => value + 1);
                    }}
                >
                    <RefreshCw size={15} />
                    重新读取
                </button>
                <button type="submit" className="button button--primary" disabled={loading || saving || !changed}>
                    <Save size={15} />
                    保存群通知
                </button>
            </div>
        </form>
    );
}
