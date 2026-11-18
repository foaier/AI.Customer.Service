import { getAccessToken } from "./auth";

const apiBase = (import.meta.env.VITE_API_BASE_URL || "").replace(/\/$/, "");

export class ApiError extends Error {
    constructor(
        message: string,
        readonly status: number,
    ) {
        super(message);
    }
}

export async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const token = await getAccessToken();
    const headers = new Headers(init.headers);
    headers.set("Authorization", `Bearer ${token}`);
    headers.set("Accept", "application/json");
    if (init.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");

    const response = await fetch(`${apiBase}/api${path}`, {
        ...init,
        headers,
        credentials: "same-origin",
        cache: "no-store",
    });
    const raw = await response.text();
    let body: Record<string, unknown> = {};
    try {
        body = raw ? JSON.parse(raw) : {};
    } catch {
        body = { message: raw };
    }
    if (!response.ok || body.isSuccess === false || (body.code && body.code !== "OK" && body.code !== "REQUESTED")) {
        throw new ApiError(
            String(body.message || body.businessMessage || body.code || `HTTP ${response.status}`),
            response.status,
        );
    }
    if ("data" in body) return body.data as T;
    if ("returnObj" in body) return body.returnObj as T;
    return body as T;
}

export function post<T>(path: string, body: unknown): Promise<T> {
    return request<T>(path, { method: "POST", body: JSON.stringify(body) });
}

export function put<T>(path: string, body: unknown): Promise<T> {
    return request<T>(path, { method: "PUT", body: JSON.stringify(body) });
}

export async function streamChat(
    path: string,
    body: unknown,
    onText: (text: string) => void,
    onCard: (value: unknown) => void,
): Promise<void> {
    const token = await getAccessToken();
    const response = await fetch(`${apiBase}/api${path}`, {
        method: "POST",
        headers: {
            Authorization: `Bearer ${token}`,
            Accept: "text/event-stream",
            "Content-Type": "application/json",
        },
        credentials: "same-origin",
        cache: "no-store",
        body: JSON.stringify(body),
    });
    if (!response.ok || !response.body) {
        const detail = await response.text();
        throw new ApiError(detail || `AI 回复失败（${response.status}）`, response.status);
    }

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let pending = "";
    let currentEvent = "message";
    let currentData: string[] = [];
    const dispatch = () => {
        if (!currentData.length) return;
        const payload = currentData.join("\n");
        currentData = [];
        if (currentEvent === "error") {
            let message = payload;
            try {
                message = JSON.parse(payload).message || payload;
            } catch {
                // 服务端兼容纯文本错误帧。
            }
            throw new Error(message);
        }
        if (payload === "[DONE]") return;
        if (currentEvent === "card") {
            onCard(JSON.parse(payload));
        } else if (currentEvent === "message") {
            const data = JSON.parse(payload);
            onText(String(data.v ?? data.content ?? ""));
        }
    };

    while (true) {
        const { value, done } = await reader.read();
        pending += decoder.decode(value, { stream: !done });
        const frames = pending.split(/\r?\n\r?\n/);
        pending = frames.pop() || "";
        for (const frame of frames) {
            currentEvent = "message";
            currentData = [];
            for (const line of frame.split(/\r?\n/)) {
                if (line.startsWith("event:")) currentEvent = line.slice(6).trim();
                if (line.startsWith("data:")) currentData.push(line.slice(5).replace(/^ /, ""));
            }
            dispatch();
        }
        if (done) break;
    }
    if (pending.trim()) {
        for (const line of pending.split(/\r?\n/)) {
            if (line.startsWith("event:")) currentEvent = line.slice(6).trim();
            if (line.startsWith("data:")) currentData.push(line.slice(5).replace(/^ /, ""));
        }
        dispatch();
    }
}

export function realtimeUrl(parameters: Record<string, string | number | boolean | null | undefined> = {}): string {
    const base = apiBase ? new URL(apiBase, window.location.href) : new URL(window.location.href);
    base.protocol = base.protocol === "https:" ? "wss:" : "ws:";
    base.pathname = `${base.pathname.replace(/\/$/, "")}/api/customer-service/realtime/ws`;
    base.search = "";
    for (const [key, value] of Object.entries(parameters)) {
        if (value !== null && value !== undefined) base.searchParams.set(key, String(value));
    }
    return base.toString();
}
