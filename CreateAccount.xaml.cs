using System.Windows;

namespace PrintLogPdf3
{
    public partial class CreateAccount : Window
    {
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

            // TODO:
            // 1. account.db INSERT
            // 2. ID 중복 체크
            // 3. 비밀번호 해시

            MessageBox.Show(
                "계정 생성 로직은 추후 구현 예정입니다.",
                "Info",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
