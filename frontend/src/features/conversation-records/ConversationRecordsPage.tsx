import { useCallback, useEffect, useState } from "react";
import { Download, Eye, FileSpreadsheet, MessageSquareText, X } from "lucide-react";
import { ApiError, request, requestFile } from "@/shared/api/client";
import type { Assistant } from "@/shared/types/customer-service";

type ExportMessage = { id: string; role: string; content: string; createdAt?: string | null };
type HumanExportMessage = { messageId: string; senderRole: string; content: string; createdAt: string };
type ConversationRecord = {
    conversationId: string;
    bventId?: string | null;
    aiMessages: ExportMessage[];
    humanMessages: HumanExportMessage[];
    humanMessagesTruncated: boolean;
    surveyStatus?: string | null;
    respondedAt?: string | null;
};
type ExportPage = { conversations: ConversationRecord[]; hasMore: boolean; page: number; size: number };

function displayTime(value?: string | null): string {
    if (!value) return "—";
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? "—" : date.toLocaleString("zh-CN", { hour12: false });
}

function firstQuestion(record: ConversationRecord): string {
    return (
        record.aiMessages.find((message) => message.role.toLowerCase() === "user" && message.content)?.content ||
        "尚未提问"
    );
}

function lastMessageTime(record: ConversationRecord): string | null {
    const humanTime = record.humanMessages.at(-1)?.createdAt;
    return humanTime || record.aiMessages.at(-1)?.createdAt || record.respondedAt || null;
}

