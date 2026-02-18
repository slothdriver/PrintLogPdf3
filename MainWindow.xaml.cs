using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Globalization;
using System.Windows.Media;
using System.IO;
using System.Text;
using System.Windows;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using QuestPDF.Helpers;
using PdfColors = QuestPDF.Helpers.Colors;
using MediaColors = System.Windows.Media.Colors;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;



namespace PrintLogPdf3
{
    public partial class MainWindow : Window
    {
        private readonly string systemLogDbPath = @"C:\Program Files (x86)\M2I Corp\TOP Design Studio\SCADA\Database\SystemLog\SystemLog.db";
        private readonly string alarmLogDbPath = @"C:\Program Files (x86)\M2I Corp\TOP Design Studio\SCADA\Database\Alarm\GlobalAlarm.db";
        private readonly string globalLogDbPath = @"C:\Program Files (x86)\M2I Corp\TOP Design Studio\SCADA\Database\Logging\GlobalLog.db";
        private readonly string approvalLogDbPath = @"C:\Database\ApprovalLog\ApprovalLog.db";

        private readonly string _currentUserId;
        private readonly string _currentUserRole;
        private BatchRange? _selectedBatch;


        private const string START_MSG = "M`0090`00 00 Data Changed 0 --> 1";
        private const string END_MSG = "M`0299`08 08 Data Changed 0 --> 1";

        public MainWindow(string userId, string role)
        {
            _currentUserId = userId;
            _currentUserRole = role;
            InitializeComponent();
            QuestPDF.Settings.License = LicenseType.Community;
            EnsureApprovalLogDb();
            Title = $"Batch PDF Generator - Login: {_currentUserId}";
            LoginInfoText.Text = $"Logged in as: {_currentUserId} ({_currentUserRole})";
            LoadBatchList();

            if (_currentUserRole == "admin")
            {
                NextButton.Visibility = Visibility.Collapsed;
                ApproveButton.Visibility = Visibility.Visible;
            }
        }

        

        private void LoadBatchList()
        {
            var batches = LoadBatches()
                .OrderBy(b => b.Start)
                .ToList();
            for (int i = 0; i < batches.Count; i++)
                batches[i].Index = i + 1;
            MergeApprovalStatus(batches);
            BatchListView.ItemsSource = batches;
        }

