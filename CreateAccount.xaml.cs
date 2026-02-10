using Microsoft.Data.Sqlite;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace PrintLogPdf3
{
    public partial class CreateAccount : Window
    {
        private readonly string dbPath =
            @"C:\Database\Account\account.db";

        public CreateAccount()
        {
            InitializeComponent();
        }

        private void OnCreate(object sender, RoutedEventArgs e)
        {
            var id = IdBox.Text.Trim();
            var pw = PwBox.Password;

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pw))
            {
                MessageBox.Show(
                    "ID와 Password를 입력하세요.",
                    "Validation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var con = new SqliteConnection($"Data Source={dbPath}");
                con.Open();

                // ID 중복 체크
                if (UserExists(con, id))
                {
                    MessageBox.Show(
                        "이미 존재하는 ID입니다.",
                        "Duplicate ID",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // 비밀번호 해시
                var hash = HashPassword(pw);

                // INSERT
                using var cmd = con.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO ACCOUNT
                    (ID, PASSWORD_HASH, ROLE, CREATED_AT)
                    VALUES
                    (@id, @hash, @role, @created)
                """;

                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@hash", hash);
                cmd.Parameters.AddWithValue("@role", "OPERATOR");
                cmd.Parameters.AddWithValue(
                    "@created",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                cmd.ExecuteNonQuery();

                MessageBox.Show(
                    "계정이 생성되었습니다.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Helper Methods
        private bool UserExists(SqliteConnection con, string id)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = """
                SELECT COUNT(*)
                FROM ACCOUNT
                WHERE ID = @id
            """;
            cmd.Parameters.AddWithValue("@id", id);

            var count = (long)cmd.ExecuteScalar();
            return count > 0;
        }

        private static string HashPassword(string password)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
