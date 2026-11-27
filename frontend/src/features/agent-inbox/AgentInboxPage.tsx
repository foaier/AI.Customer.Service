import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import {
    Activity,
    ArrowDownLeft,
    ArrowUpRight,
    BellRing,
    Check,
    CheckCheck,
    Clock3,
    LoaderCircle,
    Mail,
    MessageSquareText,
    Power,
    RefreshCw,
    Send,
    ShieldCheck,
    UserRound,
    UsersRound,
} from "lucide-react";
import { getAccessToken } from "@/shared/auth/client";
import { ApiError, post, realtimeUrl, request } from "@/shared/api/client";
import type { AgentSnapshot, Assistant, Episode, HumanMessage, InboxSummary } from "@/shared/types/customer-service";
import { useEpisodePreviews } from "@/features/agent-inbox/useEpisodePreviews";

type AgentMessage = {
    id: string;
    seq?: number;
    senderRole: "VISITOR" | "AGENT" | "AI";
    content: string;
    createdAt?: string;
};
type Availability = {
    status: "ONLINE" | "AWAY" | "OFFLINE";
    version: number;
    expiresAt?: string | null;
    activeConversations?: number;
    capacity?: number;
};
const activeStatuses = ["CLAIMED", "ACTIVE"];

function advanceGeneration(generation: { current: number }) {
    generation.current++;
}

