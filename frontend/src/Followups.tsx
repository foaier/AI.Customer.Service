import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import {
    ArrowRight,
    Check,
    Clock3,
    FileText,
    Mail,
    RefreshCw,
    RotateCcw,
    Send,
    ShieldCheck,
    UserRound,
} from "lucide-react";
import { post, realtimeUrl, request } from "./api";
import { getAccessToken } from "./auth";
import type { Assistant, Followup } from "./types";

type FollowupFormState = {
    version?: number;
    state?: string;
    contactName?: string;
    content?: string;
    assignedTo?: string;
};
type ReplyState = { version: number; state: string; subject?: string; content?: string; attempts?: number };

export default function Followups({ assistant }: { assistant: Assistant }) {
    const assistantId = assistant.assistantConfigId || assistant.configId || assistant.id;
    const query = `topOrgId=${encodeURIComponent(assistant.topOrgId)}&assistantConfigId=${encodeURIComponent(assistantId)}`;
    const [items, setItems] = useState<Followup[]>([]);
    const [selected, setSelected] = useState<Followup | null>(null);
    const [form, setForm] = useState<FollowupFormState | undefined>(undefined);
    const [reply, setReply] = useState<ReplyState | null>(null);
    const [subject, setSubject] = useState("");
    const [content, setContent] = useState("");
    const [loading, setLoading] = useState(true);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState("");
    const [notice, setNotice] = useState("");
    const seenReceipts = useRef(new Set<string>());
    const selectionGeneration = useRef(0);
    const [pageVisible, setPageVisible] = useState(() => document.visibilityState === "visible");
    const replySubmitted = Boolean(reply && reply.state !== "OPEN");

    const loadItems = useCallback(async () => {
        setLoading(true);
        try {
            const result = await request<Followup[]>(`/customer-service/agents/followups?${query}`);
            setItems(Array.isArray(result) ? result : []);
            setError("");
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "邮件工单读取失败");
        } finally {
            setLoading(false);
        }
    }, [query]);

    useEffect(() => {
        void loadItems();
        return () => {
            selectionGeneration.current++;
        };
    }, [loadItems]);

    useEffect(() => {
        let closed = false;
        let reconnect = 0;
        let refresh = 0;
        let failures = 0;
        let socket: WebSocket | null = null;
        const connect = async () => {
            try {
                const token = await getAccessToken();
                if (closed) return;
                let changeToken = "";
                socket = new WebSocket(
                    realtimeUrl({
                        authorization: `Bearer ${token}`,
                        agent: true,
                        topOrgId: assistant.topOrgId,
                        assistantConfigId: assistantId,
                        afterSeq: 0,
                    }),
                );
                socket.onopen = () => {
                    failures = 0;
                    void loadItems();
                };
                socket.onmessage = (event) => {
                    if (closed) return;
                    const frame = JSON.parse(String(event.data));
                    if (frame.type !== "inbox.notifications" || !frame.data?.changeToken) return;
                    const changed = changeToken && changeToken !== frame.data.changeToken;
                    changeToken = frame.data.changeToken;
                    if (changed && !refresh)
                        refresh = window.setTimeout(() => {
                            refresh = 0;
                            void loadItems();
                        }, 300);
                };
                socket.onclose = () => {
                    if (!closed) reconnect = window.setTimeout(connect, Math.min(1000 * 2 ** failures++, 15000));
                };
            } catch {
                if (!closed) reconnect = window.setTimeout(connect, Math.min(1000 * 2 ** failures++, 15000));
            }
        };
        void connect();
        return () => {
            closed = true;
            window.clearTimeout(reconnect);
            window.clearTimeout(refresh);
            socket?.close();
        };
    }, [assistant.topOrgId, assistantId, loadItems]);

    useEffect(() => {
        const updateVisibility = () => setPageVisible(document.visibilityState === "visible");
        document.addEventListener("visibilitychange", updateVisibility);
        return () => document.removeEventListener("visibilitychange", updateVisibility);
    }, []);

    useEffect(() => {
        if (!pageVisible || !selected?.hasUnseen || !form?.content) return;
        const key = `${selected.scope.conversationId}:${selected.episodeId}:${selected.ticketId}`;
        if (seenReceipts.current.has(key)) return;
        seenReceipts.current.add(key);
        post(
            `/customer-service/agents/conversations/${encodeURIComponent(selected.scope.conversationId)}/followups/${encodeURIComponent(selected.episodeId)}/receipts?topOrgId=${encodeURIComponent(assistant.topOrgId)}`,
            { kind: "SEEN", ticketId: selected.ticketId },
        )
            .then(() => {
                setItems((current) =>
                    current.map((item) =>
                        item.episodeId === selected.episodeId ? { ...item, hasUnseen: false } : item,
                    ),
                );
                setSelected((current) =>
                    current?.episodeId === selected.episodeId ? { ...current, hasUnseen: false } : current,
                );
            })
            .catch(() => {
                seenReceipts.current.delete(key);
            });
    }, [assistant.topOrgId, form?.content, pageVisible, selected]);

    const select = async (item: Followup) => {
        const generation = ++selectionGeneration.current;
        setSelected(item);
        setForm(undefined);
        setReply(null);
        setSubject("");
        setContent("");
        setNotice("");
        try {
            const path = `/customer-service/agents/conversations/${encodeURIComponent(item.scope.conversationId)}/followups/${encodeURIComponent(item.episodeId)}`;
            const [formResult, replyResult] = await Promise.all([
                request<FollowupFormState>(`${path}/form?topOrgId=${encodeURIComponent(assistant.topOrgId)}`),
                request<ReplyState>(`${path}?topOrgId=${encodeURIComponent(assistant.topOrgId)}`).catch(() => null),
            ]);
            if (generation !== selectionGeneration.current) return;
            setForm(formResult);
            setReply(replyResult);
            if (replyResult?.subject) setSubject(replyResult.subject);
            if (replyResult?.content) setContent(replyResult.content);
        } catch (reason) {
            if (generation !== selectionGeneration.current) return;
            setError(reason instanceof Error ? reason.message : "工单详情读取失败");
        }
    };

    const claim = async () => {
        if (!selected || !form) return;
        setBusy(true);
        try {
            const claimed = await post<{ state: string; version: number; assignedTo: string }>(
                `/customer-service/agents/conversations/${encodeURIComponent(selected.scope.conversationId)}/followups/${encodeURIComponent(selected.episodeId)}/claim?topOrgId=${encodeURIComponent(assistant.topOrgId)}`,
                {
                    requestId: `form_claim_${crypto.randomUUID().replaceAll("-", "")}`,
                    expectedVersion: form.version || selected.version,
                },
            );
            setNotice("留言已认领，仅当前客服可以读取访客联系方式和正文。");
            await select({
                ...selected,
                state: claimed.state,
                version: claimed.version,
                assignedTo: claimed.assignedTo,
            });
            await loadItems();
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "留言认领失败");
        } finally {
            setBusy(false);
        }
    };

    const submitReply = async (event: FormEvent) => {
        event.preventDefault();
        if (!selected) return;
        setBusy(true);
        try {
            const result = await post<ReplyState>(
                `/customer-service/agents/conversations/${encodeURIComponent(selected.scope.conversationId)}/followups/${encodeURIComponent(selected.episodeId)}/reply?topOrgId=${encodeURIComponent(assistant.topOrgId)}`,
                {
                    requestId: `reply_${crypto.randomUUID().replaceAll("-", "")}`,
                    expectedVersion: reply?.version || 1,
                    subject,
                    content,
                },
            );
            setReply(result);
            setNotice("邮件回复已登记，将通过可靠投递任务发送至访客确认的邮箱。");
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "回复提交失败");
        } finally {
            setBusy(false);
        }
    };

    const close = async () => {
        if (!selected || !reply || !window.confirm("确认在网关受理后结案？")) return;
        setBusy(true);
        try {
            const result = await post<ReplyState>(
                `/customer-service/agents/conversations/${encodeURIComponent(selected.scope.conversationId)}/followups/${encodeURIComponent(selected.episodeId)}/close?topOrgId=${encodeURIComponent(assistant.topOrgId)}`,
                {
                    requestId: `close_${crypto.randomUUID().replaceAll("-", "")}`,
                    expectedVersion: reply.version,
                },
            );
            setReply(result);
            setNotice("工单已结案。");
            await loadItems();
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "结案失败；请先确认投递已受理");
        } finally {
            setBusy(false);
        }
    };

    const retry = async () => {
        if (!selected || !reply || reply.state !== "DEAD") return;
        setBusy(true);
        try {
            const result = await post<ReplyState>(
                `/customer-service/agents/conversations/${encodeURIComponent(selected.scope.conversationId)}/followups/${encodeURIComponent(selected.episodeId)}/retry?topOrgId=${encodeURIComponent(assistant.topOrgId)}`,
                {
                    requestId: `retry_${crypto.randomUUID().replaceAll("-", "")}`,
                    expectedVersion: reply.version,
                },
            );
            setReply(result);
            setNotice("投递任务已重新排队，状态更新后可刷新工单查看。");
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "邮件重试失败");
        } finally {
            setBusy(false);
        }
    };

    return (
        <main className="agent-page followups-page">
            <div className="agent-page-header">
                <div>
                    <p className="eyebrow">SERVICE OPERATIONS / FOLLOW-UP</p>
                    <h1>邮件跟进</h1>
                    <p className="page-subtitle">访客主动留言后，认领并通过其确认的邮箱回复。</p>
                </div>
                <button
                    className="button button--quiet"
                    type="button"
                    onClick={() => void loadItems()}
                    disabled={loading}
                >
                    <RefreshCw size={15} className={loading ? "spin" : ""} />
                    刷新
                </button>
            </div>
            {error && (
                <div className="page-alert" role="alert">
                    {error}
                    <button className="icon-button" type="button" onClick={() => setError("")}>
                        ×
                    </button>
                </div>
            )}
            {notice && (
                <div className="page-success">
                    <Check size={15} />
                    {notice}
                </div>
            )}
            <div className="followup-workspace">
                <section className="followup-list-panel">
                    <div className="queue-panel-head">
                        <div>
                            <h2>待跟进工单</h2>
                            <span>访客留言与邮件回复</span>
                        </div>
                        <span className="queue-count">{items.length}</span>
                    </div>
                    <div className="followup-list">
                        {[...items]
                            .sort((a, b) => Number(Boolean(b.hasUnseen)) - Number(Boolean(a.hasUnseen)))
                            .map((item) => (
                                <button
                                    type="button"
                                    key={`${item.scope.conversationId}-${item.episodeId}`}
                                    className={`followup-row ${selected?.episodeId === item.episodeId ? "active" : ""}`}
                                    onClick={() => void select(item)}
                                >
                                    <span className="followup-row-icon">
                                        <Mail size={16} />
                                    </span>
                                    <span className="followup-row-copy">
                                        <strong>{item.ticketId}</strong>
                                        <span>{item.scope.conversationId.slice(-10)}</span>
                                    </span>
                                    <span className={`status-label status-label--${item.state.toLowerCase()}`}>
                                        {followupState(item.state)}
                                    </span>
                                    {item.hasUnseen && (
                                        <span className="followup-unseen" aria-label="有未查看留言">
                                            新
                                        </span>
                                    )}
                                </button>
                            ))}
                        {!loading && items.length === 0 && (
                            <div className="queue-empty">
                                <span className="queue-empty-icon">
                                    <Check size={21} />
                                </span>
                                <strong>暂无待跟进工单</strong>
                                <span>访客提交留言后会显示在这里</span>
                            </div>
                        )}
                    </div>
                </section>
                <section className="followup-detail-panel">
                    {!selected ? (
                        <div className="conversation-empty">
                            <div className="empty-illustration">
                                <FileText size={25} />
                            </div>
                            <h2>选择一张工单</h2>
                            <p>认领前只展示工单摘要，联系方式和正文会在认领后开放。</p>
                        </div>
                    ) : (
                        <>
                            <div className="conversation-header">
                                <div className="visitor-identity">
                                    <span className="queue-avatar queue-avatar--large">
                                        <Mail size={17} />
                                    </span>
                                    <div>
                                        <strong>{selected.ticketId}</strong>
                                        <span>会话 · {selected.scope.conversationId}</span>
                                    </div>
                                </div>
                                <span className={`status-label status-label--${selected.state.toLowerCase()}`}>
                                    {followupState(selected.state)}
                                </span>
                            </div>
                            <div className="followup-detail-body">
                                {selected.state === "OPEN" ? (
                                    <div className="claim-gate">
                                        <span className="claim-gate-icon">
                                            <ShieldCheck size={19} />
                                        </span>
                                        <div>
                                            <strong>认领后查看留言</strong>
                                            <span>尚未认领时不会加载访客邮箱及留言正文。</span>
                                        </div>
                                        <button
                                            className="button button--primary"
                                            type="button"
                                            onClick={() => void claim()}
                                            disabled={busy}
                                        >
                                            认领留言
                                        </button>
                                    </div>
                                ) : (
                                    <>
                                        <div className="followup-visitor-card">
                                            <div className="followup-visitor-heading">
                                                <UserRound size={15} />
                                                <strong>访客留言</strong>
                                                <time>
                                                    <Clock3 size={13} />
                                                    {formatTime(selected.submittedAt)}
                                                </time>
                                            </div>
                                            <div className="followup-meta">
                                                <span>称呼</span>
                                                <strong>{form?.contactName || "访客"}</strong>
                                                <span>联系邮箱</span>
                                                <strong className="email-value">由服务端保管</strong>
                                            </div>
                                            <p className="followup-visitor-content">
                                                {form?.content || "当前账号尚未认领此工单，正文不可见。"}
                                            </p>
                                        </div>
                                        {selected.assignedTo && (
                                            <div className="assigned-note">
                                                <ShieldCheck size={14} />
                                                已由客服 {selected.assignedTo} 认领
                                            </div>
                                        )}
                                        {selected.assignedTo && (
                                            <form className="reply-editor" onSubmit={submitReply}>
                                                <div className="reply-editor-heading">
                                                    <div>
                                                        <h3>邮件回复</h3>
                                                        <span>收件人固定为访客确认的联系邮箱</span>
                                                    </div>
                                                    {replySubmitted && (
                                                        <span
                                                            className={`delivery-tag delivery-tag--${reply?.state.toLowerCase()}`}
                                                        >
                                                            {deliveryState(reply?.state || "")}
                                                        </span>
                                                    )}
                                                </div>
                                                <label htmlFor="followup-subject">邮件主题</label>
                                                <input
                                                    id="followup-subject"
                                                    required
                                                    maxLength={200}
                                                    value={subject}
                                                    onChange={(event) => setSubject(event.target.value)}
                                                    disabled={replySubmitted}
                                                    placeholder="请输入邮件主题"
                                                />
                                                <label htmlFor="followup-content">回复内容</label>
                                                <textarea
                                                    id="followup-content"
                                                    required
                                                    rows={8}
                                                    maxLength={10000}
                                                    value={content}
                                                    onChange={(event) => setContent(event.target.value)}
                                                    disabled={replySubmitted}
                                                    placeholder="输入最终回复…"
                                                />
                                                <div className="reply-editor-actions">
                                                    <span>
                                                        <ShieldCheck size={13} />
                                                        正文提交后不可修改
                                                    </span>
                                                    {reply?.state === "SENT" ? (
                                                        <button
                                                            className="button button--primary"
                                                            type="button"
                                                            onClick={() => void close()}
                                                            disabled={busy}
                                                        >
                                                            <Check size={15} />
                                                            确认结案
                                                        </button>
                                                    ) : reply?.state === "DEAD" ? (
                                                        <button
                                                            className="button button--quiet"
                                                            type="button"
                                                            onClick={() => void retry()}
                                                            disabled={busy}
                                                        >
                                                            <RotateCcw size={15} />
                                                            重试投递
                                                        </button>
                                                    ) : replySubmitted ? (
                                                        <span className="delivery-wait">
                                                            {reply?.attempts
                                                                ? `已尝试 ${reply.attempts} 次`
                                                                : "等待投递状态更新"}
                                                        </span>
                                                    ) : (
                                                        <button
                                                            className="button button--primary"
                                                            type="submit"
                                                            disabled={busy || !subject.trim() || !content.trim()}
                                                        >
                                                            <Send size={15} />
                                                            提交最终回复
                                                        </button>
                                                    )}
                                                </div>
                                            </form>
                                        )}
                                    </>
                                )}
                            </div>
                            <div className="conversation-footer">
                                <span>
                                    <ArrowRight size={13} />
                                    消息正文由原会话 owner 保存
                                </span>
                                <span>版本 {selected.version}</span>
                            </div>
                        </>
                    )}
                </section>
            </div>
            <div className="agent-page-footer">
                <span>
                    <ShieldCheck size={14} />
                    工单按原客服组授权
                </span>
                <span>投递状态以网关受理回执为准</span>
            </div>
        </main>
    );
}

function followupState(state: string) {
    return (
        ({ OPEN: "待认领", CLAIMED: "处理中", REPLIED: "已回复", CLOSED: "已结案" } as Record<string, string>)[state] ||
        state
    );
}

function formatTime(value: string) {
    return new Intl.DateTimeFormat("zh-CN", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}

function deliveryState(state: string) {
    return (
        (
            {
                PENDING: "等待发送",
                CLAIMED: "正在投递",
                SENT: "网关已受理",
                DEAD: "投递失败",
                CLOSED: "已结案",
            } as Record<string, string>
        )[state] || state
    );
}
