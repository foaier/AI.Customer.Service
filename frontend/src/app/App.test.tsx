import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import App from "@/app/App";
import { request } from "@/shared/api/client";
import { getAccessToken } from "@/shared/auth/client";

vi.mock("@/shared/api/client", () => ({ request: vi.fn() }));
vi.mock("@/shared/auth/client", () => ({ beginLogin: vi.fn(), getAccessToken: vi.fn() }));
vi.mock("@/features/visitor/VisitorPage", () => ({ default: () => <main>访客咨询测试页面</main> }));
vi.mock("@/features/agent-inbox/AgentInboxPage", () => ({ default: () => <main>在线接待测试页面</main> }));
vi.mock("@/features/followups/FollowupsPage", () => ({ default: () => <main>邮件跟进测试页面</main> }));
vi.mock("@/features/settings/SettingsPage", () => ({ default: () => <main>客服设置测试页面</main> }));
vi.mock("@/features/reception-history/ReceptionHistoryPage", () => ({
    default: () => <main>已完成接待测试页面</main>,
}));
vi.mock("@/features/conversation-records/ConversationRecordsPage", () => ({
    default: () => <main>会话记录测试页面</main>,
}));

const assistants = [
    {
        id: "assistant-a",
        name: "展会客服",
    },
];

function renderApp(path: string) {
    return render(
        <MemoryRouter initialEntries={[path]}>
            <App />
        </MemoryRouter>,
    );
}

describe("application routing and session setup", () => {
    beforeEach(() => {
        sessionStorage.clear();
        vi.mocked(getAccessToken).mockResolvedValue("test-token");
        vi.mocked(request).mockResolvedValue(assistants);
    });

    it("loads the tenant assistants before rendering the requested agent route", async () => {
        renderApp("/b/inbox");

        expect(await screen.findByText("在线接待测试页面")).toBeInTheDocument();
        expect(screen.getByRole("link", { name: /在线接待/ })).toHaveAttribute("aria-current", "page");
        expect(screen.getByRole("combobox", { name: "切换助手" })).toHaveValue("assistant-a");
        expect(request).toHaveBeenCalledWith("/assistant/config/list");
    });

    it("redirects the application root to the visitor route", async () => {
        renderApp("/");

        expect(await screen.findByText("访客咨询测试页面")).toBeInTheDocument();
        await waitFor(() => expect(screen.getByText("在线咨询")).toBeInTheDocument());
    });

    it("opens the completed reception history route", async () => {
        renderApp("/b/history");

        expect(await screen.findByText("已完成接待测试页面")).toBeInTheDocument();
        expect(screen.getByRole("link", { name: /已完成接待/ })).toHaveAttribute("aria-current", "page");
    });

    it("opens the conversation records route", async () => {
        renderApp("/b/records");

        expect(await screen.findByText("会话记录测试页面")).toBeInTheDocument();
        expect(screen.getByRole("link", { name: /会话记录/ })).toHaveAttribute("aria-current", "page");
    });
});
