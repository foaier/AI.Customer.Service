import { useCallback, useEffect, useRef, useState } from "react";
import type { FormEvent, KeyboardEvent } from "react";
import {
    Check,
    CheckCheck,
    Clock3,
    Headphones,
    Mail,
    MessageSquareText,
    Plus,
    Send,
    ShieldCheck,
    Sparkles,
    UserRound,
    X,
} from "lucide-react";
import { getAccessToken } from "./auth";
import { post, realtimeUrl, request, streamChat } from "./api";
import type { Assistant, Episode, HumanMessage, Proposal, Waiting } from "./types";

type ChatMessage = { id: string; role: "user" | "assistant" | "agent" | "system"; text: string; pending?: boolean };
type FollowupForm = {
    episodeId?: string;
    ticketId?: string;
    origin?: string;
    state?: string;
    contactName?: string;
    content?: string;
    submittedAt?: string;
};
type FollowupSnapshot = FollowupForm & { form?: FollowupForm };
type Survey = {
    surveyId: string;
    status: "OPEN" | "SATISFIED" | "DISSATISFIED" | "EXPIRED";
    handoffChoice: "PENDING" | "OPTED_IN" | "DECLINED" | "NOT_APPLICABLE";
    version: number;
    expiresAt: string;
};

const openStatuses = ["REQUESTED", "CLAIMED", "ACTIVE"];

