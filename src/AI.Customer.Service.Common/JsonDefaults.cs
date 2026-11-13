using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.Customer.Service.Common;

/// <summary>业务回执、配置和持久复杂值统一使用的 JSON 契约。</summary>
public static class JsonDefaults
{
    /// <summary>camelCase 字段和枚举名称，保持 Java 客服接口语义。</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>创建独立且固定的序列化配置。</summary>
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
