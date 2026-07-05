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
        /// <summary>
        /// 生成考场安排表Word文档
        /// </summary>
        /// <param name="rooms">考场列表</param>
        /// <param name="schedule">考试安排信息</param>
        /// <param name="grade">年级</param>
        /// <returns>Word文档字节数组</returns>
        public byte[] GenerateSeatingChart(List<ExamRoom> rooms, ExamSchedule schedule, string grade)
        {
            using var stream = new System.IO.MemoryStream();
            using var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true);

            // 创建主文档部分
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();

            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];

                // 获取该考场的学生列表（按座位号排序）
                var students = room.Students?
                    .OrderBy(s => s.SeatNumber)
                    .ToList() ?? new List<ExamRoomStudent>();

                // === 页面标题：考试名称 ===
                var titleParagraph = new Paragraph();
                var titleRun = new Run();
                titleRun.AppendChild(new RunProperties(
                    new Bold(),
                    new FontSize { Val = "36" },  // 18pt = 36 half-points
                    new FontSizeComplexScript { Val = "36" },
                    new Justification { Val = JustificationValues.Center }
                ));
                titleRun.AppendChild(new Text(schedule.Name ?? "考试") { Space = SpaceProcessingModeValues.Preserve });
                titleParagraph.AppendChild(titleRun);
                titleParagraph.AppendChild(new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center },
                    new SpacingBetweenLines { Before = "200", After = "200" }
                ));
                body.AppendChild(titleParagraph);

                // === 考场名称（红色） ===
                var roomPara = new Paragraph();
                var roomRun = new Run();
                var roomRunProps = new RunProperties(
                    new Bold(),
                    new FontSize { Val = "32" },
                    new FontSizeComplexScript { Val = "32" },
                    new Justification { Val = JustificationValues.Center },
                    new Color { Val = "FF0000" }  // 红色
                );
                roomRun.AppendChild(roomRunProps);
                roomRun.AppendChild(new Text(room.RoomName ?? "") { Space = SpaceProcessingModeValues.Preserve });
                roomPara.AppendChild(roomRun);
                roomPara.AppendChild(new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center },
                    new SpacingBetweenLines { Before = "100", After = "200" }
                ));
                body.AppendChild(roomPara);

                // === 元数据行：年级、日期、打印时间 ===
                var metaPara = new Paragraph();
                var metaRun = new Run();
                var metaRunProps = new RunProperties(
                    new FontSize { Val = "20" },
                    new FontSizeComplexScript { Val = "20" }
                );
                metaRun.AppendChild(metaRunProps);

                string examDate = schedule.ExamDate?.ToString("yyyy年MM月dd日") ?? "";
                string printTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string metaText = $"年级：{grade}   日期：{examDate}   打印时间：{printTime}";

                metaRun.AppendChild(new Text(metaText) { Space = SpaceProcessingModeValues.Preserve });
                metaPara.AppendChild(metaRun);
                metaPara.AppendChild(new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center },
                    new SpacingBetweenLines { After = "200" }
                ));
                body.AppendChild(metaPara);

                // === 座位表（双栏） ===
                int totalStudents = students.Count;
                int leftCount = (int)Math.Ceiling(totalStudents / 2.0);

                // 创建表格
                var table = new Table();

                // 表格宽度
                var tblPr = new TableProperties(
                    new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 6 },
                        new BottomBorder { Val = BorderValues.Single, Size = 6 },
                        new LeftBorder { Val = BorderValues.Single, Size = 6 },
                        new RightBorder { Val = BorderValues.Single, Size = 6 },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6 },
                        new InsideVerticalBorder { Val = BorderValues.Single, Size = 6 }
                    ),
                    new TableLayout { Type = TableLayoutValues.Fixed }
                );
                table.AppendChild(tblPr);

                // 表头行：双栏6列 = 座位号 | 姓名 | 班级 | 座位号 | 姓名 | 班级
                var headerRow = new TableRow();
                string[] headers = { "座位号", "姓名", "班级", "座位号", "姓名", "班级" };
                foreach (var headerText in headers)
                {
                    var cell = CreateTableCell(headerText, true, "22", "FFFFFF", "333333");
                    headerRow.AppendChild(cell);
                }
                table.AppendChild(headerRow);

                // 数据行
                int maxRows = Math.Max(leftCount, totalStudents - leftCount);
                for (int r = 0; r < maxRows; r++)
                {
                    var dataRow = new TableRow();

                    // 左列：座位号、姓名、班级
                    if (r < leftCount)
                    {
                        var student = students[r];
                        dataRow.AppendChild(CreateTableCell(student.SeatNumber.ToString(), false, "20"));
                        dataRow.AppendChild(CreateTableCell(student.Student?.Name ?? "", false, "20"));
                        dataRow.AppendChild(CreateTableCell(student.Student?.ClassName ?? "", false, "20"));
                    }
                    else
                    {
                        dataRow.AppendChild(CreateTableCell("", false, "20"));
                        dataRow.AppendChild(CreateTableCell("", false, "20"));
                        dataRow.AppendChild(CreateTableCell("", false, "20"));
                    }

                    // 右列：座位号、姓名、班级
                    int rightIndex = r + leftCount;
                    if (rightIndex < totalStudents)
                    {
                        var student = students[rightIndex];
                        dataRow.AppendChild(CreateTableCell(student.SeatNumber.ToString(), false, "20"));
                        dataRow.AppendChild(CreateTableCell(student.Student?.Name ?? "", false, "20"));
                        dataRow.AppendChild(CreateTableCell(student.Student?.ClassName ?? "", false, "20"));
                    }
                    else
                    {
                        dataRow.AppendChild(CreateTableCell("", false, "20"));
                        dataRow.AppendChild(CreateTableCell("", false, "20"));
                        dataRow.AppendChild(CreateTableCell("", false, "20"));
                    }

                    table.AppendChild(dataRow);
                }

                body.AppendChild(table);

                // === 底部说明文字 ===
                var footerPara = new Paragraph();
                var footerRun = new Run();
                var footerRunProps = new RunProperties(
                    new FontSize { Val = "18" },
                    new FontSizeComplexScript { Val = "18" },
                    new Color { Val = "666666" },
                    new Italic()
                );
                footerRun.AppendChild(footerRunProps);
                footerRun.AppendChild(new Text("说明：请考生按指定座位号入座，遵守考场纪律。") { Space = SpaceProcessingModeValues.Preserve });
                footerPara.AppendChild(footerRun);
                footerPara.AppendChild(new ParagraphProperties(
                    new Justification { Val = JustificationValues.Center },
                    new SpacingBetweenLines { Before = "200", After = "100" }
                ));
                body.AppendChild(footerPara);

                // === 如果不是最后一个考场，添加分页符 ===
                if (i < rooms.Count - 1)
                {
                    body.AppendChild(new Paragraph(
                        new Run(new Break { Type = BreakValues.Page })
                    ));
                }
            }

            mainPart.Document.Body = body;
            mainPart.Document.Save();

            return stream.ToArray();
        }

        /// <summary>
        /// 创建表格单元格
        /// </summary>
        private TableCell CreateTableCell(string text, bool isHeader, string fontSize, string bgColor = null, string fontColor = null)
        {
            var cell = new TableCell();

            // 单元格属性
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

            // 段落
            var para = new Paragraph();
            var run = new Run();

            var runProps = new RunProperties(
                new FontSize { Val = fontSize },
                new FontSizeComplexScript { Val = fontSize },
                new FontName { Val = "宋体" },
                new Justification { Val = JustificationValues.Center }
            );

            if (isHeader)
            {
                runProps.AppendChild(new Bold());
            }

            if (!string.IsNullOrEmpty(fontColor))
            {
                runProps.AppendChild(new Color { Val = fontColor });
            }

            run.AppendChild(runProps);
            run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            para.AppendChild(run);
            para.AppendChild(new ParagraphProperties(
                new Justification { Val = JustificationValues.Center },
                new SpacingBetweenLines { Before = "20", After = "20" }
            ));

            cell.AppendChild(para);
            return cell;
        }
    }
}