export default function Visitor({ assistant }: { assistant: Assistant }) {
    const assistantId = assistant.assistantConfigId || assistant.configId || assistant.id;
    const [conversationId, setConversationId] = useState("");
    const [messages, setMessages] = useState<ChatMessage[]>([]);
    const [episode, setEpisode] = useState<Episode | null>(null);
    const [waiting, setWaiting] = useState<Waiting | null>(null);
    const [followup, setFollowup] = useState<FollowupForm | null>(null);
    const [proposals, setProposals] = useState<Proposal[]>([]);
    const [survey, setSurvey] = useState<Survey | null>(null);
    const [input, setInput] = useState("");
    const [email, setEmail] = useState("");
    const [contactName, setContactName] = useState("");
    const [formContent, setFormContent] = useState("");
    const [sending, setSending] = useState(false);
    const [loading, setLoading] = useState(true);
    const [notice, setNotice] = useState("");
    const [error, setError] = useState("");
    const [showFollowup, setShowFollowup] = useState(false);
    const [showHandoffForm, setShowHandoffForm] = useState(false);
    const [handoffProposal, setHandoffProposal] = useState<Proposal | null>(null);
    const [pageVisible, setPageVisible] = useState(() => document.visibilityState === "visible");
    const listRef = useRef<HTMLDivElement>(null);
    const sequence = useRef(0);
    const messageNonce = useRef(0);
    const initializedAssistant = useRef("");
    const surveyReceiptSent = useRef("");
    const isHuman = Boolean(episode && openStatuses.includes(episode.status));

    const addHumanMessages = useCallback((items: HumanMessage[]) => {
        if (!items.length) return;
        sequence.current = Math.max(sequence.current, ...items.map((item) => item.seq));
        setMessages((current) => {
            const known = new Set(current.map((item) => item.id));
            return current.concat(
                items
                    .filter((item) => !known.has(item.messageId))
                    .map((item) => ({
                        id: item.messageId,
                        role: item.senderRole === "AGENT" ? ("agent" as const) : ("user" as const),
                        text: item.content,
                    })),
            );
        });
    }, []);

    const loadEpisode = useCallback(
        async (id: string) => {
            const latest = await request<{ episode: Episode | null }>(
                `/customer-service/conversations/${encodeURIComponent(id)}/handoffs/latest`,
            );
            const current = latest.episode;
            setEpisode(current);
            const [pending, waitState, followupState, surveyState] = await Promise.all([
                request<{ proposals: Proposal[] }>(
                    `/customer-service/conversations/${encodeURIComponent(id)}/handoff-proposals/pending`,
                ),
                current
                    ? request<Waiting>(`/customer-service/conversations/${encodeURIComponent(id)}/waiting`)
                    : Promise.resolve(null),
                request<FollowupSnapshot>(`/customer-service/conversations/${encodeURIComponent(id)}/followup-form`),
                request<{ survey: Survey | null }>(`/customer-service/conversations/${encodeURIComponent(id)}/survey`),
            ]);
            setProposals(pending.proposals || []);
            setWaiting(waitState);
            const submittedForm = followupState.form || followupState;
            setFollowup(Object.keys(submittedForm).length ? submittedForm : null);
            setSurvey(surveyState.survey);
            if (current && openStatuses.includes(current.status)) {
                const timeline = await request<{ messages: HumanMessage[] }>(
                    `/customer-service/conversations/${encodeURIComponent(id)}/messages?afterSeq=0&limit=100`,
                );
                addHumanMessages(timeline.messages || []);
            }
        },
        [addHumanMessages],
    );

    const initialize = useCallback(async () => {
        setLoading(true);
        setError("");
        try {
            const params = new URLSearchParams(window.location.search);
            const stored = sessionStorage.getItem(`cs.conversation.${assistantId}`);
            let id = params.get("conversationId") || stored || "";
            if (!id) {
                const result = await request<{ sessionId: string }>(
                    `/agent/sessionId?assistantConfigId=${encodeURIComponent(assistantId)}`,
                );
                id = result.sessionId;
            }
            sessionStorage.setItem(`cs.conversation.${assistantId}`, id);
            setConversationId(id);
            const history = await request<{ messages: { id: string; role: string; content: string }[] }>(
                `/agent/history_detail?sessionId=${encodeURIComponent(id)}&assistantConfigId=${encodeURIComponent(assistantId)}`,
            );
            setMessages(
                (history.messages || []).map((item) => ({
                    id: item.id,
                    role: item.role === "user" ? "user" : "assistant",
                    text: item.content,
                })),
            );
            sequence.current = 0;
            await loadEpisode(id);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "无法读取会话");
        } finally {
            setLoading(false);
        }
    }, [assistantId, loadEpisode]);

    useEffect(() => {
        if (initializedAssistant.current === assistantId) return;
        initializedAssistant.current = assistantId;
        void initialize();
    }, [assistantId, initialize]);

    useEffect(() => {
        const updateVisibility = () => setPageVisible(document.visibilityState === "visible");
        document.addEventListener("visibilitychange", updateVisibility);
        return () => document.removeEventListener("visibilitychange", updateVisibility);
    }, []);

    useEffect(() => {
        listRef.current?.scrollTo({ top: listRef.current.scrollHeight, behavior: "smooth" });
    }, [messages, proposals, notice]);

    useEffect(() => {
        if (!pageVisible || !survey || survey.status !== "OPEN" || surveyReceiptSent.current === survey.surveyId) return;
        surveyReceiptSent.current = survey.surveyId;
        post(`/customer-service/conversations/${encodeURIComponent(conversationId)}/survey/${encodeURIComponent(survey.surveyId)}/receipt`, {})
            .catch(() => { surveyReceiptSent.current = ""; });
    }, [conversationId, pageVisible, survey]);

    useEffect(() => {
        if (!conversationId || !episode || !openStatuses.includes(episode.status)) return;
        let disposed = false;
        let socket: WebSocket | null = null;
        let reconnectTimer = 0;
        let retry = 0;
        const connect = async () => {
            try {
                const token = await getAccessToken();
                if (disposed) return;
                socket = new WebSocket(realtimeUrl({
                    authorization: `Bearer ${token}`,
                    agent: false,
                    conversationId,
                    topOrgId: assistant.topOrgId,
                    assistantConfigId: assistantId,
                    afterSeq: sequence.current,
                }));
                socket.onopen = () => {
                    retry = 0;
                };
                socket.onmessage = (event) => {
                    const frame = JSON.parse(String(event.data));
                    if (frame.type !== "snapshot") return;
                    const data = frame.data as {
                        episode?: Episode;
                        waiting?: Waiting;
                        proposals?: Proposal[];
                        timeline?: { messages: HumanMessage[] };
                    };
                    if (data.episode) setEpisode(data.episode);
                    if (data.waiting) setWaiting(data.waiting);
                    if (data.proposals) setProposals(data.proposals);
                    if (data.timeline?.messages) addHumanMessages(data.timeline.messages);
                };
                socket.onclose = () => {
                    if (!disposed) reconnectTimer = window.setTimeout(connect, Math.min(1000 * 2 ** retry++, 15000));
                };
            } catch {
                if (!disposed) reconnectTimer = window.setTimeout(connect, Math.min(1000 * 2 ** retry++, 15000));
            }
        };
        void connect();
        return () => {
            disposed = true;
            window.clearTimeout(reconnectTimer);
            socket?.close();
        };
    }, [conversationId, episode?.status, assistant.topOrgId, assistantId, addHumanMessages]);

    const resetSession = async () => {
        setMessages([]);
        setEpisode(null);
        setWaiting(null);
        setFollowup(null);
        setProposals([]);
        setSurvey(null);
        setShowHandoffForm(false);
        setHandoffProposal(null);
        setNotice("");
        setError("");
        sequence.current = 0;
        sessionStorage.removeItem(`cs.conversation.${assistantId}`);
        window.history.replaceState(
            null,
            "",
            window.location.pathname + `?assistantConfigId=${encodeURIComponent(assistantId)}`,
        );
        initializedAssistant.current = assistantId;
        await initialize();
    };

    const send = async (event: FormEvent) => {
        event.preventDefault();
        const text = input.trim();
        if (!text || sending || !conversationId) return;
        setInput("");
        setError("");
        setSending(true);
        const userId = `local-user-${Date.now()}`;
        const replyId = `local-reply-${++messageNonce.current}`;
        setMessages((current) => [
            ...current,
            { id: userId, role: "user", text },
            { id: replyId, role: isHuman ? "system" : "assistant", text: "", pending: !isHuman },
        ]);
        try {
            if (isHuman) {
                const saved = await post<HumanMessage>(
                    `/customer-service/conversations/${encodeURIComponent(conversationId)}/messages`,
                    {
                        clientMsgId: `visitor_${crypto.randomUUID().replaceAll("-", "")}`,
                        content: text,
                    },
                );
                sequence.current = Math.max(sequence.current, saved.seq);
                setMessages((current) => {
                    const received = current.some((message) => message.id === saved.messageId);
                    return current.flatMap((message) => {
                        if (message.id !== userId) return [message];
                        return received ? [] : [{ ...message, id: saved.messageId, text: saved.content || text }];
                    });
                });
                await loadEpisode(conversationId);
            } else {
                await streamChat(
                    "/agent/chatStream",
                    {
                        sessionId: conversationId,
                        userInput: text,
                        businessType: "event-assistant",
                        extensions: {
                            "event-assistant": { assistantConfigId: assistantId, bventId: assistant.bventId || "" },
                        },
                        isReconnect: false,
                        speechEnabled: false,
                    },
                    (chunk) => {
                        setMessages((current) =>
                            current.map((item) => (item.id === replyId ? { ...item, text: item.text + chunk } : item)),
                        );
                    },
                    (card) => {
                        const value = card as { status?: string; data?: Proposal & Survey; cardType?: string; content?: { data?: Survey } };
                        if (value.status === "handoff.proposed" && value.data)
                            setProposals((current) =>
                                current.some((item) => item.proposalId === value.data!.proposalId)
                                    ? current
                                    : [...current, value.data!],
                            );
                        if (value.status === "handoff.email_required") {
                            setHandoffProposal(null);
                            setShowHandoffForm(true);
                            setNotice("请先填写联系邮箱，再提交人工申请。");
                        }
                        const surveyCard = value.status === "ai-survey" ? value.data : value.cardType === "ai-survey" ? value.content?.data : undefined;
                        if (surveyCard?.surveyId) setSurvey(surveyCard as Survey);
                    },
                );
                await loadEpisode(conversationId);
            }
            setMessages((current) =>
                current.map((item) =>
                    item.id === replyId
                        ? {
                              ...item,
                              pending: false,
                              text: item.text || (isHuman ? "消息已发送，等待客服回复。" : "本轮已完成。"),
                          }
                        : item,
                ),
            );
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "消息发送失败");
            setMessages((current) =>
                current.map((item) =>
                    item.id === replyId ? { ...item, pending: false, text: "这条消息暂时没有发送成功。" } : item,
                ),
            );
        } finally {
            setSending(false);
        }
    };

    const requestAgent = async (contactEmail: string) => {
        if (!conversationId || sending) return;
        setSending(true);
        setError("");
        const storageKey = `cs.handoff.${assistantId}.${conversationId}`;
        const key = sessionStorage.getItem(storageKey) || `handoff_${crypto.randomUUID().replaceAll("-", "")}`;
        sessionStorage.setItem(storageKey, key);
        try {
            const result = await post<Episode>(
                `/customer-service/conversations/${encodeURIComponent(conversationId)}/handoffs`,
                { requestId: key, email: contactEmail.trim() },
            );
            sessionStorage.removeItem(storageKey);
            setEpisode(result);
            setShowHandoffForm(false);
            setNotice("人工申请已登记。等待期间你可以继续补充信息。");
            await loadEpisode(conversationId);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "申请失败");
        } finally {
            setSending(false);
        }
    };

    const decideProposal = async (proposal: Proposal, decision: "CONFIRM" | "DISMISS", contactEmail?: string) => {
        if (decision === "CONFIRM" && !contactEmail) {
            setHandoffProposal(proposal);
            setShowHandoffForm(true);
            return;
        }
        const storageKey = `cs.proposal.${assistantId}.${conversationId}.${proposal.proposalId}`;
        const requestId = sessionStorage.getItem(storageKey) || `proposal_${crypto.randomUUID().replaceAll("-", "")}`;
        sessionStorage.setItem(storageKey, requestId);
        try {
            const result = await post<Episode>(
                `/customer-service/conversations/${encodeURIComponent(conversationId)}/handoff-proposals/${encodeURIComponent(proposal.proposalId)}/decision`,
                {
                    requestId,
                    expectedProposalVersion: proposal.version,
                    decision,
                    email: decision === "CONFIRM" ? contactEmail?.trim() : null,
                },
            );
            sessionStorage.removeItem(storageKey);
            setProposals((current) => current.filter((item) => item.proposalId !== proposal.proposalId));
            setShowHandoffForm(false);
            setHandoffProposal(null);
            if (decision === "CONFIRM") setEpisode(result);
            setNotice(decision === "CONFIRM" ? "已确认人工协助申请。" : "已关闭这条人工建议。");
            await loadEpisode(conversationId);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "操作未完成");
        }
    };

    const submitHandoff = (event: FormEvent) => {
        event.preventDefault();
        if (handoffProposal) void decideProposal(handoffProposal, "CONFIRM", email);
        else void requestAgent(email);
    };

    const answerSurvey = async (answer: "SATISFIED" | "DISSATISFIED") => {
        if (!survey) return;
        try {
            const updated = await post<Survey>(
                `/customer-service/conversations/${encodeURIComponent(conversationId)}/survey/${encodeURIComponent(survey.surveyId)}/answer`,
                { expectedVersion: survey.version, answer },
            );
            setSurvey(updated);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "评价提交失败");
        }
    };

    const chooseSurveyHandoff = async (choice: "OPTED_IN" | "DECLINED") => {
        if (!survey) return;
        try {
            await post(
                `/customer-service/conversations/${encodeURIComponent(conversationId)}/survey/${encodeURIComponent(survey.surveyId)}/handoff-choice`,
                { expectedVersion: survey.version, choice, email: choice === "OPTED_IN" ? email.trim() : null },
            );
            if (choice === "OPTED_IN") setNotice("人工申请已提交，正在等待客服接单。");
            await loadEpisode(conversationId);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "人工选择未完成");
        }
    };

    const cancelHandoff = async () => {
        if (!episode) return;
        try {
            await post(`/customer-service/conversations/${encodeURIComponent(conversationId)}/reception/cancel`, {
                requestId: `cancel_${crypto.randomUUID().replaceAll("-", "")}`,
                expectedVersion: episode.version,
            });
            setNotice("等待已取消，你可以继续向 AI 咨询。");
            await loadEpisode(conversationId);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "取消未完成");
            await loadEpisode(conversationId);
        }
    };

    const chooseWaiting = async (action: "CONTINUE" | "CONTACT" | "EMAIL") => {
        if (!waiting) return;
        try {
            const result = await post<Waiting>(
                `/customer-service/conversations/${encodeURIComponent(conversationId)}/waiting`,
                {
                    expectedVersion: waiting.version,
                    action,
                    email: action === "CONTINUE" ? null : email,
                },
            );
            setWaiting(result);
            setNotice(action === "EMAIL" ? "已切换为邮件跟进。" : "联系方式已保存。");
            if (action === "EMAIL") await loadEpisode(conversationId);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "等待设置未完成");
        }
    };

    const submitFollowup = async (event: FormEvent) => {
        event.preventDefault();
        if (!waiting) return;
        try {
            await post(`/customer-service/conversations/${encodeURIComponent(conversationId)}/followup-form`, {
                requestId: `form_${crypto.randomUUID().replaceAll("-", "")}`,
                expectedVersion: waiting.version,
                email,
                contactName: contactName || null,
                content: formContent,
            });
            setNotice("留言已提交。客服认领后会按你留下的邮箱联系。");
            setShowFollowup(false);
            setFormContent("");
            await loadEpisode(conversationId);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "留言提交失败");
        }
    };

    const onComposerKey = (event: KeyboardEvent<HTMLTextAreaElement>) => {
        if (event.key === "Enter" && !event.shiftKey) {
            event.preventDefault();
            event.currentTarget.form?.requestSubmit();
        }
    };

    const statusLabel =
        waiting?.state === "FOLLOW_UP"
            ? "邮件跟进已登记"
            : episode
              ? {
                    REQUESTED: "等待客服接入",
                    CLAIMED: "客服已接单",
                    ACTIVE: "人工服务中",
                    RESOLVED: "本次服务已结束",
                    CANCELLED: "等待已结束",
                }[episode.status]
              : "AI 助手在线";
    const canSubmitFollowup = Boolean(
        waiting && ["WAITING", "BUSY", "CONTINUING", "NEED_EMAIL"].includes(waiting.state),
    );

    return (
        <main className="visitor-page">
            <section className="visitor-heading">
                <div className="visitor-heading-main">
                    <div className="assistant-avatar">
                        <Sparkles size={19} />
                    </div>
                    <div>
                        <p className="eyebrow">{assistant.name || "在线服务"}</p>
                        <h1>有什么可以帮你？</h1>
                    </div>
                </div>
                <div className={`conversation-status ${isHuman ? "conversation-status--human" : ""}`}>
                    <span />
                    {statusLabel}
                </div>
            </section>
            {((episode?.autoCloseAt && episode.status === "ACTIVE") || (episode?.status === "RESOLVED" && episode.resolutionReason)) && (
                <p className="visitor-state-note">
                    {episode.status === "ACTIVE"
                        ? `若人工接待期间访客 5 分钟未回复，将于 ${formatDateTime(episode.autoCloseAt!)} 自动结束。`
                        : episode.resolutionReason === "VISITOR_INACTIVE_5M"
                          ? "本次人工接待因访客 5 分钟未回复而自动结束，你可以继续向 AI 咨询。"
                          : "客服已结束本次接待，你可以继续向 AI 咨询。"}
                </p>
            )}

            <div className="visitor-layout">
                <section className="chat-panel" aria-label="在线咨询">
                    <div className="chat-panel-toolbar">
                        <div>
                            <span className="toolbar-kicker">当前咨询</span>
                            <span className="conversation-ref">
                                {conversationId ? `#${conversationId.slice(-8)}` : "正在创建会话"}
                            </span>
                        </div>
                        <button
                            className="icon-button"
                            type="button"
                            onClick={() => void resetSession()}
                            title="开始新的咨询"
                            aria-label="开始新的咨询"
                        >
                            <Plus size={18} />
                        </button>
                    </div>
                    <div ref={listRef} className="chat-thread" role="log" aria-live="polite">
                        {loading ? (
                            <div className="thread-loading">
                                <span className="loader" />
                                正在恢复对话
                            </div>
                        ) : messages.length === 0 ? (
                            <div className="thread-empty">
                                <div className="assistant-avatar assistant-avatar--large">
                                    <Sparkles size={21} />
                                </div>
                                <h2>你好，我是{assistant.name || "在线助手"}</h2>
                                <p>可以咨询展会服务、入场安排或现场指引。需要人工帮助时，也可以随时联系服务人员。</p>
                                <div className="suggestion-row">
                                    {["如何办理入场证？", "展馆开放时间是什么？", "我想联系人工客服"].map((value) => (
                                        <button key={value} type="button" onClick={() => setInput(value)}>
                                            {value}
                                        </button>
                                    ))}
                                </div>
                            </div>
                        ) : (
                            messages.map((message) => (
                                <div key={message.id} className={`chat-row chat-row--${message.role}`}>
                                    {message.role !== "user" && (
                                        <span className={`message-avatar message-avatar--${message.role}`}>
                                            {message.role === "agent" ? (
                                                <Headphones size={15} />
                                            ) : message.role === "system" ? (
                                                <Clock3 size={14} />
                                            ) : (
                                                <Sparkles size={14} />
                                            )}
                                        </span>
                                    )}
                                    <div className="message-stack">
                                        <span className="message-author">
                                            {message.role === "user"
                                                ? "你"
                                                : message.role === "agent"
                                                  ? "人工客服"
                                                  : message.role === "system"
                                                    ? "服务状态"
                                                    : assistant.name || "AI 助手"}
                                        </span>
                                        <div className={`message-bubble message-bubble--${message.role}`}>
                                            {message.text ||
                                                (message.pending && (
                                                    <span className="typing-dots">
                                                        <i />
                                                        <i />
                                                        <i />
                                                    </span>
                                                ))}
                                        </div>
                                    </div>
                                    {message.role === "user" && (
                                        <span className="message-avatar message-avatar--user">
                                            <UserRound size={15} />
                                        </span>
                                    )}
                                </div>
                            ))
                        )}
                        {proposals.map((proposal) => (
                            <div className="proposal-inline" key={proposal.proposalId}>
                                <div className="proposal-icon">
                                    <Headphones size={17} />
                                </div>
                                <div className="proposal-copy">
                                    <strong>需要人工协助吗？</strong>
                                    <span>{proposal.userFacingReason}</span>
                                </div>
                                <button
                                    className="button button--small button--primary"
                                    type="button"
                                    onClick={() => void decideProposal(proposal, "CONFIRM")}
                                >
                                    联系人工
                                </button>
                                <button
                                    className="icon-button"
                                    type="button"
                                    aria-label="暂不需要"
                                    title="暂不需要"
                                    onClick={() => void decideProposal(proposal, "DISMISS")}
                                >
                                    <X size={16} />
                                </button>
                            </div>
                        ))}
                        {survey?.status === "OPEN" && new Date(survey.expiresAt).getTime() > Date.now() && (
                            <div className="survey-inline" key={survey.surveyId}>
                                <strong>本次服务是否解决了您的问题？</strong>
                                <div className="survey-actions">
                                    <button type="button" onClick={() => void answerSurvey("SATISFIED")}>
                                        <Check size={15} /> 已解决
                                    </button>
                                    <button type="button" onClick={() => void answerSurvey("DISSATISFIED")}>
                                        还需要帮助
                                    </button>
                                </div>
                            </div>
                        )}
                        {survey?.status === "DISSATISFIED" && survey.handoffChoice === "PENDING" && (
                            <div className="survey-inline survey-inline--choice">
                                <strong>需要客服继续协助吗？</strong>
                                <label htmlFor="survey-contact-email">联系邮箱</label>
                                <input
                                    id="survey-contact-email"
                                    type="email"
                                    required
                                    maxLength={254}
                                    value={email}
                                    onChange={(event) => setEmail(event.target.value)}
                                    placeholder="name@example.com"
                                />
                                <div className="survey-actions">
                                    <button type="button" disabled={!email.trim()} onClick={() => void chooseSurveyHandoff("OPTED_IN")}>
                                        联系人工
                                    </button>
                                    <button type="button" onClick={() => void chooseSurveyHandoff("DECLINED")}>
                                        暂不需要
                                    </button>
                                </div>
                            </div>
                        )}
                        {survey?.status === "SATISFIED" && (
                            <div className="survey-inline survey-inline--complete">
                                <Check size={15} /> 感谢反馈
                            </div>
                        )}
                        {notice && (
                            <div className="thread-notice">
                                <Check size={15} />
                                {notice}
                            </div>
                        )}
                    </div>
                    <form className="chat-composer" onSubmit={send}>
                        {error && (
                            <div className="inline-error" role="alert">
                                {error}
                            </div>
                        )}
                        <label className="sr-only" htmlFor="visitor-message">
                            输入你的问题
                        </label>
                        <textarea
                            id="visitor-message"
                            value={input}
                            maxLength={4000}
                            rows={2}
                            onChange={(event) => setInput(event.target.value)}
                            onKeyDown={onComposerKey}
                            placeholder={isHuman ? "给人工客服发送消息…" : "描述一下你遇到的问题…"}
                            disabled={loading || sending || !conversationId}
                        />
                        <div className="composer-controls">
                            <span className="composer-hint">
                                <ShieldCheck size={13} /> 请勿发送密码或敏感证件信息
                            </span>
                            <div className="composer-submit">
                                <span>{input.length}/4000</span>
                                <button
                                    className="send-button"
                                    type="submit"
                                    disabled={!input.trim() || sending || loading}
                                    aria-label="发送消息"
                                    title="发送消息"
                                >
                                    <Send size={17} />
                                </button>
                            </div>
                        </div>
                    </form>
                </section>

                <aside className="visitor-rail">
                    <section className="rail-section rail-service-card">
                        <div className="rail-section-title">
                            <span className="rail-icon rail-icon--teal">
                                <Headphones size={17} />
                            </span>
                            <div>
                                <h2>人工服务</h2>
                                <span>与现场服务团队联系</span>
                            </div>
                        </div>
                        {isHuman ? (
                            <>
                                <div className="handoff-state">
                                    <span className="state-dot" />
                                    <div>
                                        <strong>{statusLabel}</strong>
                                        <span>
                                            {episode?.status === "REQUESTED"
                                                ? "接入后会在这里同步消息"
                                                : "本会话已开启实时消息"}
                                        </span>
                                    </div>
                                </div>
                                {waiting && (
                                    <div className="wait-meta">
                                        <Clock3 size={14} />
                                        <span>
                                            {waiting.state === "BUSY"
                                                ? "当前等待较久，客服会尽快接入"
                                                : "申请已进入接待队列"}
                                        </span>
                                    </div>
                                )}
                                {episode?.status === "REQUESTED" && (
                                    <button
                                        className="button button--quiet button--full"
                                        type="button"
                                        onClick={() => void cancelHandoff()}
                                    >
                                        取消等待，继续咨询 AI
                                    </button>
                                )}
                            </>
                        ) : waiting?.state === "FOLLOW_UP" ? (
                            <div className="handoff-state">
                                <span className="state-dot" />
                                <div>
                                    <strong>邮件跟进已登记</strong>
                                    <span>客服认领后会按已确认的邮箱联系你</span>
                                </div>
                            </div>
                        ) : (
                            <>
                                <p className="rail-copy">联系在线客服，继续处理需要人工协助的问题。</p>
                                {showHandoffForm ? (
                                    <form className="handoff-email-form" onSubmit={submitHandoff}>
                                        <label htmlFor="handoff-contact-email">联系邮箱</label>
                                        <input
                                            id="handoff-contact-email"
                                            type="email"
                                            required
                                            maxLength={254}
                                            value={email}
                                            onChange={(event) => setEmail(event.target.value)}
                                            placeholder="name@example.com"
                                            disabled={sending}
                                        />
                                        <button className="button button--primary button--full" type="submit" disabled={sending || loading}>
                                            <Headphones size={16} /> {handoffProposal ? "确认并联系人工" : "提交并联系人工"}
                                        </button>
                                        <button className="link-button" type="button" onClick={() => { setShowHandoffForm(false); setHandoffProposal(null); }}>
                                            取消
                                        </button>
                                    </form>
                                ) : (
                                    <button
                                        className="button button--primary button--full"
                                        type="button"
                                        disabled={sending || loading}
                                        onClick={() => { setHandoffProposal(null); setShowHandoffForm(true); }}
                                    >
                                        <Headphones size={16} /> 联系人工客服
                                    </button>
                                )}
                            </>
                        )}
                    </section>

                    {waiting && (isHuman || waiting.state === "NEED_EMAIL" || waiting.state === "FOLLOW_UP") && (
                        <section className="rail-section contact-card">
                            <div className="rail-section-title">
                                <span className="rail-icon rail-icon--amber">
                                    <Mail size={17} />
                                </span>
                                <div>
                                    <h2>邮件跟进</h2>
                                    <span>工作时间外也可留言</span>
                                </div>
                            </div>
                            {waiting.state === "FOLLOW_UP" ? (
                                <div className="followup-confirmation">
                                    <strong>{followup?.origin === "EMAIL" ? "已选择邮件跟进" : "留言已提交"}</strong>
                                    <span>{followup?.content || "客服认领后会按已确认的邮箱联系你。"}</span>
                                    {followup?.contactName && <small>称呼：{followup.contactName}</small>}
                                </div>
                            ) : canSubmitFollowup ? (
                                <>
                                    <label htmlFor="visitor-email">联系邮箱</label>
                                    <input
                                        id="visitor-email"
                                        type="email"
                                        maxLength={254}
                                        value={email}
                                        onChange={(event) => setEmail(event.target.value)}
                                        placeholder="name@example.com"
                                    />
                                    {isHuman && (
                                        <div className="contact-actions">
                                            <button
                                                className="button button--quiet button--small"
                                                type="button"
                                                disabled={!email}
                                                onClick={() => void chooseWaiting("CONTACT")}
                                            >
                                                保存邮箱
                                            </button>
                                            <button
                                                className="button button--quiet button--small"
                                                type="button"
                                                disabled={!email}
                                                onClick={() => void chooseWaiting("EMAIL")}
                                            >
                                                改为邮件
                                            </button>
                                        </div>
                                    )}
                                    <button
                                        className="link-button"
                                        type="button"
                                        onClick={() => setShowFollowup((value) => !value)}
                                    >
                                        <MessageSquareText size={14} />
                                        {showFollowup ? "收起留言表单" : "提交留言给客服"}
                                    </button>
                                    {showFollowup && (
                                        <form className="followup-form" onSubmit={submitFollowup}>
                                            <label htmlFor="visitor-contact-name">称呼（选填）</label>
                                            <input
                                                id="visitor-contact-name"
                                                maxLength={100}
                                                value={contactName}
                                                onChange={(event) => setContactName(event.target.value)}
                                            />
                                            <label htmlFor="visitor-followup-content">留言内容</label>
                                            <textarea
                                                id="visitor-followup-content"
                                                rows={4}
                                                maxLength={4000}
                                                required
                                                value={formContent}
                                                onChange={(event) => setFormContent(event.target.value)}
                                            />
                                            <button
                                                className="button button--primary button--full"
                                                type="submit"
                                                disabled={!email || !formContent.trim()}
                                            >
                                                提交留言
                                            </button>
                                        </form>
                                    )}
                                </>
                            ) : (
                                <div className="followup-confirmation">
                                    <strong>人工服务已接入</strong>
                                    <span>当前在线会话已由客服处理。</span>
                                </div>
                            )}
                        </section>
                    )}

                    <section className="rail-section service-notes">
                        <div className="rail-section-title">
                            <span className="rail-icon rail-icon--coral">
                                <Sparkles size={16} />
                            </span>
                            <div>
                                <h2>本次服务</h2>
                                <span>安全与接待状态</span>
                            </div>
                        </div>
                        <div className="service-note-row">
                            <span>当前助手</span>
                            <strong>{assistant.name || assistantId}</strong>
                        </div>
                        <div className="service-note-row">
                            <span>组织</span>
                            <strong>{assistant.topOrgId}</strong>
                        </div>
                        <div className="privacy-note">
                            <ShieldCheck size={14} />
                            <span>会话仅对当前账号和授权客服开放。</span>
                        </div>
                    </section>
                    <div className="visitor-footer">
                        <span>AI 回答仅供参考</span>
                        <span>·</span>
                        <span>需要时可转接人工</span>
                    </div>
                </aside>
            </div>
            <footer className="visitor-page-footer">
                <span>
                    <CheckCheck size={14} /> 连接已加密
                </span>
                <span>由 AI.Customer.Service 提供支持</span>
            </footer>
        </main>
    );
}

function formatDateTime(value: string) {
    return new Intl.DateTimeFormat("zh-CN", {
        month: "numeric",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit",
    }).format(new Date(value));
}
