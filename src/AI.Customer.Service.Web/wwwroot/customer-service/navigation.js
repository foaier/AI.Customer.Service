/** 调试菜单沿用公开助手和组织引用，不携带登录态或业务命令。 */
(function () {
    const current = new URL(location.href),
        query = new URLSearchParams();
    for (const key of ["assistantConfigId", "topOrgId"])
        if (current.searchParams.get(key)) query.set(key, current.searchParams.get(key));
    document.querySelectorAll("[data-cs-navigation]").forEach((link) => {
        const target = new URL(link.href, current);
        target.search = query.toString();
        link.href = target.href;
    });
})();
