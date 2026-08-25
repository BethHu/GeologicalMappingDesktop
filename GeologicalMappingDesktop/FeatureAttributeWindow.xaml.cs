using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Data;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace GeologicalMappingDesktop
{
    public partial class FeatureAttributeWindow : Window
    {
        private FeatureTable _featureTable;
        private Geometry _geometry;
        private Dictionary<string, FrameworkElement> _fieldControls = new Dictionary<string, FrameworkElement>();
        private Dictionary<string, object> _attributeValues = new Dictionary<string, object>();

        public bool? AttributeDialogResult { get; private set; }

        public FeatureAttributeWindow(FeatureTable featureTable, Geometry geometry)
        {
            InitializeComponent();
            _featureTable = featureTable;
            _geometry = geometry;
            AttributeDialogResult = null;
            GenerateAttributeControls();
        }

        private void GenerateAttributeControls()
        {
            foreach (var field in _featureTable.Fields)
            {
                if (IsSystemField(field))
                    continue;

                var row = new StackPanel();
                row.Orientation = Orientation.Horizontal;
                row.Margin = new Thickness(0, 5, 0, 0);

                var label = new Label();
                label.Content = field.Name + (field.IsNullable ? "" : "*");
                label.Width = 100;
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(label);

                FrameworkElement control = CreateControlForField(field);
                row.Children.Add(control);

                AttributePanel.Children.Add(row);

                if (!_fieldControls.ContainsKey(field.Name))
                    _fieldControls.Add(field.Name, control);
            }

            AutoCalculateFields();
        }

        private bool IsSystemField(Field field)
        {
            string name = field.Name.ToLower();
            return name == "fid" || name == "shape" || name == "shape_length" || name == "shape_area";
        }

        private FrameworkElement CreateControlForField(Field field)
        {
            FrameworkElement control;

            switch (field.FieldType)
            {
                case FieldType.Text:
                    control = new TextBox();
                    ((TextBox)control).Width = 300;
                    ((TextBox)control).TextChanged += (s, e) => 
                    {
                        string value = ((TextBox)s).Text;
                        _attributeValues[field.Name] = string.IsNullOrEmpty(value) ? null : value;
                    };
                    break;

                case FieldType.Int16:
                case FieldType.Int32:
                    control = new TextBox();
                    ((TextBox)control).Width = 100;
                    ((TextBox)control).TextChanged += (s, e) =>
                    {
                        string value = ((TextBox)s).Text;
                        if (string.IsNullOrEmpty(value))
                            _attributeValues[field.Name] = null;
                        else if (int.TryParse(value, out int intValue))
                            _attributeValues[field.Name] = intValue;
                    };
                    break;

                case FieldType.Float32:
                case FieldType.Float64:
                    control = new TextBox();
                    ((TextBox)control).Width = 100;
                    ((TextBox)control).TextChanged += (s, e) =>
                    {
                        string value = ((TextBox)s).Text;
                        if (string.IsNullOrEmpty(value))
                            _attributeValues[field.Name] = null;
                        else if (double.TryParse(value, out double doubleValue))
                            _attributeValues[field.Name] = doubleValue;
                    };
                    break;

                case FieldType.Date:
                    control = new DatePicker();
                    ((DatePicker)control).Width = 150;
                    ((DatePicker)control).SelectedDateChanged += (s, e) =>
                    {
                        _attributeValues[field.Name] = ((DatePicker)s).SelectedDate;
                    };
                    break;

                case FieldType.OID:
                default:
                    control = new TextBlock();
                    ((TextBlock)control).Text = "系统字段";
                    break;
            }

            control.VerticalAlignment = VerticalAlignment.Center;
            return control;
        }

        private void SetControlValue(FrameworkElement control, object value)
        {
            if (control is TextBox textBox)
                textBox.Text = value?.ToString() ?? "";
            else if (control is DatePicker datePicker)
                datePicker.SelectedDate = value as DateTime?;
        }

        private void AutoCalculateFields()
        {
            foreach (var field in _featureTable.Fields)
            {
                string name = field.Name.ToLower();

                if (name == "length" || name == "shape_length")
                {
                    if (_geometry is Polyline polyline)
                    {
                        double length = GeometryEngine.Length(polyline);
                        SetCalculatedField(field.Name, length);
                    }
                }
                else if (name == "area" || name == "shape_area")
                {
                    if (_geometry is Polygon polygon)
                    {
                        double area = GeometryEngine.Area(polygon);
                        SetCalculatedField(field.Name, area);
                    }
                }
            }
        }

        private void SetCalculatedField(string fieldName, double value)
        {
            if (_fieldControls.TryGetValue(fieldName, out var control))
            {
                if (control is TextBox textBox)
                {
                    textBox.Text = value.ToString("F2");
                    textBox.IsReadOnly = true;
                    textBox.Background = System.Windows.Media.Brushes.LightGray;
                    _attributeValues[fieldName] = value;
                }
            }
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            AttributeDialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            AttributeDialogResult = false;
            Close();
        }

        public Dictionary<string, object> GetAttributeValues()
        {
            return _attributeValues;
        }
    }
}