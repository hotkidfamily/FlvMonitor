using FFmpeg.AutoGen;
using FFmpeg.AutoGen.Example;
using FlvMonitor.Library;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.Schema;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
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


    public class ParseListViewItem
    {
        public string TagType { get; set; } = "unknow";
        public int FrameId { get; set; }
        public long Offset { get; set; }
        public uint TagSize { get; set; }
        public string NalType { get; set; } = "unknow";
        public string CodecId { get; set; } = "unknow";
        public string PTS { get; set; } = "unknow";
        public long VdtsD { get; set; }
        public long VptsD { get; set; }
        public long AptsD { get; set; }
        public string Image { get; set; } = "unknow";
    }

    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : WindowEx, INotifyPropertyChanged
    {
        private Thread? _statusThread;
        private CancellationTokenSource _downloadcts = new ();
        private Thread? _downloadThread;
        private DispatcherQueue _queue;
        private ObservableCollection<ParseListViewItem> _itemsViewList = [];
        private List<FlvTag> _itemsList = [];
        private ContentDialog _contentDialog;
        private string _tempPath = default!;

        private long _totalDownloadBytes = 0;

        public long TotalDownloadBytes
        {
            get => _totalDownloadBytes; 
            set 
            {
                if(_totalDownloadBytes != value) 
                {
                    _totalDownloadBytes = value;
                    OnPropertyChanged();
                }
            }
        }
        public static readonly DependencyProperty TotalDownloadBytesProperty =
            DependencyProperty.Register("TotalDownloadBytes", typeof(long), typeof(MainWindow), new PropertyMetadata(0));

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

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
            AppTitle.Text = $"FlvMonitor {VersionInfo.DisplayVersion}";
            DVButton.IsEnabled = _itemsViewList.Count > 0;
            _contentDialog = new ContentDialog();

            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            _tempPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp\\FlvMonitor");

            if (!Path.Exists(_tempPath))
            {
                Directory.CreateDirectory(_tempPath);
            }
        }

        private IEnumerable<FlvTag> _extraFile(string path)
        {
            FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
            FlvSpecs parser = new(fs, fs.Length);
            long offset = parser.parseFileHeader();

            while (parser.parseTag(offset, out var tag))
            {
                offset += tag.previousTagSize + 4;
                yield return tag;
            }
        }

        private IEnumerable<FlvTag> _extraStreamFile(string path)
        {
            FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Write, 65536);
            FlvSpecs parser = new(fs, fs.Length);
            long offset = parser.parseFileHeader();

            while (parser.parseTag(offset, out var tag))
            {
                offset += tag.previousTagSize + 4;
                yield return tag;
            }
        }

        private void UpdateListView()
        {
            var TagTypes = TagComboBox.SelectedIndex;
            _itemsViewList.Clear();

            int frameid = 0;
            long last_video_dts = long.MaxValue;
            long last_video_pts = long.MaxValue;
            long last_audio_pts = long.MaxValue;

            if (TagTypes == 0)
            {
                foreach (var flv in _itemsList)
                {
                    ParseListViewItem it = new();
                    string format = @"hh\:mm\:ss\:fff";
                    it.PTS = $"{TimeSpan.FromMilliseconds(flv.timestamp).ToString(format)} / {flv.timestamp}";
                    it.Offset = flv.addr;
                    it.FrameId =  frameid++;
                    it.TagSize = flv.dataSize;
                    if (flv.tagType == 8)
                    {
                        var soundFormat = FlvSpecs.strSoundFormat(flv.a.soundFormat) + "[" + flv.a.soundFormat + "]";
                        //var soundRate = FlvSpecs.strSoundSampleRate(flv.a.soundRate) + "[" + flv.a.soundRate + "]";
                        //var soundSize = flv.a.soundSize == 0 ? "8bits " : "16bits " + "[" + flv.a.soundSize + "]";
                        //var soundType = flv.a.soundType == 0 ? "Mono " : "Stereo " + "[" + flv.a.soundType + "]";
                        var aacPacketType = flv.a.aacPacketType == 0 ? "aac sequence header " : flv.a.aacPacketType == 1 ? "aac raw " : " ";
                        aacPacketType += "[" + flv.a.aacPacketType + "]";

                        it.CodecId = soundFormat;
                        it.NalType = aacPacketType;
                        it.TagType = $"🔈{flv.tagType}";

                        if (last_audio_pts == long.MaxValue)
                        {
                            last_audio_pts = flv.timestamp;
                        }

                        it.AptsD = flv.timestamp - last_audio_pts;
                        it.Image = Path.Join(_tempPath, $"audio_{flv.timestamp}.png");
                        last_audio_pts = flv.timestamp;
                    }
                    else if (flv.tagType == 9)
                    {
                        it.CodecId = FlvSpecs.strVideoCodecID(flv.v.codecID) + "[" + flv.v.codecID + "]"; ;

                        //detail.v.frametype = FlvSpecs.strVideoTagFrameType(frametype) + "[" + frametype + "]";
                        //detail.v.codecID = FlvSpecs.strVideoCodecID(codecID) + "[" + codecID + "]";
                        //detail.v.avcPacketType = FlvSpecs.strVideoAVCPacketType(avcPacketType) + "[" + avcPacketType + "]";

                        List<string> types = [];
                        foreach (var v in flv.v.NaluDetails)
                        {
                            if (v != null)
                                types.Add(v.type);
                        }
                        it.NalType = string.Join(", ", types);
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
                        //it.PTS = $"{TimeSpan.FromMilliseconds(flv.timestamp).ToString(format)} / {pts}";
                        it.Image = Path.Join(_tempPath, $"{pts}.png");
                    }
                    else
                    {
                        it.TagType = $"📄{flv.tagType}";
                    }
                    _itemsViewList.Add(it);
                }
            }
            else if (TagTypes == 1)
            {
                foreach (var flv in _itemsList)
                {
                    if (flv.tagType == 9)
                    {
                        ParseListViewItem it = new();
                        string format = @"hh\:mm\:ss\:fff";
                        it.PTS = $"{TimeSpan.FromMilliseconds(flv.timestamp).ToString(format)} / {flv.timestamp}";
                        it.Offset = flv.addr;
                        it.FrameId =  frameid++;
                        it.TagSize = flv.dataSize;
                        it.CodecId = FlvSpecs.strVideoCodecID(flv.v.codecID) + "[" + flv.v.codecID + "]"; ;
                        List<string> types = [];
                        foreach (var v in flv.v.NaluDetails)
                        {
                            if (v != null)
                                types.Add(v.type);
                        }
                        it.NalType = string.Join(", ", types);
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

                        it.Image = Path.Join(_tempPath, $"{pts}.png");

                        _itemsViewList.Add(it);
                    }
                }
            }
            else if (TagTypes == 2)
            {
                foreach (var flv in _itemsList)
                {
                    if (flv.tagType == 8)
                    {
                        ParseListViewItem it = new();
                        string format = @"hh\:mm\:ss\:fff";
                        it.PTS = $"{TimeSpan.FromMilliseconds(flv.timestamp).ToString(format)} / {flv.timestamp}";
                        it.Offset = flv.addr;
                        it.FrameId =  frameid++;
                        it.TagSize = flv.dataSize;

                        var soundFormat = FlvSpecs.strSoundFormat(flv.a.soundFormat) + "[" + flv.a.soundFormat + "]";
                        //var soundRate = FlvSpecs.strSoundSampleRate(flv.a.soundRate) + "[" + flv.a.soundRate + "]";
                        //var soundSize = flv.a.soundSize == 0 ? "8bits " : "16bits " + "[" + flv.a.soundSize + "]";
                        //var soundType = flv.a.soundType == 0 ? "Mono " : "Stereo " + "[" + flv.a.soundType + "]";
                        var aacPacketType = flv.a.aacPacketType == 0 ? "aac sequence header " : flv.a.aacPacketType == 1 ? "aac raw " : " ";
                        aacPacketType += "[" + flv.a.aacPacketType + "]";

                        it.CodecId = soundFormat;
                        it.NalType = aacPacketType;
                        it.TagType = $"🔈{flv.tagType}";

                        if (last_audio_pts == long.MaxValue)
                        {
                            last_audio_pts = flv.timestamp;
                        }

                        it.AptsD = flv.timestamp - last_audio_pts;

                        last_audio_pts = flv.timestamp;
                        it.Image = Path.Join(_tempPath, $"audio_{flv.timestamp}.png");
                        _itemsViewList.Add(it);
                    }
                }
            }
            if (LVMain != null && _itemsList.Count > 0)
            {
                LVMain.ItemsSource = _itemsViewList;
            }
        }

        private void RunAysnc(string path, bool isVideoON, bool isAudioON)
        {
            FileInfo fi = new(path);
            
            _itemsList.Clear();

            if (isVideoON || isAudioON)
            {
                FFmpegBinariesHelper.RegisterFFmpegBinaries();

                try
                {
                    ffmpeg.av_version_info();
                }
                catch (Exception)
                {
                    isVideoON = false;
                    isAudioON = false;
                }
            }

            _statusThread = new Thread(() =>
            {
                try
                {
                    double oldp = 0;

                    FFmpegDecoder vd = new(_tempPath);
                    FFmpegDecoder ad = new(_tempPath);

                    foreach (var flv in _extraFile(path))
                    {
                        _itemsList.Add(flv);

                        if(flv.tagType == 9)
                        {
                            if (isVideoON)
                            {
                                var d = new Span<byte>(flv.data, 16, flv.data.Length - 16);
                                if (!vd.Ready && flv.v.avcPacketType == 0)
                                {
                                    AVCodecID ID = FFmpegDecoder.FlvVideoTypeToFFmpeg(flv.v.codecID);
                                    vd.CreateDecoder(ID, d, 64, 64);
                                }

                                {
                                    long pts = flv.timestamp + flv.v.compositionTime;
                                    vd.Decode(d, flv.timestamp, pts);
                                }
                            }
                        }

                        if (flv.tagType == 8)
                        {
                            int audio_tag_len = 11 + (flv.a.soundFormat==10 ? 2 : 1);
                            var d = new Span<byte>(flv.data, audio_tag_len, flv.data.Length - audio_tag_len);

                            if (isAudioON)
                            {
                                if (!ad.Ready && flv.a.aacPacketType == 0)
                                {
                                    AVCodecID ID = FFmpegDecoder.FlvAudioTypeToFFmpeg(flv.a.soundFormat);
                                    ad.CreateDecoder(ID, d, 72, 36);
                                }
                                else
                                {
                                    long pts = flv.timestamp + flv.v.compositionTime;
                                    ad.Decode(d, flv.timestamp, pts);
                                }
                            }
                        }

                        var newp = (flv.addr * 100 / fi.Length + 0.5);
                        if (oldp != newp)
                        {
                            _queue.TryEnqueue(() =>
                            {
                                Progress.Value = newp;
                            });
                        }
                    }
                    if (vd.Ready)
                    {
                        vd.FreeCodec();
                    }
                    
                    if (ad.Ready)
                    {
                        ad.FreeCodec();
                    }
                    
                    _queue.TryEnqueue(() =>
                    {
                        Progress.Value = 100;

                        DVButton.IsEnabled = false;

                        UpdateListView();

                        DVButton.IsEnabled = _itemsViewList.Count > 0;
                    });
                }
                catch (Exception ex)
                {
                    _queue.TryEnqueue(() =>
                    {
                        _contentDialog.XamlRoot = MainGrid.XamlRoot;
                        _contentDialog.Content = ex.Message;
                        _contentDialog.Title = "Error";
                        _contentDialog.CloseButtonText = "OK";
                        _ = _contentDialog.ShowAsync();
                    });
                }
            });

            _statusThread.Start();
        }


        private void RunUrlAysnc(string urlpath, bool isVideON, bool isAudioON, CancellationToken token)
        {
            _downloadThread = new Thread( async () =>
            {
                long downloadbytes = 0;
                try
                {
                    using (HttpClient client = new())
                    {
                        try
                        {
                            HttpResponseMessage response = await client.GetAsync(urlpath, HttpCompletionOption.ResponseHeadersRead, token);
                            response.EnsureSuccessStatusCode();

                            using (Stream stream = await response.Content.ReadAsStreamAsync(token))
                            {
                                string filepath = Path.Combine(_tempPath, "live-stream.flv");
                                using (FileStream fileStream = new(filepath, FileMode.Create, FileAccess.Write, FileShare.Read))
                                {
                                    byte[] buffer = new byte[8192];
                                    int bytesRead;

                                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                                    {
                                    	downloadbytes += bytesRead;
                                        _queue.TryEnqueue(() =>
                                        {
                                            TotalDownloadBytes = downloadbytes/1024;
                                        });
                                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"发生错误: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _queue.TryEnqueue(() =>
                    {
                        _contentDialog.XamlRoot = MainGrid.XamlRoot;
                        _contentDialog.Content = ex.Message;
                        _contentDialog.Title = "Error";
                        _contentDialog.CloseButtonText = "OK";
                        _ = _contentDialog.ShowAsync();
                    });
                }
            });

            _downloadThread.Start();
        }

        private async void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            bool isVideoON = VideoToggle.IsOn;
            bool isAudioON = AVisualToggle.IsOn;

            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var files = (await e.DataView.GetStorageItemsAsync()).ToList();
                if (files.Count <= 0)
                {
                    return;
                }

                var path = files[0].Path;
                if (Path.Exists(path))
                {
                    RunAysnc(path, isVideoON, isAudioON);
                }
            }
        }

        private void TagComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_itemsList.Count > 0)
            {
                DVButton.IsEnabled = false;

                UpdateListView();

                DVButton.IsEnabled = _itemsViewList.Count > 0;
            }
        }

        private async void Browser_Click(object sender, RoutedEventArgs e)
        {
            FileOpenPicker filePicker = new()
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };

            IntPtr hwnd = this.GetWindowHandle();

            filePicker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

            var file = await filePicker.PickSingleFileAsync();

            bool isVideoON = VideoToggle.IsOn;
            bool isAudioON = AVisualToggle.IsOn;

            if (file == null)
                return;
            else
            {
                RunAysnc(file.Path, isVideoON, isAudioON);
            }
        }

        private void Stream_Click(object sender, RoutedEventArgs e)
        {
            string urlpath = UrlTextBox.Text;
            //{
            //    var baseWidth = MainGrid.ActualWidth;
            //    TextBox urlTextBox = new()
            //    {
            //        PlaceholderText = "请输入 URL",
            //        MinWidth = baseWidth - 200
            //    };

            //    var dialog = new ContentDialog
            //    {
            //        Title = "输入 URL",
            //        PrimaryButtonText = "确认",
            //        SecondaryButtonText = "取消",
            //        XamlRoot = MainGrid.XamlRoot,
            //        Content = urlTextBox,
            //        MinWidth = baseWidth - 100
            //    };

            //    ContentDialogResult result = await dialog.ShowAsync();

            //    // 确认按钮被点击
            //    if (result == ContentDialogResult.Primary)
            //    {
            //        urlpath = urlTextBox.Text;
            //    }
            //}

            bool isVideoON = VideoToggle.IsOn;
            bool isAudioON = AVisualToggle.IsOn;

            if(_downloadThread == null)
            {
                if (urlpath.StartsWith("http://"))
                {
                    ProgressDownload.IsIndeterminate = true;
                    TotalDownloadBytes = 0;
                    _downloadcts = new CancellationTokenSource();
                    RunUrlAysnc(urlpath, isVideoON, isAudioON, _downloadcts.Token);
                    DownloadBtn.Content = "Stop";
                }
            }
            else
            {
                _downloadcts.Cancel();
                _downloadThread.Join();
                _downloadThread = null;
                DownloadBtn.Content = "Download";
                ProgressDownload.IsIndeterminate = false;
            }
        }

        private void UrlTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UrlTextBox.Select(0, 0);
        }
    }
}
