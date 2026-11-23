import { useEffect, useRef, useState } from "react";
import { ApiError, request } from "./api";
import type { Episode, HumanMessage } from "./types";

/** 列表预览只读消息，不提交 READ；仅为当前可读周期加载，最多三个并发请求。 */
export function useEpisodePreviews(episodes: Episode[]) {
    const [previews, setPreviews] = useState<Record<string, string>>({});
    const loaded = useRef(new Map<string, string>());
    const signature = JSON.stringify(
        episodes
            .filter((item) => ["CLAIMED", "ACTIVE"].includes(item.status) && item.lastVisitorSeq !== null)
            .map((item) => ({
                id: item.episodeId,
                conversationId: item.scope.conversationId,
                seq: item.lastVisitorSeq || 0,
                version: item.version,
            }))
            .sort((a, b) => a.id.localeCompare(b.id)),
    );
    useEffect(() => {
        const controller = new AbortController();
        const candidates: { id: string; conversationId: string; seq: number; version: number }[] =
            JSON.parse(signature);
        const keys = new Set(candidates.map((item) => item.id));
        for (const id of loaded.current.keys()) if (!keys.has(id)) loaded.current.delete(id);
        setPreviews((current) => Object.fromEntries(Object.entries(current).filter(([id]) => keys.has(id))));
        const pending = candidates.filter((item) => loaded.current.get(item.id) !== `${item.seq}:${item.version}`);
        const worker = async () => {
            while (pending.length && !controller.signal.aborted) {
                const item = pending.shift()!;
                const mark = `${item.seq}:${item.version}`;
                let cursor = Math.max(0, item.seq - 1);
                let last: HumanMessage | undefined;
                try {
                    for (let page = 0; page < 20; page++) {
                        const result = await request<{ messages: HumanMessage[] }>(
                            `/customer-service/agents/conversations/${encodeURIComponent(item.conversationId)}/messages?afterSeq=${cursor}&limit=100`,
                            { signal: controller.signal },
                        );
                        if (controller.signal.aborted) return;
                        const messages = result.messages || [];
                        last = messages.filter((message) => message.content).at(-1) || last;
                        const next = messages.at(-1)?.seq || cursor;
                        if (messages.length < 100 || next <= cursor) break;
                        cursor = next;
                    }
                    loaded.current.set(item.id, mark);
                    setPreviews((current) => ({ ...current, [item.id]: last?.content || "暂无人工消息" }));
                } catch (reason) {
                    if (controller.signal.aborted) return;
                    if (reason instanceof ApiError && [401, 403].includes(reason.status)) {
                        loaded.current.set(item.id, mark);
                        setPreviews((current) => ({ ...current, [item.id]: "暂无可查看消息" }));
                    }
                }
            }
        };
        void Promise.all(Array.from({ length: Math.min(3, pending.length) }, worker));
        return () => controller.abort();
    }, [signature]);
    return previews;
}
