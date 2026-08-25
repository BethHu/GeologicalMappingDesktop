using Esri.ArcGISRuntime;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GeologicalMappingDesktop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            System.AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try
            {
                ArcGISRuntimeEnvironment.ApiKey = "AAPTaHMNa0MN4o4so22wXNOf2sA..O2MK3rHy6tqM87UzMvBqvy3Bp0UFFvdnNiOBhU6RF1GGGSZeyQFabvS22MbKR0-qmvq6wjPdRiQqvgQxXVmcJ1-L2aN2IZQI-L0P_V9bQwx1XnOwdcf5-n30Y4LrLy9Wl87tJYHTzFl9is6WAUwosQi12pfPUxxTDkmm7pFU9PpVlWx4dEYrVW6o2xLsnq2YRqVLsRpPsLospvvLJPVi_XJ9GBi_HY07cGhGd_Lonf40wAqjOxQyAT1_FL6wdkLV";
                ArcGISRuntimeEnvironment.Initialize();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"ArcGIS Runtime 初始化失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"应用程序发生未处理异常:\n{e.Exception.Message}\n\n堆栈跟踪:\n{e.Exception.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as System.Exception;
            MessageBox.Show($"应用程序发生致命异常:\n{ex?.Message}\n\n堆栈跟踪:\n{ex?.StackTrace}", "致命错误", MessageBoxButton.OK, MessageBoxImage.Stop);
        }
    }

    public class EditableIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEditable && isEditable)
                return "✎";
            return "🔒";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EditableTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEditable && isEditable)
                return "该图层可编辑，点击锁定";
            return "该图层已锁定，点击解锁";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EditableColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEditable && isEditable)
                return System.Windows.Media.Brushes.Green;
            return System.Windows.Media.Brushes.Yellow;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ExpandIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isExpanded && isExpanded)
                return "▾";
            return "▸";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b)
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
                return v == Visibility.Visible;
            return false;
        }
    }

    /// <summary>去掉分组键前缀（"1 点" → "点"，用于分组头显示）。</summary>
    public class GroupPrefixConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string;
            if (string.IsNullOrEmpty(s)) return "";
            int sp = s.IndexOf(' ');
            return sp > 0 ? s.Substring(sp + 1) : s;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}