import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import VisitorPage from "./VisitorPage";
import { post, request, streamChat } from "@/shared/api/client";

vi.mock("@/shared/api/client", () => ({ post: vi.fn(), request: vi.fn(), streamChat: vi.fn(), realtimeUrl: vi.fn() }));
vi.mock("@/shared/auth/client", () => ({ getAccessToken: vi.fn() }));

const payloadKey = "cs.handoff.payload.assistant-a.conversation-a";
const pending = {
    requestId: "handoff-offline",
    followupRequestId: "followup-offline",
    email: "visitor@example.com",
    contactName: null,
    content: "请核实退款进度",
    expectedVersion: 3,
};
const closedEpisode = {
    episodeId: "episode-a",
    scope: { conversationId: "conversation-a", assistantConfigId: "assistant-a" },
    status: "CANCELLED",
    version: 2,
};
let hasEpisode = false;
let submittedRequestId = "";

function renderVisitor() {
    return render(<VisitorPage assistant={{ id: "assistant-a", name: "客服助手" }} />);
}

describe("offline handoff recovery", () => {
    beforeEach(() => {
        vi.resetAllMocks();
        sessionStorage.clear();
        hasEpisode = false;
        submittedRequestId = "";
        HTMLElement.prototype.scrollTo = vi.fn();
        vi.mocked(request).mockImplementation(async (path) => {
            if (path.startsWith("/agent/sessionId")) return { sessionId: "conversation-a" };
            if (path.startsWith("/agent/history_detail")) return { messages: [] };
            if (path.endsWith("/handoffs/latest")) return { episode: hasEpisode ? closedEpisode : null };
            if (path.endsWith("/handoff-proposals/pending")) return { proposals: [] };
            if (path.endsWith("/followup-form")) {
                return submittedRequestId ? { requestId: submittedRequestId, content: pending.content } : { form: {} };
            }
            if (path.endsWith("/survey")) return { survey: null };
            if (path.endsWith("/waiting"))
                return { version: 3, state: submittedRequestId ? "FOLLOW_UP" : "NEED_EMAIL" };
            throw new Error(`Unexpected request: ${path}`);
        });
        vi.mocked(streamChat).mockImplementation(async (_path, _body, _onText, onCard) => {
            onCard?.({
                status: "handoff.offline_followup_required",
                data: {
                    requestId: "handoff-offline",
                    category: "VISITOR",
                    nextOpensAt: "2026-11-26T01:00:00Z",
                    timeZone: "Asia/Shanghai",
                    serviceHours: { opens: "10:00", closes: "17:00", workingDays: [1, 2, 3, 4, 5] },
                },
            });
        });
    });

    it("keeps the form available and replays the original submission after a lost response", async () => {
        const user = userEvent.setup();
        const formBodies: unknown[] = [];
        vi.mocked(post).mockImplementation(async (path, body) => {
            hasEpisode = true;
            if (path.endsWith("/handoffs")) return closedEpisode;
            formBodies.push(body);
            if (formBodies.length === 1) throw new Error("网络连接中断");
            submittedRequestId = (body as { requestId: string }).requestId;
            return {};
        });
        renderVisitor();
        await waitFor(() => expect(screen.getByLabelText("输入你的问题")).toBeEnabled());
        await user.type(screen.getByLabelText("输入你的问题"), "请转人工");
        await user.click(screen.getByRole("button", { name: "发送消息" }));
        expect(post).not.toHaveBeenCalled();
        await user.type(await screen.findByLabelText("联系邮箱"), pending.email);
        await user.type(screen.getByLabelText("问题描述"), pending.content);
        expect(
            screen.getByText(
                (_, element) =>
                    element?.tagName === "P" &&
                    element.textContent?.includes("常规服务时间：周一至周五 10:00–17:00（Asia/Shanghai）") === true,
            ),
        ).toBeInTheDocument();
        await user.click(screen.getByRole("button", { name: "提交留言" }));
        expect(await screen.findByRole("alert")).toHaveTextContent("网络连接中断");
        expect(screen.getByRole("button", { name: "提交留言" })).toBeEnabled();
        await user.click(screen.getByRole("button", { name: "提交留言" }));
        await screen.findByText("留言已提交。客服将在工作时间内按你留下的邮箱跟进。");
        expect(formBodies).toHaveLength(2);
        expect(formBodies[1]).toEqual(formBodies[0]);
        expect(vi.mocked(post).mock.calls.find(([path]) => path.endsWith("/handoffs"))?.[1]).toMatchObject({
            requestId: "handoff-offline",
            category: "VISITOR",
        });
        expect(sessionStorage.getItem(payloadKey)).toBeNull();
    });

    it("restores a pending form after refresh even when its reception was already cancelled", async () => {
        hasEpisode = true;
        sessionStorage.setItem(payloadKey, JSON.stringify(pending));
        renderVisitor();
        expect(await screen.findByLabelText("问题描述")).toHaveValue(pending.content);
        expect(document.querySelector<HTMLInputElement>("#handoff-contact-email")).toHaveValue(pending.email);
        expect(screen.getByRole("button", { name: "提交留言" })).toBeEnabled();
        expect(post).not.toHaveBeenCalled();
    });

    it("recognizes a committed form after refresh without submitting it again", async () => {
        hasEpisode = true;
        submittedRequestId = pending.followupRequestId;
        sessionStorage.setItem(payloadKey, JSON.stringify(pending));
        renderVisitor();
        await screen.findAllByText("邮件跟进已登记");
        expect(sessionStorage.getItem(payloadKey)).toBeNull();
        expect(screen.queryByRole("button", { name: "提交留言" })).not.toBeInTheDocument();
        expect(post).not.toHaveBeenCalled();
    });
});
