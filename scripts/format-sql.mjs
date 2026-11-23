import { readFileSync, writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(new URL("../frontend/package.json", import.meta.url));
const { format } = require("sql-formatter");
const root = fileURLToPath(new URL("../", import.meta.url));

// 格式化本项目从 ORM 生成的三种方言初始脚本。
for (const [provider, language] of [
    ["sqlite", "sqlite"],
    ["mysql", "mysql"],
    ["kingbase", "postgresql"],
]) {
    const file = path.join(root, "data", "sql", provider, "001_initial.sql");
    writeFileSync(file, format(readFileSync(file, "utf8"), { language, tabWidth: 4, keywordCase: "upper" }) + "\n");
    console.log(`已格式化 ${provider} DDL`);
}
