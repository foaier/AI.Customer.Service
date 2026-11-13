using System.Text.Json;
using System.Text.RegularExpressions;
using AI.Customer.Service.Common;
using AI.Customer.Service.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AI.Customer.Service.Infrastructure.Persistence;

/// <summary>两个本地业务 owner 共用的事务数据源，ORM 配置不进入领域层。</summary>
/// <param name="options">由数据库协议适配器创建的上下文选项。</param>
public sealed class CustomerServiceDbContext(DbContextOptions<CustomerServiceDbContext> options) : DbContext(options)
{
    /// <summary>映射独立数据库结构；不把 Java 原库增量 DDL 自动施加到本项目。</summary>
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Ignore<Aggregate>();
        model.Ignore<ConversationAggregate>();
        Configure<Conversation>(model, "ai_conversation_generation");
        Configure<Episode>(model, "cs_handoff_episode");
        Configure<Waiting>(model, "cs_waiting_workflow");
        Configure<Proposal>(model, "cs_handoff_proposal");
        Configure<Availability>(model, "cs_agent_availability");
        Configure<HumanMessage>(model, "ai_human_message");
        Configure<MessageReceipt>(model, "ai_human_receipt");
        Configure<AiTurn>(model, "ai_turn");
        Configure<Survey>(model, "ai_resolution_survey");
        Configure<ResolutionEvidence>(model, "ai_resolution_evidence");
        Configure<Followup>(model, "cs_followup_form");
        Configure<FollowupResponse>(model, "cs_followup_response");
        Configure<ReliableTask>(model, "cs_reliable_task");
        Configure<CommandReceipt>(model, "cs_command_receipt");
        Configure<RoutingConfiguration>(model, "cs_reception_configuration");
        Configure<CalendarPolicy>(model, "cs_reception_calendar");

