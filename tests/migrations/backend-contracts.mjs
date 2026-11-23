import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";

// 从发布的历史 DDL 升级，验证财务分类调整保留已有路由和成员。
const db = new DatabaseSync(":memory:");
const sql = (file) => readFileSync(new URL(`../../data/sql/sqlite/${file}`, import.meta.url), "utf8");
try {
    for (const file of ["001_initial.sql", "002_inbox_auto_close.sql", "003_chat_notifications.sql"])
        db.exec(sql(file));
    const groups = [
        {
            id: "original-media-id",
            category: "MEDIA",
            name: "自定义财务组",
            members: [{ accountId: "agent-a", displayName: "客服" }],
        },
    ];
    const routes = [{ assistantConfigId: "assistant-a", groups: { MEDIA: "original-media-id" } }];
    db.prepare(
        "INSERT INTO cs_reception_configuration (id,tenant_id,top_org_id,version,groups,routes) VALUES (?,?,?,?,?,?)",
    ).run("routing", "tenant-a", "org-a", 7, JSON.stringify(groups), JSON.stringify(routes));
    db.prepare(
        "INSERT INTO cs_chat_notification_configuration (id,tenant_id,top_org_id,version,chat_group) VALUES (?,?,?,?,?)",
    ).run("chat", "tenant-a", "org-a", 1, JSON.stringify({ FINANCE: "finance-hook-123" }));
    db.exec("BEGIN");
    db.exec(sql("004_backend_contracts.sql"));
    db.exec("COMMIT");
    const upgraded = db.prepare("SELECT * FROM cs_reception_configuration").get();
    assert.equal(upgraded.version, 8);
    assert.deepEqual(JSON.parse(upgraded.groups), [{ ...groups[0], category: "FINANCE" }]);
    assert.deepEqual(JSON.parse(upgraded.routes), [{ ...routes[0], groups: { FINANCE: "original-media-id" } }]);
    const config = db.prepare("SELECT * FROM cs_chat_notification_configuration").get();
    assert.equal(config.enabled, 0);
    assert.equal(config.api_token_ciphertext, null);
    assert.deepEqual(JSON.parse(config.chat_group), { FINANCE: "finance-hook-123" });
    assert.equal(db.prepare("PRAGMA integrity_check").get().integrity_check, "ok");
    assert.deepEqual(db.prepare("PRAGMA foreign_key_check").all(), []);
    for (const table of ["ai_anonymous_conversation_access", "ai_conversation_external_identity"]) {
        assert.equal(db.prepare(`PRAGMA foreign_key_list(${table})`).all().length, 3);
    }
    console.log("PASS SQLite 001–004 升级、旧类别转换、组 ID/成员保留、版本推进、默认通知关闭及授权表外键");
} finally {
    db.close();
}
