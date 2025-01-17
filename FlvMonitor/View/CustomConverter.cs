using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System;
using FlvMonitor.Model;

namespace FlvMonitor.View
{
    internal class ListViewAVFrameIntervalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is ParseListViewItem b)
            {
                return b.TagType == $"🔈8" ? $"  {b.AptsD}" :
                            b.TagType == $"🎥9" ? $"  \t{b.VdtsD} / {b.VptsD}" : " ";
            }
            throw new NotImplementedException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    internal class ListViewItemOEConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is ParseListViewItem b)
            {
                return b.FrameId%2 == 0 ? new SolidColorBrush(Colors.AliceBlue) : new SolidColorBrush(Colors.NavajoWhite);
            }
            throw new NotImplementedException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    internal class LongToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return $"0x{value:X8}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

}