export default function ConversationRecordsPage({ assistant }: { assistant: Assistant }) {
    const assistantId = assistant.assistantConfigId || assistant.configId || assistant.id;
    const [page, setPage] = useState(1);
    const [size, setSize] = useState(20);
    const [records, setRecords] = useState<ConversationRecord[]>([]);
    const [hasMore, setHasMore] = useState(false);
    const [selected, setSelected] = useState<ConversationRecord | null>(null);
    const [loading, setLoading] = useState(true);
    const [exporting, setExporting] = useState(false);
    const [error, setError] = useState("");
    const [notice, setNotice] = useState("");

    const load = useCallback(async () => {
        setLoading(true);
        setError("");
        try {
            const query = new URLSearchParams({
                assistantConfigId: assistantId,
                page: String(page),
                size: String(size),
                format: "json",
            });
            const result = await request<ExportPage>(`/customer-service/exports/conversations?${query}`);
            setRecords(result.conversations || []);
            setHasMore(result.hasMore === true);
        } catch (reason) {
            setRecords([]);
            setHasMore(false);
            setError(reason instanceof Error ? reason.message : "会话记录读取失败");
        } finally {
            setLoading(false);
        }
    }, [assistantId, page, size]);

    useEffect(() => {
        void load();
    }, [load]);

    const exportAll = async () => {
        if (exporting) return;
        setExporting(true);
        setNotice("");
        try {
            const query = new URLSearchParams({ assistantConfigId: assistantId, format: "xlsx", all: "true" });
            const result = await requestFile(`/customer-service/exports/conversations?${query}`);
            const url = URL.createObjectURL(result.blob);
            const link = document.createElement("a");
            link.href = url;
            link.download = `会话记录-${assistant.name.replace(/[\\/:*?"<>|]/g, "_")}.xlsx`;
            document.body.appendChild(link);
            link.click();
            link.remove();
            window.setTimeout(() => URL.revokeObjectURL(url), 1000);
            const count = Number(result.headers.get("X-Exported-Count") || 0);
            setNotice(
                result.headers.get("X-Has-More") === "true"
                    ? `已导出前 ${count} 条记录；超过 20,000 条上限，请缩小范围后再导出。`
                    : `Excel 导出完成，共 ${count} 条会话。`,
            );
        } catch (reason) {
            setError(
                reason instanceof ApiError
                    ? reason.message
                    : reason instanceof Error
                      ? reason.message
                      : "Excel 导出失败",
            );
        } finally {
            setExporting(false);
        }
    };

    return (
        <main className="agent-page records-page">
            <header className="agent-page-header">
                <div>
                    <div className="eyebrow">会话分析</div>
                    <h1>会话记录</h1>
                    <p className="page-subtitle">
                        查看 AI 与人工客服历史消息及评价，可下载最多 20,000 条会话的 Excel 工作簿。
                    </p>
                </div>
                <div className="page-header-actions">
                    <button
                        className="button button--primary"
                        type="button"
                        disabled={loading || exporting}
                        onClick={() => void exportAll()}
                    >
                        <Download size={15} /> {exporting ? "正在生成 Excel" : "导出 Excel"}
                    </button>
                </div>
            </header>
            {error && (
                <div className="page-alert" role="alert">
                    {error}
                </div>
            )}
            {notice && (
                <div className="page-success" role="status">
                    {notice}
                </div>
            )}
            <section className="records-panel" aria-label="会话记录列表">
                <div className="records-panel-head">
                    <div>
                        <strong>{assistant.name}</strong>
                        <span>按会话创建时间分页</span>
                    </div>
                    <label>
                        每页
                        <select
                            value={size}
                            onChange={(event) => {
                                setPage(1);
                                setSize(Number(event.target.value));
                            }}
                        >
                            <option value={10}>10</option>
                            <option value={20}>20</option>
                            <option value={50}>50</option>
                        </select>
                        条
                    </label>
                </div>
                <div className="records-table-wrap">
                    <table className="records-table">
                        <thead>
                            <tr>
                                <th>会话 ID</th>
                                <th>首条提问</th>
                                <th>接待方式</th>
                                <th>AI 评价</th>
                                <th>最后消息时间</th>
                                <th>详情</th>
                            </tr>
                        </thead>
                        <tbody>
                            {records.map((record) => {
                                const hasHuman = record.humanMessages.length > 0;
                                const survey = record.surveyStatus || "未评价";
                                return (
                                    <tr key={record.conversationId}>
                                        <td title={record.conversationId}>{record.conversationId}</td>
                                        <td className="records-question" title={firstQuestion(record)}>
                                            {firstQuestion(record)}
                                        </td>
                                        <td>
                                            <span
                                                className={hasHuman ? "record-pill record-pill--human" : "record-pill"}
                                            >
                                                {hasHuman ? "人工客服" : "AI 咨询"}
                                            </span>
                                        </td>
                                        <td>{survey}</td>
                                        <td>{displayTime(lastMessageTime(record))}</td>
                                        <td>
                                            <button
                                                className="button button--quiet button--small"
                                                type="button"
                                                onClick={() => setSelected(record)}
                                            >
                                                <Eye size={14} /> 查看
                                            </button>
                                        </td>
                                    </tr>
                                );
                            })}
                            {!loading && records.length === 0 && (
                                <tr>
                                    <td colSpan={6} className="records-empty">
                                        暂无会话记录
                                    </td>
                                </tr>
                            )}
                            {loading && (
                                <tr>
                                    <td colSpan={6} className="records-empty">
                                        正在读取会话…
                                    </td>
                                </tr>
                            )}
                        </tbody>
                    </table>
                </div>
                <footer className="records-pagination">
                    <span>
                        第 {page} 页 · 当前 {records.length} 条{hasMore ? " · 还有下一页" : " · 已到末页"}
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
                        aria-labelledby="record-detail-title"
                    >
                        <header>
                            <div>
                                <span>
                                    <MessageSquareText size={16} /> 对话详情
                                </span>
                                <h2 id="record-detail-title">{selected.conversationId}</h2>
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
                        {selected.humanMessagesTruncated && (
                            <p className="records-note">
                                人工消息达到安全上限，当前详情仅展示已返回的部分；完整情况会标记在导出工作簿中。
                            </p>
                        )}
                        <div className="records-transcript">
                            {[
                                ...selected.aiMessages.map((message) => ({
                                    id: message.id,
                                    role: message.role === "user" ? "访客" : "智伴",
                                    content: message.content,
                                    createdAt: message.createdAt,
                                    kind: message.role === "user" ? "visitor" : "ai",
                                })),
                                ...selected.humanMessages.map((message) => ({
                                    id: message.messageId,
                                    role: message.senderRole === "AGENT" ? "人工客服" : "访客",
                                    content: message.content,
                                    createdAt: message.createdAt,
                                    kind: message.senderRole === "AGENT" ? "agent" : "visitor",
                                })),
                            ]
                                .sort(
                                    (left, right) =>
                                        new Date(left.createdAt || 0).getTime() -
                                        new Date(right.createdAt || 0).getTime(),
                                )
                                .map((message) => (
                                    <article
                                        className={`records-message records-message--${message.kind}`}
                                        key={message.id}
                                    >
                                        <div>
                                            <strong>{message.role}</strong>
                                            <time>{displayTime(message.createdAt)}</time>
                                        </div>
                                        <p>{message.content || "—"}</p>
                                    </article>
                                ))}
                            {selected.aiMessages.length === 0 && selected.humanMessages.length === 0 && (
                                <p className="records-empty">尚未提问</p>
                            )}
                        </div>
                    </section>
                </div>
            )}
            <div className="records-footer">
                <FileSpreadsheet size={14} />{" "}
                导出文件包含会话摘要、消息明细和导出范围说明，不包含联系人邮箱或内部提示词。
            </div>
        </main>
    );
}
