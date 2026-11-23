using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AI.Customer.Service.Common.Converter;

/// <summary>
/// ORM 读取的无 Kind 时间统一按 UTC 输出。
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    /// <summary>
    /// 解析带时区时间并归一化 UTC。
    /// </summary>
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().UtcDateTime;

    /// <summary>
    /// 数据库时间统一以 Z 后缀写出，避免浏览器按本地时间解释。
    /// </summary>
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
