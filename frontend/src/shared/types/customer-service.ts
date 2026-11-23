export type Assistant = {
    id: string;
    assistantConfigId?: string;
    configId?: string;
    name: string;
    bventId?: string | null;
};

export function getAssistantId(assistant: Assistant): string {
    return assistant.assistantConfigId || assistant.configId || assistant.id;
}

export type HumanMessage = {
    seq: number;
    messageId: string;
    senderRole: "VISITOR" | "AGENT";
    senderId: string;
    content: string;
    createdAt: string;
};

export type Episode = {
    scope: {
        conversationId: string;
        assistantConfigId: string;
        bventId?: string | null;
    };
    episodeId: string;
    status: "REQUESTED" | "CLAIMED" | "ACTIVE" | "RESOLVED" | "CANCELLED";
    version: number;
    principalId?: string;
    agentId?: string | null;
    groupId?: string;
    requestedAt: string;
    triggerSource?: string;
    reasonCode?: string;
    autoCloseAt?: string | null;
    resolutionReason?: "AGENT_MANUAL" | "VISITOR_INACTIVE_5M" | null;
    unreadCount?: number | null;
    lastVisitorSeq?: number | null;
    readSeq?: number | null;
    lastVisitorMessageAt?: string | null;
};

export type Proposal = {
    proposalId: string;
    reasonCode: string;
    userFacingReason: string;
    confidence: number;
    status: string;
    version: number;
    expiresAt: string;
};

export type Group = {
    id: string;
    category: string;
    name: string;
    members: { accountId: string; displayName: string }[];
};

export type Route = {
    assistantConfigId: string;
    groups: Record<string, string>;
};

export type Routing = { version: number; groups: Group[]; routes: Route[] };

export type Calendar = {
    version: number;
    calendarVersion: string;
    zone: string;
    opens: string;
    closes: string;
    overrides: Record<string, boolean>;
};

export type AgentSnapshot = {
    reception?: Episode;
    timeline?: { messages: HumanMessage[]; receipt?: { deliveredSeq: number; readSeq: number } };
    queue?: Episode[];
    followups?: Followup[];
};

export type InboxSummary = {
    unreadMessageCount: number;
    unreadConversationCount: number;
    newFollowupCount: number;
    pendingReceptionCount: number;
    changeToken: string;
};

export type Waiting = {
    version: number;
    state: string;
    reminded: boolean;
    nextOpensAt?: string | null;
    closesAt?: string | null;
};

export type Followup = {
    scope: { conversationId: string; assistantConfigId: string };
    episodeId: string;
    ticketId: string;
    state: string;
    version: number;
    assignedTo?: string | null;
    submittedAt: string;
    hasSubmittedForm?: boolean;
    hasUnseen?: boolean;
    formState?: "OPEN" | "CLAIMED" | null;
};
