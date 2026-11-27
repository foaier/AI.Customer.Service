import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import SettingsPage from "./SettingsPage";
import { post, request } from "@/shared/api/client";
import type { Calendar, Routing } from "@/shared/types/customer-service";

vi.mock("@/shared/api/client", () => ({ post: vi.fn(), put: vi.fn(), request: vi.fn() }));
vi.mock("./components/ChatNotifications", () => ({ default: () => null }));

const routing: Routing = { version: 1, groups: [], routes: [] };
const calendar: Calendar = {
    version: 1,
    calendarVersion: "cn-office-v1",
    zone: "Asia/Shanghai",
    opens: "10:00",
    closes: "17:00",
    overrides: {},
    workingDays: [1, 2, 3, 4, 5],
    slots: [{ opens: "10:00", closes: "17:00" }],
};

describe("service calendar settings", () => {
    beforeEach(() => {
        vi.resetAllMocks();
        vi.mocked(request).mockImplementation(async (path) => (path.endsWith("/calendar") ? calendar : routing));
        vi.mocked(post).mockImplementation(async (_path, body) => body as Calendar);
    });

    it("saves selected weekdays and multiple time slots", async () => {
        const user = userEvent.setup();
        render(<SettingsPage assistant={{ id: "assistant-a", name: "客服助手" }} />);

        await screen.findByRole("heading", { name: "人工服务日历" });
        await user.click(screen.getByRole("button", { name: "六" }));
        await user.click(screen.getByRole("button", { name: "添加时段" }));
        await user.click(screen.getByRole("button", { name: "保存营业日历" }));

        await waitFor(() => expect(post).toHaveBeenCalledTimes(1));
        const body = vi.mocked(post).mock.calls[0][1] as Calendar;
        expect(body.workingDays).toEqual([1, 2, 3, 4, 5, 6]);
        expect(body.slots).toHaveLength(2);
        expect(body.slots[0]).toEqual({ opens: "10:00", closes: "17:00" });
    });
});
