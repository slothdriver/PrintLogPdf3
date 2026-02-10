using Microsoft.Data.Sqlite;
using System.IO;
using System.Windows;

namespace PrintLogPdf3
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            InitAccountDbOnce();
        }

        private readonly string accountDbPath = @"C:\Database\Account\account.db";
        private void InitAccountDbOnce()
        {
            // 디렉터리 없으면 생성
            var dir = Path.GetDirectoryName(accountDbPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // DB 파일 없을 때만 생성 + 테이블 초기화
            if (!File.Exists(accountDbPath))
            {
                using var con = new SqliteConnection(
                    $"Data Source={accountDbPath}");
                con.Open();

                using var cmd = con.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE ACCOUNT (
                        ID TEXT PRIMARY KEY,
                        PASSWORD_HASH TEXT NOT NULL,
                        ROLE TEXT NOT NULL,
                        CREATED_AT TEXT NOT NULL
                    );
                """;
                cmd.ExecuteNonQuery();
            }
        }

        private bool CheckLogin(string id, string pw)
        {
            using var con = new SqliteConnection(
                $"Data Source={accountDbPath}");
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*)
                FROM ACCOUNT
                WHERE ID = @id
                AND PASSWORD_HASH = @pw
            """;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@pw", pw); // 나중에 hash로 교체

            var count = (long)cmd.ExecuteScalar();
            return count > 0;
        }


        private void OnLogin(object sender, RoutedEventArgs e)
        {
            var id = IdBox.Text;
            var pw = PwBox.Password;

            if (!CheckLogin(id, pw))
            {
                MessageBox.Show(
                    "계정이 존재하지 않거나 비밀번호가 올바르지 않습니다.\n계정을 생성하세요.",
                    "Login Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var main = new MainWindow();
            main.Show();
            this.Close();
        }


        private void OnCreateAccount(object sender, RoutedEventArgs e)
        {
            // 나중에 구현할 화면
            var win = new CreateAccount();
            win.ShowDialog();
        }
    }
}
