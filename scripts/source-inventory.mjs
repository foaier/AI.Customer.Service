import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync } from "node:fs";
import { createHash } from "node:crypto";
import path from "node:path";

// 只读 Java 仓库并保存迁移依据的路径和摘要，既不 checkout 也不修改源文件。
const source = path.resolve(process.argv[2] || "D:/WorkCode/2_Backend/byt-cloud-agent");
const files = execFileSync("rg", ["--files"], { cwd: source, encoding: "utf8" })
    .trim()
    .split(/\r?\n/)
    .map((file) => file.replaceAll("\\", "/"))
    .filter(
        (file) =>
            /customerservice|CustomerService|Handoff|HumanMessage|AiResolution|ResolutionSurvey/.test(file) ||
            file.startsWith("specs/013-human-customer-service/") ||
            file.includes("static/customer-service/") ||
            /formal-customer-service-workbench\.(js|css)$/.test(file),
    )
    .sort();
const entries = files.map((file) => ({
    source: file,
    sha256: createHash("sha256")
        .update(readFileSync(path.join(source, file)))
        .digest("hex"),
    purpose:
        file.includes("/test/") || file.includes("/tests/")
            ? "回归依据"
            : file.startsWith("specs/")
              ? "迁移规范"
              : "业务迁移参考",
}));
const result = {
    sourceRoot: source,
    sourceCommit: execFileSync("git", ["rev-parse", "HEAD"], { cwd: source, encoding: "utf8" }).trim(),
    sourceStatus: execFileSync("git", ["status", "--porcelain"], { cwd: source, encoding: "utf8" }).trim(),
    instruction: "摘要用于来源审计，不能将 Java 原有验收结果当作 .NET 新项目验证结果。",
    entries,
};
writeFileSync("docs/source-inventory.json", JSON.stringify(result, null, 2) + "\n");
console.log(`已只读记录 ${entries.length} 项 Java 来源，原仓库状态：${result.sourceStatus || "干净"}`);
