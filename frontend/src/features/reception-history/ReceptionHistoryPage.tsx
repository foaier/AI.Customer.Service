import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Clock3, History, RefreshCw, Search, X } from "lucide-react";
import { request } from "@/shared/api/client";
import type { Assistant, Episode, HumanMessage } from "@/shared/types/customer-service";

type CompletedPage = { episodes: Episode[]; page: number; size: number; hasMore: boolean };
type CompletedMessages = { messages: HumanMessage[]; hasMore: boolean };

function formatTime(value?: string | null): string {
    if (!value) return "—";
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? "—" : date.toLocaleString("zh-CN", { hour12: false });
}

export default function ReceptionHistoryPage({ assistant }: { assistant: Assistant }) {
    const assistantId = assistant.assistantConfigId || assistant.configId || assistant.id;
    const [page, setPage] = useState(1);
    const [email, setEmail] = useState("");
    const [closedFrom, setClosedFrom] = useState("");
    const [closedBefore, setClosedBefore] = useState("");
    const [applied, setApplied] = useState({ email: "", closedFrom: "", closedBefore: "" });
    const [episodes, setEpisodes] = useState<Episode[]>([]);
    const [hasMore, setHasMore] = useState(false);
    const [selected, setSelected] = useState<Episode | null>(null);
    const [messages, setMessages] = useState<HumanMessage[]>([]);
    const [messagesMore, setMessagesMore] = useState(false);
    const [loading, setLoading] = useState(true);
    const [detailLoading, setDetailLoading] = useState(false);
    const [error, setError] = useState("");

    const load = useCallback(async () => {
        setLoading(true);
        setError("");
        try {
            const query = new URLSearchParams({ assistantConfigId: assistantId, page: String(page), size: "50" });
            if (applied.email) query.set("email", applied.email);
            if (applied.closedFrom) query.set("closedFrom", new Date(applied.closedFrom).toISOString());
            if (applied.closedBefore) query.set("closedBefore", new Date(applied.closedBefore).toISOString());
            const result = await request<CompletedPage>(`/customer-service/agents/receptions/completed?${query}`);
            setEpisodes(result.episodes || []);
            setHasMore(result.hasMore === true);
        } catch (reason) {
            setEpisodes([]);
            setHasMore(false);
            setError(reason instanceof Error ? reason.message : "已完成接待历史读取失败");
        } finally {
            setLoading(false);
        }
    }, [assistantId, applied, page]);

    useEffect(() => {
        void load();
    }, [load]);

    const openDetail = async (episode: Episode) => {
        setSelected(episode);
        setMessages([]);
        setMessagesMore(false);
        setDetailLoading(true);
        try {
            const conversationId = encodeURIComponent(episode.scope.conversationId);
            const episodeId = encodeURIComponent(episode.episodeId);
            const [summary, timeline] = await Promise.all([
                request<Episode>(
                    `/customer-service/agents/conversations/${conversationId}/episodes/${episodeId}/reception`,
                ),
                request<CompletedMessages>(
                    `/customer-service/agents/conversations/${conversationId}/episodes/${episodeId}/messages?limit=100`,
                ),
            ]);
            setSelected(summary);
            setMessages(timeline.messages || []);
            setMessagesMore(timeline.hasMore === true);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "历史详情读取失败");
        } finally {
            setDetailLoading(false);
        }
    };

    const loadMoreMessages = async () => {
        if (!selected || !messages.length) return;
        setDetailLoading(true);
        try {
            const conversationId = encodeURIComponent(selected.scope.conversationId);
            const episodeId = encodeURIComponent(selected.episodeId);
            const afterSeq = messages[messages.length - 1].seq;
            const result = await request<CompletedMessages>(
                `/customer-service/agents/conversations/${conversationId}/episodes/${episodeId}/messages?afterSeq=${afterSeq}&limit=100`,
            );
            setMessages((current) => [...current, ...(result.messages || [])]);
            setMessagesMore(result.hasMore === true);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "历史消息读取失败");
        } finally {
            setDetailLoading(false);
        }
    };

    const applyFilters = (event: FormEvent) => {
        event.preventDefault();
        if (closedFrom && closedBefore && new Date(closedFrom) >= new Date(closedBefore)) {
            setError("完成时间范围的开始时间必须早于结束时间。");
            return;
        }
        setError("");
        setPage(1);
        setApplied({ email: email.trim(), closedFrom, closedBefore });
    };

    return (
        <main className="agent-page records-page">
            <header className="agent-page-header">
                <div>
                    <div className="eyebrow">接待归档</div>
                    <h1>已完成接待</h1>
                    <p className="page-subtitle">只显示由你完成且当前仍有权限查看的接待周期；详情固定读取原周期。</p>
                </div>
                <button className="button button--quiet" type="button" disabled={loading} onClick={() => void load()}>
                    <RefreshCw size={15} /> 刷新
                </button>
            </header>
            <form className="history-filters" onSubmit={applyFilters}>
                <label>
                    联系邮箱
                    <input
                        type="email"
                        value={email}
                        onChange={(event) => setEmail(event.target.value)}
                        placeholder="精确邮箱筛选"
                        maxLength={254}
                    />
                </label>
                <label>
                    完成时间起
                    <input
                        type="datetime-local"
                        value={closedFrom}
                        onChange={(event) => setClosedFrom(event.target.value)}
                    />
                </label>
                <label>
                    完成时间止
                    <input
                        type="datetime-local"
                        value={closedBefore}
                        onChange={(event) => setClosedBefore(event.target.value)}
                    />
                </label>
                <button className="button button--primary" type="submit">
                    <Search size={15} /> 筛选
                </button>
            </form>
            {error && (
                <div className="page-alert" role="alert">
                    {error}
                </div>
            )}
            <section className="records-panel" aria-label="已完成接待列表">
                <div className="records-panel-head">
                    <div>
                        <strong>{assistant.name}</strong>
                        <span>{episodes.length} 条历史接待</span>
                    </div>
                    <span className="record-pill record-pill--human">
                        <History size={13} /> 已完成
                    </span>
                </div>
                <div className="records-table-wrap">
                    <table className="records-table">
                        <thead>
                            <tr>
                                <th>会话 ID</th>
                                <th>接待周期</th>
                                <th>业务组</th>
                                <th>申请时间</th>
                                <th>完成时间</th>
                                <th>结束原因</th>
                                <th>详情</th>
                            </tr>
                        </thead>
                        <tbody>
                            {episodes.map((episode) => (
                                <tr key={episode.episodeId}>
                                    <td title={episode.scope.conversationId}>{episode.scope.conversationId}</td>
                                    <td title={episode.episodeId}>{episode.episodeId}</td>
                                    <td>{episode.groupId || "—"}</td>
                                    <td>{formatTime(episode.requestedAt)}</td>
                                    <td>{formatTime(episode.finishedAt)}</td>
                                    <td>
                                        {episode.resolutionReason === "VISITOR_INACTIVE_5M"
                                            ? "访客超时结束"
                                            : "客服手动结束"}
                                    </td>
                                    <td>
                                        <button
                                            className="button button--quiet button--small"
                                            type="button"
                                            onClick={() => void openDetail(episode)}
                                        >
                                            查看消息
                                        </button>
                                    </td>
                                </tr>
                            ))}
                            {!loading && episodes.length === 0 && (
                                <tr>
                                    <td colSpan={7} className="records-empty">
                                        暂无符合条件的历史接待
                                    </td>
                                </tr>
                            )}
                            {loading && (
                                <tr>
                                    <td colSpan={7} className="records-empty">
                                        正在读取历史…
                                    </td>
                                </tr>
                            )}
                        </tbody>
                    </table>
                </div>
                <footer className="records-pagination">
                    <span>
                        第 {page} 页{hasMore ? " · 还有下一页" : " · 已到末页"}
                    </span>
                    <div>
                        <button
                            className="button button--quiet button--small"
                            type="button"
                            disabled={loading || page <= 1}
                            onClick={() => setPage((current) => Math.max(1, current - 1))}
                        >
                            上一页
                        </button>
                        <button
                            className="button button--quiet button--small"
                            type="button"
                            disabled={loading || !hasMore}
                            onClick={() => setPage((current) => current + 1)}
                        >
                            下一页
                        </button>
                    </div>
                </footer>
            </section>
            {selected && (
                <div
                    className="records-modal-backdrop"
                    role="presentation"
                    onMouseDown={(event) => {
                        if (event.target === event.currentTarget) setSelected(null);
                    }}
                >
                    <section
                        className="records-modal"
                        role="dialog"
                        aria-modal="true"
                        aria-labelledby="history-detail-title"
                    >
                        <header>
                            <div>
                                <span>
                                    <Clock3 size={15} /> 原周期历史消息
                                </span>
                                <h2 id="history-detail-title">{selected.scope.conversationId}</h2>
                            </div>
                            <button
                                className="icon-button"
                                type="button"
                                aria-label="关闭详情"
                                onClick={() => setSelected(null)}
                            >
                                <X size={17} />
                            </button>
                        </header>
                        <div className="history-detail-meta">
                            <span>周期 {selected.episodeId}</span>
                            <span>申请 {formatTime(selected.requestedAt)}</span>
                            <span>完成 {formatTime(selected.finishedAt)}</span>
                        </div>
                        <div className="records-transcript">
                            {messages.map((message) => (
                                <article
                                    className={`records-message ${message.senderRole === "AGENT" ? "records-message--agent" : "records-message--visitor"}`}
                                    key={message.messageId}
                                >
                                    <div>
                                        <strong>{message.senderRole === "AGENT" ? "人工客服" : "访客"}</strong>
                                        <time>{formatTime(message.createdAt)}</time>
                                    </div>
                                    <p>{message.content}</p>
                                </article>
                            ))}
                            {detailLoading && <p className="records-empty">正在读取原周期消息…</p>}
                            {!detailLoading && messages.length === 0 && (
                                <p className="records-empty">该接待周期没有人工消息。</p>
                            )}
                        </div>
                        {messagesMore && (
                            <button
                                className="button button--quiet records-load-more"
                                type="button"
                                disabled={detailLoading}
                                onClick={() => void loadMoreMessages()}
                            >
                                读取更早消息
                            </button>
                        )}
                    </section>
                </div>
            )}
        </main>
    );
}
