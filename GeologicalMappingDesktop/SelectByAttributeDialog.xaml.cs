using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Esri.ArcGISRuntime.Data;

namespace GeologicalMappingDesktop
{
    public partial class SelectByAttributeDialog : Window
    {
        public string WhereClause { get; private set; }
        public SelectionMethod SelectedMethod { get; private set; }

        public enum SelectionMethod
        {
            NewSelection,
            AddToSelection,
            RemoveFromSelection
        }

        private List<Field> _fields;
        private string _expression = "";

        public SelectByAttributeDialog(List<Field> fields)
        {
            InitializeComponent();
            _fields = fields;

            foreach (var field in fields)
            {
                if (field.FieldType != FieldType.OID && 
                    field.FieldType != FieldType.Geometry &&
                    field.FieldType != FieldType.Blob)
                {
                    CboField.Items.Add(new ComboBoxItem
                    {
                        Content = field.Name,
                        Tag = field.FieldType
                    });
                }
            }

            if (CboField.Items.Count > 0)
                CboField.SelectedIndex = 0;

            if (CboOperator.Items.Count > 0)
                CboOperator.SelectedIndex = 0;

            TxtValue.TextChanged += (s, ev) => UpdateExpression();
        }

        private void CboField_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateExpression();
        }

        private void UpdateExpression()
        {
            if (CboField.SelectedItem is ComboBoxItem fieldItem &&
                CboOperator.SelectedItem is ComboBoxItem operatorItem)
            {
                string fieldName = fieldItem.Content.ToString();
                string op = operatorItem.Content.ToString();
                string value = TxtValue.Text.Trim();
                FieldType fieldType = (FieldType)fieldItem.Tag;

                if (!string.IsNullOrEmpty(value))
                {
                    if (fieldType == FieldType.Text || fieldType == FieldType.Date)
                    {
                        _expression = $"{fieldName} {op} '{value}'";
                    }
                    else
                    {
                        _expression = $"{fieldName} {op} {value}";
                    }
                }
                else
                {
                    _expression = $"{fieldName} {op} ";
                }

                TxtExpression.Text = _expression;
            }
        }

        private void BtnLogic_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _expression += $" {btn.Content} ";
                TxtExpression.Text = _expression;
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            _expression = "";
            TxtExpression.Text = "";
            TxtValue.Text = "";
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            UpdateExpression();

            if (string.IsNullOrEmpty(_expression) || _expression.Trim().EndsWith("=") || _expression.Trim().EndsWith("LIKE"))
            {
                MessageBox.Show("请完整填写查询条件", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            WhereClause = _expression;

            if (RdoNewSelection.IsChecked == true)
                SelectedMethod = SelectionMethod.NewSelection;
            else if (RdoAddToSelection.IsChecked == true)
                SelectedMethod = SelectionMethod.AddToSelection;
            else if (RdoRemoveFromSelection.IsChecked == true)
                SelectedMethod = SelectionMethod.RemoveFromSelection;

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