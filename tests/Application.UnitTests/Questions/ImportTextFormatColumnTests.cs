using System.IO.Compression;
using System.Security;
using System.Text;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.TestTemplates;
using CleanArchitectureBase.Infrastructure.File;

namespace CleanArchitectureBase.Application.UnitTests.Questions;

/// <summary>
/// Cột tuỳ chọn "Text Format" của file import xlsx. Tạo xlsx tối thiểu bằng ZipArchive (inline strings) để không cần thư viện ghi xlsx.
/// </summary>
public class ImportTextFormatColumnTests
{
    private static string Cell(int col, int row, string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var reference = $"{(char)('A' + col)}{row}";
        return $"<c r=\"{reference}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{SecurityElement.Escape(value)}</t></is></c>";
    }

    private static byte[] BuildXlsx(string[][] rows)
    {
        var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        for (var r = 0; r < rows.Length; r++)
        {
            sheet.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < rows[r].Length; c++) sheet.Append(Cell(c, r + 1, rows[r][c]));
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string name, string content)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                w.Write(content);
            }

            Add("[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Add("_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add("xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return ms.ToArray();
    }

    private static async Task<ImportedQuestionDto> Import(params string[][] rows)
    {
        using var stream = new MemoryStream(BuildXlsx(rows));
        return await new FileService().GetQuestionFromFile(new FileStreamData { FileName = "q.xlsx", Data = stream });
    }

    [Test]
    public async Task WithoutTextFormatColumn_QuestionsHaveNoFormat()
    {
        var result = await Import(
            new[] { "Question Type", "Question Text", "Score", "Option 1", "Option 2" },
            new[] { "MultipleChoice", "Q1", "1", "*a", "b" });

        result.validQuestions.Should().HaveCount(1);
        result.validQuestions![0].TextFormat.Should().BeNull();
    }

    [Test]
    public async Task WithTextFormatColumn_FormatIsCarriedThroughAndNormalised()
    {
        var result = await Import(
            new[] { "Question Type", "Question Text", "Score", "Text Format", "Option 1", "Option 2" },
            new[] { "MultipleChoice", "Q1\n```csharp\nint a = 5;\n```", "1", "markdown", "*`a`", "`b`" },
            new[] { "MultipleChoice", "Q2", "1", "Html", "*a", "b" },
            new[] { "MultipleChoice", "Q3", "1", "Rtf", "*a", "b" });

        result.validQuestions.Should().HaveCount(2);
        result.validQuestions![0].TextFormat.Should().Be("Markdown");
        result.validQuestions[0].QuestionText.Should().Contain("```csharp");
        result.validQuestions[0].MultipleChoices![0].Text.Should().Be("`a`");
        result.validQuestions[1].TextFormat.Should().Be("Html");
        result.invalidQuestions.Should().HaveCount(1);
        result.invalidQuestions![0].TextFormat.Should().Be("Rtf");
    }
}
