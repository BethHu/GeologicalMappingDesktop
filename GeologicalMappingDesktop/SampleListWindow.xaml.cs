using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows;

namespace GeologicalMappingDesktop
{
    public partial class SampleListWindow : Window
    {
        private List<FeatureState> _samples;
        private DataTable _dataTable;

        public SampleListWindow(List<FeatureState> samples, string title = "样品清单")
        {
            _samples = samples;
            InitializeComponent();
            Title = title;
            BuildTable();
        }

        private void BuildTable()
        {
            _dataTable = new DataTable("样品清单");

            // Collect all keys
            var keys = new List<string>
            {
                "id", "customId", "pointType", "typeCode",
                "latitude", "longitude", "elevation",
                "description", "timestamp", "PhotoCount", "PhotoFiles"
            };
            foreach (var s in _samples)
                foreach (var k in s.Attributes.Keys)
                    if (!keys.Contains(k, StringComparer.OrdinalIgnoreCase))
                        keys.Add(k);

            foreach (var k in keys)
                _dataTable.Columns.Add(k, typeof(string));

            foreach (var s in _samples)
            {
                var row = _dataTable.NewRow();
                foreach (var k in keys)
                {
                    row[k] = s.Attributes.TryGetValue(k, out object v) && v != null ? v.ToString() : "";
                }
                _dataTable.Rows.Add(row);
            }

            SampleGrid.ItemsSource = _dataTable.DefaultView;
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV 文件|*.csv",
                FileName = $"样品清单_{DateTime.Now:yyyyMMdd}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            using (var writer = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8))
            {
                // Header
                var cols = _dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
                writer.WriteLine(string.Join(",", cols.Select(c => $"\"{c}\"")));
                // Data
                foreach (DataRow row in _dataTable.Rows)
                {
                    var vals = _dataTable.Columns.Cast<DataColumn>()
                        .Select(c => row[c]?.ToString()?.Replace("\"", "\"\"") ?? "");
                    writer.WriteLine(string.Join(",", vals.Select(v => $"\"{v}\"")));
                }
            }
            MessageBox.Show($"已导出 {_dataTable.Rows.Count} 条记录", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Excel 导出需要安装 Office 组件。\n请先导出 CSV 后用 Excel 打开。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
