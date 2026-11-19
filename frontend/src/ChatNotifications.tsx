import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { BellRing, RefreshCw, Save } from "lucide-react";
import { ApiError, put, request } from "./api";

type ChatConfiguration = {
    version: number;
    enabled: boolean;
    activeProvider: "WECOM_ROBOT";
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

export default function ChatNotifications({ topOrgId }: { topOrgId: string }) {
    const path = `/customer-service/configuration/chat-notifications?topOrgId=${encodeURIComponent(topOrgId)}`;
    const [saved, setSaved] = useState<ChatConfiguration | null>(null);
    const [draft, setDraft] = useState<Record<string, string>>({});
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
                setSaved(value);
                setDraft(readGroups(value));
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
    const changed = saved && groups.some(({ key }) => (draft[key] || "").trim() !== readGroups(saved)[key]);
    const save = async (event: FormEvent) => {
        event.preventDefault();
        if (!saved || !changed || saving) return;
        setSaving(true);
        setError("");
        setNotice("");
        try {
            const value = await put<ChatConfiguration>(path, {
                version: saved.version,
                chatGroup: Object.fromEntries(groups.map(({ key }) => [key, (draft[key] || "").trim()])),
            });
            setSaved(value);
            setDraft(readGroups(value));
            setNotice("企业微信群配置已保存。");
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
                    <span>按接待类别将等待提醒发送到对应群，可共用同一个群</span>
                </div>
            </div>
            <p className="settings-footnote">填写群机器人 Webhook 中 key= 后的 Hook ID，无需填写完整地址。</p>
            {saved && !saved.enabled && (
                <p className="settings-footnote">通知发送当前关闭。群配置可以保存，管理员开启通知服务后生效。</p>
            )}
            {groups.map(({ key, label }) => (
                <label className="chat-notification-field" key={key}>
                    <span>{label}</span>
                    <input
                        value={draft[key] || ""}
                        onChange={(event) => setDraft((value) => ({ ...value, [key]: event.target.value }))}
                        disabled={loading || saving || !saved}
                        required
                        pattern="[A-Za-z0-9_\-]{10,200}"
                        maxLength={200}
                        autoComplete="off"
                        placeholder="群机器人 Hook ID"
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
