using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Esri.ArcGISRuntime.Data;

namespace GeologicalMappingDesktop
{
    public partial class FieldCalculatorDialog : Window
    {
        public string TargetFieldName { get; private set; }
        public string Expression { get; private set; }

        private List<Field> _fields;

        public FieldCalculatorDialog(List<Field> fields)
        {
            InitializeComponent();
            _fields = fields;

            foreach (var field in fields)
            {
                if (field.FieldType != FieldType.OID && 
                    field.FieldType != FieldType.Geometry &&
                    field.FieldType != FieldType.Blob)
                {
                    CboTargetField.Items.Add(new ComboBoxItem
                    {
                        Content = $"{field.Name} ({field.FieldType})",
                        Tag = field.Name
                    });
                }
            }

            if (CboTargetField.Items.Count > 0)
                CboTargetField.SelectedIndex = 0;

            foreach (var field in fields)
            {
                if (field.FieldType != FieldType.OID && 
                    field.FieldType != FieldType.Geometry &&
                    field.FieldType != FieldType.Blob)
                {
                    FieldList.Items.Add($"[{field.Name}]");
                }
            }

            var functions = new List<string>
            {
                "Abs([字段])",
                "Sqrt([字段])",
                "Round([字段], 小数位)",
                "Ceiling([字段])",
                "Floor([字段])",
                "Sin([字段])",
                "Cos([字段])",
                "Tan([字段])",
                "Log([字段])",
                "Exp([字段])",
                "Len([字段])",
                "Upper([字段])",
                "Lower([字段])",
                "Left([字段], 长度)",
                "Right([字段], 长度)",
                "Mid([字段], 起始, 长度)",
                "Trim([字段])",
                "IIf(条件, 真, 假)",
                "IsNull([字段], 默认值)",
                "Now()"
            };
            foreach (var func in functions)
            {
                FunctionList.Items.Add(func);
            }

            FieldList.MouseDoubleClick += FieldList_MouseDoubleClick;
            FunctionList.MouseDoubleClick += FunctionList_MouseDoubleClick;
        }

        private void FieldList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FieldList.SelectedItem != null)
            {
                TxtExpression.SelectedText = FieldList.SelectedItem.ToString();
                TxtExpression.Focus();
            }
        }

        private void FunctionList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FunctionList.SelectedItem != null)
            {
                TxtExpression.SelectedText = FunctionList.SelectedItem.ToString();
                TxtExpression.Focus();
            }
        }

        private void BtnOperator_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                TxtExpression.SelectedText = btn.Content.ToString();
                TxtExpression.Focus();
            }
        }

        private void BtnFunction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                TxtExpression.SelectedText = btn.Content.ToString();
                TxtExpression.Focus();
            }
        }

        private void BtnExecute_Click(object sender, RoutedEventArgs e)
        {
            if (CboTargetField.SelectedItem is ComboBoxItem targetItem)
            {
                TargetFieldName = targetItem.Tag?.ToString();
            }
            else
            {
                MessageBox.Show("请选择目标字段", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Expression = TxtExpression.Text.Trim();
            if (string.IsNullOrEmpty(Expression))
            {
                MessageBox.Show("请输入计算表达式", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"确定要将计算结果写入字段 '{TargetFieldName}' 吗？",
                "确认执行", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}