using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PrintLogPdf3
{
    public partial class MainWindow : Window
    {
        private readonly string systemLogDbPath = @"C:\Database\SystemLog\SystemLog.db";

        private const string START_MSG = "M`0090`00 00 Data Changed 0 --> 1";
        private const string END_MSG = "M`0299`08 08 Data Changed 0 --> 1";

        public MainWindow()
        {
            InitializeComponent();
            QuestPDF.Settings.License = LicenseType.Community;
        }

        private void OnGenerateClick(object sender, RoutedEventArgs e)
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

                ExportAllBatchesToPdf(batches, pdfPath);

                MessageBox.Show(
                    "PDF 생성 완료:\n" + pdfPath,
                    "완료");
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

                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(20);

                        page.Header()
                            .Text($"Batch {batch.Index}\n{batch.Start:yyyy-MM-dd HH:mm:ss} ~ {batch.End:yyyy-MM-dd HH:mm:ss}")
                            .FontSize(14)
                            .Bold();

                        page.Content().Table(table =>
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
}
