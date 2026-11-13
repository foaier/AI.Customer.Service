(function () {
    "use strict";
    const el = (id) => document.getElementById(id);
    const categories = { EXHIBITOR: "展商", VISITOR: "观众", MEDIA: "媒体", OTHER: "其他" };
    const base = (location.pathname.includes("/ai_chat/") ? "/ai_chat" : "") + "/api";
    const params = new URL(location.href).searchParams;
    let config = null,
        busy = false,
        authReady,
        loadedOrg = "";
    let directoryGroups = [],
        directoryCursor = null,
        directoryHasMore = false;
    const memberDirectories = new Map();
    el("org").value = params.get("topOrgId") || "";
    el("assistant").value = params.get("assistantConfigId") || "";

    /** 复用项目登录组件，初始化失败允许重新登录；不复制令牌到URL或本地存储。 */
    async function auth() {
        if (!authReady)
            authReady = (async () => {
                if (typeof OIDC_CONFIG === "undefined")
                    await new Promise((resolve, reject) => {
                        const timer = setTimeout(() => reject(new Error("登录组件加载超时，请刷新重试")), 15000);
                        document.addEventListener(
                            "auth-scripts-loaded",
                            (event) => {
                                clearTimeout(timer);
                                event.detail?.error ? reject(new Error("登录组件加载失败")) : resolve();
                            },
                            { once: true },
                        );
                    });
                await authManager.init();
            })().catch((error) => {
                authReady = null;
                throw error;
            });
        await authReady;
    }

    /** 组织归属取自当前认证上下文，页面不能传入操作者或租户身份。 */
    async function request(path, method = "GET", body) {
        await auth();
        const token = await authManager.getToken();
        const authorization = authManager.getAuthorizationHeader(token);
        if (!authorization) throw new Error("请先登录");
        const response = await fetch(base + path, {
            method,
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Authorization: authorization,
                "Content-Type": "application/json",
                "x-CurrentOrgId": el("org").value.trim(),
            },
            body: body === undefined ? undefined : JSON.stringify(body),
        });
        const data = await response.json();
        if (!response.ok || data.isSuccess === false) {
            throw new Error(data.message || data.businessMessage || "HTTP " + response.status);
        }
        return data.data !== undefined ? data.data : data.returnObj !== undefined ? data.returnObj : data;
    }

    /** 页面写入串行；错误保留已编辑配置，组织或授权错误由服务端说明。 */
    async function run(action) {
        if (busy) return;
        busy = true;
        el("status").dataset.error = "false";
        document.querySelectorAll("button").forEach((button) => {
            button.disabled = true;
        });
        try {
            await action();
        } catch (error) {
            el("status").textContent = error.message;
            el("status").dataset.error = "true";
        } finally {
            busy = false;
            document.querySelectorAll("button").forEach((button) => {
                button.disabled = false;
            });
        }
    }

    /** 导航只沿用公开助手和组织引用，不传递身份或凭据。 */
    function links() {
        const query = new URLSearchParams({
            assistantConfigId: el("assistant").value.trim(),
            topOrgId: el("org").value.trim(),
        });
        document.querySelectorAll("[data-entry]").forEach((link) => {
            link.href = link.dataset.entry + "?" + query;
        });
        if (el("debug-home")) el("debug-home").href = "debug.html?" + query;
    }

    /** 为四类咨询构造本地业务组映射，切换助手只修改当前草稿。 */
    function renderRoutes() {
        links();
        if (!config) return;
        el("routes").replaceChildren();
        const assistantId = el("assistant").value.trim();
        const route = config.routes.find((item) => item.assistantConfigId === assistantId);
        for (const [category, name] of Object.entries(categories)) {
            const row = document.createElement("div");
            const label = document.createElement("label");
            const select = document.createElement("select");
            select.id = "route-" + category;
            label.htmlFor = select.id;
            label.textContent = name + "咨询 → 接待组";
            for (const group of config.groups) {
                const option = document.createElement("option");
                option.value = group.id;
                option.textContent = group.name;
                select.append(option);
            }
            select.value = route?.groups[category] || config.groups.find((group) => group.category === category).id;
            row.append(label, select);
            el("routes").append(row);
        }
        el("route-status").textContent = route
            ? "此助手已有映射。修改后需保存。"
            : "此助手尚未保存映射，保存后新申请才可进入客服组。";
    }

    /** 按业务类别保存搜索词、来源组和分页候选，重绘成员列表时保留当前目录状态。 */
    function directoryState(category) {
        if (!memberDirectories.has(category)) {
            memberDirectories.set(category, {
                groupId: "",
                keyword: "",
                candidates: [],
                nextCursor: null,
                hasMore: false,
            });
        }
        return memberDirectories.get(category);
    }

    /** 仅渲染姓名和账号ID；是否已加入本地业务组由客服配置自身决定。 */
    function renderCandidates(category, select) {
        const state = directoryState(category);
        select.replaceChildren();
        for (const candidate of state.candidates) {
            const option = document.createElement("option");
            option.value = candidate.accountId;
            option.textContent = candidate.displayName + " · " + candidate.accountId;
            option.disabled = config.groups
                .find((group) => group.category === category)
                .members.some((member) => member.accountId === candidate.accountId);
            select.append(option);
        }
    }

    /** 加载或续读用户中心组内账号；游标在组织、分组及搜索词变化后失效。 */
    async function loadCandidates(category, append) {
        const state = directoryState(category);
        if (!state.groupId) throw new Error("请先选择用户中心客服组");
        const query = new URLSearchParams({
            topOrgId: el("org").value.trim(),
            groupId: state.groupId,
            limit: "50",
        });
        if (state.keyword) query.set("keyword", state.keyword);
        if (append && state.nextCursor) query.set("afterCursor", state.nextCursor);
        else if (append) throw new Error("没有更多账号可加载");
        const page = await request("/customer-service/configuration/directory/accounts?" + query);
        if (page.status !== "READY")
            throw new Error(page.status === "NOT_CONFIGURED" ? "用户中心账号目录尚未配置" : "用户中心账号目录暂不可用");
        state.candidates = append ? state.candidates.concat(page.candidates) : page.candidates;
        state.nextCursor = page.nextCursor;
        state.hasMore = page.hasMore;
        el("directory-status").textContent = "已读取 " + state.candidates.length + " 个账号候选。";
    }

    /** 用户中心组分页已加载后再供四个本地业务组选择，不复制外部分组资料。 */
    function renderDirectoryGroups() {
        for (const category of Object.keys(categories)) {
            const select = el("source-group-" + category);
            if (!select) continue;
            const state = directoryState(category);
            select.replaceChildren();
            const placeholder = document.createElement("option");
            placeholder.value = "";
            placeholder.textContent = directoryGroups.length ? "选择用户中心客服组" : "请先读取用户中心客服组";
            select.append(placeholder);
            for (const group of directoryGroups) {
                const option = document.createElement("option");
                option.value = group.groupId;
                option.textContent = group.name;
                select.append(option);
            }
            select.value = state.groupId;
        }
        el("more-directory-groups").hidden = !directoryHasMore;
    }

    /** 读取当前组织用户中心分组；查询词改变时从第一页重新开始。 */
    async function loadDirectoryGroups(append) {
        if (!loadedOrg || loadedOrg !== el("org").value.trim()) throw new Error("请先加载当前组织配置");
        const query = new URLSearchParams({ topOrgId: loadedOrg, limit: "100" });
        const keyword = el("directory-keyword").value.trim();
        if (keyword) query.set("keyword", keyword);
        if (append && directoryCursor) query.set("afterCursor", directoryCursor);
        const page = await request("/customer-service/configuration/directory/groups?" + query);
        if (page.status !== "READY")
            throw new Error(
                page.status === "NOT_CONFIGURED" ? "用户中心客服组目录尚未配置" : "用户中心客服组目录暂不可用",
            );
        directoryGroups = append ? directoryGroups.concat(page.groups) : page.groups;
        directoryCursor = page.nextCursor;
        directoryHasMore = page.hasMore;
        if (!append) {
            for (const state of memberDirectories.values()) {
                state.groupId = "";
                state.candidates = [];
                state.nextCursor = null;
                state.hasMore = false;
            }
        }
        renderDirectoryGroups();
        el("directory-status").textContent = "已读取 " + directoryGroups.length + " 个用户中心分组。";
    }

    /** 构造本地四类业务组编辑器，候选成员只能来自用户中心现有组。 */
    function renderGroups() {
        el("groups").replaceChildren();
        for (const group of config.groups) {
            const panel = document.createElement("section");
            panel.className = "panel";
            const category = group.category;
            const state = directoryState(category);
            const title = document.createElement("h3");
            title.textContent = categories[category] + "业务组";
            const nameLabel = document.createElement("label");
            const nameInput = document.createElement("input");
            nameInput.id = "group-" + category;
            nameInput.value = group.name;
            nameInput.required = true;
            nameInput.maxLength = 64;
            nameLabel.htmlFor = nameInput.id;
            nameLabel.textContent = "分组名称";
            nameInput.addEventListener("change", () => {
                group.name = nameInput.value.trim();
                document.querySelectorAll("#routes option").forEach((option) => {
                    if (option.value === group.id) option.textContent = group.name;
                });
            });

            const members = document.createElement("ul");
            members.className = "member-list";
            if (!group.members.length) {
                const empty = document.createElement("li");
                empty.textContent = "暂无成员，请从用户中心客服组选择账号。";
                members.append(empty);
            }
            for (const member of group.members) {
                const row = document.createElement("li");
                const label = document.createElement("span");
                const accountId = document.createElement("small");
                const remove = document.createElement("button");
                label.textContent = member.displayName;
                accountId.textContent = member.accountId;
                label.append(accountId);
                remove.type = "button";
                remove.className = "secondary";
                remove.textContent = "移除";
                remove.setAttribute("aria-label", "从" + group.name + "移除" + member.displayName);
                remove.addEventListener("click", () => {
                    group.members = group.members.filter((item) => item.accountId !== member.accountId);
                    renderGroups();
                });
                row.append(label, remove);
                members.append(row);
            }

            const sourceLabel = document.createElement("label");
            const source = document.createElement("select");
            source.id = "source-group-" + category;
            sourceLabel.htmlFor = source.id;
            sourceLabel.textContent = "用户中心客服组";
            source.addEventListener("change", () => {
                state.groupId = source.value;
                state.candidates = [];
                state.nextCursor = null;
                state.hasMore = false;
                renderGroups();
            });

            const searchLabel = document.createElement("label");
            const search = document.createElement("input");
            search.type = "search";
            search.maxLength = 128;
            search.value = state.keyword;
            searchLabel.htmlFor = "search-" + category;
            search.id = "search-" + category;
            searchLabel.textContent = "搜索账号";
            search.addEventListener("input", () => {
                state.keyword = search.value.trim();
            });

            const candidateLabel = document.createElement("label");
            const candidates = document.createElement("select");
            candidates.id = "candidate-" + category;
            candidates.size = 5;
            candidateLabel.htmlFor = candidates.id;
            candidateLabel.textContent = "可加入的客服账号";
            renderCandidates(category, candidates);

            const controls = document.createElement("div");
            controls.className = "directory-actions";
            const load = document.createElement("button");
            load.type = "button";
            load.className = "secondary";
            load.textContent = "搜索账号";
            load.addEventListener("click", () =>
                run(async () => {
                    state.keyword = search.value.trim();
                    await loadCandidates(category, false);
                    renderGroups();
                }),
            );
            const more = document.createElement("button");
            more.type = "button";
            more.className = "secondary";
            more.textContent = "加载更多账号";
            more.hidden = !state.hasMore;
            more.addEventListener("click", () =>
                run(async () => {
                    await loadCandidates(category, true);
                    renderGroups();
                }),
            );
            const add = document.createElement("button");
            add.type = "button";
            add.className = "secondary";
            add.textContent = "加入" + categories[category] + "组";
            add.addEventListener("click", () =>
                run(async () => {
                    const account = state.candidates.find((item) => item.accountId === candidates.value);
                    if (!account) throw new Error("请先选择要加入的账号");
                    if (group.members.some((member) => member.accountId === account.accountId)) {
                        throw new Error("该账号已在此业务组");
                    }
                    group.members.push({ accountId: account.accountId, displayName: account.displayName });
                    renderGroups();
                    el("status").textContent = "成员已加入草稿，保存后生效。";
                }),
            );
            controls.append(load, more, add);
            panel.append(
                title,
                nameLabel,
                nameInput,
                members,
                sourceLabel,
                source,
                searchLabel,
                search,
                candidateLabel,
                candidates,
                controls,
            );
            el("groups").append(panel);
        }
        renderDirectoryGroups();
    }

    /** 加载组织配置并尝试读取用户中心目录；目录暂不可用不丢失本地配置。 */
    async function load() {
        const org = el("org").value.trim();
        if (!org) throw new Error("请填写组织 ID");
        const previousOrg = loadedOrg;
        config = await request("/customer-service/configuration?topOrgId=" + encodeURIComponent(org));
        if (previousOrg !== org) memberDirectories.clear();
        loadedOrg = org;
        el("routing-fields").disabled = false;
        directoryGroups = [];
        directoryCursor = null;
        directoryHasMore = false;
        renderGroups();
        renderRoutes();
        try {
            await loadDirectoryGroups(false);
            el("status").textContent = "已加载配置 · 版本 " + config.version;
        } catch (error) {
            el("directory-status").textContent = error.message;
            el("directory-status").dataset.error = "true";
            el("status").textContent = "配置已加载 · 版本 " + config.version + "；用户中心分组暂不可用。";
        }
    }

    el("load")?.addEventListener("click", () => run(load));
    el("reload")?.addEventListener("click", () => run(load));
    // 调试中心也使用本脚本；用户中心目录控件只在分组设置页存在。
    el("search-directory-groups")?.addEventListener("click", () =>
        run(async () => {
            directoryGroups = [];
            directoryCursor = null;
            directoryHasMore = false;
            await loadDirectoryGroups(false);
        }),
    );
    el("more-directory-groups")?.addEventListener("click", () => run(() => loadDirectoryGroups(true)));
    el("directory-keyword")?.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
            event.preventDefault();
            el("search-directory-groups").click();
        }
    });
    el("load-assistants").addEventListener("click", () =>
        run(async () => {
            if (!el("org").value.trim()) throw new Error("请填写组织 ID");
            const list = await request("/assistant/config/listAssistants");
            el("assistant-list").replaceChildren();
            for (const item of list) {
                const option = document.createElement("option");
                option.value = item.assistantConfigId;
                option.textContent = item.buttonName || item.bventName || item.assistantConfigId;
                el("assistant-list").append(option);
            }
            if (!list.length) throw new Error("此组织暂无助手");
            if (!el("assistant").value) el("assistant").value = list[0].assistantConfigId;
            el("assistant-list").value = el("assistant").value;
            renderRoutes();
            el("status").textContent = "已读取 " + list.length + " 个助手";
        }),
    );
    el("assistant-list").addEventListener("change", () => {
        el("assistant").value = el("assistant-list").value;
        renderRoutes();
    });
    el("assistant").addEventListener("change", renderRoutes);
    el("org").addEventListener("change", () => {
        const fields = el("routing-fields");
        if (fields) fields.disabled = el("org").value.trim() !== loadedOrg;
        links();
    });
    el("routing-form")?.addEventListener("submit", (event) => {
        event.preventDefault();
        run(async () => {
            if (!config || loadedOrg !== el("org").value.trim()) throw new Error("请先加载当前组织的配置");
            const assistant = el("assistant").value.trim();
            if (!assistant) throw new Error("请选择要绑定的助手");
            for (const group of config.groups) group.name = el("group-" + group.category).value.trim();
            const route = {
                assistantConfigId: assistant,
                groups: Object.fromEntries(
                    Object.keys(categories).map((category) => [category, el("route-" + category).value]),
                ),
            };
            const payload = {
                ...config,
                routes: [...config.routes.filter((item) => item.assistantConfigId !== assistant), route],
            };
            config = await request(
                "/customer-service/configuration?topOrgId=" + encodeURIComponent(loadedOrg),
                "PUT",
                payload,
            );
            renderGroups();
            renderRoutes();
            el("status").textContent = "接待配置已保存 · 版本 " + config.version + "，新申请使用此映射。";
        });
    });
    el("preview")?.addEventListener("click", () =>
        run(async () => {
            const result = await request(
                "/customer-service/configuration/preview?topOrgId=" + encodeURIComponent(el("org").value.trim()),
                "POST",
                { assistantConfigId: el("assistant").value.trim(), context: el("preview-context").value },
            );
            const group = config?.groups.find((item) => item.id === result.groupId);
            el("preview-result").textContent =
                "咨询类别：" +
                categories[result.classification.category] +
                "\n接待组：" +
                (group?.name || result.groupId) +
                "\n识别来源：" +
                result.classification.source +
                " · 置信度：" +
                result.classification.confidence +
                "\n使用已保存版本：" +
                result.configVersion;
        }),
    );
    el("login").addEventListener("click", () =>
        run(async () => {
            await auth();
            await authManager.login();
        }),
    );
    links();
    if (el("routing-fields") && el("org").value) run(load);
})();
