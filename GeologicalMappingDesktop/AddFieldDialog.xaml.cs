using System;
using System.Windows;
using System.Windows.Controls;

namespace GeologicalMappingDesktop
{
    public partial class AddFieldDialog : Window
    {
        public string FieldName { get; private set; }
        public string FieldType { get; private set; }
        public int Length { get; private set; }
        public int Precision { get; private set; }
        public bool IsNullable { get; private set; }
        public string FieldAlias { get; private set; }

        public AddFieldDialog()
        {
            InitializeComponent();
            
            CboFieldType.SelectedIndex = 0;
            
            CboFieldType.SelectionChanged += CboFieldType_SelectionChanged;
        }

        private void CboFieldType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFieldPropertiesVisibility();
        }

        private void UpdateFieldPropertiesVisibility()
        {
            if (CboFieldType.SelectedItem is ComboBoxItem selectedItem)
            {
                string tag = selectedItem.Tag?.ToString();
                
                switch (tag)
                {
                    case "Text":
                        TxtLength.IsEnabled = true;
                        TxtLength.Text = "255";
                        TxtPrecision.IsEnabled = false;
                        TxtPrecision.Text = "0";
                        break;
                    case "ShortInteger":
                        TxtLength.IsEnabled = true;
                        TxtLength.Text = "0";
                        TxtPrecision.IsEnabled = true;
                        TxtPrecision.Text = "0";
                        break;
                    case "LongInteger":
                        TxtLength.IsEnabled = true;
                        TxtLength.Text = "0";
                        TxtPrecision.IsEnabled = true;
                        TxtPrecision.Text = "0";
                        break;
                    case "Float":
                        TxtLength.IsEnabled = true;
                        TxtLength.Text = "0";
                        TxtPrecision.IsEnabled = true;
                        TxtPrecision.Text = "6";
                        break;
                    case "Double":
                        TxtLength.IsEnabled = true;
                        TxtLength.Text = "0";
                        TxtPrecision.IsEnabled = true;
                        TxtPrecision.Text = "10";
                        break;
                }
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtFieldName.Text))
            {
                MessageBox.Show("请输入字段名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!IsValidFieldName(TxtFieldName.Text))
            {
                MessageBox.Show("字段名称仅支持英文字母、数字和下划线（ASCII），且不能以数字开头。如需中文显示名，请在「字段别名」中填写。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (CboFieldType.SelectedItem is ComboBoxItem selectedItem)
            {
                FieldName = TxtFieldName.Text.Trim();
                FieldType = selectedItem.Tag?.ToString();
                
                int.TryParse(TxtLength.Text, out int length);
                Length = length;
                
                int.TryParse(TxtPrecision.Text, out int precision);
                Precision = precision;
                
                IsNullable = ChkNullable.IsChecked ?? true;
                FieldAlias = string.IsNullOrWhiteSpace(TxtFieldAlias.Text) ? FieldName : TxtFieldAlias.Text.Trim();

                DialogResult = true;
                Close();
            }
        }

        private bool IsValidFieldName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (char.IsDigit(name[0])) return false;
            if (name.Length > 10) { return false; }
            
            foreach (char c in name)
            {
                if (c > 127) return false;
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return false;
            }
            return true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}