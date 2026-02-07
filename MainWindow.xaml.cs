using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using QuestPDF.Helpers;


namespace PrintLogPdf3
{
    public partial class MainWindow : Window
    {
        private readonly string systemLogDbPath = @"C:\Database\SystemLog\SystemLog.db";
        private readonly string alarmLogDbPath = @"C:\Database\Alarm\GlobalAlarm.db";
        private readonly string globalLogDbPath = @"C:\Database\Logging\GlobalLog.db";


        private const string START_MSG = "M`0090`00 00 Data Changed 0 --> 1";
        private const string END_MSG = "M`0299`08 08 Data Changed 0 --> 1";

        public MainWindow()
        {
            InitializeComponent();
            QuestPDF.Settings.License = LicenseType.Community;
        }

        private async void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!File.Exists(systemLogDbPath))
                {
                    MessageBox.Show(
                        "DB 파일 없음:\n" + systemLogDbPath,
                        "ERROR");
                    return;
                }

                var batches = LoadBatches().OrderBy(b =>b.Start).ToList();
                for (int i = 0; i < batches.Count; i++)
                {
                    batches[i].Index = i + 1;
                }
                if (batches.Count == 0)
                {
                    MessageBox.Show("완료된 Batch 없음");
                    return;
                }
                
                var pdfPath = Path.Combine(
                    @"C:\Users\Airex Korea\Documents\넓적부리황새",
                    "Batch_SystemLog.pdf");
                Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);

                await Task.Run(() =>
                {
                    ExportAllBatchesToPdf(batches, pdfPath);
                });

                MessageBox.Show("PDF 생성 완료:\n" + pdfPath, "완료");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "FATAL ERROR");
            }
        }



        private List<BatchRange> LoadBatches()
        {
            var StartEnds = new List<(DateTime Time, bool IsStart, bool IsEnd)>();
            int BatchIndex = 1;

            using var conn = new SqliteConnection($"Data Source={systemLogDbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
            SELECT log_date, log_time, log_msg
            FROM TB_SECULOG
            WHERE log_msg LIKE '%' || @start || '%'
            OR log_msg LIKE '%' || @end || '%'
            ORDER BY log_date DESC, log_time DESC
            ";
            cmd.Parameters.AddWithValue("@start", START_MSG);
            cmd.Parameters.AddWithValue("@end", END_MSG);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                // 날짜 (TEXT/INT 상관없이 처리)
                var dateRaw = r.GetValue(0)?.ToString();
                if (string.IsNullOrWhiteSpace(dateRaw))
                    continue;

                if (!DateTime.TryParseExact(
                        dateRaw,
                        "yyyyMMdd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date))
                    continue;

                // 시간 (TEXT/INT 상관없이 처리)
                var timeRaw = r.GetValue(1)?.ToString();
                if (!long.TryParse(timeRaw, out var timeVal))
                    continue;

                var time = ParseTimeSafe(timeVal);

                var dt = date.Date.Add(time);
                var msg = r.GetString(2);

                StartEnds.Add((dt,
                    msg.Contains(START_MSG),
                    msg.Contains(END_MSG)));
            }

            // 역순 배치 분리
            var batches = new List<BatchRange>();
            int idx = 0;

            while (idx < StartEnds.Count)
            {
                while (idx < StartEnds.Count && !StartEnds[idx].IsEnd)
                    idx++;
                if (idx >= StartEnds.Count) break;

                var endTime = StartEnds[idx++].Time;

                while (idx < StartEnds.Count && !StartEnds[idx].IsStart)
                    idx++;
                if (idx >= StartEnds.Count) break;

                var startTime = StartEnds[idx++].Time;

                batches.Add(new BatchRange
                {
                    Index = BatchIndex++,
                    Start = startTime,
                    End = endTime
                });
            }

            return batches;
        }

        private List<SystemLogRow> LoadRowsInBatch(BatchRange batch)
        {
            var rows = new List<SystemLogRow>();

            using var conn = new SqliteConnection($"Data Source={systemLogDbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT log_date, log_time, log_msg
                FROM TB_SECULOG
                WHERE
                    (log_date || printf('%09d', log_time)) >= @start
                AND (log_date || printf('%09d', log_time)) <= @end
                ORDER BY log_date, log_time

            ";

            cmd.Parameters.AddWithValue(
                    "@start", batch.Start.ToString("yyyyMMddHHmmssfff"));

                cmd.Parameters.AddWithValue(
                    "@end", batch.End.ToString("yyyyMMddHHmmssfff"));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                // 날짜 파싱
                var date = DateTime.ParseExact(
            r.GetString(0), "yyyyMMdd", CultureInfo.InvariantCulture);

            var time = ParseTimeSafe(long.Parse(r.GetString(1)));
            var dt = date.Date.Add(time);


                rows.Add(new SystemLogRow
                {
                    Time = dt,
                    Msg = r.GetString(2)
                });
            }

            return rows;
        }

        private List<AlarmLogRow> LoadAlarmRowsInBatch(BatchRange batch)
        {
            var rows = new List<AlarmLogRow>();

            using var conn = new SqliteConnection($"Data Source={alarmLogDbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT OCCURE_DATE, OCCURE_TIME, RECOVERY_DATE, RECOVERY_TIME, MSG 
                FROM TB_ALARM1
                WHERE
                    (OCCURE_DATE || printf('%09d', OCCURE_TIME)) >= @start
                AND (OCCURE_DATE || printf('%09d', OCCURE_TIME)) <= @end
                ORDER BY OCCURE_DATE, OCCURE_TIME
            ";

            cmd.Parameters.AddWithValue(
                "@start", batch.Start.ToString("yyyyMMddHHmmssfff"));
            cmd.Parameters.AddWithValue(
                "@end", batch.End.ToString("yyyyMMddHHmmssfff"));

            
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var date = DateTime.ParseExact(
                    r.GetString(0), "yyyyMMdd", CultureInfo.InvariantCulture);

                var time = ParseTimeSafe(r.GetInt64(1));
                var occurDt = date.Date.Add(time);

                var alarmId = r.GetString(4);

                string recovery;
                if (!r.IsDBNull(2) && !r.IsDBNull(3))
                {
                    recovery = $"{r.GetString(2)} {ParseTimeSafe(r.GetInt64(3)):hh\\:mm\\:ss}";
                }
                else
                {
                    recovery = "NOT RECOVERED";
                }

                rows.Add(new AlarmLogRow
                {
                    Time = occurDt,
                    Msg = $"AlarmID: {alarmId} / Recovery: {recovery}"
                });
            }


            return rows;
        }

        private List<GlobalLogPoint> LoadGlobalLogPoints(BatchRange batch)
        {
            var list = new List<GlobalLogPoint>();

            if (!File.Exists(globalLogDbPath))
                return list;

            using var con = new SqliteConnection($"Data Source={globalLogDbPath}");
            con.Open();

            using var cmd = con.CreateCommand();

            cmd.CommandText = @"
                SELECT LOG_DATE, LOG_TIME, COLUMN_1, COLUMN_2, COLUMN_3, COLUMN_8
                FROM TB_LOG1
                WHERE
                    (LOG_DATE || printf('%09d', LOG_TIME)) >= @start
                AND (LOG_DATE || printf('%09d', LOG_TIME)) <= @end
                ORDER BY LOG_DATE, LOG_TIME
            ";

            cmd.Parameters.AddWithValue("@start", batch.Start.ToString("yyyyMMddHHmmssfff"));
            cmd.Parameters.AddWithValue("@end",   batch.End.ToString("yyyyMMddHHmmssfff"));

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                // LOG_DATE: "yyyyMMdd" (예: 20260203)
                var dateRaw = r.GetValue(0)?.ToString();
                if (string.IsNullOrWhiteSpace(dateRaw))
                    continue;

                if (!DateTime.TryParseExact(
                        dateRaw, "yyyyMMdd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date))
                    continue;

                // LOG_TIME: HHMMSSmmm (정수/문자열 어떤 형태든 안전하게)
                var timeRaw = r.GetValue(1)?.ToString();
                if (!long.TryParse(timeRaw, out var timeVal))
                    continue;

                var dt = date.Date.Add(ParseTimeSafe(timeVal));

                // 값 컬럼 null 방어
                if((r.IsDBNull(2) || r.IsDBNull(3) || r.IsDBNull(4) || r.IsDBNull(5)))
                    continue;

                list.Add(new GlobalLogPoint
                {
                    Time    = dt,
                    Column1 = Convert.ToDouble(r.GetValue(2)),
                    Column2 = Convert.ToDouble(r.GetValue(3)),
                    Column3 = Convert.ToDouble(r.GetValue(4)),
                    ProcessCode = Convert.ToInt32(r.GetValue(5))
                });
            }

            return list;
        }


        private string BuildGlobalLogSvg(List<GlobalLogPoint> points, int width = 600, int height = 400)
        {
            if (points.Count == 0)
                return "";

            var minTime = points.Min(p => p.Time);
            var maxTime = points.Max(p => p.Time);

            int marginLeft = 35;
            int marginTop = 10;
            int marginBottom = 25;

            int plotLeft = marginLeft;
            int plotTop = marginTop;
            int plotBottom = height - marginBottom;
            int plotRight = width;

            double yStep = 50;

            double minVal = points.Min(p =>
                Math.Min(
                    p.Column1,
                    Math.Min(p.Column2 / 10.0, p.Column3)
                ));

            double maxVal = points.Max(p =>
                Math.Max(
                    p.Column1,
                    Math.Max(p.Column2 / 10.0, p.Column3)
                ));

            // y는 절대 음수로 안 내려감
            double paddedMin = 0;
            double paddedMax = maxVal * 1.1;   // 위쪽 여유만
            var totalSeconds = Math.Max((maxTime - minTime).TotalSeconds, 1);
            double X(DateTime t) =>
                plotLeft + (t - minTime).TotalSeconds / totalSeconds * (plotRight - plotLeft);

            double Y(double v) =>
                plotBottom
                - (v - paddedMin) / (paddedMax - paddedMin)
                * (plotBottom - plotTop);

            var sb = new StringBuilder();
            sb.AppendLine(
                $@"<svg xmlns='http://www.w3.org/2000/svg'
                        width='{width}' height='{height}'
                        viewBox='0 0 {width} {height}'>");

            // 공정 변경 세로선 (뒤 레이어)
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].ProcessCode != points[i - 1].ProcessCode)
                {
                    var x = X(points[i].Time);
                    var color = GetProcessColor(points[i].ProcessCode);

                    sb.AppendLine(
                        $"<line x1='{x:0.##}' y1='{plotTop}' " +
                        $"x2='{x:0.##}' y2='{plotBottom}' " +
                        $"stroke='{color}' stroke-width='0.8' />");
                }
            }


            // Y grid (50, 100, 150...)
            for (double v = yStep; v <= paddedMax; v += yStep)
            {
                var y = Y(v);
                if (y < plotTop || y > plotBottom)
                    continue;

                sb.AppendLine(
                    $"<line x1='{plotLeft}' y1='{y:0.##}' " +
                    $"x2='{plotRight}' y2='{y:0.##}' " +
                    $"stroke='#eee' stroke-width='1' />");

                sb.AppendLine(
                    $"<text x='{plotLeft - 6}' y='{y + 4:0.##}' " +
                    $"font-size='10' text-anchor='end' " +
                    $"fill='#666'>{v:0}</text>");
            }

            sb.AppendLine(
                $"<line x1='{plotLeft}' y1='{plotTop}' " +
                $"x2='{plotLeft}' y2='{plotBottom}' " +
                $"stroke='#666' stroke-width='1.2' />");

            // 데이터 라인
            void DrawPolyline(Func<GlobalLogPoint, double> sel, string color)
            {
                var pts = string.Join(" ",
                    points.Select(p =>
                        $"{X(p.Time):0.##},{Y(sel(p)):0.##}"));

                sb.AppendLine(
                    $"<polyline fill='none' stroke='{color}' " +
                    $"stroke-width='1' points='{pts}' />");
            }

            DrawPolyline(p => p.Column1, "#E53935");
            DrawPolyline(p => p.Column2 / 10.0, "#FBC02D");
            DrawPolyline(p => p.Column3, "#1E88E5");

            // X축 = y = 0 (유일한 x축)
            var yZero = Y(0);

            sb.AppendLine(
                $"<line x1='{plotLeft}' y1='{yZero:0.##}' " +
                $"x2='{plotRight}' y2='{yZero:0.##}' " +
                $"stroke='#666' stroke-width='1.2' />");

            // X축 tick (10분)
            var tickInterval = TimeSpan.FromMinutes(10);

            var firstTick = new DateTime(
                minTime.Year, minTime.Month, minTime.Day,
                minTime.Hour, minTime.Minute / 10 * 10, 0);

            if (firstTick < minTime)
                firstTick = firstTick.AddMinutes(10);

            for (var t = firstTick; t <= maxTime; t += tickInterval)
            {
                var x = X(t);

                sb.AppendLine(
                    $"<circle cx='{x:0.##}' cy='{yZero:0.##}' r='1.5' fill='#666' />");

                sb.AppendLine(
                    $"<text x='{x:0.##}' y='{yZero + 14:0.##}' " +
                    $"font-size='10' text-anchor='middle' " +
                    $"fill='#666'>{t:HH:mm}</text>");
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

            string GetProcessColor(int code) => code switch
            {
                1 => "#E6007E", // 분홍
                2 => "#F57C00", // 주황
                3 => "#FFD400", // 개나리
                4 => "#2E7D32", // 초록
                5 => "#4FC3F7", // 하늘
                6 => "#0D47A1", // 남색
                7 => "#7B1FA2", // 보라
                _ => "#BDBDBD"
            };

            string GetProcessLabel(int code) => code switch
            {
                1 => "Leak Test",
                2 => "Dehumidification",
                3 => "VHP High",
                4 => "VHP Low",
                5 => "VHP Hold",
                6 => "Aeration",
                7 => "Sterile Operation",
                _ => $"Process {code}"
            };

            
        private void ComposeLegend(IContainer container, List<GlobalLogPoint> points)
        {
            var usedProcesses = points
                .Select(p => p.ProcessCode)
                .Where(c => c > 0)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            var dataLegends = new[]
            {
                (Color: "#E53935", Label: "Internal Pressure"),     // red
                (Color: "#FBC02D", Label: "Internal Humidity"),  // gold
                (Color: "#1E88E5", Label: "Internal Temperature")          // blue
            };


            container.Column(col =>
            {
                col.Spacing(6);

                // 공정 범례
                col.Item().Text("공정변화구분선").Bold().FontSize(11);

                col.Item().Row(row =>
                {
                    row.Spacing(12);

                    foreach (var code in usedProcesses)
                    {
                        row.AutoItem().Row(r =>
                        {
                            r.AutoItem().Width(10).Height(10)
                                .Background(GetProcessColor(code));

                            r.AutoItem().PaddingLeft(4)
                                .Text(GetProcessLabel(code))
                                .FontSize(10);
                        });
                    }
                });

                // 데이터 범례
                col.Item().PaddingTop(4);
                col.Item().Text("Data").Bold().FontSize(11);

                col.Item().Row(row =>
                {
                    row.Spacing(12);

                    foreach (var d in dataLegends)
                    {
                        row.AutoItem().Row(r =>
                        {
                            r.AutoItem().Width(14).Height(2)
                                .Background(d.Color);

                            r.AutoItem().PaddingLeft(4)
                                .Text(d.Label)
                                .FontSize(10);
                        });
                    }
                });
            });
        }


        private void ComposeBatchGlobalLogGraph(ColumnDescriptor col,BatchRange batch)
        {
            var points = LoadGlobalLogPoints(batch);

            col.Item().Column(c =>
            {
                c.Item().AlignCenter()
                    .Text($"Batch {batch.Index} - Global Log Trend")
                    .FontSize(16)
                    .Bold();

                c.Item().PaddingTop(10);

                if (points.Count == 0)
                {
                    c.Item()
                    .AlignCenter()
                    .PaddingTop(100)
                    .Text("데이터 보존 정책에 따라 자동 삭제되었습니다.")
                    .Italic()
                    .FontColor(Colors.Grey.Darken2);
                }
                else
                {
                    var svg = BuildGlobalLogSvg(points);

                    c.Item()
                    .Height(400)
                    .Border(1)
                    .Svg(svg);

                    c.Item()
                    .PaddingTop(10)
                    .Border(1)
                    .Padding(8)
                    .Element(x => ComposeLegend(x, points));
                }
            });
        }

        private TimeSpan ParseTimeSafe(long value)
        {
            // HHMMSSmmm 보장 안 될 때 대비
            var s = value.ToString().PadLeft(9, '0');

            int hh = int.Parse(s.Substring(0, 2));
            int mm = int.Parse(s.Substring(2, 2));
            int ss = int.Parse(s.Substring(4, 2));
            int ms = int.Parse(s.Substring(6, 3));

            return new TimeSpan(0, hh, mm, ss, ms);
        }

        

        private void ExportAllBatchesToPdf(
            List<BatchRange> batches,
            string pdfPath)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            Document.Create(container =>
            {
                foreach (var batch in batches)
                {
                    var rows = LoadRowsInBatch(batch);
                    var alarmRows = LoadAlarmRowsInBatch(batch);

                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(20);

                        page.Header()
                            .Text($"Batch {batch.Index}\n{batch.Start:yyyy-MM-dd HH:mm:ss} ~ {batch.End:yyyy-MM-dd HH:mm:ss}")
                            .FontSize(14)
                            .Bold();

                        page.Content().Column(col =>
                        {
                            col.Spacing(15);

                            // System Log
                            col.Item().Text("System Log")
                                .FontSize(12)
                                .Bold();

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(160);
                                    c.RelativeColumn();
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Text("Time").Bold();
                                    h.Cell().Text("Message").Bold();
                                });

                                foreach (var row in rows)
                                {
                                    table.Cell().Text(row.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                                    table.Cell().Text(row.Msg);
                                }
                            });

                            // Alarm Log
                            
                            col.Item().PaddingTop(10);
                            col.Item().Text("Alarm Log")
                                .FontSize(12)
                                .Bold()
                                .FontColor(Colors.Red.Medium);

                            if (alarmRows.Count == 0)
                            {
                                // Alarm 없음 표시
                                col.Item()
                                    .PaddingLeft(10)
                                    .Text("No Alarm in this batch")
                                    .Italic()
                                    .FontColor(Colors.Grey.Darken1);
                            }
                            else
                            {
                                // Alarm 테이블 출력
                                col.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(c =>
                                    {
                                        c.ConstantColumn(160);
                                        c.RelativeColumn();
                                    });

                                    table.Header(h =>
                                    {
                                        h.Cell().Text("Time").Bold();
                                        h.Cell().Text("Alarm").Bold();
                                    });

                                    foreach (var row in alarmRows)
                                    {
                                        table.Cell()
                                            .Text(row.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                                        table.Cell()
                                            .Text(row.Msg);
                                    }
                                });
                                
                            }
                                col.Item().PageBreak();
                                ComposeBatchGlobalLogGraph(col, batch);
                                col.Item().PageBreak();

                                
                        });
                            page.Footer()
                            .AlignCenter()
                            .Text(x =>
                            {
                                x.Span("Page ");
                                x.CurrentPageNumber();
                                x.Span(" / ");
                                x.TotalPages();
                            });
                    });
                }
            })
            .GeneratePdf(pdfPath);
        }
    }



    class BatchRange
    {
        public int Index { get; set; }
        public DateTime Start;
        public DateTime End;

        public override string ToString()
            => $"Batch {Index} ({Start:yyyy-MM-dd HH:mm:ss} ~ {End:yyyy-MM-dd HH:mm:ss})";
    }

    class SystemLogRow
    {
        public DateTime Time;
        public string Msg = "";
    }

    class AlarmLogRow
    {
        public DateTime Time;
        public string Msg = "";
    }
    class GlobalLogPoint
    {
        public DateTime Time { get; set; }

        public double Column1 { get; set; }   
        public double Column2 { get; set; }   
        public double Column3 { get; set; }   
        public int ProcessCode { get; set; }   // COLUMN_8
    }

}
