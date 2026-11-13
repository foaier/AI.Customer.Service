(function () {
    "use strict";

    const state = {
        runtime: null,
        conversationId: "",
        conversationVersion: 0,
        episode: null,
        bound: false,
        busy: false,
        humanSeq: 0,
        agentSeq: 0,
        agent: null,
        polling: false,
        pendingVisitorMessage: null,
        pendingAgentMessage: null,
        waiting: null,
        survey: null,
        visitorStream: null,
        agentStream: null,
        agentHistoryId: "",
        agentVersion: 0,
        agentSelected: "",
        visitorFormTicket: "",
    };
    const apiBase = (window.location.pathname.indexOf("/ai_chat/") >= 0 ? "/ai_chat" : "") + "/api";
    const el = (id) => document.getElementById(id);
    // 业务入口复用同一状态与协议实现；角色只控制界面，所有权限仍由服务端核验。
    const pageMode = document.body?.dataset.customerServiceRole || "workbench";
    let initialized = false;
    /** 只有本页明确上线后才续约；恢复页面只读，不隐式将客服设为在线。 */
    let availabilityWanted = null,
        availabilityVersion = null,
        availabilityPolling = false,
        pendingTransfer = null;
    /** 本页心跳与明确下线串行，防止迟到上线请求覆盖本页的下线选择。 */
    let availabilityFlight = Promise.resolve();

    async function authHeaders(extra) {
        const result = Object.assign({ "Content-Type": "application/json" }, extra || {});
        const manager = typeof authManager !== "undefined" ? authManager : null;
        if (!manager || !manager.getToken) throw new Error("登录组件未就绪");
        const token = await manager.getToken();
        const authorization = manager.getAuthorizationHeader(token);
        if (!authorization) throw new Error("未获取到登录 Token，请重新登录后重试");
        result.Authorization = authorization;
        return result;
    }

    async function request(path, options) {
        const response = await fetch(apiBase + path, Object.assign({ credentials: "same-origin" }, options || {}));
        const raw = await response.text();
        let body;
        try {
            body = raw ? JSON.parse(raw) : {};
        } catch (_) {
            body = { message: raw };
        }
        if (!response.ok || body.isSuccess === false) {
            const error = new Error(
                (body && (body.message || body.businessMessage || body.code)) || "HTTP " + response.status,
            );
            error.status = response.status;
            if (response.status === 403 && /\/agents\/conversations\/.*\/(messages|history)/.test(path))
                clearAgentAccess();
            throw error;
        }
        return body;
    }

    function requestId(prefix) {
        return prefix + "_" + crypto.randomUUID().replaceAll("-", "");
    }
    function showResult(id, value) {
        if (!el(id)) return;
        if (pageMode !== "workbench" && typeof value !== "string") {
            const data = value.data?.episode || value.data || value;
            const labels = {
                REQUESTED: "正在等待客服接单",
                CLAIMED: "客服已接单",
                ACTIVE: "正在人工接待",
                RESOLVED: "接待已结束，可继续向 AI 咨询",
                CANCELLED: "等待已取消，可继续向 AI 咨询",
            };
            value = labels[data.status] || value.message || "状态已更新";
        }
        el(id).textContent = typeof value === "string" ? value : JSON.stringify(value, null, 2);
    }
    /** 每次切换递增本地代次，阻止旧会话的迟到查询覆盖当前状态。 */
    function setConversation(id) {
        stopTimeline(false);
        state.conversationId = id || "";
        state.conversationVersion++;
        state.episode = null;
        state.bound = false;
        state.humanSeq = 0;
        state.waiting = null;
        state.survey = null;
        state.visitorFormTicket = "";
        if (pageMode === "visitor" && el("followup-form-content")) {
            el("followup-form-content").value = "";
            el("followup-contact-name").value = "";
            el("followup-form-status").textContent = "";
        }
        renderWaiting();
        renderSurvey();
        el("visitor-receipt").textContent = "";
        el("conversation-id").textContent = state.conversationId || "尚未创建";
        if (pageMode === "visitor" && window.history?.replaceState) {
            const url = new URL(window.location.href);
            if (id) url.searchParams.set("conversationId", id);
            else url.searchParams.delete("conversationId");
            window.history.replaceState(null, "", url);
        }
    }
    function ensureRuntime() {
        if (!state.runtime)
            throw new Error(
                pageMode === "workbench"
                    ? "请先加载 assistantConfigId 对应的运行配置"
                    : "服务尚未就绪，请检查服务链接后重试",
            );
        return state.runtime;
    }
    function ensureConversation() {
        if (!state.conversationId) throw new Error("请先发送一条咨询消息");
        return state.conversationId;
    }

    function addMessage(role, content, target) {
        const list = el(target || "chat-messages");
        const empty = list.querySelector(".empty-chat");
        if (empty) empty.remove();
        const node = document.createElement("div");
        node.className = "chat-message " + role;
        const label = document.createElement("strong");
        label.textContent = role === "user" ? "访客" : role === "agent" ? "人工客服" : "AI";
        const body = document.createElement("div");
        body.textContent = content || "";
        node.append(label, body);
        list.appendChild(node);
        list.scrollTop = list.scrollHeight;
        return body;
    }

    async function loadRuntime() {
        const assistantConfigId = el("assistant-config-id").value.trim();
        if (!assistantConfigId) throw new Error("请输入 assistantConfigId");
        const headers = await authHeaders({ "x-lang": navigator.language || "zh-CN" });
        const response = await fetch(
            apiBase + "/assistant/config/getRuntimeConfig?assistantConfigId=" + encodeURIComponent(assistantConfigId),
            { headers, credentials: "same-origin" },
        );
        const result = await response.json();
        if (!response.ok || result.isSuccess === false)
            throw new Error(result.message || result.businessMessage || "HTTP " + response.status);
        const config = result.returnObj !== undefined ? result.returnObj : result;
        if (!config || !config.topOrgId || !["EVENT", "ORGANIZATION"].includes(config.scopeType))
            throw new Error("运行配置缺少可信组织或级别");
        if (config.scopeType === "EVENT" && !config.bventId) throw new Error("会议级运行配置缺少 bventId");
        stopRealtime("queueStream");
        state.runtime = {
            assistantConfigId,
            topOrgId: config.topOrgId,
            scopeType: config.scopeType,
            bventId: config.bventId || "",
            language: config.resolvedLanguage || "zh-CN",
        };
        el("runtime-state").textContent = "运行配置已锁定";
        el("runtime-state").className = "badge";
        if (el("runtime-org")) el("runtime-org").textContent = state.runtime.topOrgId;
        if (el("directory-org")) el("directory-org").value = state.runtime.topOrgId;
        if (pageMode === "agent") {
            stopTimeline(true);
            state.agent = null;
            state.agentSelected = "";
            state.agentVersion++;
            state.agentHistoryId = "";
            state.agentSeq = 0;
            selectedFollowup = null;
            renderReply(null);
            startQueueRealtime();
            return;
        }
        el("chat-messages").replaceChildren();
        const empty = document.createElement("div");
        empty.className = "empty-chat";
        empty.textContent = "运行配置已加载，发送消息后开始对话。";
        el("chat-messages").appendChild(empty);
        el("proposal-list").replaceChildren();
        state.episode = null;
        el("cancel-handoff").disabled = true;
        setConversation("");
    }

    async function createSession() {
        ensureRuntime();
        const result = await request(
            "/agent/sessionId?assistantConfigId=" + encodeURIComponent(ensureRuntime().assistantConfigId),
            { headers: await authHeaders() },
        );
        const sessionId =
            result.returnObj && result.returnObj.sessionId ? result.returnObj.sessionId : result.sessionId;
        if (!sessionId) throw new Error("服务端未返回 conversationId");
        setConversation(sessionId);
        el("chat-messages").replaceChildren();
        state.episode = null;
        el("proposal-list").replaceChildren();
        el("cancel-handoff").disabled = true;
        showResult("handoff-result", "新会话已创建，尚未提交转人工申请。");
    }

    function extractStreamText(parsed) {
        if (typeof parsed === "string") return parsed;
        if (!parsed || typeof parsed !== "object") return "";
        if (typeof parsed.data === "string") return parsed.data;
        if (typeof parsed.v === "string") return parsed.v;
        if (typeof parsed.content === "string") return parsed.content;
        if (typeof parsed.text === "string") return parsed.text;
        if (parsed.data && typeof parsed.data.content === "string") return parsed.data.content;
        const delta = parsed.choices && parsed.choices[0] && parsed.choices[0].delta;
        return delta && typeof delta.content === "string" ? delta.content : "";
    }

    /** 按 SSE 空行分帧；支持任意网络分块、CRLF 和多行 data，不丢最后一帧。 */
    function createSseParser(onEvent) {
        let buffer = "";
        function frame(text) {
            let event = "message";
            const data = [];
            for (const line of text.split("\n")) {
                if (line.startsWith("event:")) event = line.slice(6).trim();
                if (line.startsWith("data:")) data.push(line.slice(5).replace(/^ /, ""));
            }
            if (data.length) onEvent(event, data.join("\n"));
        }
        return (text, done) => {
            buffer += text;
            let match;
            while ((match = /\r?\n\r?\n/.exec(buffer))) {
                frame(buffer.slice(0, match.index).replaceAll("\r", ""));
                buffer = buffer.slice(match.index + match[0].length);
            }
            if (done && buffer.trim()) {
                frame(buffer.replaceAll("\r", ""));
                buffer = "";
            }
        };
    }

    /** 控制卡与 AI 文本独立处理，防止卡片 JSON 被渲染成回答。 */
    function receiveCard(card) {
        if (card.status === "handoff.proposed" && card.data && card.data.proposalId) appendProposal(card.data);
        if (card.status === "handoff.requested") showResult("handoff-result", "转人工申请已记录，等待客服接单。");
    }

    async function streamRequest(payload, onText) {
        const runtime = ensureRuntime();
        const headers = await authHeaders({
            Accept: "text/event-stream",
            "x-currentOrgId": runtime.topOrgId,
            "x-lang": runtime.language,
        });
        if (runtime.scopeType === "EVENT") headers["x-bventId"] = runtime.bventId;
        const response = await fetch(apiBase + "/agent/chatStream", {
            method: "POST",
            headers,
            credentials: "same-origin",
            body: JSON.stringify(payload),
        });
        if (!response.ok) throw new Error("AI 请求失败：HTTP " + response.status);
        const reader = response.body.getReader();
        const decoder = new TextDecoder("utf-8");
        const parse = createSseParser((eventName, data) => {
            if (eventName === "error") {
                let detail = data;
                try {
                    const error = JSON.parse(data);
                    detail = error.message || error.error || data;
                } catch (_) {}
                throw new Error("AI 流式请求异常：" + detail);
            }
            if (!data || data === "[DONE]") return;
            if (eventName === "card") {
                receiveCard(JSON.parse(data));
                return;
            }
            if (["heartbeat", "task", "complete", "status", "schema", "schema_stream"].includes(eventName)) return;
            let parsed;
            try {
                parsed = JSON.parse(data);
            } catch (_) {
                parsed = data;
            }
            onText(extractStreamText(parsed));
        });
        while (true) {
            const chunk = await reader.read();
            parse(decoder.decode(chunk.value, { stream: !chunk.done }), chunk.done);
            if (chunk.done) break;
        }
    }

    async function sendMessage(event) {
        event.preventDefault();
        const input = el("user-input");
        const userInput = input.value.trim();
        if (!userInput) return;
        const runtime = ensureRuntime();
        if (!state.conversationId) await createSession();
        const conversationId = state.conversationId;
        const sendVersion = state.conversationVersion;
        // 每次发送前恢复 owner，避免人工等待/接待期间继续向 AI 请求。
        if (state.bound) await loadHandoff();
        if (state.episode && ["REQUESTED", "CLAIMED", "ACTIVE"].includes(state.episode.status)) {
            await sendHumanMessage(false, conversationId, state.episode.episodeId, userInput);
            input.value = "";
            await loadHumanMessages();
            return;
        }
        addMessage("user", userInput);
        input.value = "";
        const aiNode = addMessage("assistant", "");
        const payload = {
            sessionId: conversationId,
            userInput,
            businessType: "event-assistant",
            extensions: {
                "event-assistant": { assistantConfigId: runtime.assistantConfigId, bventId: runtime.bventId || "" },
            },
            isReconnect: false,
            speechEnabled: false,
        };
        try {
            await streamRequest(payload, (value) => {
                if (sendVersion === state.conversationVersion && conversationId === state.conversationId) {
                    aiNode.textContent += value;
                    el("chat-messages").scrollTop = el("chat-messages").scrollHeight;
                }
            });
            if (sendVersion !== state.conversationVersion || conversationId !== state.conversationId) return;
            state.bound = true;
            await Promise.allSettled([loadProposals(), loadHandoff()]);
        } catch (error) {
            if (sendVersion !== state.conversationVersion || conversationId !== state.conversationId) return;
            aiNode.textContent = "本轮请求失败：" + error.message;
            // 服务端可能已持久化会话和可信故障信号；恢复owner确认，不能把错误文本当已转接。
            state.bound = true;
            await loadHandoff().catch(() => {});
            throw error;
        }
    }

    async function requestHandoff() {
        if (!state.conversationId) await createSession();
        const conversationId = ensureConversation();
        const version = state.conversationVersion;
        const id = requestId("handoff");
        const result = await request(
            "/customer-service/conversations/" + encodeURIComponent(conversationId) + "/handoffs",
            {
                method: "POST",
                headers: await authHeaders({ "X-Request-Id": id }),
                body: JSON.stringify({ requestId: id }),
            },
        );
        if (version !== state.conversationVersion || conversationId !== state.conversationId) return;
        state.episode = result.data || null;
        renderEpisode(result);
    }

    async function loadHandoff() {
        const conversationId = ensureConversation();
        const version = state.conversationVersion;
        const result = await request(
            "/customer-service/conversations/" + encodeURIComponent(conversationId) + "/handoffs/latest",
            { headers: await authHeaders({ "X-Request-Id": requestId("latest") }) },
        );
        if (version !== state.conversationVersion || conversationId !== state.conversationId) return;
        state.episode = result.data && result.data.episode ? result.data.episode : null;
        renderEpisode(result);
    }

    function renderEpisode(result) {
        const episode = state.episode;
        el("cancel-handoff").disabled =
            !episode || episode.status !== "REQUESTED" || !episode.episodeId || !episode.version;
        showResult("handoff-result", result);
        if (state.bound || episode) startTimeline(false);
        else stopTimeline(false);
    }

    /** 附属卡片读取同样绑定本地代次，旧会话响应不能覆盖当前选择。 */
    async function loadWaiting() {
        const id = ensureConversation(),
            version = state.conversationVersion;
        let result;
        try {
            result = await request("/customer-service/conversations/" + encodeURIComponent(id) + "/waiting", {
                headers: await authHeaders(),
            });
        } catch (error) {
            if (version === state.conversationVersion && error.status === 409 && error.message.includes("尚未启用")) {
                state.waitingEnabled = false;
                el("waiting-status").textContent = error.message;
                return;
            }
            throw error;
        }
        if (version !== state.conversationVersion || id !== state.conversationId) return;
        state.waitingEnabled = true;
        state.waiting = result.data;
        renderWaiting();
    }

    /** 按持久状态呈现下一步；网关受理不能显示为邮件送达。 */
    function renderWaiting() {
        const w = state.waiting;
        if (el("submit-followup-form")) {
            el("submit-followup-form").disabled =
                !w || !["WAITING", "BUSY", "CONTINUING", "NEED_EMAIL"].includes(w.state);
        }
        if (
            pageMode === "visitor" &&
            w?.state === "FOLLOW_UP" &&
            w.ticketId &&
            state.visitorFormTicket !== w.ticketId
        ) {
            state.visitorFormTicket = w.ticketId;
            const id = state.conversationId,
                version = state.conversationVersion;
            restoreVisitorForm().catch((error) => {
                if (id === state.conversationId && version === state.conversationVersion)
                    showResult("followup-form-status", error.message);
            });
        }
        el("continue-waiting").disabled = !w || w.state !== "BUSY";
        el("email-followup").disabled = !w || !["WAITING", "BUSY", "CONTINUING", "NEED_EMAIL"].includes(w.state);
        el("save-contact-email").disabled = !w || !["WAITING", "BUSY", "CONTINUING"].includes(w.state);
        const labels = {
            WAITING: "正在等待客服接单。",
            BUSY: "客服繁忙，可继续等待或选择邮件跟进。",
            CONTINUING: "已选择继续等待，营业结束时将提示邮件跟进。",
            NEED_EMAIL: "当前不在营业时间，已恢复 AI。请留下有效邮箱以创建跟进。",
            FOLLOW_UP: "已创建跟进工单。",
            ACCEPTED: "客服已接单。",
            CANCELLED: "等待已取消。",
        };
        let text = w ? labels[w.state] || w.state : "申请人工后可恢复等待状态。";
        if (w && ["NEED_EMAIL", "FOLLOW_UP"].includes(w.state))
            text += " 下次营业：" + new Date(w.nextOpensAt).toLocaleString("zh-CN");
        if (w && w.ticketId) text += " 工单：" + w.ticketId;
        if (w && w.contactCollected) text += " 联系邮箱已保存。";
        if (w)
            for (const d of w.deliveries || [])
                if (d.kind === "EMAIL")
                    text +=
                        " 邮件：" +
                        ({ PENDING: "待投递", CLAIMED: "投递中", SENT: "网关已受理", DEAD: "投递失败，待客服处理" }[
                            d.status
                        ] || d.status);
        el("waiting-status").textContent = text;
    }

    /** 选择使用最近恢复的版本，冲突时由用户恢复后重试，不猜测成功。 */
    async function chooseWaiting(action) {
        if (!state.waiting) throw new Error("请先恢复等待状态");
        const id = ensureConversation(),
            version = state.conversationVersion;
        const email = el("followup-email").value.trim();
        if (
            ["EMAIL", "CONTACT"].includes(action) &&
            (!email || (el("followup-email").checkValidity && !el("followup-email").checkValidity()))
        )
            throw new Error("请输入有效邮箱");
        const result = await request("/customer-service/conversations/" + encodeURIComponent(id) + "/waiting", {
            method: "POST",
            headers: await authHeaders(),
            body: JSON.stringify({
                expectedVersion: state.waiting.version,
                action,
                email: ["EMAIL", "CONTACT"].includes(action) ? email : null,
            }),
        });
        if (version !== state.conversationVersion || id !== state.conversationId) return;
        state.waiting = result.data;
        renderWaiting();
        await loadHandoff();
    }

    /** 恢复不创建调查；卡片渲染后明确回执，失败可在下一次恢复安全重试。 */
    async function loadSurvey() {
        const id = ensureConversation(),
            version = state.conversationVersion;
        const base = "/customer-service/conversations/" + encodeURIComponent(id) + "/survey";
        let result;
        try {
            result = await request(base, { headers: await authHeaders() });
        } catch (error) {
            if (version === state.conversationVersion && error.status === 404) {
                state.surveyEnabled = false;
                el("survey-status").textContent = "评价功能尚未启用。";
                return;
            }
            throw error;
        }
        if (version !== state.conversationVersion || id !== state.conversationId) return;
        state.surveyEnabled = true;
        state.survey = result.data.survey;
        renderSurvey();
        if (state.survey) {
            const surveyId = state.survey.surveyId,
                headers = await authHeaders();
            if (version === state.conversationVersion && id === state.conversationId)
                await request(base + "/" + encodeURIComponent(surveyId) + "/receipt", { method: "POST", headers });
        }
    }

    /** 不满意后的人工 opt-in 独立显示，评价本身从不提交人工申请。 */
    function renderSurvey() {
        const s = state.survey,
            actions = el("survey-actions");
        actions.replaceChildren();
        const labels = {
            OPEN: "本次 AI 服务是否解决了您的问题？",
            SATISFIED: "感谢评价，很高兴为您提供帮助。",
            DISSATISFIED: "感谢反馈。是否需要转人工继续处理？",
            EXPIRED: "本次评价已过期。",
        };
        el("survey-status").textContent = s
            ? s.handoffChoice === "OPTED_IN"
                ? "已按您的选择申请人工，请查看接待进度。"
                : s.handoffChoice === "DECLINED"
                  ? "已记录评价，暂不转人工。"
                  : labels[s.status]
            : "当前没有可展示的评价。";
        const options = !s
            ? []
            : s.status === "OPEN"
              ? [
                    ["answer", "SATISFIED", "满意"],
                    ["answer", "DISSATISFIED", "不满意"],
                ]
              : s.status === "DISSATISFIED" && s.handoffChoice === "PENDING"
                ? [
                      ["handoff-choice", "OPTED_IN", "需要人工"],
                      ["handoff-choice", "DECLINED", "暂不需要"],
                  ]
                : [];
        if (pageMode === "visitor" && el("survey-panel")) el("survey-panel").hidden = !s;
        for (const [action, value, label] of options) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "secondary";
            button.textContent = label;
            button.addEventListener("click", () => run(() => decideSurvey(s, action, value), "survey-status"));
            actions.appendChild(button);
        }
    }

    /** 决策只传版本和显式答案；旧会话的迟到成功不改变当前界面。 */
    async function decideSurvey(survey, action, value) {
        const id = ensureConversation(),
            version = state.conversationVersion;
        const result = await request(
            "/customer-service/conversations/" +
                encodeURIComponent(id) +
                "/survey/" +
                encodeURIComponent(survey.surveyId) +
                "/" +
                action,
            {
                method: "POST",
                headers: await authHeaders(),
                body: JSON.stringify(
                    Object.assign(
                        { expectedVersion: survey.version },
                        action === "answer" ? { answer: value } : { choice: value },
                    ),
                ),
            },
        );
        if (version !== state.conversationVersion || id !== state.conversationId) return;
        state.survey = result.data;
        renderSurvey();
        if (value === "OPTED_IN") {
            await loadHandoff();
            await loadWaiting();
            el("followup-email").focus();
        }
    }

    async function cancelHandoff() {
        const episode = state.episode;
        if (!episode) return;
        const conversationId = ensureConversation();
        const id = requestId("cancel");
        const result = await request(
            "/customer-service/conversations/" + encodeURIComponent(conversationId) + "/reception/cancel",
            {
                method: "POST",
                headers: await authHeaders(),
                body: JSON.stringify({ requestId: id, expectedVersion: episode.version }),
            },
        );
        showResult("handoff-result", result);
        await loadHandoff();
    }

    function appendProposal(card) {
        const list = el("proposal-list");
        const existing = Array.from(list.children).find((row) => row.dataset.proposalId === card.proposalId);
        if (existing) return;
        const empty = list.querySelector(".result-box");
        if (empty) empty.remove();
        const row = document.createElement("div");
        row.className = "proposal-card";
        row.dataset.proposalId = card.proposalId;
        const title = document.createElement("strong");
        title.textContent = card.userFacingReason || card.reasonCode;
        const detail = document.createElement("small");
        detail.textContent =
            pageMode === "workbench"
                ? "状态：" + card.status + "，版本：" + card.version
                : "确认后将为您申请人工客服。";
        const actions = document.createElement("div");
        actions.className = "button-row";
        for (const [decision, label, style] of [
            ["CONFIRM", "确认转人工", "primary"],
            ["DISMISS", "暂不需要", "secondary"],
        ]) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = style;
            button.dataset.proposal = card.proposalId;
            button.dataset.version = card.version;
            button.dataset.decision = decision;
            button.textContent = label;
            actions.appendChild(button);
        }
        row.append(title, detail, actions);
        el("proposal-list").appendChild(row);
    }

    async function loadProposals() {
        const conversationId = ensureConversation();
        const version = state.conversationVersion;
        const result = await request(
            "/customer-service/conversations/" + encodeURIComponent(conversationId) + "/handoff-proposals/pending",
            { headers: await authHeaders({ "X-Request-Id": requestId("proposal") }) },
        );
        if (version !== state.conversationVersion || conversationId !== state.conversationId) return;
        const cards = result.data && Array.isArray(result.data.proposals) ? result.data.proposals : [];
        el("proposal-list").replaceChildren();
        if (cards.length) cards.forEach(appendProposal);
        else if (pageMode === "workbench") {
            const empty = document.createElement("div");
            empty.className = "result-box";
            empty.textContent = "当前没有待处理建议卡。";
            el("proposal-list").appendChild(empty);
        }
    }

    async function decideProposal(proposalId, version, decision) {
        const conversationId = ensureConversation();
        const conversationVersion = state.conversationVersion;
        const id = requestId("decision");
        const result = await request(
            "/customer-service/conversations/" +
                encodeURIComponent(conversationId) +
                "/handoff-proposals/" +
                encodeURIComponent(proposalId) +
                "/decision",
            {
                method: "POST",
                headers: await authHeaders({ "X-Request-Id": id }),
                body: JSON.stringify({ requestId: id, expectedProposalVersion: Number(version), decision }),
            },
        );
        if (conversationVersion !== state.conversationVersion || conversationId !== state.conversationId) return;
        showResult("handoff-result", result);
        await loadHandoff();
        await loadProposals();
    }

    /** 同会话、同周期、同内容的失败重试保留发送键，响应丢失时不会重复保存正文。 */
    async function sendHumanMessage(agentMode, conversationId, episodeId, content) {
        const key = agentMode ? "pendingAgentMessage" : "pendingVisitorMessage";
        let pending = state[key];
        if (
            !pending ||
            pending.conversationId !== conversationId ||
            pending.episodeId !== episodeId ||
            pending.content !== content
        ) {
            pending = { conversationId, episodeId, content, clientMsgId: requestId(agentMode ? "agent" : "visitor") };
            state[key] = pending;
        }
        const query = agentMode ? "?topOrgId=" + encodeURIComponent(ensureRuntime().topOrgId) : "";
        await request(
            "/customer-service/" +
                (agentMode ? "agents/" : "") +
                "conversations/" +
                encodeURIComponent(conversationId) +
                "/messages" +
                query,
            {
                method: "POST",
                headers: await authHeaders(),
                body: JSON.stringify({ clientMsgId: pending.clientMsgId, content }),
            },
        );
        if (state[key] === pending) state[key] = null;
    }

    /** 持久消息按 seq 去重，展示后明确回执；后台标签不自动推进 READ。 */
    async function loadHumanMessages(agentMode) {
        const id = agentMode ? el("agent-conversation-id").value.trim() : state.conversationId;
        if (!id) return;
        const version = agentMode ? state.agentVersion : state.conversationVersion;
        const key = agentMode ? "agentSeq" : "humanSeq";
        const base = "/customer-service/" + (agentMode ? "agents/" : "") + "conversations/" + encodeURIComponent(id);
        const query = agentMode ? "?topOrgId=" + encodeURIComponent(ensureRuntime().topOrgId) : "?";
        const result = await request(base + "/messages" + query + "&afterSeq=" + state[key], {
            headers: await authHeaders(),
        });
        if (
            (!agentMode && version !== state.conversationVersion) ||
            id !== (agentMode ? el("agent-conversation-id").value.trim() : state.conversationId)
        )
            return;
        await receiveTimeline(result.data, agentMode, id, version, base, query);
    }

    /** 流和补偿查询共用序列去重及回执；在等待认证前冻结 seq 并重验会话。 */
    async function receiveTimeline(page, agentMode, id, version, base, query) {
        const current = () =>
            id === (agentMode ? el("agent-conversation-id").value.trim() : state.conversationId) &&
            version === (agentMode ? state.agentVersion : state.conversationVersion);
        if (!current()) return;
        const key = agentMode ? "agentSeq" : "humanSeq";
        for (const message of page.messages) {
            if (message.seq <= state[key]) continue;
            addMessage(
                message.senderRole === "AGENT" ? "agent" : "user",
                message.content,
                agentMode ? "agent-messages" : "chat-messages",
            );
            state[key] = message.seq;
        }
        const peer = page.peerReceipt;
        if (peer)
            showResult(
                agentMode ? "agent-receipt" : "visitor-receipt",
                "对方已收到至 " + peer.deliveredSeq + "，已读至 " + peer.readSeq,
            );
        const kind = document.visibilityState === "visible" ? "READ" : "DELIVERED";
        const seq = state[key];
        if (seq > page.receipt[kind === "READ" ? "readSeq" : "deliveredSeq"]) {
            const headers = await authHeaders();
            if (current())
                await request(base + "/receipts" + query, {
                    method: "POST",
                    headers,
                    body: JSON.stringify({ kind, seq }),
                });
        }
    }

    /** 切会话立即关闭Socket和重连定时器，旧异步认证结果不再生效。 */
    function stopRealtime(key) {
        const stream = state[key];
        if (!stream) return;
        state[key] = null;
        stream.stopped = true;
        clearTimeout(stream.retry);
        clearInterval(stream.watchdog);
        stream.socket?.close();
    }
    function stopTimeline(agentMode) {
        stopRealtime(agentMode ? "agentStream" : "visitorStream");
    }

    /** 同源WS/WSS直接复用登录Bearer，首帧提交订阅选择；断线按已处理游标重连。 */
    function openRealtime(key, body, cursor, consume) {
        if (typeof WebSocket !== "function" || state[key]) return;
        const stream = { stopped: false, attempts: 0, socket: null, retry: null, watchdog: null };
        state[key] = stream;
        const current = () => state[key] === stream && !stream.stopped;
        async function connect() {
            if (!current()) return;
            try {
                const headers = await authHeaders();
                if (!current()) return;
                const url = new URL(apiBase + "/customer-service/realtime/ws", window.location.href);
                url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
                const socket = new WebSocket(url.href);
                stream.socket = socket;
                let lastReceived = Date.now(),
                    chain = Promise.resolve();
                stream.watchdog = setInterval(() => {
                    if (Date.now() - lastReceived > 35000) socket.close();
                }, 5000);
                socket.onopen = () => {
                    if (current())
                        socket.send(
                            JSON.stringify({ ...body, afterSeq: cursor(), authorization: headers.Authorization }),
                        );
                    else socket.close();
                };
                socket.onmessage = (event) => {
                    if (!current()) return;
                    lastReceived = Date.now();
                    chain = chain
                        .then(async () => {
                            if (!current()) return;
                            const frame = JSON.parse(event.data);
                            if (frame.type === "snapshot") {
                                await consume(frame.data);
                                stream.attempts = 0;
                            }
                        })
                        .catch(() => {
                            socket.close();
                        });
                };
                socket.onclose = (event) => {
                    clearInterval(stream.watchdog);
                    if (!current()) return;
                    if (event.code === 1008 && key === "agentStream") clearAgentAccess();
                    reconnect();
                };
                socket.onerror = () => socket.close();
            } catch (error) {
                if (current()) {
                    showResult(
                        key === "visitorStream" ? "handoff-result" : "agent-result",
                        "实时连接暂不可用，将自动重连：" + error.message,
                    );
                    reconnect();
                }
            }
        }
        function reconnect() {
            if (!current()) return;
            const delay = Math.min(30000, 1000 * 2 ** Math.min(stream.attempts++, 5));
            stream.retry = setTimeout(connect, delay);
        }
        connect();
    }

    /** 快照覆盖状态，消息仍按持久seq去重；无需持续刷新HTTP状态接口。 */
    function startTimeline(agentMode) {
        const key = agentMode ? "agentStream" : "visitorStream";
        const id = agentMode ? el("agent-conversation-id").value.trim() : state.conversationId;
        const version = agentMode ? state.agentVersion : state.conversationVersion;
        if (!id) return;
        if (state[key]?.id === id && state[key]?.version === version) return;
        stopTimeline(agentMode);
        const runtime = ensureRuntime(),
            base = "/customer-service/" + (agentMode ? "agents/" : "") + "conversations/" + encodeURIComponent(id);
        const query = agentMode ? "?topOrgId=" + encodeURIComponent(runtime.topOrgId) : "?";
        // 认证、历史恢复和回执均可跨异步边界；每次继续消费前重验会话代次。
        const current = () =>
            id === (agentMode ? el("agent-conversation-id").value.trim() : state.conversationId) &&
            version === (agentMode ? state.agentVersion : state.conversationVersion);
        openRealtime(
            key,
            {
                agent: agentMode,
                conversationId: id,
                topOrgId: runtime.topOrgId,
                assistantConfigId: runtime.assistantConfigId,
            },
            () => state[agentMode ? "agentSeq" : "humanSeq"],
            async (data) => {
                if (!current()) return;
                if (agentMode && data.reception) {
                    const before = state.agent;
                    state.agent = data.reception;
                    renderAgentState({ data: state.agent });
                    if (state.agent.agentId && state.agentHistoryId !== id) await agentLoad();
                    if (!current()) return;
                    if (before?.agentId && before.agentId !== state.agent.agentId) {
                        clearAgentAccess();
                        return;
                    }
                }
                if (!agentMode) {
                    state.episode = data.episode || null;
                    renderEpisode({ data: { episode: state.episode } });
                    if (data.waiting) {
                        state.waiting = data.waiting;
                        renderWaiting();
                    }
                    const priorSurvey = state.survey;
                    state.survey = data.survey || null;
                    renderSurvey();
                    if (state.survey && (!priorSurvey || priorSurvey.version !== state.survey.version)) {
                        const surveyId = state.survey.surveyId,
                            headers = await authHeaders();
                        if (!current()) return;
                        await request(
                            "/customer-service/conversations/" +
                                encodeURIComponent(id) +
                                "/survey/" +
                                encodeURIComponent(surveyId) +
                                "/receipt",
                            { method: "POST", headers },
                        );
                        if (!current()) return;
                    }
                    el("proposal-list").replaceChildren();
                    (data.proposals || []).forEach(appendProposal);
                }
                if (data.timeline) await receiveTimeline(data.timeline, agentMode, id, version, base, query);
            },
        );
        if (state[key]) {
            state[key].id = id;
            state[key].version = version;
        }
    }

    /** 队列独立订阅，即使尚未选择接待，也能实时出现新申请。 */
    function startQueueRealtime() {
        const runtime = ensureRuntime();
        openRealtime(
            "queueStream",
            {
                agent: true,
                conversationId: null,
                topOrgId: runtime.topOrgId,
                assistantConfigId: runtime.assistantConfigId,
            },
            () => 0,
            async (data) => {
                renderQueue(data.queue || []);
                renderFollowups(data.followups || []);
            },
        );
    }

    async function agentLoad() {
        if (pageMode === "agent") {
            el("agent-reception-panel").hidden = false;
            el("reply-panel").hidden = true;
        }
        const id = el("agent-conversation-id").value.trim();
        if (!id) throw new Error("请输入接待 conversationId");
        if (state.agentSelected !== id) {
            stopTimeline(true);
            state.agentSelected = id;
            state.agentVersion++;
            state.agent = null;
            state.agentSeq = 0;
            state.agentHistoryId = "";
            el("agent-messages").replaceChildren();
        }
        const version = state.agentVersion;
        const current = () => version === state.agentVersion && id === el("agent-conversation-id").value.trim();
        const result = await request(
            "/customer-service/agents/conversations/" +
                encodeURIComponent(id) +
                "/reception?topOrgId=" +
                encodeURIComponent(ensureRuntime().topOrgId),
            { headers: await authHeaders() },
        );
        if (!current()) return;
        state.agent = result.data;
        renderAgentState(result);
        startTimeline(true);
        if (state.agent.agentId) {
            if (state.agentHistoryId !== id) {
                const history = await request(
                    "/customer-service/agents/conversations/" +
                        encodeURIComponent(id) +
                        "/history?topOrgId=" +
                        encodeURIComponent(ensureRuntime().topOrgId),
                    { headers: await authHeaders() },
                );
                if (!current()) return;
                el("agent-messages").replaceChildren();
                state.agentHistoryId = id;
                history.data.forEach((item) =>
                    addMessage(item.role === "user" ? "user" : "assistant", item.content, "agent-messages"),
                );
            }
            await loadHumanMessages(true);
            if (current()) startTimeline(true);
        }
    }

    /** 首次HTTP恢复和Socket状态更新共用按钮状态，不重复请求历史。 */
    function renderAgentState(result) {
        el("agent-claim").disabled = state.agent.status !== "REQUESTED";
        const active = ["CLAIMED", "ACTIVE"].includes(state.agent.status);
        el("agent-send").disabled = !active;
        el("agent-resolve").disabled = !active;
        if (el("agent-transfer-panel")) {
            el("agent-transfer-panel").hidden = !active && !pendingTransfer;
            el("agent-transfer").disabled = !active && !pendingTransfer;
        }
        showResult("agent-result", result);
    }

    /** 授权撤销后清除已加载正文并停止旧连接，保留未确认改派的稳定键用于恢复结果。 */
    function clearAgentAccess() {
        stopTimeline(true);
        state.agent = null;
        state.agentVersion++;
        state.agentHistoryId = "";
        state.agentSeq = 0;
        el("agent-messages")?.replaceChildren();
        if (el("agent-receipt")) el("agent-receipt").textContent = "";
        if (el("agent-input")) el("agent-input").value = "";
        for (const id of ["agent-send", "agent-resolve", "agent-claim"]) if (el(id)) el(id).disabled = true;
        if (el("agent-transfer")) el("agent-transfer").disabled = !pendingTransfer;
    }

    /** 在线状态用于展示和接收改派；手动接单独立于状态及心跳，容量仍由服务器计算。 */
    async function agentAvailability(status, heartbeat = false) {
        if (!el("agent-availability-status")) return;
        const r = ensureRuntime(),
            query =
                "?topOrgId=" +
                encodeURIComponent(r.topOrgId) +
                "&assistantConfigId=" +
                encodeURIComponent(r.assistantConfigId);
        const result = await request(
            "/customer-service/agents/availability" + (heartbeat ? "/heartbeat" : "") + query,
            {
                method: status || heartbeat ? "POST" : "GET",
                headers: await authHeaders(),
                ...(status || heartbeat
                    ? { body: JSON.stringify(heartbeat ? { version: availabilityVersion } : { status }) }
                    : {}),
            },
        );
        if (status) availabilityWanted = status;
        const a = result.data;
        if (status) availabilityVersion = a.version;
        el("agent-availability-status").textContent =
            ({ ONLINE: "在线", AWAY: "暂离", OFFLINE: "离线" }[a.status] || a.status) +
            " · 当前接待 " +
            a.activeCount +
            "/" +
            a.capacity;
    }
    /** 心跳只续在线展示和改派资格；失败停止续约，不把它作为手动接单前提。 */
    async function pulseAvailability() {
        if (availabilityWanted !== "ONLINE" || availabilityPolling) return;
        availabilityPolling = true;
        try {
            availabilityFlight = agentAvailability(null, true);
            await availabilityFlight;
        } catch (error) {
            availabilityWanted = null;
            availabilityVersion = null;
            showResult("agent-availability-status", "在线状态更新失败，如需接收改派请重新设为在线：" + error.message);
        } finally {
            availabilityPolling = false;
        }
    }
    /** 明确选择等待已发心跳完成，避免本页迟到上线覆盖下线。 */
    async function selectAvailability(status) {
        availabilityWanted = null;
        await availabilityFlight.catch(() => {});
        availabilityWanted = null;
        await agentAvailability(status);
    }
    /** 候选分页只负责展示，执行改派时服务端再次核验目标资格及容量。 */
    async function transferCandidates() {
        const r = ensureRuntime(),
            select = el("agent-transfer-target");
        select.replaceChildren();
        let cursor = "",
            seen = new Set();
        do {
            if (seen.has(cursor)) throw new Error("客服目录分页异常，请稍后重试");
            seen.add(cursor);
            const result = await request(
                "/customer-service/agents/transfer-candidates?topOrgId=" +
                    encodeURIComponent(r.topOrgId) +
                    "&assistantConfigId=" +
                    encodeURIComponent(r.assistantConfigId) +
                    "&afterCursor=" +
                    encodeURIComponent(cursor),
                { headers: await authHeaders() },
            );
            for (const c of result.data.candidates) {
                const option = document.createElement("option");
                option.value = c.accountId;
                option.textContent = c.displayName;
                select.appendChild(option);
            }
            if (!result.data.hasMore) break;
            cursor = result.data.nextCursor;
            if (!cursor || seen.size >= 100) throw new Error("客服目录分页异常，请稍后重试");
        } while (true);
        if (!select.children.length) showResult("agent-result", "当前没有同组可改派客服。");
    }
    /** 响应丢失时锁定目标和原版本并复用首次键；确认成功后立即清除原客服时间线。 */
    async function transferAgent() {
        if (!pendingTransfer) {
            if (!state.agent || !el("agent-transfer-target").value) throw new Error("请先选择当前接待和接手客服");
            pendingTransfer = {
                id: state.agent.scope.conversationId,
                requestId: requestId("transfer"),
                expectedVersion: state.agent.version,
                targetAccountId: el("agent-transfer-target").value,
            };
        }
        const p = pendingTransfer;
        el("agent-transfer-target").disabled = true;
        el("agent-transfer-candidates").disabled = true;
        try {
            await request(
                "/customer-service/agents/conversations/" +
                    encodeURIComponent(p.id) +
                    "/transfer?topOrgId=" +
                    encodeURIComponent(ensureRuntime().topOrgId),
                {
                    method: "POST",
                    headers: await authHeaders(),
                    body: JSON.stringify({
                        requestId: p.requestId,
                        expectedVersion: p.expectedVersion,
                        targetAccountId: p.targetAccountId,
                    }),
                },
            );
        } catch (error) {
            if ([400, 403, 409].includes(error.status)) {
                pendingTransfer = null;
                el("agent-transfer-target").disabled = false;
                el("agent-transfer-candidates").disabled = false;
            } else {
                el("agent-transfer").textContent = "重试确认改派";
                error.message = "改派结果未确认，请重试确认：" + error.message;
            }
            throw error;
        }
        pendingTransfer = null;
        clearAgentAccess();
        el("agent-transfer-panel").hidden = true;
        el("agent-transfer-target").disabled = false;
        el("agent-transfer-candidates").disabled = false;
        el("agent-transfer").textContent = "确认改派";
        await agentQueue();
        showResult("agent-result", "已改派，由接手客服继续处理本次咨询。");
    }
    /** 生产死信最小摘要；重试只重置已有预算，不接受系统来源或目标身份输入。 */
    async function systemFailures() {
        const r = ensureRuntime(),
            query =
                "?topOrgId=" +
                encodeURIComponent(r.topOrgId) +
                "&assistantConfigId=" +
                encodeURIComponent(r.assistantConfigId);
        const result = await request("/customer-service/agents/system-handoffs" + query, {
            headers: await authHeaders(),
        });
        const list = el("agent-system-list");
        list.replaceChildren();
        for (const item of result.data) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "secondary";
            button.textContent = "重试转接 · " + item.conversationId;
            button.addEventListener("click", () =>
                run(async () => {
                    await request(
                        "/customer-service/agents/conversations/" +
                            encodeURIComponent(item.conversationId) +
                            "/system-handoff/retry" +
                            query,
                        { method: "POST", headers: await authHeaders(), body: "{}" },
                    );
                    await systemFailures();
                    showResult("agent-system-status", "已恢复重试；待服务端确认后将在接待队列中出现。");
                }, "agent-system-status"),
            );
            list.appendChild(button);
        }
        showResult(
            "agent-system-status",
            result.data.length ? "请先确认相关服务已恢复，再重试转接。" : "当前没有需要人工恢复的系统转接。",
        );
    }

    async function agentCommand(action) {
        if (!state.agent) throw new Error("请先恢复接待");
        const id = state.agent.scope.conversationId;
        await request(
            "/customer-service/agents/conversations/" +
                encodeURIComponent(id) +
                "/" +
                action +
                "?topOrgId=" +
                encodeURIComponent(ensureRuntime().topOrgId),
            {
                method: "POST",
                headers: await authHeaders(),
                body: JSON.stringify({ requestId: requestId(action), expectedVersion: state.agent.version }),
            },
        );
        await agentLoad();
    }

    async function agentSend() {
        const content = el("agent-input").value.trim();
        if (!content) return;
        if (!state.agent) throw new Error("请先恢复接待");
        await sendHumanMessage(true, state.agent.scope.conversationId, state.agent.episodeId, content);
        el("agent-input").value = "";
        await agentLoad();
    }

    async function agentQueue() {
        const runtime = ensureRuntime();
        const result = await request(
            "/customer-service/agents/queue?topOrgId=" +
                encodeURIComponent(runtime.topOrgId) +
                "&assistantConfigId=" +
                encodeURIComponent(runtime.assistantConfigId),
            { headers: await authHeaders() },
        );
        renderQueue(result.data.episodes);
        startQueueRealtime();
    }

    /** 按服务端过滤后的队列渲染，不将待接单当作已接待。 */
    function renderQueue(episodes) {
        const list = el("agent-queue-list");
        list.replaceChildren();
        for (const s of episodes) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "secondary";
            button.textContent =
                pageMode === "workbench"
                    ? s.status + " · " + s.scope.conversationId
                    : ({ REQUESTED: "待接单", CLAIMED: "我的接待", ACTIVE: "接待中" }[s.status] || "接待") +
                      " · " +
                      new Date(s.requestedAt).toLocaleString("zh-CN");
            button.addEventListener("click", () => {
                if (pendingTransfer) {
                    showResult("agent-result", "请先重试确认当前改派结果。");
                    return;
                }
                el("agent-conversation-id").value = s.scope.conversationId;
                run(agentLoad, "agent-result");
            });
            list.appendChild(button);
        }
        if (!episodes.length) showResult("agent-result", "当前没有可接单或本人接待。");
    }

    /** 跟进选择保存原周期，同一会话再次申请人工后仍操作原工单。 */
    let selectedFollowup = null;
    /** 留言提交与认领都复用首次请求键，网络失败后不新建业务事实。 */
    const pendingForms = new Map();
    let followupFormView = null;

    /** 刷新或重连后按原会话恢复已提交留言；切换会话时丢弃迟到正文，不重复创建表单。 */
    async function restoreVisitorForm() {
        const id = state.conversationId,
            version = state.conversationVersion;
        const result = await request("/customer-service/conversations/" + encodeURIComponent(id) + "/followup-form", {
            headers: await authHeaders(),
        });
        if (id !== state.conversationId || version !== state.conversationVersion || !result.data.ticketId) return;
        el("followup-contact-name").value = result.data.contactName || "";
        el("followup-form-content").value = result.data.content;
        showResult(
            "followup-form-status",
            "留言已提交，工单 " +
                result.data.ticketId +
                "，" +
                (result.data.state === "CLAIMED" ? "客服已认领。" : "等待客服认领。"),
        );
    }

    /** 提交明确的留言正文，邮箱由原等待owner校验并与工单一起保存。 */
    async function submitFollowupForm() {
        if (!state.conversationId || !state.episode) throw new Error("请先联系人工客服，再提交留言");
        await loadWaiting();
        const id = state.conversationId;
        const key = id + ":" + state.episode.episodeId;
        let pending = pendingForms.get(key);
        if (!pending) {
            const email = el("followup-email").value.trim();
            const content = el("followup-form-content").value.trim();
            if (!email || !content) throw new Error("请填写有效邮箱和留言内容");
            pending = {
                requestId: requestId("form"),
                expectedVersion: state.waiting.version,
                email,
                contactName: el("followup-contact-name").value.trim(),
                content,
            };
            pendingForms.set(key, pending);
        }
        const result = await request("/customer-service/conversations/" + encodeURIComponent(id) + "/followup-form", {
            method: "POST",
            headers: await authHeaders(),
            body: JSON.stringify(pending),
        });
        pendingForms.delete(key);
        if (id !== state.conversationId) return;
        showResult("followup-form-status", "留言已提交，工单 " + result.data.ticketId + "，等待客服认领。");
        await loadWaiting();
    }

    /** 认领前只加载摘要，认领后服务器才返回访客留言正文。 */
    async function loadFollowupForm(item) {
        if (!el("followup-form-detail")) return true;
        const result = await request(replyPath(item) + "/form" + replyQuery(), { headers: await authHeaders() });
        if (item !== selectedFollowup) return false;
        followupFormView = result.data;
        const exists = !!followupFormView.ticketId;
        const claimed = Object.prototype.hasOwnProperty.call(followupFormView, "content");
        el("claim-followup-form").hidden = !exists || claimed || followupFormView.state !== "OPEN";
        el("followup-form-detail").textContent = claimed
            ? (followupFormView.contactName || "访客") + "：\n" + followupFormView.content
            : "";
        el("followup-form-status").textContent = !exists
            ? "历史邮件工单，无独立留言表单。"
            : claimed
              ? "已认领此留言。"
              : followupFormView.state === "OPEN"
                ? "待认领；认领后可查看留言并回复。"
                : "该留言已由其他客服认领。";
        return !exists || claimed;
    }

    /** 仅以当前客服身份认领原周期，异步切换工单后不覆盖新选择。 */
    async function claimFollowupForm() {
        const item = requireFollowup();
        if (!followupFormView || followupFormView.state !== "OPEN") throw new Error("请先刷新留言状态");
        item.formClaimRequestId ||= requestId("form-claim");
        await request(replyPath(item) + "/claim" + replyQuery(), {
            method: "POST",
            headers: await authHeaders(),
            body: JSON.stringify({ requestId: item.formClaimRequestId, expectedVersion: followupFormView.version }),
        });
        if (item === selectedFollowup) await loadReply();
    }
    /** 后续跟进独立于在线队列，只展示工单引用及实际投递状态。 */
    async function agentFollowups() {
        const runtime = ensureRuntime();
        const result = await request(
            "/customer-service/agents/followups?topOrgId=" +
                encodeURIComponent(runtime.topOrgId) +
                "&assistantConfigId=" +
                encodeURIComponent(runtime.assistantConfigId),
            { headers: await authHeaders() },
        );
        renderFollowups(result.data);
    }

    /** 跟进列表只接收组内最小投影，选择仍固定原周期。 */
    function renderFollowups(items) {
        const list = el("agent-followup-list");
        list.replaceChildren();
        for (const item of items) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "secondary";
            button.textContent = (item.ticketId || "待补邮箱") + " · " + item.scope.conversationId;
            button.addEventListener("click", () => {
                if (pendingTransfer) {
                    showResult("agent-result", "请先重试确认当前改派结果。");
                    return;
                }
                selectedFollowup = item;
                replyView = null;
                renderReply(null);
                if (pageMode === "agent") {
                    stopTimeline(true);
                    state.agent = null;
                    state.agentSelected = "";
                    state.agentVersion++;
                    el("agent-reception-panel").hidden = true;
                    el("reply-panel").hidden = false;
                    el("agent-transfer-panel").hidden = true;
                }
                el("agent-conversation-id").value = item.scope.conversationId;
                run(async () => {
                    await agentWaiting();
                    if (item.ticketId) await loadReply();
                }, "agent-followup-status");
            });
            list.appendChild(button);
        }
        if (!items.length) showResult("agent-followup-status", "当前没有后续跟进。");
    }

    /** 失败通知需客服显式重试；SENT 代表网关已受理，不能显示为邮件送达。 */
    async function agentWaiting(retry = false) {
        const id = el("agent-conversation-id").value.trim();
        if (!id) throw new Error("请输入接待 conversationId");
        const base = "/customer-service/agents/conversations/" + encodeURIComponent(id);
        const chosen = selectedFollowup?.scope.conversationId === id ? selectedFollowup : null;
        const query =
            "?topOrgId=" +
            encodeURIComponent(ensureRuntime().topOrgId) +
            (chosen ? "&episodeId=" + encodeURIComponent(chosen.episodeId) : "");
        if (retry)
            await request(base + "/notifications/retry" + query, { method: "POST", headers: await authHeaders() });
        const result = await request(base + "/waiting" + query, { headers: await authHeaders() });
        if (id === el("agent-conversation-id").value.trim() && (!chosen || chosen === selectedFollowup)) {
            const w = result.data;
            showResult(
                "agent-followup-status",
                pageMode === "workbench"
                    ? w
                    : (w.ticketId ? "工单 " + w.ticketId + "；" : "待访客填写邮箱；") +
                          (w.deliveries || [])
                              .map(
                                  (d) =>
                                      ({
                                          PENDING: "通知待投递",
                                          CLAIMED: "通知投递中",
                                          SENT: "通知网关已受理",
                                          DEAD: "通知失败，需重试",
                                      })[d.status] || d.status,
                              )
                              .join("，"),
            );
        }
    }

    /** 精确工单的最终回复，跨周期/会话切换时丢弃迟到响应；失败重试保留首次请求键。 */
    let replyView = null;
    const pendingReplies = new Map();
    function replyPath(item) {
        return (
            "/customer-service/agents/conversations/" +
            encodeURIComponent(item.scope.conversationId) +
            "/followups/" +
            encodeURIComponent(item.episodeId)
        );
    }
    function replyQuery() {
        return "?topOrgId=" + encodeURIComponent(ensureRuntime().topOrgId);
    }
    function requireFollowup() {
        if (!selectedFollowup?.ticketId) throw new Error("请先从后续跟进中选择已有邮箱的工单");
        return selectedFollowup;
    }
    /** 刷新只读取原周期，不根据页面会话框改写工单目标。 */
    async function loadReply() {
        const item = requireFollowup();
        if (!(await loadFollowupForm(item))) {
            replyView = null;
            renderReply(null);
            return;
        }
        const result = await request(replyPath(item) + replyQuery(), { headers: await authHeaders() });
        if (item !== selectedFollowup) return;
        replyView = result.data;
        if (replyView.state === "OPEN") pendingReplies.delete(replyPath(item));
        renderReply(replyView);
    }
    /** 网关受理前不能结案；未知提交结果时锁住正文，先恢复状态再修改。 */
    function renderReply(view) {
        if (!el("followup-reply-status")) return;
        const pending = selectedFollowup && pendingReplies.get(replyPath(selectedFollowup));
        const labels = {
            OPEN: "待客服回复",
            PENDING: "回复已保存，等待邮件网关受理",
            CLAIMED: "邮件提交中",
            SENT: "网关已受理，可以结案；受理不表示送达或已读",
            DEAD: "自动发送失败，请核查网关后重试",
            CLOSED: "工单已结案",
        };
        el("followup-reply-status").textContent = view ? labels[view.state] : "选择后续跟进工单后可提交最终邮件回复。";
        for (const [id, field] of [
            ["followup-reply-subject", "subject"],
            ["followup-reply-content", "content"],
        ]) {
            el(id).value = view?.[field] || pending?.[field] || "";
            el(id).disabled = !view || view.state !== "OPEN" || !!pending;
        }
        el("followup-reply-submit").disabled = !view || view.state !== "OPEN";
        el("followup-reply-retry").disabled = view?.state !== "DEAD";
        el("followup-reply-close").disabled = view?.state !== "SENT";
    }
    /** 正文/版本/幂等键一并重放，响应丢失不能产生另一份最终回复。 */
    async function submitReply() {
        const item = requireFollowup();
        if (!replyView || replyView.state !== "OPEN") throw new Error("请先恢复工单回复状态");
        const path = replyPath(item);
        let pending = pendingReplies.get(path);
        if (!pending) {
            const subject = el("followup-reply-subject").value.trim(),
                content = el("followup-reply-content").value.trim();
            if (!subject || !content) throw new Error("请填写邮件主题和回复正文");
            pending = { requestId: requestId("reply"), expectedVersion: replyView.version, subject, content };
            pendingReplies.set(path, pending);
        }
        renderReply(replyView);
        const result = await request(path + "/reply" + replyQuery(), {
            method: "POST",
            headers: await authHeaders(),
            body: JSON.stringify(pending),
        });
        pendingReplies.delete(path);
        if (item === selectedFollowup) {
            replyView = result.data;
            renderReply(replyView);
        }
    }
    /** 失败只恢复同一投递键；结案请求键在响应丢失后沿用。 */
    async function replyCommand(action) {
        const item = requireFollowup();
        if (!replyView) throw new Error("请先恢复工单回复状态");
        if (action === "close" && !item.closeRequestId) item.closeRequestId = requestId("close");
        const result = await request(replyPath(item) + "/" + action + replyQuery(), {
            method: "POST",
            headers: await authHeaders(),
            body: JSON.stringify({
                requestId: action === "close" ? item.closeRequestId : requestId("retry"),
                expectedVersion: replyView.version,
            }),
        });
        if (item === selectedFollowup) {
            replyView = result.data;
            renderReply(replyView);
        }
    }

    async function restoreConversation() {
        const id = el("restore-conversation-id").value.trim();
        if (!id) throw new Error("请输入 conversationId");
        setConversation(id);
        state.episode = null;
        el("chat-messages").replaceChildren();
        const version = state.conversationVersion;
        const history = await request(
            "/agent/history_detail?sessionId=" +
                encodeURIComponent(id) +
                "&businessType=event-assistant&assistantConfigId=" +
                encodeURIComponent(ensureRuntime().assistantConfigId),
            { headers: await authHeaders() },
        );
        if (version !== state.conversationVersion || id !== state.conversationId) return;
        const data = history.returnObj || history;
        if (history.isSuccess === false) throw new Error(history.message || "会话历史读取失败");
        state.bound = true;
        (data.messages || data.details || []).forEach((m) =>
            addMessage(m.role === "user" ? "user" : "assistant", m.content || m.originalUserInput),
        );
        await loadHandoff();
        await loadProposals();
        if (state.episode) await loadHumanMessages();
    }

    async function run(handler, resultId) {
        if (state.busy) return;
        state.busy = true;
        if (el("send-message")) el("send-message").disabled = true;
        if (document.querySelectorAll)
            document.querySelectorAll("button:not([data-proposal])").forEach((button) => {
                button.inert = true;
            });
        try {
            await handler();
        } catch (error) {
            showResult(resultId, error.message);
        } finally {
            state.busy = false;
            if (el("send-message")) el("send-message").disabled = false;
            if (document.querySelectorAll)
                document.querySelectorAll("button:not([data-proposal])").forEach((button) => {
                    button.inert = false;
                });
        }
    }

    function bind(id, handler, resultId) {
        el(id)?.addEventListener("click", () => run(handler, resultId));
    }

    function init() {
        if (initialized) return;
        initialized = true;
        const manager = typeof authManager !== "undefined" ? authManager : null;
        if (manager) {
            manager
                .init()
                .then(async () => {
                    // OIDC SDK 初始化成功不等于返回了有效 Token，验证后才能展示已登录。
                    await authHeaders();
                    el("auth-state").textContent = "已获取登录凭据";
                    const info = manager.userInfo || {};
                    if (info.tenantId && el("directory-tenant")) el("directory-tenant").value = info.tenantId;
                    if (info.accountId && el("directory-principal")) el("directory-principal").value = info.accountId;
                    if (pageMode !== "workbench") {
                        const params = new URLSearchParams(window.location.search);
                        const assistant = params.get("assistantConfigId");
                        if (!assistant) {
                            el("runtime-state").textContent = "入口缺少服务配置，请使用完整服务链接";
                            return;
                        }
                        try {
                            el("assistant-config-id").value = assistant;
                            await loadRuntime();
                            el("runtime-state").textContent = "服务已就绪";
                            if (pageMode === "agent") {
                                await agentQueue();
                                await agentFollowups();
                                await agentAvailability().catch((error) =>
                                    showResult("agent-availability-status", error.message),
                                );
                            } else if (params.get("conversationId")) {
                                el("restore-conversation-id").value = params.get("conversationId");
                                await restoreConversation();
                            }
                        } catch (error) {
                            el("runtime-state").textContent = "服务暂不可用";
                            showResult(pageMode === "agent" ? "agent-result" : "handoff-result", error.message);
                        }
                    }
                })
                .catch(() => {
                    el("auth-state").textContent = "登录失败，请先完成登录";
                });
        }
        // 开发 SSO 允许匿名初始化，因此提供明确的登录入口，不把 SDK 的空令牌当登录成功。
        bind(
            "login-auth",
            async () => {
                if (!manager || !manager.oidcAuth) throw new Error("登录组件尚未就绪，请刷新页面");
                await manager.login();
            },
            "auth-state",
        );
        bind("load-runtime", loadRuntime, "handoff-result");
        bind("new-session", createSession, "handoff-result");
        bind("request-handoff", requestHandoff, "handoff-result");
        bind("load-handoff", loadHandoff, "handoff-result");
        bind("cancel-handoff", cancelHandoff, "handoff-result");
        bind("load-proposals", loadProposals, "handoff-result");
        bind("load-waiting", loadWaiting, "waiting-status");
        bind("continue-waiting", () => chooseWaiting("CONTINUE"), "waiting-status");
        bind("save-contact-email", () => chooseWaiting("CONTACT"), "waiting-status");
        bind("email-followup", () => chooseWaiting("EMAIL"), "waiting-status");
        bind("load-survey", loadSurvey, "survey-status");
        bind("agent-followups", agentFollowups, "agent-followup-status");
        bind("agent-waiting", () => agentWaiting(), "agent-followup-status");
        bind("agent-retry-notifications", () => agentWaiting(true), "agent-followup-status");
        bind("followup-reply-load", loadReply, "followup-reply-status");
        bind("followup-reply-submit", submitReply, "followup-reply-status");
        bind("followup-reply-retry", () => replyCommand("retry"), "followup-reply-status");
        bind("followup-reply-close", () => replyCommand("close"), "followup-reply-status");
        bind("restore-conversation", restoreConversation, "handoff-result");
        bind("agent-queue", agentQueue, "agent-result");
        bind("submit-followup-form", submitFollowupForm, "followup-form-status");
        bind("claim-followup-form", claimFollowupForm, "followup-form-status");
        bind("agent-load", agentLoad, "agent-result");
        bind("agent-claim", () => agentCommand("claim"), "agent-result");
        bind("agent-resolve", () => agentCommand("resolve"), "agent-result");
        bind("agent-send", agentSend, "agent-result");
        bind("agent-online", () => selectAvailability("ONLINE"), "agent-availability-status");
        bind("agent-away", () => selectAvailability("AWAY"), "agent-availability-status");
        bind("agent-offline", () => selectAvailability("OFFLINE"), "agent-availability-status");
        bind("agent-transfer-candidates", transferCandidates, "agent-result");
        bind("agent-transfer", transferAgent, "agent-result");
        bind("agent-system-failures", systemFailures, "agent-system-status");
        if (pageMode === "agent" && typeof setInterval === "function") setInterval(pulseAvailability, 15000);
        if (window.addEventListener)
            window.addEventListener("pagehide", () => {
                availabilityWanted = null;
                stopTimeline(false);
                stopTimeline(true);
                stopRealtime("queueStream");
            });
        el("chat-form")?.addEventListener("submit", (event) => {
            // 忙碌时也必须阻止浏览器默认提交，避免重复点击触发整页导航丢失会话。
            event.preventDefault();
            return run(() => sendMessage(event), "handoff-result");
        });
        document.addEventListener("click", (event) => {
            const proposal = event.target.closest("[data-proposal]");
            if (proposal && !proposal.disabled) {
                // 卡片可在 AI 正文仍流式输出时确认，独立于聊天请求的 busy 状态。
                const buttons = proposal.parentElement.querySelectorAll("button");
                buttons.forEach((b) => {
                    b.disabled = true;
                });
                decideProposal(proposal.dataset.proposal, proposal.dataset.version, proposal.dataset.decision).catch(
                    (error) => {
                        showResult("handoff-result", error.message);
                        buttons.forEach((b) => {
                            b.disabled = false;
                        });
                    },
                );
            }
        });
    }

    window.FormalCustomerServiceWorkbench = { extractStreamText, createSseParser, receiveCard, apiBase };
    document.addEventListener("auth-scripts-loaded", init, { once: true });
    if (window.AUTH_SCRIPTS_LOADED && typeof OIDC_CONFIG !== "undefined") init();
})();
