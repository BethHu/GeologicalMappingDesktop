using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GeologicalMappingDesktop
{
    public partial class ExportDialog : Window
    {
        public string ExportFormat { get; private set; }
        public string ExportPath { get; private set; }
        public bool ExportSelectedOnly { get; private set; }

        public ExportDialog()
        {
            InitializeComponent();

            if (CboExportFormat.Items.Count > 0)
                CboExportFormat.SelectedIndex = 0;
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            string extension = ".csv";
            string filter = "CSV 文件 (*.csv)|*.csv";

            if (CboExportFormat.SelectedItem is ComboBoxItem selectedItem)
            {
                string format = selectedItem.Tag?.ToString();
                switch (format)
                {
                    case "XLSX":
                        extension = ".xlsx";
                        filter = "Excel 文件 (*.xlsx)|*.xlsx";
                        break;
                    case "DBF":
                        extension = ".dbf";
                        filter = "DBF 文件 (*.dbf)|*.dbf";
                        break;
                    default:
                        extension = ".csv";
                        filter = "CSV 文件 (*.csv)|*.csv";
                        break;
                }
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = extension,
                FileName = "属性表数据" + extension,
                Title = "选择导出文件路径"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                TxtPath.Text = saveFileDialog.FileName;
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (CboExportFormat.SelectedItem is ComboBoxItem formatItem)
            {
                ExportFormat = formatItem.Tag?.ToString();
            }
            else
            {
                MessageBox.Show("请选择导出格式", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ExportPath = TxtPath.Text.Trim();
            if (string.IsNullOrEmpty(ExportPath))
            {
                MessageBox.Show("请选择导出路径", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ExportSelectedOnly = ChkSelectedOnly.IsChecked ?? false;

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}