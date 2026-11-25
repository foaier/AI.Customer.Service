import { lazy, Suspense } from "react";
import { Navigate, Route, Routes, useLocation } from "react-router-dom";
import type { Assistant } from "@/shared/types/customer-service";

const VisitorPage = lazy(() => import("@/features/visitor/VisitorPage"));
const AgentInboxPage = lazy(() => import("@/features/agent-inbox/AgentInboxPage"));
const FollowupsPage = lazy(() => import("@/features/followups/FollowupsPage"));
const SettingsPage = lazy(() => import("@/features/settings/SettingsPage"));
const ReceptionHistoryPage = lazy(() => import("@/features/reception-history/ReceptionHistoryPage"));
const ConversationRecordsPage = lazy(() => import("@/features/conversation-records/ConversationRecordsPage"));

type AppRoutesProps = {
    assistant: Assistant;
};

export function AppRoutes({ assistant }: AppRoutesProps) {
    const location = useLocation();
    const assistantId = assistant.assistantConfigId || assistant.configId || assistant.id;
    const fallbackPath = location.pathname.startsWith("/b/") ? "/b/inbox" : "/c/chat";

    return (
        <Suspense
            fallback={
                <main className="loading-screen" aria-live="polite">
                    <span className="loader" />
                    <span>正在加载页面</span>
                </main>
            }
        >
            <Routes>
                <Route path="/" element={<Navigate to="/c/chat" replace />} />
                <Route path="/c/chat" element={<VisitorPage assistant={assistant} />} />
                <Route path="/b/inbox" element={<AgentInboxPage key={assistantId} assistant={assistant} />} />
                <Route path="/b/followups" element={<FollowupsPage key={assistantId} assistant={assistant} />} />
                <Route path="/b/history" element={<ReceptionHistoryPage key={assistantId} assistant={assistant} />} />
                <Route
                    path="/b/records"
                    element={<ConversationRecordsPage key={assistantId} assistant={assistant} />}
                />
                <Route path="/b/settings" element={<SettingsPage key={assistantId} assistant={assistant} />} />
                <Route path="*" element={<Navigate to={fallbackPath} replace />} />
            </Routes>
        </Suspense>
    );
}
