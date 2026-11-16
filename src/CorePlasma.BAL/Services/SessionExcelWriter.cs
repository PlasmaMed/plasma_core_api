using ClosedXML.Excel;
using CorePlasma.BAL.DTOs.Sessions;

namespace CorePlasma.BAL.Services;

/// <summary>Session history sheet, same layout as the history table (one row per session, tank 1 and tank 2 side by side).</summary>
internal static class SessionExcelWriter
{
    private const int TankColumns = 6; // Mã bình danh mục, RFID máy đọc, Trước(%), Sau(%), KL trước, KL sau

    public static byte[] Write(IReadOnlyList<SessionDto> sessions, TimeSpan displayOffset)
    {
        var tankCount = Math.Max(2, sessions.Count == 0 ? 0 : sessions.Max(s => s.Tanks.Count));
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Lịch sử sử dụng");

        // Three header rows, merged like the screen.
        var col = 1;
        void Group(string title, int width, params string[] children)
        {
            sheet.Range(1, col, 1, col + width - 1).Merge().Value = title;
            if (children.Length == 0)
            {
                sheet.Range(1, col, 3, col).Merge();
            }
            else
            {
                for (var i = 0; i < children.Length; i++)
                {
                    sheet.Range(2, col + i, 3, col + i).Merge().Value = children[i];
                }
            }

            col += width;
        }

        Group("Máy", 1);
        Group("Mã bệnh nhân", 1);
        Group("Thời gian", 3, "Bắt đầu", "Kết thúc", "Tổng");
        Group("Mã lỗi", 1);
        Group("Chế độ chạy", 1);
        Group("Tốc độ trung bình", 1);
        Group("Nhiệt tay cầm", 2, "Trước", "Sau");
        Group("Nhiệt môi trường", 2, "Trước", "Sau");
        for (var t = 1; t <= tankCount; t++)
        {
            sheet.Range(1, col, 1, col + TankColumns - 1).Merge().Value = $"Bình {t}";
            sheet.Range(2, col, 3, col).Merge().Value = "Mã bình danh mục";
            sheet.Range(2, col + 1, 3, col + 1).Merge().Value = "RFID máy đọc";
            sheet.Range(2, col + 2, 2, col + 3).Merge().Value = "CB Cao áp";
            sheet.Cell(3, col + 2).Value = "Trước(%)";
            sheet.Cell(3, col + 3).Value = "Sau(%)";
            sheet.Range(2, col + 4, 2, col + 5).Merge().Value = "Khối lượng";
            sheet.Cell(3, col + 4).Value = "Trước";
            sheet.Cell(3, col + 5).Value = "Sau";
            col += TankColumns;
        }

        var lastCol = col - 1;
        var header = sheet.Range(1, 1, 3, lastCol);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F5F8");
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        var row = 4;
        foreach (var s in sessions)
        {
            var c = 1;
            sheet.Cell(row, c++).Value = s.EngineSerial ?? s.EngineCode;
            sheet.Cell(row, c++).Value = s.UserCode;
            sheet.Cell(row, c++).Value = s.StartTime + displayOffset;
            sheet.Cell(row, c++).Value = s.EndTime.HasValue ? s.EndTime.Value + displayOffset : Blank.Value;
            sheet.Cell(row, c++).Value = Raw(s.PlasmaOnTime);
            sheet.Cell(row, c++).Value = Raw(s.ErrorCode);
            sheet.Cell(row, c++).Value = s.Mode;
            sheet.Cell(row, c++).Value = Raw(s.AverageSpeed);
            sheet.Cell(row, c++).Value = Raw(s.HandleTemperatureBefore);
            sheet.Cell(row, c++).Value = Raw(s.HandleTemperatureAfter);
            sheet.Cell(row, c++).Value = Raw(s.EnvironmentTemperatureBefore);
            sheet.Cell(row, c++).Value = Raw(s.EnvironmentTemperatureAfter);
            var firstTankColumn = c;
            foreach (var tank in s.Tanks)
            {
                sheet.Cell(row, c).Value = tank.TankCode;
                sheet.Cell(row, c + 1).Value = tank.TankRfid;
                sheet.Cell(row, c + 2).Value = tank.PressureBeforePercent;
                sheet.Cell(row, c + 3).Value = tank.PressureAfterPercent;
                sheet.Cell(row, c + 4).Value = tank.WeightBefore;
                sheet.Cell(row, c + 5).Value = tank.WeightAfter;
                c += TankColumns;
            }

            // Same red row as the screen when the machine clock recorded impossible times.
            if (s.TimeIssue is { } issue)
            {
                sheet.Range(row, 1, row, Math.Max(lastCol, firstTankColumn)).Style.Font.FontColor = XLColor.FromHtml("#DC2626");
                sheet.Cell(row, 3).CreateComment().AddText(issue);
            }

            row++;
        }

        sheet.Range(4, 3, Math.Max(4, row - 1), 4).Style.DateFormat.Format = "dd-MM-yyyy HH:mm:ss";
        sheet.Range(1, 1, Math.Max(3, row - 1), lastCol).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        sheet.Range(1, 1, Math.Max(3, row - 1), lastCol).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        sheet.SheetView.FreezeRows(3);
        sheet.Columns(1, lastCol).AdjustToContents(1, Math.Min(row, 500));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Source values are text; numbers are written as numbers so Excel can sum/filter them.</summary>
    private static XLCellValue Raw(string? value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : value ?? (XLCellValue)Blank.Value;
}
