import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import {
    CalendarDays,
    Check,
    CircleAlert,
    Clock3,
    Plus,
    RefreshCw,
    Save,
    Search,
    Trash2,
    UsersRound,
} from "lucide-react";
import { post, put, request } from "@/shared/api/client";
import type { Assistant, Calendar, Group, Routing } from "@/shared/types/customer-service";
import ChatNotifications from "@/features/settings/components/ChatNotifications";

const categories = [
    { id: "EXHIBITOR", label: "展商咨询", accent: "teal" },
    { id: "VISITOR", label: "观众咨询", accent: "amber" },
    { id: "FINANCE", label: "财务咨询", accent: "coral" },
    { id: "OTHER", label: "其他咨询", accent: "green" },
];

export default function Settings({ assistant }: { assistant: Assistant }) {
    const assistantId = assistant.assistantConfigId || assistant.configId || assistant.id;
    const [routing, setRouting] = useState<Routing | null>(null);
    const [calendar, setCalendar] = useState<Calendar | null>(null);
    const [routeDraft, setRouteDraft] = useState<Record<string, string>>({});
    const [directoryGroups, setDirectoryGroups] = useState<{ id: string; name: string }[]>([]);
    const [directoryGroupId, setDirectoryGroupId] = useState("");
    const [directoryAccounts, setDirectoryAccounts] = useState<{ accountId: string; displayName: string }[]>([]);
    const [selectedAccount, setSelectedAccount] = useState("");
    const [targetCategory, setTargetCategory] = useState("EXHIBITOR");
    const [directorySearch, setDirectorySearch] = useState("");
    const [overrideDate, setOverrideDate] = useState("");
    const [overrideOpen, setOverrideOpen] = useState(false);
    const [previewText, setPreviewText] = useState("");
    const [previewResult, setPreviewResult] = useState("");
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [error, setError] = useState("");
    const [notice, setNotice] = useState("");

    const load = useCallback(async () => {
        setLoading(true);
        try {
            const [routingState, calendarState] = await Promise.all([
                request<Routing>(`/customer-service/configuration`),
                request<Calendar>(`/customer-service/configuration/calendar`),
            ]);
            setRouting({
                ...routingState,
                groups: routingState.groups.map((group) =>
                    group.category === "FINANCE" && ["媒体组", "媒体客服"].includes(group.name)
                        ? { ...group, name: "财务客服" }
                        : group,
                ),
            });
            setCalendar(calendarState);
            const existing = routingState.routes.find((item) => item.assistantConfigId === assistantId);
            setRouteDraft(
                Object.fromEntries(
                    categories.map((category) => [
                        category.id,
                        existing?.groups[category.id] ||
                            routingState.groups.find((item) => item.category === category.id)?.id ||
                            "",
                    ]),
                ),
            );
            setError("");
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "配置读取失败");
        } finally {
            setLoading(false);
        }
    }, [assistantId]);

    useEffect(() => {
        void load();
    }, [load]);

    const loadDirectoryGroups = async () => {
        setSaving(true);
        try {
            const query = new URLSearchParams({ limit: "100" });
            if (directorySearch.trim()) query.set("keyword", directorySearch.trim());
            const result = await request<{ groups: { id: string; name: string }[]; status: string }>(
                `/customer-service/configuration/directory/groups?${query}`,
            );
            if (result.status !== "READY") throw new Error("用户中心人员组目录暂不可用。");
            setDirectoryGroups(result.groups || []);
            setDirectoryGroupId(result.groups?.[0]?.id || "");
            setDirectoryAccounts([]);
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "人员组目录读取失败");
        } finally {
            setSaving(false);
        }
    };

    const loadAccounts = async () => {
        if (!directoryGroupId) return;
        setSaving(true);
        try {
            const query = new URLSearchParams({ groupId: directoryGroupId, limit: "50" });
            const result = await request<{ candidates: { accountId: string; displayName: string }[]; status: string }>(
                `/customer-service/configuration/directory/accounts?${query}`,
            );
            if (result.status !== "READY") throw new Error("所选人员组暂时无法读取账号。");
            setDirectoryAccounts(result.candidates || []);
            setSelectedAccount(result.candidates?.[0]?.accountId || "");
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "人员候选读取失败");
        } finally {
            setSaving(false);
        }
    };

    const updateGroup = (index: number, patch: Partial<Group>) => {
        setRouting((current) =>
            current
                ? {
                      ...current,
                      groups: current.groups.map((group, i) => (i === index ? { ...group, ...patch } : group)),
                  }
                : current,
        );
    };

    const addMember = () => {
        if (!routing || !selectedAccount) return;
        const candidate = directoryAccounts.find((item) => item.accountId === selectedAccount);
        const groupIndex = routing.groups.findIndex((item) => item.category === targetCategory);
        if (!candidate || groupIndex < 0) return;
        const group = routing.groups[groupIndex];
        if (group.members.some((item) => item.accountId === candidate.accountId)) return;
        updateGroup(groupIndex, { members: [...group.members, candidate] });
    };

    const saveRouting = async (event: FormEvent) => {
        event.preventDefault();
        if (!routing) return;
        setSaving(true);
        setError("");
        try {
            const route: Routing["routes"][number] = { assistantConfigId: assistantId, groups: routeDraft };
            const routes = routing.routes.some((item) => item.assistantConfigId === assistantId)
                ? routing.routes.map((item) => (item.assistantConfigId === assistantId ? route : item))
                : [...routing.routes, route];
            const result = await put<Routing>(`/customer-service/configuration`, {
                version: routing.version,
                groups: routing.groups,
                routes,
            });
            setRouting(result);
            setNotice("分组与助手路由已保存。");
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "配置保存失败，请刷新后核对版本");
        } finally {
            setSaving(false);
        }
    };

    const saveCalendar = async (event: FormEvent) => {
        event.preventDefault();
        if (!calendar) return;
        setSaving(true);
        setError("");
        try {
            const result = await put<Calendar>(`/customer-service/configuration/calendar`, calendar);
            setCalendar(result);
            setNotice("营业日历已保存，新等待周期将使用此规则。");
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "营业日历保存失败，请检查业务版本");
        } finally {
            setSaving(false);
        }
    };

    const addOverride = () => {
        if (!calendar || !overrideDate) return;
        setCalendar({ ...calendar, overrides: { ...calendar.overrides, [overrideDate]: overrideOpen } });
        setOverrideDate("");
    };

    const preview = async () => {
        setSaving(true);
        try {
            const result = await post<{
                classification: { category: string; confidence: number; source: string };
                groupId?: string;
                configVersion: number;
            }>(`/customer-service/configuration/preview`, {
                assistantConfigId: assistantId,
                context: previewText,
            });
            const category = categories.find((item) => item.id === result.classification.category)?.label || "其他咨询";
            const groupName = routing?.groups.find((item) => item.id === result.groupId)?.name || "未配置接待组";
            setPreviewResult(
                `${category} · ${Math.round(result.classification.confidence * 100)}% 置信度 · ${groupName}`,
            );
        } catch (reason) {
            setError(reason instanceof Error ? reason.message : "路由预览失败");
        } finally {
            setSaving(false);
        }
    };

    return (
        <main className="agent-page settings-page">
            <div className="agent-page-header">
                <div>
                    <p className="eyebrow">SERVICE OPERATIONS / CONFIGURATION</p>
                    <h1>分组与营业时间</h1>
                    <p className="page-subtitle">管理本组织接待组、助手分类路由和在线等待日历。</p>
                </div>
                <button className="button button--quiet" type="button" onClick={() => void load()} disabled={loading}>
                    <RefreshCw size={15} className={loading ? "spin" : ""} />
                    重新加载
                </button>
            </div>
            {error && (
                <div className="page-alert" role="alert">
                    <CircleAlert size={15} />
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
            {loading || !routing || !calendar ? (
                <div className="settings-loading">
                    <span className="loader" />
                    正在读取组织配置
                </div>
            ) : (
                <div className="settings-grid">
                    <form className="settings-section settings-section--routing" onSubmit={saveRouting}>
                        <div className="settings-section-heading">
                            <span className="settings-heading-icon settings-heading-icon--teal">
                                <UsersRound size={18} />
                            </span>
                            <div>
                                <h2>接待组与助手路由</h2>
                                <span>
                                    整体版本 {routing.version} · {assistant.name}
                                </span>
                            </div>
                        </div>
                        <div className="group-grid">
                            {routing.groups.map((group, index) => {
                                const meta = categories.find((item) => item.id === group.category)!;
                                return (
                                    <div className="group-config-row" key={group.id}>
                                        <div className={`group-category-dot group-category-dot--${meta.accent}`} />
                                        <div className="group-config-main">
                                            <div className="group-config-title">
                                                <span>{meta.label}</span>
                                                <input
                                                    aria-label={`${meta.label}接待组名称`}
                                                    value={group.name}
                                                    maxLength={128}
                                                    onChange={(event) =>
                                                        updateGroup(index, { name: event.target.value })
                                                    }
                                                />
                                            </div>
                                            <div className="member-list">
                                                {group.members.map((member) => (
                                                    <span className="member-chip" key={member.accountId}>
                                                        <span className="member-avatar">
                                                            {member.displayName.slice(0, 1)}
                                                        </span>
                                                        {member.displayName}
                                                        <small>{member.accountId}</small>
                                                        <button
                                                            type="button"
                                                            title="移除成员"
                                                            aria-label={`移除 ${member.displayName}`}
                                                            onClick={() =>
                                                                updateGroup(index, {
                                                                    members: group.members.filter(
                                                                        (item) => item.accountId !== member.accountId,
                                                                    ),
                                                                })
                                                            }
                                                        >
                                                            <Trash2 size={13} />
                                                        </button>
                                                    </span>
                                                ))}
                                                {group.members.length === 0 && (
                                                    <span className="empty-members">尚未关联客服账号</span>
                                                )}
                                            </div>
                                        </div>
                                        <div className="group-route">
                                            <label htmlFor={`route-${group.category}`}>该类别由哪个组接待</label>
                                            <select
                                                id={`route-${group.category}`}
                                                value={routeDraft[group.category] || ""}
                                                onChange={(event) =>
                                                    setRouteDraft((current) => ({
                                                        ...current,
                                                        [group.category]: event.target.value,
                                                    }))
                                                }
                                            >
                                                {routing.groups.map((option) => (
                                                    <option key={option.id} value={option.id}>
                                                        {option.name}
                                                    </option>
                                                ))}
                                            </select>
                                        </div>
                                    </div>
                                );
                            })}
                        </div>
                        <div className="directory-adder">
                            <div className="directory-adder-title">
                                <span>
                                    <Plus size={15} />
                                </span>
                                <strong>添加客服成员</strong>
                            </div>
                            <div className="directory-controls">
                                <select
                                    aria-label="目标业务组"
                                    value={targetCategory}
                                    onChange={(event) => setTargetCategory(event.target.value)}
                                >
                                    {categories.map((item) => (
                                        <option key={item.id} value={item.id}>
                                            {item.label}
                                        </option>
                                    ))}
                                </select>
                                <div className="directory-search">
                                    <Search size={14} />
                                    <input
                                        value={directorySearch}
                                        onChange={(event) => setDirectorySearch(event.target.value)}
                                        placeholder="用户中心人员组关键词"
                                    />
                                </div>
                                <button
                                    className="button button--quiet button--small"
                                    type="button"
                                    onClick={() => void loadDirectoryGroups()}
                                    disabled={saving}
                                >
                                    读取人员组
                                </button>
                            </div>
                            {directoryGroups.length > 0 && (
                                <div className="directory-controls">
                                    <select
                                        aria-label="用户中心人员组"
                                        value={directoryGroupId}
                                        onChange={(event) => setDirectoryGroupId(event.target.value)}
                                    >
                                        {directoryGroups.map((item) => (
                                            <option value={item.id} key={item.id}>
                                                {item.name}
                                            </option>
                                        ))}
                                    </select>
                                    <button
                                        className="button button--quiet button--small"
                                        type="button"
                                        onClick={() => void loadAccounts()}
                                        disabled={saving}
                                    >
                                        <RefreshCw size={14} />
                                        读取账号
                                    </button>
                                    {directoryAccounts.length > 0 && (
                                        <>
                                            <select
                                                aria-label="候选客服账号"
                                                value={selectedAccount}
                                                onChange={(event) => setSelectedAccount(event.target.value)}
                                            >
                                                {directoryAccounts.map((item) => (
                                                    <option value={item.accountId} key={item.accountId}>
                                                        {item.displayName} · {item.accountId}
                                                    </option>
                                                ))}
                                            </select>
                                            <button
                                                className="button button--primary button--small"
                                                type="button"
                                                onClick={addMember}
                                            >
                                                <Plus size={14} />
                                                添加
                                            </button>
                                        </>
                                    )}
                                </div>
                            )}
                            <span className="settings-footnote">成员身份会在保存时由用户中心重新核验。</span>
                        </div>
                        <div className="settings-form-footer">
                            <span>保存使用版本比较；冲突后请重新加载并核对。</span>
                            <button className="button button--primary" type="submit" disabled={saving}>
                                <Save size={15} />
                                保存分组与路由
                            </button>
                        </div>
                    </form>

                    <ChatNotifications key={assistantId} />

                    <section className="settings-section route-preview-section">
                        <div className="settings-section-heading">
                            <span className="settings-heading-icon settings-heading-icon--coral">
                                <CircleAlert size={17} />
                            </span>
                            <div>
                                <h2>分类路由预览</h2>
                                <span>仅显示建议目标，不创建人工申请</span>
                            </div>
                        </div>
                        <label htmlFor="preview-context">输入一段模拟咨询</label>
                        <textarea
                            id="preview-context"
                            rows={4}
                            maxLength={16000}
                            value={previewText}
                            onChange={(event) => setPreviewText(event.target.value)}
                            placeholder="例如：展位搭建时间可以延长吗？"
                        />
                        <button
                            className="button button--quiet"
                            type="button"
                            disabled={saving || !previewText.trim()}
                            onClick={() => void preview()}
                        >
                            <Search size={15} />
                            预览接待类别
                        </button>
                        {previewResult && (
                            <div className="preview-result">
                                <span className="preview-result-dot" />
                                <span>{previewResult}</span>
                            </div>
                        )}
                    </section>

                    <form className="settings-section calendar-section" onSubmit={saveCalendar}>
                        <div className="settings-section-heading">
                            <span className="settings-heading-icon settings-heading-icon--amber">
                                <CalendarDays size={18} />
                            </span>
                            <div>
                                <h2>人工服务日历</h2>
                                <span>只影响新开始的等待周期</span>
                            </div>
                        </div>
                        <div className="calendar-fields">
                            <label>
                                业务版本
                                <input
                                    value={calendar.calendarVersion}
                                    maxLength={64}
                                    onChange={(event) =>
                                        setCalendar({ ...calendar, calendarVersion: event.target.value })
                                    }
                                />
                            </label>
                            <label>
                                时区
                                <select
                                    value={calendar.zone}
                                    onChange={(event) => setCalendar({ ...calendar, zone: event.target.value })}
                                >
                                    <option value="Asia/Shanghai">中国标准时间 · Asia/Shanghai</option>
                                    <option value="UTC">协调世界时 · UTC</option>
                                </select>
                            </label>
                            <label>
                                开始接待
                                <input
                                    type="time"
                                    value={calendar.opens.slice(0, 5)}
                                    onChange={(event) => setCalendar({ ...calendar, opens: event.target.value })}
                                />
                            </label>
                            <label>
                                结束接待
                                <input
                                    type="time"
                                    value={calendar.closes.slice(0, 5)}
                                    onChange={(event) => setCalendar({ ...calendar, closes: event.target.value })}
                                />
                            </label>
                        </div>
                        <div className="weekday-summary">
                            <span>
                                <Clock3 size={14} />
                                默认营业日
                            </span>
                            <div>
                                {["一", "二", "三", "四", "五", "六", "日"].map((day, index) => (
                                    <span key={day} className={index < 5 ? "weekday-on" : ""}>
                                        {day}
                                    </span>
                                ))}
                            </div>
                            <small>周一至周五</small>
                        </div>
                        <div className="calendar-overrides">
                            <div className="calendar-overrides-head">
                                <strong>日期例外</strong>
                                <span>最多 1000 个 ISO 日期覆盖</span>
                            </div>
                            <div className="calendar-controls">
                                <input
                                    type="date"
                                    value={overrideDate}
                                    onChange={(event) => setOverrideDate(event.target.value)}
                                />
                                <label className="toggle-field">
                                    <input
                                        type="checkbox"
                                        checked={overrideOpen}
                                        onChange={(event) => setOverrideOpen(event.target.checked)}
                                    />
                                    <span>{overrideOpen ? "额外营业" : "休息日"}</span>
                                </label>
                                <button
                                    className="button button--quiet button--small"
                                    type="button"
                                    disabled={!overrideDate}
                                    onClick={addOverride}
                                >
                                    <Plus size={14} />
                                    添加例外
                                </button>
                            </div>
                            {Object.entries(calendar.overrides)
                                .sort(([left], [right]) => left.localeCompare(right))
                                .map(([date, isOpen]) => (
                                    <div className="override-row" key={date}>
                                        <span>{date}</span>
                                        <strong className={isOpen ? "override-open" : "override-closed"}>
                                            {isOpen ? "额外营业" : "休息日"}
                                        </strong>
                                        <button
                                            type="button"
                                            className="icon-button"
                                            aria-label={`移除 ${date}`}
                                            onClick={() => {
                                                const overrides = { ...calendar.overrides };
                                                delete overrides[date];
                                                setCalendar({ ...calendar, overrides });
                                            }}
                                        >
                                            <Trash2 size={14} />
                                        </button>
                                    </div>
                                ))}
                        </div>
                        <div className="settings-form-footer">
                            <span>修改营业规则时请同步升级业务版本。</span>
                            <button className="button button--primary" type="submit" disabled={saving}>
                                <Save size={15} />
                                保存营业日历
                            </button>
                        </div>
                    </form>
                </div>
            )}
        </main>
    );
}