        private void BatchListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BatchListView.SelectedItem is BatchRange batch)
            {
                _selectedBatch = batch;

                if (_currentUserRole == "admin")
                {
                    // 결재요청된 & 아직 미승인 batch만 승인 가능
                    bool canApprove = batch.IsRequested && !batch.IsApproved;
                    ApproveButton.IsEnabled = canApprove;
                    ApproveButton.Background = canApprove
                        ? new SolidColorBrush(MediaColors.Green)
                        : new SolidColorBrush(MediaColors.LightGray);
                    ApproveButton.Foreground = canApprove
                        ? new SolidColorBrush(MediaColors.White)
                        : new SolidColorBrush(MediaColors.Black);

                    // 승인완료된 batch → 미리보기/저장 표시
                    if (batch.IsApproved)
                    {
                        PreviewButton.Visibility = Visibility.Visible;
                        SaveButton.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        PreviewButton.Visibility = Visibility.Collapsed;
                        SaveButton.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    NextButton.IsEnabled = true;
                    NextButton.Background = new SolidColorBrush(MediaColors.Green);
                    NextButton.Foreground = new SolidColorBrush(MediaColors.White);
                }
            }
            else
            {
                _selectedBatch = null;

                if (_currentUserRole == "admin")
                {
                    ApproveButton.IsEnabled = false;
                    ApproveButton.Background = new SolidColorBrush(MediaColors.LightGray);
                    ApproveButton.Foreground = new SolidColorBrush(MediaColors.Black);
                    PreviewButton.Visibility = Visibility.Collapsed;
                    SaveButton.Visibility = Visibility.Collapsed;
                }
                else
                {
                    NextButton.IsEnabled = false;
                    NextButton.Background = new SolidColorBrush(MediaColors.LightGray);
                    NextButton.Foreground = new SolidColorBrush(MediaColors.Black);
                }
            }
        }


        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedBatch == null)
                return;

            BatchSelectPanel.Visibility = Visibility.Collapsed;
            ChecklistPanel.Visibility = Visibility.Visible;

            BackButton.Visibility = Visibility.Visible;
            NextButton.Visibility = Visibility.Collapsed;

            if (_selectedBatch.IsRequested)
            {
                ApprovalButton.Visibility = Visibility.Collapsed;
                PreviewButton.Visibility = Visibility.Visible;
            }
            else
            {
                ApprovalButton.Visibility = Visibility.Visible;
                PreviewButton.Visibility = Visibility.Collapsed;
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ChecklistPanel.Visibility = Visibility.Collapsed;
            BatchSelectPanel.Visibility = Visibility.Visible;
            LoadBatchList();

            BackButton.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Visible;
            ApprovalButton.Visibility = Visibility.Collapsed;
            PreviewButton.Visibility = Visibility.Collapsed;

            //batch 선택 무효화 
            BatchListView.SelectedItem = null;
            _selectedBatch = null;
            NextButton.IsEnabled = false;

            //체크, 사유 초기화
            Check1.IsChecked = false;
            Check2.IsChecked = false;
            Check3.IsChecked = false;
            Reason1.Text = "";
            Reason2.Text = "";
            Reason3.Text = "";
            Reason1.Visibility = Visibility.Collapsed;
            Reason2.Visibility = Visibility.Collapsed;
            Reason3.Visibility = Visibility.Collapsed;
            NextButton.Background = new SolidColorBrush(MediaColors.LightGray);
            NextButton.Foreground = new SolidColorBrush(MediaColors.Black);

        }
        private void OnCheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb)
            {
                TextBox? target = cb.Name switch
                {
                    "Check1" => Reason1,
                    "Check2" => Reason2,
                    "Check3" => Reason3,
                    _ => null
                };

                if (target != null)
                {
                    target.Visibility = cb.IsChecked == true
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                    if (cb.IsChecked != true)
                        target.Text = "";
                }
            }
        }

        private void EnsureApprovalLogDb()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(approvalLogDbPath)!);

            using var con = new SqliteConnection($"Data Source={approvalLogDbPath}");
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS ApprovalLog (
                id INTEGER PRIMARY KEY AUTOINCREMENT,

                start_time     TEXT NOT NULL,
                end_time       TEXT NOT NULL,
                request_time   TEXT NOT NULL,
                request_user   TEXT NOT NULL,
                approval_time  TEXT NULL,
                check1         INTEGER NOT NULL,
                check2         INTEGER NOT NULL,
                check3         INTEGER NOT NULL,
                reason1        TEXT NOT NULL DEFAULT '해당없음',
                reason2        TEXT NOT NULL DEFAULT '해당없음',
                reason3        TEXT NOT NULL DEFAULT '해당없음',

                UNIQUE(start_time, end_time)
            );";
            cmd.ExecuteNonQuery();
        }
        private void InsertApprovalRequest(BatchRange batch,
            bool check1, bool check2, bool check3,
            string reason1, string reason2, string reason3)
        {
            using var con = new SqliteConnection($"Data Source={approvalLogDbPath}");
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
            INSERT INTO ApprovalLog
            (start_time, end_time, request_time, request_user, approval_time,
             check1, check2, check3, reason1, reason2, reason3)
            VALUES
            (@start, @end, @reqTime, @user, NULL,
             @c1, @c2, @c3, @r1, @r2, @r3);
            ";

            cmd.Parameters.AddWithValue("@start", batch.Start.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@end", batch.End.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@reqTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@user", _currentUserId);
            cmd.Parameters.AddWithValue("@c1", check1 ? 1 : 0);
            cmd.Parameters.AddWithValue("@c2", check2 ? 1 : 0);
            cmd.Parameters.AddWithValue("@c3", check3 ? 1 : 0);
            cmd.Parameters.AddWithValue("@r1", check1 ? reason1 : "해당없음");
            cmd.Parameters.AddWithValue("@r2", check2 ? reason2 : "해당없음");
            cmd.Parameters.AddWithValue("@r3", check3 ? reason3 : "해당없음");

            cmd.ExecuteNonQuery();
        }
        private void OnRequestClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedBatch is not BatchRange batch)
                {
                    MessageBox.Show("Batch가 선택되지 않았습니다.", "알림");
                    return;
                }

                bool check1 = Check1.IsChecked == true;
                bool check2 = Check2.IsChecked == true;
                bool check3 = Check3.IsChecked == true;

                // 체크된 항목에 사유 미입력 검증
                if ((check1 && string.IsNullOrWhiteSpace(Reason1.Text)) ||
                    (check2 && string.IsNullOrWhiteSpace(Reason2.Text)) ||
                    (check3 && string.IsNullOrWhiteSpace(Reason3.Text)))
                {
                    MessageBox.Show("체크 항목에 대한 사유를 입력하세요.", "입력 필요");
                    return;
                }

                try
                {
                    InsertApprovalRequest(batch, check1, check2, check3,
                        Reason1.Text.Trim(), Reason2.Text.Trim(), Reason3.Text.Trim());
                    MessageBox.Show("결재요청 완료");

                    // 버튼 전환: 결재요청 숨김, 미리보기 표시
                    ApprovalButton.Visibility = Visibility.Collapsed;
                    PreviewButton.Visibility = Visibility.Visible;
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    MessageBox.Show("이미 결재요청된 Batch입니다.", "중복 요청 불가");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "FATAL ERROR");
            }
        }

        private async void OnPreviewClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedBatch is not BatchRange batch)
                {
                    MessageBox.Show("Batch가 선택되지 않았습니다.", "알림");
                    return;
                }

                // 이미지 생성
                var images = await RenderPreviewImagesAsync(
                    new List<BatchRange> { batch });

                // 미리보기 창 열기
                var previewWindow = new PreviewWindow(images);
                previewWindow.Owner = this;
                previewWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "VIEW ERROR");
            }
        }

        private void OnApproveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedBatch is not BatchRange batch)
                {
                    MessageBox.Show("Batch가 선택되지 않았습니다.", "알림");
                    return;
                }

                using var con = new SqliteConnection($"Data Source={approvalLogDbPath}");
                con.Open();

                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
                    UPDATE ApprovalLog
                    SET approval_time = @approveTime
                    WHERE start_time = @start
                      AND end_time   = @end
                      AND approval_time IS NULL;
                ";
                cmd.Parameters.AddWithValue("@approveTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@start", batch.Start.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@end", batch.End.ToString("yyyy-MM-dd HH:mm:ss"));

                int affected = cmd.ExecuteNonQuery();

                if (affected > 0)
                {
                    MessageBox.Show("결재승인 완료", "승인");
                    LoadBatchList();
                    ApproveButton.IsEnabled = false;
                    ApproveButton.Background = new SolidColorBrush(MediaColors.LightGray);
                    ApproveButton.Foreground = new SolidColorBrush(MediaColors.Black);
                }
                else
                {
                    MessageBox.Show("이미 승인되었거나 요청이 없는 Batch입니다.", "알림");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "FATAL ERROR");
            }
        }

        private readonly string pdfSaveDir = @"C:\Users\acatu\Documents\batchpdf";

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedBatch is not BatchRange batch)
                {
                    MessageBox.Show("Batch가 선택되지 않았습니다.", "알림");
                    return;
                }

                Directory.CreateDirectory(pdfSaveDir);
                var fileName = $"Batch_{batch.Index}_{batch.Start:yyyyMMdd_HHmmss}.pdf";
                var fullPath = Path.Combine(pdfSaveDir, fileName);

                if (File.Exists(fullPath))
                {
                    MessageBox.Show("이미 저장된 Batch입니다.", "중복 저장 불가");
                    return;
                }

                ExportAllBatchesToFile(new List<BatchRange> { batch }, fullPath);
                MessageBox.Show($"PDF 저장 완료\n{fullPath}", "저장");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "SAVE ERROR");
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnInputGotFocus(object sender, RoutedEventArgs e)
        {
            ShowTouchKeyboard();
        }

        private void ShowTouchKeyboard()
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName("TabTip"))
                {
                    proc.Kill();
                    proc.WaitForExit(500);
                }
                Process.Start(new ProcessStartInfo
                {
                    FileName = @"C:\Program Files\Common Files\Microsoft Shared\ink\TabTip.exe",
                    UseShellExecute = true
                });
            }
            catch
            {
                // 터치PC가 아니거나 TabTip 없는 경우 무시
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

        private void MergeApprovalStatus(List<BatchRange> batches)
        {
            if (!File.Exists(approvalLogDbPath))
                return;

            using var con = new SqliteConnection($"Data Source={approvalLogDbPath}");
            con.Open();

            foreach (var batch in batches)
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = @"
                    SELECT approval_time
                    FROM ApprovalLog
                    WHERE start_time = @start
                    AND end_time   = @end
                    LIMIT 1;
                ";

                cmd.Parameters.AddWithValue("@start",
                    batch.Start.ToString("yyyy-MM-dd HH:mm:ss"));

                cmd.Parameters.AddWithValue("@end",
                    batch.End.ToString("yyyy-MM-dd HH:mm:ss"));

                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    batch.IsRequested = true;

                    var approveObj = reader["approval_time"];
                    batch.IsApproved = approveObj != DBNull.Value;
                }
                else
                {
                    batch.IsRequested = false;
                    batch.IsApproved  = false;
                }
            }
        }

        private ApprovalInfo? LoadApprovalInfo(BatchRange batch)
        {
            if (!File.Exists(approvalLogDbPath))
                return null;

            using var con = new SqliteConnection($"Data Source={approvalLogDbPath}");
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                SELECT request_user, request_time, approval_time,
                       check1, check2, check3, reason1, reason2, reason3
                FROM ApprovalLog
                WHERE start_time = @start
                  AND end_time   = @end
                LIMIT 1;
            ";

            cmd.Parameters.AddWithValue("@start",
                batch.Start.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@end",
                batch.End.ToString("yyyy-MM-dd HH:mm:ss"));

            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                return null;

            return new ApprovalInfo
            {
                RequestUser  = reader["request_user"]?.ToString() ?? "",
                RequestTime  = reader["request_time"]?.ToString() ?? "",
                ApprovalTime = reader["approval_time"] == DBNull.Value
                    ? null
                    : reader["approval_time"]?.ToString(),
                Check1 = Convert.ToInt32(reader["check1"]) == 1,
                Check2 = Convert.ToInt32(reader["check2"]) == 1,
                Check3 = Convert.ToInt32(reader["check3"]) == 1,
                Reason1 = reader["reason1"]?.ToString() ?? "해당없음",
                Reason2 = reader["reason2"]?.ToString() ?? "해당없음",
                Reason3 = reader["reason3"]?.ToString() ?? "해당없음",
            };
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
                    Msg = $"{alarmId} / Recovery: {recovery}"
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


        private string BuildGlobalLogSvg(List<GlobalLogPoint> points, int width = 600, int height = 400, int seriesIndex = 0)
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

            double yStep = seriesIndex switch
            {
                2 => 5,   // Temperature: 5마다
                1 => 10,  // Humidity: 10마다
                _ => 50   // Pressure: 기존 유지
            };

            Func<GlobalLogPoint, double> seriesSel = seriesIndex switch
            {
                1 => p => p.Column2 / 10.0,
                2 => p => p.Column3 / 10.0,
                _ => p => p.Column1
            };

            double maxVal = points.Max(seriesSel);

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

            var (seriesColor, seriesSelector) = seriesIndex switch
            {
                1 => ("#FBC02D", (Func<GlobalLogPoint, double>)(p => p.Column2 / 10.0)),
                2 => ("#1E88E5", (Func<GlobalLogPoint, double>)(p => p.Column3 / 10.0)),
                _ => ("#E53935", (Func<GlobalLogPoint, double>)(p => p.Column1))
            };
            DrawPolyline(seriesSelector, seriesColor);

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


        private void ComposeBatchGlobalLogGraph(ColumnDescriptor col, BatchRange batch, int seriesIndex)
        {
            var points = LoadGlobalLogPoints(batch);

            col.Item().Column(c =>
            {
                if (points.Count == 0)
                {
                    c.Item()
                    .AlignCenter()
                    .PaddingTop(100)
                    .Text("데이터 보존 정책에 따라 자동 삭제되었습니다.")
                    .Italic()
                    .FontColor(PdfColors.Grey.Darken2);
                }
                else
                {
                    var svg = BuildGlobalLogSvg(points, seriesIndex: seriesIndex);

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

        private IDocument CreateBatchForm(
            List<BatchRange> batches
            )
        {
            QuestPDF.Settings.License = LicenseType.Community;

            return Document.Create(container =>
            {
                foreach (var batch in batches)
                {
                    var rows = LoadRowsInBatch(batch);
                    var alarmRows = LoadAlarmRowsInBatch(batch);
                    var approval = LoadApprovalInfo(batch);

                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(20);

                        page.Content().Column(col =>
                        {
                            // 제목 (첫 페이지에만)
                            col.Item()
                                .PaddingBottom(6)
                                .Text("Isolator Batch Process Record")
                                .FontSize(16)
                                .Bold();

                            // 두꺼운 초록색 라인
                            col.Item()
                                .Height(3)
                                .Background("#2E7D32");

                            col.Item().Height(20);

                            // 하늘색 상단 라인
                            col.Item()
                                .Height(2)
                                .Background("#4FC3F7");

                            // Batch 정보
                            col.Item()
                                .PaddingVertical(6)
                                .PaddingHorizontal(4)
                                .Text($"Batch {batch.Index}    {batch.Start:yyyy-MM-dd HH:mm:ss} ~ {batch.End:yyyy-MM-dd HH:mm:ss}")
                                .FontSize(12);

                            // 하늘색 하단 라인
                            col.Item()
                                .Height(2)
                                .Background("#4FC3F7");
                            col.Item()
                                .PaddingBottom(25);


                            // 결재 정보
                            if (approval != null)
                            {
                                col.Item()
                                    .Text("1. 결재 및 승인 정보")
                                    .FontSize(12)
                                    .Bold();
                                col.Item().PaddingTop(3).LineHorizontal(2);
                                col.Item().PaddingBottom(10);

                                col.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(c =>
                                    {
                                        c.ConstantColumn(120);
                                        c.RelativeColumn();
                                    });

                                    table.Cell().Text("결재요청자").Bold();
                                    table.Cell().Text(approval.RequestUser);

                                    table.Cell().Text("결재요청 시간").Bold();
                                    table.Cell().Text(approval.RequestTime);

                                    table.Cell().Text("결재승인 시간").Bold();
                                    table.Cell().Text(approval.ApprovalTime ?? "미승인");
                                });

                                col.Item().PaddingTop(5).Column(chk =>
                                {
                                    chk.Item().Text($"{(approval.Check1 ? "☑" : "☐")} 승인되지 않은 사용자 변경이 있습니까? 변경 사유 기입");
                                    if (approval.Check1)
                                        chk.Item().PaddingLeft(20).Text($"사유: {approval.Reason1}").FontSize(11).Italic();

                                    chk.Item().Text($"{(approval.Check2 ? "☑" : "☐")} 알람이 발생했습니까? 알람 발생후 조치 내용 기입");
                                    if (approval.Check2)
                                        chk.Item().PaddingLeft(20).Text($"사유: {approval.Reason2}").FontSize(11).Italic();

                                    chk.Item().Text($"{(approval.Check3 ? "☑" : "☐")} 자동운전의 설정값을 변경한 적이 있습니까? 변경 이유 기입");
                                    if (approval.Check3)
                                        chk.Item().PaddingLeft(20).Text($"사유: {approval.Reason3}").FontSize(11).Italic();
                                });

                                col.Item().PaddingTop(10);
                            }
                            // Alarm Log
                            
                            col.Item().PaddingTop(10);
                            col.Item().Text("2. Alarm Log")
                                .FontSize(12)
                                .Bold();
                            col.Item().PaddingTop(3).LineHorizontal(2);
                            col.Item().PaddingBottom(10);
                                
                            if (alarmRows.Count == 0)
                            {
                                // Alarm 없음 표시
                                col.Item()
                                    .PaddingLeft(10)
                                    .Text("No Alarm in this batch")
                                    .Italic()
                                    .FontColor(PdfColors.Grey.Darken1);
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
                                        h.Cell().Background("#3D3D3D").Padding(5).Text("Time").Bold().FontColor(PdfColors.White);
                                        h.Cell().Background("#3D3D3D").Padding(5).Text("Alarm").Bold().FontColor(PdfColors.White);
                                    });

                                    int alarmRowIdx = 0;
                                    foreach (var row in alarmRows)
                                    {
                                        var bg = alarmRowIdx % 2 == 0 ? "#FFFFFF" : "#F0F0F0";
                                        table.Cell().Background(bg).Padding(5)
                                            .Text(row.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                                        table.Cell().Background(bg).Padding(5)
                                            .Text(row.Msg);
                                        alarmRowIdx++;
                                    }
                                }
                                );

                            }

                            // Alarm section 후 page break
                            col.Item().PageBreak();

                            // System Log
                            col.Item().Text("3. System Log")
                                .FontSize(12)
                                .Bold();
                            col.Item().PaddingTop(3).LineHorizontal(2);
                            col.Item().PaddingBottom(10);

                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(c =>
                                {
                                    c.ConstantColumn(160);
                                    c.RelativeColumn();
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Background("#3D3D3D").Padding(5).Text("Time").Bold().FontColor(PdfColors.White);
                                    h.Cell().Background("#3D3D3D").Padding(5).Text("Message").Bold().FontColor(PdfColors.White);
                                });

                                int sysRowIdx = 0;
                                foreach (var row in rows)
                                {
                                    var bg = sysRowIdx % 2 == 0 ? "#FFFFFF" : "#F0F0F0";
                                    table.Cell().Background(bg).Padding(5).Text(row.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                                    table.Cell().Background(bg).Padding(5).Text(row.Msg);
                                    sysRowIdx++;
                                }
                            });

                            col.Item().PageBreak();

                            // 4. Internal Pressure
                            col.Item().Text("4. Operation Graph - Internal Pressure")
                                .FontSize(12)
                                .Bold();
                            col.Item().PaddingTop(3).LineHorizontal(2);
                            col.Item().PaddingBottom(10);
                            ComposeBatchGlobalLogGraph(col, batch, 0);

                            col.Item().PageBreak();

                            // 5. Internal Humidity
                            col.Item().Text("5. Operation Graph - Internal Humidity")
                                .FontSize(12)
                                .Bold();
                            col.Item().PaddingTop(3).LineHorizontal(2);
                            col.Item().PaddingBottom(10);
                            ComposeBatchGlobalLogGraph(col, batch, 1);

                            col.Item().PageBreak();

                            // 6. Internal Temperature
                            col.Item().Text("6. Operation Graph - Internal Temperature")
                                .FontSize(12)
                                .Bold();
                            col.Item().PaddingTop(3).LineHorizontal(2);
                            col.Item().PaddingBottom(10);
                            ComposeBatchGlobalLogGraph(col, batch, 2);
       
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
                
            }});
            
        }

        public void ExportAllBatchesToFile(
            List<BatchRange> batches,
            string pdfPath)
        {
            var document = CreateBatchForm(batches);

            document.GeneratePdf(pdfPath);
        }

        public async Task<List<byte[]>> RenderPreviewImagesAsync(List<BatchRange> batches)
        {
            return await Task.Run(() =>
            {
                var document = CreateBatchForm(batches);

                var settings = new ImageGenerationSettings
                {
                    ImageFormat = ImageFormat.Png,
                    RasterDpi = 144   // 터치PC 기준 적당
                };

                return document.GenerateImages(settings).ToList();
            });
        }



        public class BatchRange
        {
            public int Index { get; set; }
            public DateTime Start;
            public DateTime End;

            // 결재 상태 (DB에서 채움)
            public bool IsRequested { get; set; }
            public bool IsApproved { get; set; }

            // ListView 표기용(원하면)
            public string RequestStatus => IsRequested ? "Requested" : "-";
            public string ApproveStatus => IsApproved ? "승인완료" : "-";

            public string DisplayName =>
                $"Batch {Index} ({Start:yyyy-MM-dd HH:mm:ss} ~ {End:yyyy-MM-dd HH:mm:ss})";

            public override string ToString() => DisplayName;
        }



        class ApprovalInfo
        {
            public string RequestUser = "";
            public string RequestTime = "";
            public string? ApprovalTime;
            public bool Check1;
            public bool Check2;
            public bool Check3;
            public string Reason1 = "해당없음";
            public string Reason2 = "해당없음";
            public string Reason3 = "해당없음";
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
}
