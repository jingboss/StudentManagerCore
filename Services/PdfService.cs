using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StudentManagerCore.Models;

namespace StudentManagerCore.Services;

public class PdfService
{
    public byte[] GenerateSeatingChart(
        List<ExamRoom> rooms,
        ExamSchedule schedule,
        string grade)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            foreach (var room in rooms.OrderBy(r => r.RoomName))
            {
                var students = room.Students?
                    .OrderBy(s => s.SeatNumber)
                    .ToList() ?? new();

                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(25, Unit.Millimetre);

                    // 页眉
                    page.Header().DefaultTextStyle(x => x.FontSize(9))
                        .AlignCenter()
                        .Text($"{schedule.Name} 考场安排表  {room.RoomName}（{room.SeatCount}人）\n年级：{grade}    考试日期：{schedule.ExamDate:yyyy-MM-dd}    打印时间：{DateTime.Now:yyyy-MM-dd HH:mm}")
                        .FontSize(12).Bold();

                    // 座位表
                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.RelativeColumn(1);
                            cd.RelativeColumn(4);
                            cd.RelativeColumn(3);
                            cd.RelativeColumn(1);
                            cd.RelativeColumn(4);
                            cd.RelativeColumn(3);
                        });

                        table.Header(hdr =>
                        {
                            hdr.Cell().Border(1).AlignCenter().PaddingVertical(2)
                                .Text("座位号").FontSize(8).Bold();
                            hdr.Cell().Border(1).AlignCenter().PaddingVertical(2)
                                .Text("姓名").FontSize(8).Bold();
                            hdr.Cell().Border(1).AlignCenter().PaddingVertical(2)
                                .Text("班级").FontSize(8).Bold();
                            hdr.Cell().Border(1).AlignCenter().PaddingVertical(2)
                                .Text("座位号").FontSize(8).Bold();
                            hdr.Cell().Border(1).AlignCenter().PaddingVertical(2)
                                .Text("姓名").FontSize(8).Bold();
                            hdr.Cell().Border(1).AlignCenter().PaddingVertical(2)
                                .Text("班级").FontSize(8).Bold();
                        });

                        int total = students.Count;
                        int leftCount = (int)Math.Ceiling(total / 2.0);

                        for (int i = 0; i < leftCount; i++)
                        {
                            var left = students[i];
                            var right = (i + leftCount) < total ? students[i + leftCount] : null;

                            table.Cell().Border(0.5f).AlignCenter().PaddingVertical(1).PaddingHorizontal(2)
                                .Text(left.SeatNumber.ToString()).FontSize(8);
                            table.Cell().Border(0.5f).AlignLeft().PaddingVertical(1).PaddingHorizontal(4)
                                .Text(left.Student?.Name ?? "").FontSize(8);
                            table.Cell().Border(0.5f).AlignLeft().PaddingVertical(1).PaddingHorizontal(4)
                                .Text(left.Student?.ClassName ?? "").FontSize(7);

                            if (right != null)
                            {
                                table.Cell().Border(0.5f).AlignCenter().PaddingVertical(1).PaddingHorizontal(2)
                                    .Text(right.SeatNumber.ToString()).FontSize(8);
                                table.Cell().Border(0.5f).AlignLeft().PaddingVertical(1).PaddingHorizontal(4)
                                    .Text(right.Student?.Name ?? "").FontSize(8);
                                table.Cell().Border(0.5f).AlignLeft().PaddingVertical(1).PaddingHorizontal(4)
                                    .Text(right.Student?.ClassName ?? "").FontSize(7);
                            }
                            else
                            {
                                table.Cell().Border(0.5f);
                                table.Cell().Border(0.5f);
                                table.Cell().Border(0.5f);
                            }
                        }
                    });

                    // 页脚
                    page.Footer().AlignCenter().PaddingTop(10)
                        .Text("本表为座位编排依据，请监考教师按座位号安排考生就座。")
                        .FontSize(7);
                });
            }
        }).GeneratePdf();
    }
}