export default function AgentInbox({ assistant }: { assistant: Assistant }) {
    const assistantId = assistant.assistantConfigId || assistant.configId || assistant.id;
    const query = `assistantConfigId=${encodeURIComponent(assistantId)}`;
    const [episodes, setEpisodes] = useState<Episode[]>([]);
    const [selected, setSelected] = useState<Episode | null>(null);
    const [messages, setMessages] = useState<AgentMessage[]>([]);
    const [readyEpisodeId, setReadyEpisodeId] = useState("");
    const [availability, setAvailability] = useState<Availability | null>(null);
    const [inboxSummary, setInboxSummary] = useState<InboxSummary | null>(null);
    const [input, setInput] = useState("");
    const [busy, setBusy] = useState(false);
    const [refreshing, setRefreshing] = useState(false);
    const [error, setError] = useState("");
    const [socketState, setSocketState] = useState("CONNECTING");
    const listRef = useRef<HTMLDivElement>(null);
    const lastSeq = useRef(0);
    const textNonce = useRef(0);
    const selectedRef = useRef<Episode | null>(null);
    const selectionGeneration = useRef(0);
    const inboxToken = useRef("");
    const queueRefreshTimer = useRef(0);
    const queueRevision = useRef(0);
    const loadRequest = useRef(0);
    const readWatermark = useRef({ conversationId: "", seq: 0 });
    const pendingRead = useRef("");
    const openedLink = useRef("");
    const [pageVisible, setPageVisible] = useState(() => document.visibilityState === "visible");
    const previews = useEpisodePreviews(episodes);
    const selectedEpisodeId = selected?.episodeId;
    const selectedConversationId = selected?.scope.conversationId;
    const selectedStatus = selected?.status;

    const syncSelection = useCallback((queue: Episode[]) => {
        const current = selectedRef.current;
        if (!current) return;
        const updated = queue.find((item) => item.episodeId === current.episodeId);
        if (updated) {
            selectedRef.current = updated;
            setSelected(updated);
            return;
        }
        selectedRef.current = null;
        selectionGeneration.current++;
        setSelected(null);
        setMessages([]);
        lastSeq.current = 0;
    }, []);

    const loadQueue = useCallback(
        async (quiet = false) => {
            const requestId = ++loadRequest.current;
            const revision = ++queueRevision.current;
            if (!quiet) setRefreshing(true);
            try {
                const [queue, currentAvailability, summary] = await Promise.all([
                    request<{ episodes: Episode[] }>(`/customer-service/agents/queue?${query}`),
                    request<Availability>(`/customer-service/agents/availability?${query}`),
                    request<InboxSummary>(`/customer-service/agents/inbox/summary?${query}`).catch(() => null),
                ]);
                if (requestId !== loadRequest.current) return;
                setAvailability(currentAvailability);
                if (revision !== queueRevision.current) return;
                setEpisodes(queue.episodes || []);
                if (summary) {
                    inboxToken.current = summary.changeToken;
                    setInboxSummary(summary);
                }
                syncSelection(queue.episodes || []);
                setError("");
            } catch (reason) {
                if (revision !== queueRevision.current) return;
                if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
                    setEpisodes([]);
                    selectedRef.current = null;
                    selectionGeneration.current++;
                    setSelected(null);
                    setMessages([]);
                    lastSeq.current = 0;
                }
                setError(reason instanceof Error ? reason.message : "队列读取失败");
            } finally {
                if (!quiet) setRefreshing(false);
            }
        },
        [query, syncSelection],
    );

    const receiveTimeline = useCallback((items: HumanMessage[]) => {
        if (!items.length) return;
        lastSeq.current = Math.max(lastSeq.current, ...items.map((item) => item.seq));
        setMessages((current) => {
            const seen = new Set(current.map((message) => message.id));
            return current.concat(
                items
                    .filter((item) => !seen.has(item.messageId))
                    .map((item) => ({
                        id: item.messageId,
                        seq: item.seq,
                        senderRole: item.senderRole,
                        content: item.content,
                        createdAt: item.createdAt,
                    })),
            );
        });
    }, []);

    const loadConversation = useCallback(
        async (episode: Episode) => {
            const generation = ++selectionGeneration.current;
            selectedRef.current = episode;
            setSelected(episode);
            setMessages([]);
            setReadyEpisodeId("");
            lastSeq.current = 0;
            if (!activeStatuses.includes(episode.status)) return;
            try {
                const [timeline, history] = await Promise.all([
                    request<{ messages: HumanMessage[] }>(
                        `/customer-service/agents/conversations/${encodeURIComponent(episode.scope.conversationId)}/messages?afterSeq=0&limit=100`,
                    ),
                    request<{ id: string; role: string; content: string }[]>(
                        `/customer-service/agents/conversations/${encodeURIComponent(episode.scope.conversationId)}/history`,
                    ),
                ]);
                if (generation !== selectionGeneration.current) return;
                setMessages(
                    (history || []).map((item, index) => ({
                        id: item.id || `ai-${index}`,
                        senderRole: item.role === "user" ? "VISITOR" : "AI",
                        content: item.content,
                    })),
                );
                receiveTimeline(timeline.messages || []);
                setReadyEpisodeId(episode.episodeId);
            } catch (reason) {
                if (generation === selectionGeneration.current)
                    setError(reason instanceof Error ? reason.message : "会话读取失败");
            }
        },
        [receiveTimeline],
    );

    useEffect(() => {
        const params = new URLSearchParams(window.location.search);
        const conversationId = params.get("conversationId");
        if (!conversationId || params.get("assistantConfigId") !== assistantId) return;
        const linkKey = `${query}&conversationId=${conversationId}`;
        if (openedLink.current === linkKey) return;
        const episode = episodes.find((item) => item.scope.conversationId === conversationId);
        if (!episode) return;
        openedLink.current = linkKey;
        void loadConversation(episode);
    }, [episodes, assistantId, query, loadConversation]);

    useEffect(() => {
        void loadQueue();
        return () => {
            advanceGeneration(loadRequest);
            advanceGeneration(queueRevision);
            advanceGeneration(selectionGeneration);
            window.clearTimeout(queueRefreshTimer.current);
        };
    }, [loadQueue]);

    useEffect(() => {
        if (!assistantId) return;
        let closed = false;
        let timer = 0;
        let socket: WebSocket | null = null;
        let failures = 0;
        const connect = async () => {
            try {
                let baseline = true;
                const token = await getAccessToken();
                if (closed) return;
                socket = new WebSocket(
                    realtimeUrl({
                        authorization: `Bearer ${token}`,
                        agent: true,
                        assistantConfigId: assistantId,
                        afterSeq: 0,
                    }),
                );
                socket.onopen = () => {
                    failures = 0;
                    setSocketState("CONNECTED");
                };
                socket.onmessage = (event) => {
                    if (closed) return;
                    const frame = JSON.parse(String(event.data));
                    if (frame.type === "inbox.notifications") {
                        const summary = frame.data as InboxSummary;
                        const changed = !baseline && summary.changeToken !== inboxToken.current;
                        baseline = false;
                        if (summary.changeToken) {
                            inboxToken.current = summary.changeToken;
                            setInboxSummary(summary);
                            if (changed && !queueRefreshTimer.current) {
                                queueRefreshTimer.current = window.setTimeout(() => {
                                    queueRefreshTimer.current = 0;
                                    void loadQueue(true);
                                }, 300);
                            }
                        }
                        return;
                    }
                    if (frame.type === "snapshot") {
                        const data = frame.data as AgentSnapshot;
                        if (data.queue) {
                            queueRevision.current++;
                            setEpisodes(data.queue);
                            syncSelection(data.queue);
                        }
                    }
                };
                socket.onclose = (event) => {
                    if (closed) return;
                    if (event.code === 1008) {
                        loadRequest.current++;
                        queueRevision.current++;
                        selectionGeneration.current++;
                        setEpisodes([]);
                        selectedRef.current = null;
                        setSelected(null);
                        setMessages([]);
                        lastSeq.current = 0;
                    }
                    setSocketState("RECONNECTING");
                    if (!closed) timer = window.setTimeout(connect, Math.min(1000 * 2 ** failures++, 15000));
                };
                socket.onerror = () => setSocketState("RECONNECTING");
            } catch {
                setSocketState("RECONNECTING");
                if (!closed) timer = window.setTimeout(connect, Math.min(1000 * 2 ** failures++, 15000));
            }
        };
        void connect();
        return () => {
            closed = true;
            window.clearTimeout(timer);
            window.clearTimeout(queueRefreshTimer.current);
            queueRefreshTimer.current = 0;
            socket?.close();
        };
    }, [assistantId, syncSelection, loadQueue]);

    useEffect(() => {
        if (
            !selectedEpisodeId ||
            !selectedConversationId ||
            readyEpisodeId !== selectedEpisodeId ||
            !activeStatuses.includes(selectedStatus || "")
        )
            return;
        let closed = false;
        let timer = 0;
        let socket: WebSocket | null = null;
        let failures = 0;
        const connect = async () => {
            try {
                const token = await getAccessToken();
                if (closed) return;
                socket = new WebSocket(
                    realtimeUrl({
                        authorization: `Bearer ${token}`,
                        agent: true,
                        conversationId: selectedConversationId,
                        assistantConfigId: assistantId,
                        afterSeq: lastSeq.current,
                    }),
                );
                socket.onmessage = (event) => {
                    if (closed || selectedRef.current?.episodeId !== selectedEpisodeId) return;
                    const frame = JSON.parse(String(event.data));
                    if (frame.type !== "snapshot") return;
                    const data = frame.data as AgentSnapshot;
                    if (data.reception) {
                        selectedRef.current = data.reception;
                        setSelected(data.reception);
                    }
                    if (data.timeline?.messages) receiveTimeline(data.timeline.messages);
                };
                socket.onclose = (event) => {
                    if (closed || selectedRef.current?.episodeId !== selectedEpisodeId) return;
                    if (event.code === 1008) {
                        selectedRef.current = null;
                        setSelected(null);
                        setMessages([]);
                        lastSeq.current = 0;
                        return;
                    }
                    if (!closed) timer = window.setTimeout(connect, Math.min(1000 * 2 ** failures++, 15000));
                };
            } catch {
                if (!closed) timer = window.setTimeout(connect, Math.min(1000 * 2 ** failures++, 15000));
            }
        };
        void connect();
        return () => {
            closed = true;
            window.clearTimeout(timer);
            socket?.close();
        };
    }, [selectedConversationId, selectedStatus, selectedEpisodeId, readyEpisodeId, assistantId, receiveTimeline]);

    useEffect(() => {
        listRef.current?.scrollTo({ top: listRef.current.scrollHeight, behavior: "smooth" });
    }, [messages]);

    useEffect(() => {
        const updateVisibility = () => setPageVisible(document.visibilityState === "visible");
        document.addEventListener("visibilitychange", updateVisibility);
        return () => document.removeEventListener("visibilitychange", updateVisibility);
    }, []);

    useEffect(() => {
        if (!pageVisible || !selected || !activeStatuses.includes(selected.status)) return;
        const seq = messages.reduce(
            (latest, message) => (message.senderRole === "VISITOR" ? Math.max(latest, message.seq || 0) : latest),
            0,
        );
        const conversationId = selected.scope.conversationId;
        if (seq <= 0) return;
        if (readWatermark.current.conversationId !== conversationId) {
            readWatermark.current = { conversationId, seq: 0 };
        }
        if (seq <= readWatermark.current.seq || pendingRead.current === `${conversationId}:${seq}`) return;
        const requestKey = `${conversationId}:${seq}`;
        const generation = selectionGeneration.current;
        pendingRead.current = requestKey;
        post(`/customer-service/agents/conversations/${encodeURIComponent(conversationId)}/receipts`, {
            kind: "READ",
            seq,
        })
            .then(() => {
                if (generation !== selectionGeneration.current) return;
                readWatermark.current = { conversationId, seq: Math.max(seq, readWatermark.current.seq) };
                void loadQueue(true);
            })
            .catch(() => undefined)
            .finally(() => {
                if (pendingRead.current === requestKey) pendingRead.current = "";
            });
    }, [loadQueue, messages, pageVisible, selected]);

    useEffect(() => {
        if (availability?.status !== "ONLINE") return;
        const timer = window.setInterval(() => {
            post<Availability>(`/customer-service/agents/availability/heartbeat?${query}`, {
                version: availability.version,
            })
                .then(setAvailability)
                .catch(() => setAvailability((current) => (current ? { ...current, status: "OFFLINE" } : current)));
        }, 15000);
        return () => window.clearInterval(timer);
    }, [availability?.status, availability?.version, query]);

    const setStatus = async (status: Availability["status"]) => {
        setBusy(true);
        try {
            const result = await post<Availability>(`/customer-service/agents/availability?${query}`, { status });
            setAvailability(result);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "在线状态更新失败");
        } finally {
            setBusy(false);
        }
    };

    const claim = async () => {
        if (!selected) return;
        setBusy(true);
        setError("");
        try {
            await post(
                `/customer-service/agents/conversations/${encodeURIComponent(selected.scope.conversationId)}/claim`,
                {
                    requestId: `claim_${crypto.randomUUID().replaceAll("-", "")}`,
                    expectedVersion: selected.version,
                },
            );
            await loadQueue(true);
            const current = { ...selected, status: "CLAIMED" as const, version: selected.version + 1 };
            await loadConversation(current);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "接单未完成");
            await loadQueue(true);
        } finally {
            setBusy(false);
        }
    };

    const sendMessage = async (event: FormEvent) => {
        event.preventDefault();
        const content = input.trim();
        if (!selected || !content || busy) return;
        setInput("");
        setBusy(true);
        setError("");
        const id = `agent_${crypto.randomUUID().replaceAll("-", "")}`;
        const optimistic = `optimistic-${++textNonce.current}`;
        setMessages((current) => [...current, { id: optimistic, senderRole: "AGENT", content }]);
        try {
            const saved = await post<HumanMessage>(
                `/customer-service/agents/conversations/${encodeURIComponent(selected.scope.conversationId)}/messages`,
                { clientMsgId: id, content },
            );
            setMessages((current) =>
                current.flatMap((message) => {
                    if (message.id !== optimistic) return [message];
                    return current.some((item) => item.id === saved.messageId)
                        ? []
                        : [{ ...message, id: saved.messageId, createdAt: saved.createdAt }];
                }),
            );
            const current = selectedRef.current;
            if (current?.status === "CLAIMED") {
                const updated = { ...current, status: "ACTIVE" as const, version: current.version + 1 };
                selectedRef.current = updated;
                setSelected(updated);
            }
            void loadQueue(true);
        } catch (reason) {
            setMessages((current) => current.filter((message) => message.id !== optimistic));
            setInput(content);
            setError(reason instanceof Error ? reason.message : "消息发送失败");
        } finally {
            setBusy(false);
        }
    };

    const resolve = async () => {
        if (!selected || !window.confirm("结束本次人工接待？访客随后可以继续咨询 AI。")) return;
        setBusy(true);
        try {
            await post(
                `/customer-service/agents/conversations/${encodeURIComponent(selected.scope.conversationId)}/resolve`,
                {
                    requestId: `resolve_${crypto.randomUUID().replaceAll("-", "")}`,
                    expectedVersion: selected.version,
                },
            );
            const resolved = { ...selected, status: "RESOLVED" as const, version: selected.version + 1 };
            selectedRef.current = resolved;
            setSelected(resolved);
            setError("");
            await loadQueue(true);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "结束接待失败");
        } finally {
            setBusy(false);
        }
    };

    const requested = episodes.filter((item) => item.status === "REQUESTED").length;
    const mine = episodes.filter((item) => activeStatuses.includes(item.status)).length;
    const unreadMessages =
        inboxSummary?.unreadMessageCount ?? episodes.reduce((sum, item) => sum + (item.unreadCount || 0), 0);

    return (
        <main className="agent-page">
            <div className="agent-page-header">
                <div>
                    <p className="eyebrow">SERVICE OPERATIONS / INBOX</p>
                    <h1>在线接待</h1>
                    <p className="page-subtitle">新申请由有权限的客服认领；接待回复会同步回访客会话。</p>
                </div>
                <div className="page-header-actions">
                    <span className={`realtime-indicator ${socketState === "CONNECTED" ? "is-connected" : ""}`}>
                        <span />
                        {socketState === "CONNECTED" ? "实时已连接" : "正在连接"}
                    </span>
                    <button
                        className="button button--quiet"
                        type="button"
                        onClick={() => void loadQueue()}
                        disabled={refreshing}
                    >
                        <RefreshCw size={15} className={refreshing ? "spin" : ""} />
                        刷新
                    </button>
                </div>
            </div>

            {error && (
                <div className="page-alert" role="alert">
                    {error}
                    <button className="icon-button" type="button" aria-label="关闭" onClick={() => setError("")}>
                        ×
                    </button>
                </div>
            )}
            <div className="agent-metrics">
                <div className="metric-item">
                    <span className="metric-icon metric-icon--amber">
                        <ArrowDownLeft size={17} />
                    </span>
                    <div>
                        <span>待接单</span>
                        <strong>
                            {(inboxSummary?.pendingReceptionCount ?? requested).toString().padStart(2, "0")}
                        </strong>
                    </div>
                </div>
                <div className="metric-item">
                    <span className="metric-icon metric-icon--teal">
                        <UsersRound size={17} />
                    </span>
                    <div>
                        <span>我的接待</span>
                        <strong>{mine.toString().padStart(2, "0")}</strong>
                    </div>
                </div>
                <div className="metric-item">
                    <span className="metric-icon metric-icon--coral">
                        <BellRing size={17} />
                    </span>
                    <div>
                        <span>未读访客消息</span>
                        <strong>{unreadMessages.toString().padStart(2, "0")}</strong>
                    </div>
                </div>
                <div className="metric-item">
                    <span className="metric-icon metric-icon--amber">
                        <Mail size={17} />
                    </span>
                    <div>
                        <span>新留言</span>
                        <strong>{(inboxSummary?.newFollowupCount ?? 0).toString().padStart(2, "0")}</strong>
                    </div>
                </div>
                <div className="metric-item">
                    <span className="metric-icon metric-icon--coral">
                        <Activity size={17} />
                    </span>
                    <div>
                        <span>我的在线状态</span>
                        <strong className="metric-status">
                            {availability?.status === "ONLINE"
                                ? "在线"
                                : availability?.status === "AWAY"
                                  ? "暂离"
                                  : "离线"}
                        </strong>
                    </div>
                </div>
                <div className="availability-switch" role="group" aria-label="在线状态">
                    <button
                        type="button"
                        className={availability?.status === "ONLINE" ? "selected" : ""}
                        disabled={busy}
                        onClick={() => void setStatus("ONLINE")}
                    >
                        <span className="availability-dot availability-dot--online" />
                        上线
                    </button>
                    <button
                        type="button"
                        className={availability?.status === "AWAY" ? "selected" : ""}
                        disabled={busy}
                        onClick={() => void setStatus("AWAY")}
                    >
                        <span className="availability-dot availability-dot--away" />
                        暂离
                    </button>
                    <button
                        type="button"
                        className={availability?.status === "OFFLINE" ? "selected" : ""}
                        disabled={busy}
                        onClick={() => void setStatus("OFFLINE")}
                    >
                        <Power size={13} />
                        下线
                    </button>
                </div>
            </div>

            <div className="agent-workspace">
                <section className="queue-panel">
                    <div className="queue-panel-head">
                        <div>
                            <h2>接待队列</h2>
                            <span>只显示有权限范围内的会话</span>
                        </div>
                        <span className="queue-count">{episodes.length}</span>
                    </div>
                    <div className="queue-filter">
                        <span>全部状态</span>
                        <span className="queue-live">
                            <span />
                            实时更新
                        </span>
                    </div>
                    <div className="queue-list">
                        {[...episodes]
                            .sort((a, b) => Number((b.unreadCount || 0) > 0) - Number((a.unreadCount || 0) > 0))
                            .map((item) => {
                                const isSelected = selected?.episodeId === item.episodeId;
                                const isRequested = item.status === "REQUESTED";
                                const preview =
                                    isSelected && messages.length
                                        ? messages.at(-1)!.content
                                        : previews[item.episodeId] || (isRequested ? "等待接待" : "正在读取消息…");
                                return (
                                    <button
                                        key={item.episodeId}
                                        type="button"
                                        className={`queue-item ${isSelected ? "active" : ""}`}
                                        onClick={() => void loadConversation(item)}
                                    >
                                        <span className={`queue-avatar ${isRequested ? "queue-avatar--new" : ""}`}>
                                            <UserRound size={17} />
                                        </span>
                                        <span className="queue-item-body">
                                            <span className="queue-item-top">
                                                <strong>{item.principalId || "访客"}</strong>
                                                <time>{formatAge(item.requestedAt)}</time>
                                            </span>
                                            <span className="queue-preview" title={preview}>
                                                {preview}
                                            </span>
                                            <span className="queue-item-bottom">
                                                <span
                                                    className={`status-label status-label--${item.status.toLowerCase()}`}
                                                >
                                                    {statusName(item.status)}
                                                </span>
                                                {item.agentDisplayName && <span>客服：{item.agentDisplayName}</span>}
                                                <span className="queue-session">
                                                    {item.scope.conversationId.slice(-8)}
                                                </span>
                                            </span>
                                        </span>
                                        {isRequested && <span className="new-request-dot" />}
                                        {typeof item.unreadCount === "number" && item.unreadCount > 0 && (
                                            <span
                                                className="unread-pill"
                                                aria-label={`${item.unreadCount} 条未读访客消息`}
                                            >
                                                {item.unreadCount > 99 ? "99+" : item.unreadCount}
                                            </span>
                                        )}
                                    </button>
                                );
                            })}
                        {episodes.length === 0 && (
                            <div className="queue-empty">
                                <span className="queue-empty-icon">
                                    <CheckCheck size={21} />
                                </span>
                                <strong>队列已清空</strong>
                                <span>新申请会自动显示在这里</span>
                            </div>
                        )}
                    </div>
                    <div className="queue-panel-foot">
                        <span>
                            <ShieldCheck size={14} /> 成员权限由组织配置校验
                        </span>
                    </div>
                </section>

                <section className="conversation-panel">
                    {!selected ? (
                        <div className="conversation-empty">
                            <div className="empty-illustration">
                                <MessageSquareText size={26} />
                                <span />
                            </div>
                            <h2>选择一条接待记录</h2>
                            <p>队列中的申请不会显示访客正文。接单后即可查看完整会话并回复。</p>
                        </div>
                    ) : (
                        <>
                            <div className="conversation-header">
                                <div className="visitor-identity">
                                    <span className="queue-avatar queue-avatar--large">
                                        <UserRound size={18} />
                                    </span>
                                    <div>
                                        <strong>访客 {selected.principalId || "会话"}</strong>
                                        <span>会话 · {selected.scope.conversationId}</span>
                                        {(selected.contactName || selected.contactEmail) && (
                                            <span className="contact-profile-line">
                                                {selected.contactName && <strong>{selected.contactName}</strong>}
                                                {selected.contactEmail && (
                                                    <a href={`mailto:${selected.contactEmail}`}>
                                                        {selected.contactEmail}
                                                    </a>
                                                )}
                                            </span>
                                        )}
                                    </div>
                                </div>
                                <div className="conversation-header-actions">
                                    <span className={`status-label status-label--${selected.status.toLowerCase()}`}>
                                        {statusName(selected.status)}
                                    </span>
                                    {selected.status === "REQUESTED" && (
                                        <button
                                            className="button button--primary button--small"
                                            type="button"
                                            disabled={busy}
                                            onClick={() => void claim()}
                                        >
                                            {busy ? (
                                                <LoaderCircle className="spin" size={15} />
                                            ) : (
                                                <ArrowDownLeft size={15} />
                                            )}{" "}
                                            接单
                                        </button>
                                    )}
                                    {activeStatuses.includes(selected.status) && (
                                        <button
                                            className="button button--quiet button--small"
                                            type="button"
                                            disabled={busy}
                                            onClick={() => void resolve()}
                                        >
                                            <Check size={15} />
                                            结束接待
                                        </button>
                                    )}
                                </div>
                            </div>
                            {selected.status === "REQUESTED" ? (
                                <div className="claim-gate">
                                    <span className="claim-gate-icon">
                                        <ShieldCheck size={19} />
                                    </span>
                                    <div>
                                        <strong>接单前不展示访客对话</strong>
                                        <span>认领成功后，服务端会核验当前客服组与接待容量。</span>
                                    </div>
                                    <button
                                        className="button button--primary"
                                        type="button"
                                        onClick={() => void claim()}
                                        disabled={busy}
                                    >
                                        {busy ? "正在处理" : "认领并查看"}
                                    </button>
                                </div>
                            ) : (
                                <>
                                    <div className="conversation-context">
                                        <span>
                                            <Clock3 size={14} />
                                            申请于 {formatDate(selected.requestedAt)}
                                        </span>
                                        <span>
                                            <ShieldCheck size={14} />{" "}
                                            {selected.triggerSource === "VISITOR_DIRECT_REQUEST"
                                                ? "访客主动申请"
                                                : "人工建议确认"}
                                        </span>
                                        {selected.autoCloseAt && selected.status === "ACTIVE" && (
                                            <span>
                                                <Clock3 size={14} />
                                                无新访客消息时将于 {formatDate(selected.autoCloseAt)} 自动结束
                                            </span>
                                        )}
                                    </div>
                                    <div ref={listRef} className="agent-thread" role="log" aria-live="polite">
                                        {messages.map((message) => (
                                            <div
                                                key={message.id}
                                                className={`agent-message agent-message--${message.senderRole.toLowerCase()}`}
                                            >
                                                <span className="agent-message-mark">
                                                    {message.senderRole === "AI"
                                                        ? "AI"
                                                        : message.senderRole === "AGENT"
                                                          ? "我"
                                                          : "访客"}
                                                </span>
                                                <div>
                                                    <span className="agent-message-author">
                                                        {message.senderRole === "AI"
                                                            ? "AI 助手"
                                                            : message.senderRole === "AGENT"
                                                              ? "我"
                                                              : "访客"}
                                                    </span>
                                                    <p>{message.content}</p>
                                                </div>
                                            </div>
                                        ))}
                                        {messages.length === 0 && (
                                            <div className="thread-loading">
                                                <span className="loader" />
                                                正在读取已授权会话
                                            </div>
                                        )}
                                    </div>
                                    {activeStatuses.includes(selected.status) ? (
                                        <form className="agent-composer" onSubmit={sendMessage}>
                                            <label htmlFor="agent-reply">回复访客</label>
                                            <div>
                                                <textarea
                                                    id="agent-reply"
                                                    value={input}
                                                    maxLength={4000}
                                                    rows={2}
                                                    onChange={(event) => setInput(event.target.value)}
                                                    placeholder="输入回复…"
                                                />
                                                <button
                                                    className="send-button"
                                                    type="submit"
                                                    disabled={!input.trim() || busy}
                                                    aria-label="发送回复"
                                                >
                                                    <Send size={17} />
                                                </button>
                                            </div>
                                            <span className="composer-hint">
                                                <CheckCheck size={13} />
                                                发送后将同步至访客会话
                                            </span>
                                        </form>
                                    ) : (
                                        <div className="resolved-banner">
                                            <Check size={15} /> {resolutionText(selected.resolutionReason)}
                                        </div>
                                    )}
                                </>
                            )}
                        </>
                    )}
                    <div className="conversation-footer">
                        <span>
                            <ArrowUpRight size={13} />
                            人工消息写入共享会话序列
                        </span>
                        <span>{selected ? `版本 ${selected.version}` : "等待选择"}</span>
                    </div>
                </section>
            </div>
            <div className="agent-page-footer">
                <span>
                    <span className="availability-dot availability-dot--online" />
                    {socketState === "CONNECTED" ? "实时连接正常" : "正在恢复实时连接"}
                </span>
                <span>自动保存已启用</span>
            </div>
        </main>
    );
}

function statusName(status: string) {
    return (
        (
            {
                REQUESTED: "待接单",
                CLAIMED: "已接单",
                ACTIVE: "接待中",
                RESOLVED: "已结束",
                CANCELLED: "已取消",
            } as Record<string, string>
        )[status] || status
    );
}

function resolutionText(reason?: Episode["resolutionReason"]) {
    if (reason === "VISITOR_INACTIVE_5M") return "访客 5 分钟未回复，本次接待已自动结束";
    if (reason === "AGENT_MANUAL") return "客服已结束本次接待";
    return "本次接待已结束";
}

function formatAge(value: string) {
    const minutes = Math.max(0, Math.floor((Date.now() - new Date(value).getTime()) / 60000));
    if (minutes < 1) return "刚刚";
    if (minutes < 60) return `${minutes} 分钟`;
    return `${Math.floor(minutes / 60)} 小时`;
}

function formatDate(value: string) {
    return new Intl.DateTimeFormat("zh-CN", {
        month: "numeric",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit",
    }).format(new Date(value));
}
