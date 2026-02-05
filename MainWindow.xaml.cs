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
        private readonly string systemLogDbPath =
            @"C:\Database\SystemLog\SystemLog.db";

        private const string START_MSG =
            "M`0090`00 00 Data Changed 0 --> 1";
        private const string END_MSG =
            "M`0299`08 08 Data Changed 0 --> 1";

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

                var batches = LoadBatches();

                if (batches.Count == 0)
                {
                    MessageBox.Show("완료된 Batch 없음", "Batch 결과");
                    return;
                }

                var sb = new StringBuilder();
                for (int i = 0; i < batches.Count; i++)
                {
                    sb.AppendLine($"Batch {i + 1}");
                    sb.AppendLine($"  Start: {batches[i].Start:yyyy-MM-dd HH:mm:ss.fff}");
                    sb.AppendLine($"  End  : {batches[i].End:yyyy-MM-dd HH:mm:ss.fff}");
                    sb.AppendLine();
                }

                var pdfPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "BatchResult.pdf");

                ExportBatchTextToPdf(sb.ToString(), pdfPath);

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
            var events = new List<(DateTime Time, bool IsStart, bool IsEnd)>();

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

                events.Add((dt,
                    msg.Contains(START_MSG),
                    msg.Contains(END_MSG)));
            }

            // 역순 배치 분리
            var batches = new List<BatchRange>();
            int idx = 0;

            while (idx < events.Count)
            {
                while (idx < events.Count && !events[idx].IsEnd)
                    idx++;
                if (idx >= events.Count) break;

                var endTime = events[idx++].Time;

                while (idx < events.Count && !events[idx].IsStart)
                    idx++;
                if (idx >= events.Count) break;

                var startTime = events[idx++].Time;

                batches.Add(new BatchRange
                {
                    Start = startTime,
                    End = endTime
                });
            }

            return batches;
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
        void ExportBatchTextToPdf(string text, string filePath)
        {
            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(QuestPDF.Helpers.PageSizes.A4);
                    page.Margin(30);

                    page.Content()
                        .Text(text)
                        .FontSize(11);
                });
            })
            .GeneratePdf(filePath);
        }

    }

    class BatchRange
    {
        public DateTime Start;
        public DateTime End;
    }
}
