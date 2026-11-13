using System.Text;
using System.Xml.Linq;
using AI.Customer.Service.Domain.Models;
using AI.Customer.Service.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

// 仅离线生成全新独立库 DDL；不打开连接、不执行 SQL、不读取 Java 数据库。
var output = Path.GetFullPath(args.Length == 0 ? "data/sql" : args[0]);
var docs = SchemaDocumentation.Load();
var dictionary = new StringBuilder("# 独立数据库字典\n\n由 ORM 模型和中文 XML 注释生成。所有表均物理保留，不启用隐式逻辑删除。\n\n");
foreach (var provider in new[] { "Sqlite", "MySql", "Kingbase" })
{
    var options = new DbContextOptionsBuilder<CustomerServiceDbContext>();
    switch (provider)
    {
        case "Sqlite":
            options.UseSqlite("Data Source=:memory:");
            break;
        case "MySql":
            options.UseMySQL("Server=localhost;Database=schema_only;User=schema_only;");
            break;
        case "Kingbase":
            options.UseNpgsql("Host=localhost;Database=schema_only;Username=schema_only");
            break;
    }
    using var db = new CustomerServiceDbContext(options.Options);
    var sql = new StringBuilder($"-- 目的：AI.Customer.Service 首次部署独立数据库；方言：{provider}。\n");
    sql.AppendLine("-- 前提：全新空库、独占建库权限、已备份目标；禁止在 Java 原业务库或已有 .NET 库执行。");
    sql.AppendLine("-- 兼容：结构与 Java 原库不同，不包含历史数据搬迁；Kingbase 要求 PostgreSQL 兼容模式。");
    sql.AppendLine("-- 回滚：失败时回滚 DDL 事务（数据库支持时）；否则仅清理本次新建的独立空库后重新部署。");
    sql.AppendLine("-- 隔离：tenant_id 来自签名身份，top_org_id 来自可信助手/用户中心；复合主键和 FK 同时约束归属。");
    sql.AppendLine("-- 约束：NOT NULL 禁止缺失必需事实，UNIQUE 保证幂等，CHECK 约束状态/租约/版本，FK 禁止跨组织引用。");
    sql.AppendLine("-- 删除：全部物理保留，不做逻辑删除；按组织停写后依赖顺序归档清理，不删除未终结工单或待投递任务。");
    foreach (var entity in db.Model.GetEntityTypes().OrderBy(x => x.GetTableName()))
    {
        var table = entity.GetTableName()!;
        var owner = SchemaDocumentation.Owner(entity.ClrType);
        var summary = docs.GetValueOrDefault("T:" + entity.ClrType.FullName, "组织内写入事务门闩，基础设施独占维护");
        sql.AppendLine($"\n-- 表 {table}：{summary}；数据 owner：{owner}；仅该 owner 写入，其他模块经应用端口读取。");
        if (provider == "Sqlite")
        {
            dictionary.AppendLine($"## {table}\n\n{summary}。Owner：{owner}。\n\n| 字段 | 必填 | 业务含义 |\n| --- | --- | --- |");
        }
        foreach (var property in entity.GetProperties())
        {
            var member = property.PropertyInfo;
            var description = docs.GetValueOrDefault("P:" + member?.DeclaringType?.FullName + "." + member?.Name, "组织并发写入版本，由基础设施事务递增");
            var column = property.GetColumnName();
            sql.AppendLine($"-- 字段 {table}.{column}：{description}；{(property.IsNullable ? "可空" : "非空，业务必需")}。");
            if (provider == "Sqlite")
            {
                dictionary.AppendLine($"| {column} | {(property.IsNullable ? "否" : "是")} | {description.Replace("|", "\\|")} |");
            }
        }
        foreach (var index in entity.GetIndexes())
        {
            sql.AppendLine($"-- {(index.IsUnique ? "唯一索引" : "查询索引")} {index.GetDatabaseName()}：{string.Join(", ", index.Properties.Select(p => p.GetColumnName()))}；用于{(index.IsUnique ? "同归属业务键去重" : "组织队列或 Worker 有界恢复")}。");
        }
        if (provider == "Sqlite")
        {
            dictionary.AppendLine();
        }
    }
    sql.AppendLine().Append(db.Database.GenerateCreateScript());
    var directory = Path.Combine(output, provider.ToLowerInvariant());
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "001_initial.sql"), sql.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
    Console.WriteLine($"已离线生成 {provider} 初始结构");
}
Directory.CreateDirectory(Path.Combine(output, "..", "..", "docs"));
File.WriteAllText(Path.GetFullPath(Path.Combine(output, "..", "..", "docs", "schema.md")), dictionary.ToString(), new UTF8Encoding(false));

/// <summary>从编译产出的中文说明恢复 schema 注释，保持模型、脚本和字典一致。</summary>
internal static class SchemaDocumentation
{
    /// <summary>加载领域及基础设施公开类型和字段的业务说明。</summary>
    public static Dictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>();
        foreach (var assembly in new[] { typeof(Conversation).Assembly, typeof(CustomerServiceDbContext).Assembly })
        {
            foreach (var element in XDocument.Load(Path.ChangeExtension(assembly.Location, ".xml")).Descendants("member"))
            {
                var name = element.Attribute("name")?.Value;
                if (name is not null)
                {
                    result[name] = string.Join(" ", (element.Element("summary")?.Value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }
        return result;
    }

    /// <summary>列出唯一主写入 owner，同库事务不改变领域边界。</summary>
    public static string Owner(Type type) => type.Name switch
    {
        nameof(Conversation) or nameof(AiTurn) or nameof(HumanMessage) or nameof(MessageReceipt) or nameof(Survey) or nameof(ResolutionEvidence) => "Conversation",
        "OrganizationGate" => "Infrastructure",
        _ => "CustomerService"
    };
}
