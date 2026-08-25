using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Esri.ArcGISRuntime.Data;

namespace GeologicalMappingDesktop
{
    public partial class BatchUpdateAttributesDialog : Window
    {
        public class FieldValueItem : INotifyPropertyChanged
        {
            private bool _isEnabled;
            private string _value;

            public string FieldName { get; set; }
            public string FieldType { get; set; }

            public bool IsEnabled
            {
                get => _isEnabled;
                set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
            }

            public string Value
            {
                get => _value;
                set { _value = value; OnPropertyChanged(nameof(Value)); }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged(string name) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private readonly ObservableCollection<FieldValueItem> _items = new ObservableCollection<FieldValueItem>();

        public Dictionary<string, string> FieldValues { get; private set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool ApplyToSelectedOnly { get; private set; }

        public BatchUpdateAttributesDialog(List<Field> fields)
        {
            InitializeComponent();

            foreach (var field in fields)
            {
                if (field.FieldType == FieldType.OID ||
                    field.FieldType == FieldType.Geometry ||
                    field.FieldType == FieldType.Blob)
                    continue;
                if (field.Name.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase) ||
                    field.Name.Equals("FID", StringComparison.OrdinalIgnoreCase))
                    continue;

                _items.Add(new FieldValueItem
                {
                    FieldName = field.Name,
                    FieldType = field.FieldType.ToString(),
                    IsEnabled = false,
                    Value = ""
                });
            }

            FieldValueGrid.ItemsSource = _items;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            var selected = _items.Where(i => i.IsEnabled).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("请至少勾选一个要更新的字段", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            FieldValues = selected.ToDictionary(i => i.FieldName, i => i.Value ?? "", StringComparer.OrdinalIgnoreCase);
            ApplyToSelectedOnly = ChkSelectedOnly.IsChecked == true;
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
