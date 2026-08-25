using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Esri.ArcGISRuntime.Data;

namespace GeologicalMappingDesktop
{
    public partial class DeleteFieldDialog : Window
    {
        public List<string> SelectedFieldNames { get; private set; }

        private List<FieldItem> _fieldItems = new List<FieldItem>();

        public DeleteFieldDialog(List<Field> fields)
        {
            InitializeComponent();
            SelectedFieldNames = new List<string>();

            foreach (var field in fields)
            {
                if (field.FieldType != FieldType.OID && 
                    !field.Name.Equals("FID", StringComparison.OrdinalIgnoreCase) &&
                    !field.Name.Equals("OBJECTID", StringComparison.OrdinalIgnoreCase))
                {
                    _fieldItems.Add(new FieldItem
                    {
                        Name = field.Name,
                        DisplayName = $"{field.Name} ({field.FieldType})",
                        IsSelected = false
                    });
                }
            }

            FieldList.ItemsSource = _fieldItems;
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = _fieldItems.Where(f => f.IsSelected).Select(f => f.Name).ToList();
            
            if (selected.Count == 0)
            {
                MessageBox.Show("请至少选择一个字段", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"确定要删除选中的 {selected.Count} 个字段吗？此操作无法撤销！",
                "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SelectedFieldNames = selected;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        public class FieldItem : INotifyPropertyChanged
        {
            private bool _isSelected;

            public string Name { get; set; }
            public string DisplayName { get; set; }

            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}