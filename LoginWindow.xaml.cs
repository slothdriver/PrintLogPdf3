using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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

        private bool TryLogin(string id, string pw, out string role)
        {
            role = "";

            string hash = HashPassword(pw);

            using var con = new SqliteConnection($"Data Source={accountDbPath}");
            con.Open();

            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                SELECT ROLE
                FROM ACCOUNT
                WHERE ID = @id
                AND PASSWORD_HASH = @hash
            """;

            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@hash", hash);

            var result = cmd.ExecuteScalar();
            if (result == null)
                return false;

            role = result.ToString()!;
            return true;
        }


        private static string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private void OnLogin(object sender, RoutedEventArgs e)
        {
            var id = IdBox.Text.Trim();
            var pw = PwBox.Password;

            if (!TryLogin(id, pw, out var role))
            {
                MessageBox.Show(
                    "계정이 존재하지 않거나 비밀번호가 올바르지 않습니다.",
                    "Login Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var main = new MainWindow(id, role);
            main.Show();
            this.Close();
        }



        private void OnCreateAccount(object sender, RoutedEventArgs e)
        {
            var win = new CreateAccount();
            win.ShowDialog();
        }
    }
}
