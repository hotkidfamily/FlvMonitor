using FlvToolbox.Library;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FlvMonitor
{
    internal class ListViewAVFrameIntervalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is ParseListViewItem b)
            {
                return b.TagType == $"🔈8" ? $"  {b.AptsD}" : 
                            b.TagType == $"🎥9" ? $"  \t{b.VdtsD}/{b.VptsD}" : " ";
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

    internal class ParseListViewItem
    {
        public string TagType { get; set; }
        public int FrameId { get; set; }
        public long Offset { get; set; }
        public uint TagSize { get; set; }
        public string NalType { get; set; }
        public string CodecId { get; set; }
        public string PTS { get; set; }
        public long VdtsD { get; set; }
        public long VptsD { get; set; }
        public long AptsD { get; set; }
        public string Image { get; set; }
    }

    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : WindowEx
    {
        private Thread? _statusThread;
        private DispatcherQueue _queue;
        private ObservableCollection<ParseListViewItem> _items = new();

        public MainWindow()
        {
            this.InitializeComponent();
            this.Width = 1280;
            this.Height = 720;
            this.MinWidth = 960;
            this.MinHeight = 544;
            this.MaxWidth = 1920;
            this.MaxHeight = 1080;
            this.Move(640, 1280);

            _queue = DispatcherQueue.GetForCurrentThread();
            LVMain.ItemsSource = _items;
        }

        private IEnumerable<FlvTag> _extraAction(string path)
        {
            ObservableCollection<ParseListViewItem> lvItems = [];
            FlvSpecs parser = new(path);
            long offset = parser.parseFileHeader();

            while (parser.parseTag(offset, out var tag))
            {
                offset += tag.previousTagSize + 4;
                yield return tag;
            }
        }

        private async void Grid_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                _items.Clear();
                var files = (await e.DataView.GetStorageItemsAsync()).ToList();
                List<string> paths = [];
                for (int i = 0; i < files.Count; i++)
                {
                    paths.Add(files[i].Path);
                }

                FileInfo fi = new(paths[0]);
                double oldp = 0;

                _statusThread = new Thread(async () =>
                {
                    int frameid = 0;
                    long last_video_dts = long.MaxValue;
                    long last_video_pts = long.MaxValue;
                    long last_audio_pts = long.MaxValue;

                    foreach (var flv in _extraAction(paths[0]))
                    {
                        ParseListViewItem it = new();
                        string format = @"hh\:mm\:ss\:fff";
                        it.PTS = $"{TimeSpan.FromMilliseconds(flv.timestamp).ToString(format)} / {flv.timestamp}";
                        it.Offset = flv.addr;
                        it.FrameId =  frameid++;
                        it.TagSize = flv.dataSize;
                        if (flv.tagType == 8)
                        {
                            it.CodecId = flv.a.soundFormat;
                            it.NalType = flv.a.aacPacketType;
                            it.TagType = $"🔈{flv.tagType}";

                            if (last_audio_pts == long.MaxValue)
                            {
                                last_audio_pts = flv.timestamp;
                            }

                            it.AptsD = flv.timestamp - last_audio_pts;

                            last_audio_pts = flv.timestamp;
                        }
                        else if (flv.tagType == 9)
                        {
                            it.CodecId = flv.v.codecID;
                            StringBuilder v2 = new();
                            foreach (var v in flv.v.NaluDetails)
                            {
                                if (v != null)
                                    v2.Append($"{v?.type}, ");
                            }
                            it.NalType = v2.ToString();
                            it.TagType = $"🎥{flv.tagType}";

                            if (last_video_dts == long.MaxValue)
                            {
                                last_video_dts = flv.timestamp;
                            }

                            long pts = flv.timestamp + flv.v.compositionTime;
                            if (last_video_pts == long.MaxValue)
                            {
                                last_video_pts = pts;
                            }

                            it.VdtsD = flv.timestamp - last_video_dts;
                            it.VptsD = pts - last_video_pts;

                            last_video_dts = flv.timestamp;
                            last_video_pts = pts;
                        }
                        else
                        {
                            it.TagType = $"📄{flv.tagType}";
                        }

                        var newp = (flv.addr * 100 / fi.Length + 0.5);
                        if (oldp != newp)
                        {
                            _queue.TryEnqueue(() =>
                            {
                                PBLoading.Value = newp;
                            });
                        }

                        _queue.TryEnqueue(() => {
                            _items.Add(it);
                        });
                        await Task.Yield();
                    }
                    _queue.TryEnqueue(() => {
                        PBLoading.Value = 100;
                    });
                });

                _statusThread.Start();
            }

            e.Handled = true;
        }
    }
}
