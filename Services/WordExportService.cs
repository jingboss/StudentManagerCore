using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using StudentManagerCore.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StudentManagerCore.Services
{
    public class WordExportService
    {
        /// <summary>生成考场安排表Word文档</summary>
        public byte[] GenerateSeatingChart(List<ExamRoom> rooms, ExamSchedule schedule, string grade)
        {
            using var stream = new System.IO.MemoryStream();
            using var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true);

            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();

            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                var students = room.Students?
                    .OrderBy(s => s.SeatNumber)
                    .ToList() ?? new List<ExamRoomStudent>();

                // === 标题：考试名称 ===
                var titlePara = MakeParagraph(schedule.Name ?? "考试", 36, bold: true, center: true, before: "200", after: "200");
                body.AppendChild(titlePara);

                // === 考场名称（红色） ===
                var roomPara = MakeParagraph(room.RoomName ?? "", 32, bold: true, center: true, before: "100", after: "200", color: "FF0000");
                body.AppendChild(roomPara);

                // === 元数据行 ===
                string examDate = schedule.ExamDate?.ToString("yyyy年MM月dd日") ?? "";
                string printTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string metaText = $"年级：{grade}   日期：{examDate}   打印时间：{printTime}";
                var metaPara = MakeParagraph(metaText, 20, center: true, after: "200");
                body.AppendChild(metaPara);

                // === 座位表 ===
                int totalStudents = students.Count;
                int leftCount = (int)Math.Ceiling(totalStudents / 2.0);

                var table = new Table();

                // 表格属性
                var tblPr = new TableProperties(
                    new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                    new TableLayout { Type = TableLayoutValues.Fixed }
                );

                // 全局边框设置（默认细线，分隔列无边框通过单元格级覆盖）
                var tblBorders = new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 6 },
                    new BottomBorder { Val = BorderValues.Single, Size = 6 },
                    new LeftBorder { Val = BorderValues.Single, Size = 6 },
                    new RightBorder { Val = BorderValues.Single, Size = 6 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 6 }
                );
                tblPr.AppendChild(tblBorders);
                table.AppendChild(tblPr);

                // === 表头行：9列 = 座位号 | 学号 | 姓名 | 班级 | 间隔 | 座位号 | 学号 | 姓名 | 班级 ===
                var headerRow = new TableRow();
                string[] leftHeaders = { "座位号", "学号", "姓名", "班级" };
                foreach (var h in leftHeaders)
                    headerRow.AppendChild(CreateCell(h, true, "20", "2D2D2D", "FFFFFF"));

                // 间隔列（无边框，空白背景）
                headerRow.AppendChild(CreateSeparatorCell());

                foreach (var h in leftHeaders)
                    headerRow.AppendChild(CreateCell(h, true, "20", "2D2D2D", "FFFFFF"));

                table.AppendChild(headerRow);

                // === 数据行 ===
                int maxRows = Math.Max(leftCount, totalStudents - leftCount);
                for (int r = 0; r < maxRows; r++)
                {
                    var dataRow = new TableRow();

                    // 左列
                    if (r < leftCount)
                    {
                        var s = students[r];
                        dataRow.AppendChild(CreateCell(s.SeatNumber.ToString(), false, "20"));
                        dataRow.AppendChild(CreateCell(s.Student?.StudentNo ?? "", false, "20"));
                        dataRow.AppendChild(CreateCell(s.Student?.Name ?? "", false, "20"));
                        dataRow.AppendChild(CreateCell(s.Student?.ClassName ?? "", false, "20"));
                    }
                    else
                    {
                        dataRow.AppendChild(CreateCell("", false, "20"));
                        dataRow.AppendChild(CreateCell("", false, "20"));
                        dataRow.AppendChild(CreateCell("", false, "20"));
                        dataRow.AppendChild(CreateCell("", false, "20"));
                    }

                    // 间隔列
                    dataRow.AppendChild(CreateSeparatorCell());

                    // 右列
                    int rightIndex = r + leftCount;
                    if (rightIndex < totalStudents)
                    {
                        var s = students[rightIndex];
                        dataRow.AppendChild(CreateCell(s.SeatNumber.ToString(), false, "20"));
                        dataRow.AppendChild(CreateCell(s.Student?.StudentNo ?? "", false, "20"));
                        dataRow.AppendChild(CreateCell(s.Student?.Name ?? "", false, "20"));
                        dataRow.AppendChild(CreateCell(s.Student?.ClassName ?? "", false, "20"));
                    }
                    else
                    {
                        dataRow.AppendChild(CreateCell("", false, "20"));
                        dataRow.AppendChild(CreateCell("", false, "20"));
                        dataRow.AppendChild(CreateCell("", false, "20"));
                        dataRow.AppendChild(CreateCell("", false, "20"));
                    }

                    table.AppendChild(dataRow);
                }

                body.AppendChild(table);

                // === 底部说明 ===
                var footerPara = MakeParagraph("说明：请考生按指定座位号入座，遵守考场纪律。", 18, italic: true, center: true, before: "200", after: "100", color: "666666");
                body.AppendChild(footerPara);

                // === 分页 ===
                if (i < rooms.Count - 1)
                    body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
            }

            mainPart.Document.Body = body;
            mainPart.Document.Save();
            return stream.ToArray();
        }

        /// <summary>创建普通段落</summary>
        private Paragraph MakeParagraph(string text, int fontSizeHalf, bool bold = false, bool italic = false,
            bool center = false, string before = "0", string after = "0", string color = null)
        {
            var para = new Paragraph();
            var run = new Run();
            var rp = new RunProperties(
                new FontSize { Val = fontSizeHalf.ToString() },
                new FontSizeComplexScript { Val = fontSizeHalf.ToString() },
                new FontName { Val = "宋体" }
            );
            if (bold) rp.AppendChild(new Bold());
            if (italic) rp.AppendChild(new Italic());
            if (!string.IsNullOrEmpty(color)) rp.AppendChild(new Color { Val = color });

            if (center) rp.AppendChild(new Justification { Val = JustificationValues.Center });

            run.AppendChild(rp);
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            para.AppendChild(run);

            var pp = new ParagraphProperties();
            if (center) pp.AppendChild(new Justification { Val = JustificationValues.Center });
            pp.AppendChild(new SpacingBetweenLines { Before = before, After = after });
            para.AppendChild(pp);

            return para;
        }

        /// <summary>创建表格单元格（带边框）</summary>
        private TableCell CreateCell(string text, bool isHeader, string fontSize,
            string bgColor = null, string fontColor = null)
        {
            var cell = new TableCell();

            var cellProps = new TableCellProperties(
                new TableCellWidth { Width = "1400", Type = TableWidthUnitValues.Dxa },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }
            );

            if (!string.IsNullOrEmpty(bgColor))
            {
                cellProps.AppendChild(new Shading
                {
                    Val = ShadingPatternValues.Clear,
                    Color = "auto",
                    Fill = bgColor
                });
            }

            cell.AppendChild(cellProps);

            var para = new Paragraph();
            var run = new Run();
            var rp = new RunProperties(
                new FontSize { Val = fontSize },
                new FontSizeComplexScript { Val = fontSize },
                new FontName { Val = "宋体" }
            );
            if (isHeader) rp.AppendChild(new Bold());
            if (!string.IsNullOrEmpty(fontColor)) rp.AppendChild(new Color { Val = fontColor });

            run.AppendChild(rp);
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            para.AppendChild(run);
            para.AppendChild(new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { Before = "20", After = "20" }
            ));

            cell.AppendChild(para);
            return cell;
        }

        /// <summary>创建中间间隔列（无边框、空白、窄宽度、灰色细线左右装饰）</summary>
        private TableCell CreateSeparatorCell()
        {
            var cell = new TableCell();

            var cellProps = new TableCellProperties(
                new TableCellWidth { Width = "400", Type = TableWidthUnitValues.Dxa },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }
            );

            // 移除所有边框 — 用无边框实现留白间隔
            cellProps.AppendChild(new TableCellBorders(
                new TopBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None }
            ));

            // 浅灰背景，形成视觉分割
            cellProps.AppendChild(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = "F5F5F5"
            });

            cell.AppendChild(cellProps);

            // 空段落（留白）
            var para = new Paragraph();
            para.AppendChild(new ParagraphProperties(
                new SpacingBetweenLines { Before = "20", After = "20" }
            ));
            cell.AppendChild(para);

            return cell;
        }
    }
}
