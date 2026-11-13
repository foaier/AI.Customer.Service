/** 正式组织营业时间及分页导出页面；复用登录，令牌不进入URL或下载文件。 */
(function () {
    "use strict";
    const el = (id) => document.getElementById(id);
    const prefix = location.pathname.includes("/ai_chat/") ? "/ai_chat" : "";
    let version = null;
    /** 每次操作取得当前登录凭据，错误保留HTTP契约。 */
    async function api(path, method = "GET", body) {
        if (!authManager.isInitialized) await authManager.init();
        const r = await fetch(prefix + "/api/customer-service/" + path, {
            method,
            headers: {
                Authorization: authManager.getAuthorizationHeader(await authManager.getToken()),
                "Content-Type": "application/json",
            },
            body: body === undefined ? undefined : JSON.stringify(body),
        });
        const json = await r.json();
        if (!r.ok || json.isSuccess === false) throw new Error(json.message || json.businessMessage || "请求失败");
        return json.data;
    }
    /** 固定当前页面组织，缺失时不使用默认组织。 */
    function query() {
        const org = el("org").value.trim();
        if (!org) throw new Error("请先填写组织");
        return "?topOrgId=" + encodeURIComponent(org);
    }
    /** 读取持久化版本作为后续CAS依据。 */
    async function load() {
        const c = await api("configuration/calendar" + query());
        version = { org: el("org").value.trim(), value: c.version };
        for (const key of ["zone", "opens", "closes"]) el("calendar-" + key).value = c[key];
        el("calendar-version").value = c.calendarVersion;
        el("calendar-overrides").value = JSON.stringify(c.overrides, null, 2);
        el("calendar-status").textContent = "已读取工作时间，版本 " + c.version;
    }
    /** 草稿整体保存，切换组织后必须重新读取版本。 */
    async function save() {
        if (!version || version.org !== el("org").value.trim()) throw new Error("请先读取当前组织工作时间");
        const c = await api("configuration/calendar" + query(), "PUT", {
            version: version.value,
            calendarVersion: el("calendar-version").value.trim(),
            zone: el("calendar-zone").value.trim(),
            opens: el("calendar-opens").value,
            closes: el("calendar-closes").value,
            overrides: JSON.parse(el("calendar-overrides").value),
        });
        version.value = c.version;
        el("calendar-status").textContent = "工作时间已保存，版本 " + c.version;
    }
    /** 下载安全投影；提示下一页，明确导出范围，不把当前页声称为全量。 */
    async function download() {
        const assistant = el("assistant").value.trim(),
            page = Number(el("export-page").value);
        if (!assistant || !Number.isInteger(page) || page < 1) throw new Error("请选择助手和有效页码");
        const data = await api(
            "exports/conversations" +
                query() +
                "&assistantConfigId=" +
                encodeURIComponent(assistant) +
                "&page=" +
                page +
                "&size=20",
        );
        const url = URL.createObjectURL(
            new Blob([JSON.stringify(data, null, 2)], { type: "application/json;charset=utf-8" }),
        );
        const link = document.createElement("a");
        link.href = url;
        link.download = "customer-service-conversations-page-" + page + ".json";
        link.click();
        URL.revokeObjectURL(url);
        el("export-status").textContent =
            "本页 " +
            data.conversations.length +
            " 个会话。" +
            (data.hasMore ? "请继续下载第 " + (page + 1) + " 页。" : "已到最后一页。");
    }
    /** 防止重复操作与异步未处理异常，错误在各功能状态区显示。 */
    function bind(id, status, action) {
        el(id).addEventListener("click", async () => {
            el(id).disabled = true;
            try {
                await action();
            } catch (e) {
                el(status).textContent = e.message;
            } finally {
                el(id).disabled = false;
            }
        });
    }
    bind("calendar-load", "calendar-status", load);
    bind("calendar-save", "calendar-status", save);
    bind("export-conversations", "export-status", download);
})();
