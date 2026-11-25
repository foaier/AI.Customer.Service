using System.IO.Compression;
using System.Text;
using System.Xml;
using AI.Customer.Service.Service;

namespace AI.Customer.Service.Web.Exports;

/// <summary>
/// 将已授权的会话导出投影写成不执行单元格公式的 Office Open XML 工作簿。
/// </summary>
public sealed class ConversationXlsxWriter
{
    private const int MaximumCellTextLength = 30000;
    private const int MaximumSheetRows = 1_048_576;
    private const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypeNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <summary>
    /// 生成会话概览、逐条文本和导出范围说明工作表。
    /// </summary>
    public byte[] Write(ConversationExportPage export)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WritePackageRelationships(archive);
            WriteOverviewSheet(archive, export);
            var transcriptSheets = WriteTranscriptSheets(archive, export);
            var sheetCount = transcriptSheets.Count + 2;
            WriteNotesSheet(archive, export, sheetCount);
            WriteContentTypes(archive, sheetCount);
            WriteWorkbook(archive, sheetCount);
            WriteWorkbookRelationships(archive, sheetCount);
        }

        return output.ToArray();
    }

    private static void WriteContentTypes(ZipArchive archive, int sheetCount)
    {
        WriteXml(archive, "[Content_Types].xml", writer =>
        {
            writer.WriteStartElement("Types", ContentTypeNamespace);
            writer.WriteStartElement("Default", ContentTypeNamespace);
            writer.WriteAttributeString("Extension", "rels");
            writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-package.relationships+xml");
            writer.WriteEndElement();
            writer.WriteStartElement("Default", ContentTypeNamespace);
            writer.WriteAttributeString("Extension", "xml");
            writer.WriteAttributeString("ContentType", "application/xml");
            writer.WriteEndElement();
            writer.WriteStartElement("Override", ContentTypeNamespace);
            writer.WriteAttributeString("PartName", "/xl/workbook.xml");
            writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            writer.WriteEndElement();
            for (var sheet = 1; sheet <= sheetCount; sheet++)
            {
                writer.WriteStartElement("Override", ContentTypeNamespace);
                writer.WriteAttributeString("PartName", $"/xl/worksheets/sheet{sheet}.xml");
                writer.WriteAttributeString("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        });
    }

    private static void WritePackageRelationships(ZipArchive archive) => WriteXml(archive, "_rels/.rels", writer =>
    {
        writer.WriteStartElement("Relationships", PackageRelationshipNamespace);
        writer.WriteStartElement("Relationship", PackageRelationshipNamespace);
        writer.WriteAttributeString("Id", "rId1");
        writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
        writer.WriteAttributeString("Target", "xl/workbook.xml");
        writer.WriteEndElement();
        writer.WriteEndElement();
    });

    private static void WriteWorkbook(ZipArchive archive, int sheetCount) => WriteXml(archive, "xl/workbook.xml", writer =>
    {
        writer.WriteStartElement("workbook", SpreadsheetNamespace);
        writer.WriteAttributeString("xmlns", "r", null, RelationshipNamespace);
        writer.WriteStartElement("sheets", SpreadsheetNamespace);
        WriteSheetDefinition(writer, 1, "会话概览");
        for (var sheet = 2; sheet < sheetCount; sheet++)
        {
            WriteSheetDefinition(writer, sheet, sheet == 2 ? "聊天记录" : $"聊天记录{sheet - 1}");
        }
        WriteSheetDefinition(writer, sheetCount, "导出说明");
        writer.WriteEndElement();
        writer.WriteEndElement();
    });

    private static void WriteSheetDefinition(XmlWriter writer, int sheet, string name)
    {
        writer.WriteStartElement("sheet", SpreadsheetNamespace);
        writer.WriteAttributeString("name", name);
        writer.WriteAttributeString("sheetId", sheet.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteAttributeString("r", "id", RelationshipNamespace, $"rId{sheet}");
        writer.WriteEndElement();
    }

    private static void WriteWorkbookRelationships(ZipArchive archive, int sheetCount) => WriteXml(archive, "xl/_rels/workbook.xml.rels", writer =>
    {
        writer.WriteStartElement("Relationships", PackageRelationshipNamespace);
        for (var sheet = 1; sheet <= sheetCount; sheet++)
        {
            writer.WriteStartElement("Relationship", PackageRelationshipNamespace);
            writer.WriteAttributeString("Id", $"rId{sheet}");
            writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
            writer.WriteAttributeString("Target", $"worksheets/sheet{sheet}.xml");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    });

    private static void WriteOverviewSheet(ZipArchive archive, ConversationExportPage export)
    {
        WriteWorksheet(archive, "xl/worksheets/sheet1.xml", writer =>
        {
            var row = 1;
            WriteRow(writer, row++, ["会话ID", "助手ID", "大会ID", "首条提问", "评价状态", "人工选择", "评价提交时间(UTC)", "AI消息数", "人工消息数", "人工消息完整"]);
            foreach (var conversation in export.Conversations)
            {
                var firstQuestion = conversation.AiMessages.FirstOrDefault(message => message.Role == "user")?.Content ?? "";
                WriteRow(writer, row++,
                [
                    conversation.ConversationId,
                    export.AssistantConfigId,
                    conversation.BventId ?? "",
                    SplitText(firstQuestion).FirstOrDefault() ?? "",
                    conversation.SurveyStatus ?? "未评价",
                    conversation.HandoffChoice ?? "",
                    FormatDate(conversation.RespondedAt),
                    conversation.AiMessages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    conversation.HumanMessages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    conversation.HumanMessagesTruncated ? "否，达到安全上限" : "是"
                ]);
            }
        });
    }

    private static List<int> WriteTranscriptSheets(ZipArchive archive, ConversationExportPage export)
    {
        var sheets = new List<int>();
        var sheetNumber = 2;
        var rowNumber = 1;
        XmlWriter? writer = null;

        void OpenSheet()
        {
            sheets.Add(sheetNumber);
            var entry = archive.CreateEntry($"xl/worksheets/sheet{sheetNumber}.xml", CompressionLevel.Fastest);
            writer = XmlWriter.Create(entry.Open(), XmlSettings(closeOutput: true));
            writer.WriteStartDocument();
            writer.WriteStartElement("worksheet", SpreadsheetNamespace);
            writer.WriteStartElement("sheetData", SpreadsheetNamespace);
            rowNumber = 1;
            WriteRow(writer, rowNumber++, ["会话ID", "消息ID", "角色", "分段", "消息时间(UTC)", "消息内容"]);
        }

        void CloseSheet()
        {
            if (writer is null) return;
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
            writer.Dispose();
            writer = null;
            sheetNumber++;
        }

        OpenSheet();
        foreach (var message in EnumerateMessages(export))
        {
            foreach (var part in SplitText(message.Content))
            {
                if (rowNumber >= MaximumSheetRows)
                {
                    CloseSheet();
                    OpenSheet();
                }
                WriteRow(writer!, rowNumber++,
                [
                    message.ConversationId,
                    message.Id,
                    message.Role,
                    message.Part.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FormatDate(message.CreatedAt),
                    part
                ]);
            }
        }
        CloseSheet();
        return sheets;
    }

    private static IEnumerable<TranscriptMessage> EnumerateMessages(ConversationExportPage export)
    {
        foreach (var conversation in export.Conversations)
        {
            var messages = conversation.AiMessages
                .Select(message => new TranscriptMessage(conversation.ConversationId, message.Id, message.Role, message.Content, message.CreatedAt))
                .Concat(conversation.HumanMessages.Select(message => new TranscriptMessage(
                    conversation.ConversationId,
                    message.MessageId,
                    message.SenderRole == "AGENT" ? "客服" : "访客",
                    message.Content,
                    message.CreatedAt)))
                .OrderBy(message => message.CreatedAt)
                .ThenBy(message => message.Id, StringComparer.Ordinal);

            foreach (var message in messages)
            {
                var parts = SplitText(message.Content);
                for (var index = 0; index < parts.Count; index++)
                {
                    yield return message with { Content = parts[index], Part = index + 1 };
                }
            }
        }
    }

    private static void WriteNotesSheet(ZipArchive archive, ConversationExportPage export, int sheetNumber) =>
        WriteWorksheet(archive, $"xl/worksheets/sheet{sheetNumber}.xml", writer =>
        {
            WriteRow(writer, 1, ["项目", "说明"]);
            WriteRow(writer, 2, ["助手ID", export.AssistantConfigId]);
            WriteRow(writer, 3, ["导出时间(UTC)", export.ExportedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)]);
            WriteRow(writer, 4, ["会话数量", export.Conversations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            WriteRow(writer, 5, ["是否超过导出上限", export.HasMore ? "是；仅包含前20000条会话" : "否"]);
            WriteRow(writer, 6, ["消息安全上限", "每个会话最多导出10000条人工消息；达到上限时概览会标记不完整"]);
            WriteRow(writer, 7, ["隐私范围", "工作簿不包含访客账号、联系邮箱、模型提示词或内部请求载荷"]);
        });

    private static void WriteWorksheet(ZipArchive archive, string path, Action<XmlWriter> writeRows) =>
        WriteXml(archive, path, writer =>
        {
            writer.WriteStartElement("worksheet", SpreadsheetNamespace);
            writer.WriteStartElement("sheetData", SpreadsheetNamespace);
            writeRows(writer);
            writer.WriteEndElement();
            writer.WriteEndElement();
        });

    private static void WriteRow(XmlWriter writer, int rowNumber, IReadOnlyList<string> values)
    {
        writer.WriteStartElement("row", SpreadsheetNamespace);
        writer.WriteAttributeString("r", rowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (var column = 0; column < values.Count; column++)
        {
            writer.WriteStartElement("c", SpreadsheetNamespace);
            writer.WriteAttributeString("r", CellReference(column + 1, rowNumber));
            writer.WriteAttributeString("t", "inlineStr");
            writer.WriteStartElement("is", SpreadsheetNamespace);
            writer.WriteStartElement("t", SpreadsheetNamespace);
            writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
            writer.WriteString(StripInvalidXmlCharacters(values[column] ?? ""));
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static IReadOnlyList<string> SplitText(string text)
    {
        if (text.Length <= MaximumCellTextLength) return [text];
        var parts = new List<string>();
        var current = new StringBuilder(MaximumCellTextLength);
        foreach (var rune in text.EnumerateRunes())
        {
            if (current.Length + rune.Utf16SequenceLength > MaximumCellTextLength)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            current.Append(rune.ToString());
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }

    /// <summary>
    /// 移除 XML 1.0 不允许的控制字符，避免单条历史消息破坏整个工作簿。
    /// </summary>
    private static string StripInvalidXmlCharacters(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value is 0x9 or 0xA or 0xD ||
                rune.Value is >= 0x20 and <= 0xD7FF or >= 0xE000 and <= 0xFFFD or >= 0x10000 and <= 0x10FFFF)
            {
                result.Append(rune.ToString());
            }
        }
        return result.ToString();
    }

    private static string CellReference(int column, int row)
    {
        var label = new StringBuilder();
        while (column > 0)
        {
            column--;
            label.Insert(0, (char)('A' + column % 26));
            column /= 26;
        }
        return label.Append(row.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString();
    }

    private static string FormatDate(DateTime? value) => value?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private static void WriteXml(ZipArchive archive, string path, Action<XmlWriter> write)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, XmlSettings());
        writer.WriteStartDocument();
        write(writer);
        writer.WriteEndDocument();
    }

    private static XmlWriterSettings XmlSettings(bool closeOutput = false) => new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
        CloseOutput = closeOutput
    };

    private sealed record TranscriptMessage(
        string ConversationId,
        string Id,
        string Role,
        string Content,
        DateTime? CreatedAt,
        int Part = 1);
}