        Json(model.Entity<RoutingConfiguration>().Property(x => x.Groups));
        Json(model.Entity<RoutingConfiguration>().Property(x => x.Routes));
        Json(model.Entity<CalendarPolicy>().Property(x => x.Overrides));
        model.Entity<Conversation>().HasIndex(x => new { x.TenantId, x.Id }).IsUnique();
        model.Entity<RoutingConfiguration>().HasIndex(x => new { x.TenantId, x.TopOrgId }).IsUnique();
        model.Entity<CalendarPolicy>().HasIndex(x => new { x.TenantId, x.TopOrgId }).IsUnique();
        model.Entity<HumanMessage>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.ConversationId, x.Seq }).IsUnique();
        model.Entity<HumanMessage>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.ConversationId, x.SenderId, x.ClientMsgId }).IsUnique();
        model.Entity<MessageReceipt>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.ConversationId, x.ActorId }).IsUnique();
        model.Entity<Survey>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.ConversationId }).IsUnique();
        model.Entity<ResolutionEvidence>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.ConversationId, x.TurnId, x.Kind }).IsUnique();
        model.Entity<Waiting>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.EpisodeId }).IsUnique();
        model.Entity<Followup>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.EpisodeId }).IsUnique();
        model.Entity<FollowupResponse>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.EpisodeId }).IsUnique();
        model.Entity<ReliableTask>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.EventKey }).IsUnique();
        model.Entity<ReliableTask>().HasIndex(x => new { x.Kind, x.State, x.DueAt });
        model.Entity<CommandReceipt>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.ConversationId, x.ActorId, x.Action, x.RequestId }).IsUnique();
        model.Entity<Episode>().HasIndex(x => new { x.TenantId, x.TopOrgId, x.AssistantConfigId, x.Status });
        model.Entity<OrganizationGate>().ToTable("cs_organization_gate");
        model.Entity<OrganizationGate>().HasKey(x => new { x.TenantId, x.TopOrgId });
        model.Entity<OrganizationGate>().Property(x => x.TenantId).HasMaxLength(64);
        model.Entity<OrganizationGate>().Property(x => x.TopOrgId).HasMaxLength(64);
        // 固定 DDL 约束只在 ORM 建模层声明，所有运行查询继续由 LINQ 和 ORM 生成。
        model.Entity<Conversation>().ToTable(t => t.HasCheckConstraint("ck_conversation_identity", "conversation_id = id AND epoch >= 1 AND next_seq >= 0"));
        model.Entity<Episode>().ToTable(t =>
        {
            t.HasCheckConstraint("ck_episode_state", "status BETWEEN 0 AND 4");
            t.HasCheckConstraint("ck_episode_assignee", "status NOT IN (1, 2, 3) OR agent_id IS NOT NULL");
        });
        model.Entity<Waiting>().ToTable(t => t.HasCheckConstraint("ck_waiting_state", "state BETWEEN 0 AND 6"));
        model.Entity<Survey>().ToTable(t => t.HasCheckConstraint("ck_survey_state", "status BETWEEN 0 AND 3 AND handoff_choice BETWEEN 0 AND 3 AND epoch >= 1"));
        model.Entity<Proposal>().ToTable(t => t.HasCheckConstraint("ck_proposal_confidence", "confidence >= 0 AND confidence <= 1 AND status BETWEEN 0 AND 3"));
        model.Entity<ReliableTask>().ToTable(t =>
        {
            t.HasCheckConstraint("ck_task_state", "state BETWEEN 0 AND 4 AND attempts BETWEEN 0 AND 5");
            t.HasCheckConstraint("ck_task_retry_audit", "total_attempts >= attempts AND manual_retry_count >= 0 AND (manual_retry_count = 0 OR (last_retried_by IS NOT NULL AND last_retried_at IS NOT NULL))");
            t.HasCheckConstraint("ck_task_lease", "(state = 1 AND lease_id IS NOT NULL AND lease_until IS NOT NULL) OR (state <> 1 AND lease_id IS NULL AND lease_until IS NULL)");
        });
        model.Entity<MessageReceipt>().ToTable(t => t.HasCheckConstraint("ck_receipt_watermark", "read_seq >= 0 AND delivered_seq >= read_seq"));
        model.Entity<HumanMessage>().ToTable(t => t.HasCheckConstraint("ck_message_role", "sender_role IN ('VISITOR', 'AGENT') AND seq > 0"));
        model.Entity<Followup>().ToTable(t =>
        {
            t.HasCheckConstraint("ck_followup_assignee", "(state = 'OPEN' AND assigned_to IS NULL) OR (state = 'CLAIMED' AND assigned_to IS NOT NULL)");
            t.HasCheckConstraint("ck_followup_origin", "origin IN ('FORM', 'EMAIL') AND (origin <> 'FORM' OR content <> '')");
        });
        foreach (var entity in model.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
            }
        }
    }

    /// <summary>每个聚合独立表及完整复合主键，组织字段同时参加引用完整性。</summary>
    private static void Configure<T>(ModelBuilder model, string table) where T : Aggregate
    {
        var entity = model.Entity<T>();
        entity.HasBaseType((Type?)null);
        entity.ToTable(table, definition => definition.HasCheckConstraint("ck_" + table + "_scope",
            "tenant_id <> '' AND top_org_id <> '' AND id <> '' AND version >= 1"));
        entity.HasKey(x => new { x.TenantId, x.TopOrgId, x.Id });
        entity.Property(x => x.Version).IsConcurrencyToken();
        foreach (var property in typeof(T).GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            entity.Property(property.Name).HasMaxLength(property.Name switch
            {
                "TenantId" or "TopOrgId" => 64,
                "Content" or "UserContent" or "AssistantContent" or "Payload" or "ResultJson" => 65535,
                "Subject" => 200,
                "UserFacingReason" => 500,
                "Email" => 254,
                "EventKey" => 512,
                _ => 128
            });
        }
        if (typeof(ConversationAggregate).IsAssignableFrom(typeof(T)))
        {
            entity.Ignore(nameof(ConversationAggregate.Scope));
            if (typeof(T) != typeof(Conversation))
            {
                entity.HasOne<Conversation>().WithMany()
                    .HasForeignKey(nameof(Aggregate.TenantId), nameof(Aggregate.TopOrgId), nameof(ConversationAggregate.ConversationId))
                    .OnDelete(DeleteBehavior.Restrict);
            }
        }
    }

    /// <summary>把同 owner 的不可变配置集合映射为 JSON，并为 ORM 提供结构比较。</summary>
    private static void Json<T>(PropertyBuilder<T> property)
    {
        property.HasConversion(
            value => JsonSerializer.Serialize(value, JsonDefaults.Options),
            value => JsonSerializer.Deserialize<T>(value, JsonDefaults.Options)!);
        property.Metadata.SetValueComparer(new ValueComparer<T>(
            (left, right) => JsonSerializer.Serialize(left, JsonDefaults.Options) == JsonSerializer.Serialize(right, JsonDefaults.Options),
            value => JsonSerializer.Serialize(value, JsonDefaults.Options).GetHashCode(),
            value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonDefaults.Options), JsonDefaults.Options)!));
    }
}

/// <summary>基础设施组织写入门闩，跨进程串行容量与本地撤权，不保存业务主数据。</summary>
internal sealed class OrganizationGate
{
    /// <summary>认证租户，复合主键组成部分。</summary>
    public string TenantId { get; set; } = "";
    /// <summary>核验组织，复合主键组成部分。</summary>
    public string TopOrgId { get; set; } = "";
    /// <summary>事务持有期间原子递增，用 ORM 类型化更新获取数据库写锁。</summary>
    public long Version { get; set; }
}
