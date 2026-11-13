import { readFileSync, writeFileSync } from "node:fs";
import { format } from "sql-formatter";

// 只格式化本项目从 ORM 生成的初始脚本，原 Java SQL 快照保持字节不变。
for (const [provider, language] of [
    ["sqlite", "sqlite"],
    ["mysql", "mysql"],
    ["kingbase", "postgresql"],
]) {
    const file = `data/sql/${provider}/001_initial.sql`;
    writeFileSync(file, format(readFileSync(file, "utf8"), { language, tabWidth: 4, keywordCase: "upper" }) + "\n");
    console.log(`已格式化 ${provider} DDL`);
}
