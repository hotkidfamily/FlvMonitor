using FFmpeg.AutoGen;
using FFmpeg.AutoGen.Example;
using FlvMonitor.Library;
using FlvMonitor.Model;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FlvMonitor
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : WindowEx, INotifyPropertyChanged, INotifyCollectionChanged
    {
        private Thread? _statusThread;
        private DispatcherQueue _queue;
        private List<FlvTag> _itemsList = [];
        private ContentDialog _contentDialog;
        private string _tempImagePath = default!;
        private string _tempDownloadPath = default!;

        private long _totalDownloadBytes = 0;
        private bool _isIdle = true;

        private readonly int _DOWNLOAD_PROGRESS_STEP = 10*1000*1000;

        private CancellationTokenSource _tokenSource = new();
        private FileStream? _fs = null;

        public bool IsSaveUrlToTile { get; set; } = false;
        public bool IsVideoOn { get; set; } = false;
        public bool IsAudioOn { get; set; } = false;

        public bool IsIdle
        {
            get => _isIdle;
            set
            {
                if(_isIdle != value)
                {
                    _isIdle = value;
                    OnPropertyChanged();
                }
            }
        }


        public ObservableCollection<ParseListViewItem> ItemsViewList = [];

        public long TotalDownloadBytes
        {
            get => _totalDownloadBytes;
            set
            {
                if (_totalDownloadBytes != value)
                {
                    _totalDownloadBytes = value;
                    OnPropertyChanged();
                }
            }
        }
        public static readonly DependencyProperty TotalDownloadBytesProperty =
            DependencyProperty.Register("TotalDownloadBytes", typeof(long), typeof(MainWindow), new PropertyMetadata(0));

        public event PropertyChangedEventHandler? PropertyChanged;
        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        private void OnCollectionChanged(NotifyCollectionChangedEventArgs v)
        {
            CollectionChanged?.Invoke(this, v);
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
            DVButton.IsEnabled = ItemsViewList.Count > 0;
            ItemsViewList.CollectionChanged +=ItemsViewList_CollectionChanged;
            _contentDialog = new ContentDialog();

            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            _tempImagePath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp\\FlvMonitor\\Images");
            _tempDownloadPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp\\FlvMonitor\\Download");
            if (!Path.Exists(_tempImagePath))
            {
                Directory.CreateDirectory(_tempImagePath);
            }
            if (!Path.Exists(_tempDownloadPath))
            {
                Directory.CreateDirectory(_tempDownloadPath);
            }
        }

        private void ItemsViewList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
            case NotifyCollectionChangedAction.Add:
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, e.NewItems));
                break;

            case NotifyCollectionChangedAction.Remove:
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, e.NewItems));
                break;

            case NotifyCollectionChangedAction.Reset:
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset, e.NewItems));
                break;
            }
        }

        private IEnumerable<FlvTag> _extraFile(string path, CancellationToken token)
        {
            IDataProvider? provider = null;
            if (path.StartsWith("http://"))
            {
                provider = new StreamDataProvider(path, token);
            }
            else
            {
                provider = new FileBaseDataProvider(path);
            }

            provider.ProgressChanged += Provider_ProgressChanged;
            provider.ProviderDataChanged += Provider_ProviderDataChanged;

            FlvSpecs parser = new(provider, token);
            long offset = parser.parseFileHeader();

            while (parser.parseTag(offset, out var tag))
            {
                offset += tag.previousTagSize + 4;
                yield return tag;
            }
        }

        private async void Provider_ProviderDataChanged(byte[] buffer, long length)
        {
            if (IsSaveUrlToTile)
            {
                if (_fs == null)
                {
                    string filepath = Path.Combine(_tempDownloadPath, "live-stream.flv");
                    _fs = new(filepath, FileMode.Create, FileAccess.Write, FileShare.Read);
                }
                if (_fs != null)
                {
                    await _fs.WriteAsync(buffer.AsMemory(0, (int)length));
                }
            }
        }

        private void Provider_ProgressChanged(long lenth)
        {
            _queue.TryEnqueue(() =>
            {
                TotalDownloadBytes = lenth / 1000;
            });
        }

        private void UpdateListView()
        {
            var TagTypes = TagComboBox.SelectedIndex;
            ItemsViewList.Clear();

            long last_video_dts = long.MaxValue;
            long last_video_pts = long.MaxValue;
            long last_audio_pts = long.MaxValue;

            if (TagTypes == 0)
            {
                int frameid = 0;

                foreach (var flv in _itemsList)
                {
                    if (flv.tagType == 8)
                    {
                        if (last_audio_pts == long.MaxValue)
                        {
                            last_audio_pts = flv.timestamp;
                        }

                        string image = Path.Join(_tempImagePath, $"audio_{flv.timestamp}.png");
                        var aptsd = flv.timestamp - last_audio_pts;
                        ParseListViewItem it = new(flv, frameid++, image, aptsd, 0);

                        last_audio_pts = flv.timestamp;
                        ItemsViewList.Add(it);
                    }
                    else if (flv.tagType == 9)
                    { 
                        if (last_video_dts == long.MaxValue)
                        {
                            last_video_dts = flv.timestamp;
                        }

                        long pts = flv.timestamp + flv.v.compositionTime;
                        if (last_video_pts == long.MaxValue)
                        {
                            last_video_pts = pts;
                        }

                        long tagPtsDiff = flv.timestamp - last_video_dts;
                        long calcPtsDiff = pts - last_video_pts;
                        string image = Path.Join(_tempImagePath, $"{pts}.png");

                        ParseListViewItem it = new(flv, frameid++, image, tagPtsDiff, calcPtsDiff);

                        last_video_dts = flv.timestamp;
                        last_video_pts = pts;
                        ItemsViewList.Add(it);

                    }
                    else
                    {
                        ParseListViewItem it = new(flv, frameid++, null, 0, 0);
                        ItemsViewList.Add(it);
                    }
                }
            }
            else if (TagTypes == 1)
            {
                int frameid = 0;

                foreach (var flv in _itemsList)
                {
                    if (flv.tagType == 9)
                    {
                        if (last_video_dts == long.MaxValue)
                        {
                            last_video_dts = flv.timestamp;
                        }

                        long pts = flv.timestamp + flv.v.compositionTime;
                        if (last_video_pts == long.MaxValue)
                        {
                            last_video_pts = pts;
                        }

                        long tagPtsDiff = flv.timestamp - last_video_dts;
                        long calcPtsDiff = pts - last_video_pts;
                        string image = Path.Join(_tempImagePath, $"{pts}.png");

                        ParseListViewItem it = new(flv, frameid++, image, tagPtsDiff, calcPtsDiff);

                        last_video_dts = flv.timestamp;
                        last_video_pts = pts;
                        ItemsViewList.Add(it);
                    }
                }
            }
            else if (TagTypes == 2)
            {
                int frameid = 0;

                foreach (var flv in _itemsList)
                {
                    if (flv.tagType == 8)
                    {
                        if (last_audio_pts == long.MaxValue)
                        {
                            last_audio_pts = flv.timestamp;
                        }

                        string image = Path.Join(_tempImagePath, $"audio_{flv.timestamp}.png");
                        var aptsd = flv.timestamp - last_audio_pts;
                        ParseListViewItem it = new(flv, frameid++, image, aptsd, 0);

                        last_audio_pts = flv.timestamp;
                        ItemsViewList.Add(it);
                    }
                }
            }
        }        

        private void RunAysnc(string path, bool isVideoON, bool isAudioON, int TagTypes)
        {
            _statusThread = new Thread(() =>
            {
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

                double oldp = 0;

                try
                {
                    FFmpegDecoder vd = new(_tempImagePath);
                    FFmpegDecoder ad = new(_tempImagePath);

                    _itemsList.Clear();

                    int frameid = 0;
                    long last_video_dts = long.MaxValue;
                    long last_video_pts = long.MaxValue;
                    long last_audio_pts = long.MaxValue;

                    long maxium_progress = 0;
                    if (path.Contains("http://"))
                    {
                        maxium_progress = _DOWNLOAD_PROGRESS_STEP;
                    }
                    else
                    {
                        FileInfo fi = new(path);
                        maxium_progress = fi.Length;
                    }

                    foreach (var flv in _extraFile(path, _tokenSource.Token))
                    {
                        _itemsList.Add(flv);

                        if (flv.tagType == 9)
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
                            if (TagTypes == 0 || TagTypes == 1)
                            {
                                if (last_video_dts == long.MaxValue)
                                {
                                    last_video_dts = flv.timestamp;
                                }

                                long pts = flv.timestamp + flv.v.compositionTime;
                                if (last_video_pts == long.MaxValue)
                                {
                                    last_video_pts = pts;
                                }

                                long tagPtsDiff = flv.timestamp - last_video_dts;
                                long calcPtsDiff = pts - last_video_pts;
                                string image = Path.Join(_tempImagePath, $"{pts}.png");

                                ParseListViewItem it = new(flv, frameid++, image, tagPtsDiff, calcPtsDiff);

                                last_video_dts = flv.timestamp;
                                last_video_pts = pts;
                                _queue.TryEnqueue(() =>
                                {
                                    ItemsViewList.Add(it);
                                });
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
                            if (TagTypes == 0 || TagTypes == 2)
                            {
                                if (last_audio_pts == long.MaxValue)
                                {
                                    last_audio_pts = flv.timestamp;
                                }

                                string image = Path.Join(_tempImagePath, $"audio_{flv.timestamp}.png");
                                var aptsd = flv.timestamp - last_audio_pts;
                                ParseListViewItem it = new(flv, frameid++, image, aptsd, 0);

                                last_audio_pts = flv.timestamp;
                                _queue.TryEnqueue(() =>
                                {
                                    ItemsViewList.Add(it);
                                });
                            }
                        }

                        var newp = (flv.addr * 100 / maxium_progress + 0.5);
                        if (newp > 100)
                        {
                            maxium_progress += _DOWNLOAD_PROGRESS_STEP;
                            continue;
                        }
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
                _queue.TryEnqueue(() =>
                {
                    IsIdle = true;
                });
            });

            _statusThread.Start();
        }

        private async void Grid_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;

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
                    if(_statusThread != null)
                    {
                        _tokenSource.Cancel();
                        if (_statusThread != null)
                        {
                            _statusThread.Join();
                        }
                        _statusThread = null;
                        IsIdle = true;
                    }

                    var TagTypes = TagComboBox.SelectedIndex;
                    TotalDownloadBytes = 0;
                    IsIdle = false;
                    ItemsViewList.Clear();
                    _tokenSource = new();
                    RunAysnc(path, IsVideoOn, IsAudioOn, TagTypes);
                }
            }
        }

        private void TagComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_itemsList.Count > 0)
            {
                DVButton.IsEnabled = false;

                UpdateListView();

                DVButton.IsEnabled = ItemsViewList.Count > 0;
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
            if (file == null)
                return;
            else
            {
                if (_statusThread != null)
                {
                    _tokenSource.Cancel();
                    if (_statusThread != null)
                    {
                        _statusThread.Join();
                    }
                    _statusThread = null;
                    IsIdle = true;
                }

                var TagTypes = TagComboBox.SelectedIndex;
                TotalDownloadBytes = 0;
                IsIdle = false;
                ItemsViewList.Clear();
                _tokenSource = new();
                RunAysnc(file.Path, IsVideoOn, IsAudioOn, TagTypes);
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

            if (DownloadBtn.Content is string c)
            {
                if (c == "Download")
                {
                    if (urlpath.StartsWith("http://"))
                    {
                        ProgressDownload.IsIndeterminate = true;
                        DownloadBtn.Content = "Stop";

                        var TagTypes = TagComboBox.SelectedIndex;
                        TotalDownloadBytes = 0;
                        IsIdle = false;
                        ItemsViewList.Clear();
                        _tokenSource = new();
                        if (_fs != null)
                        {
                            _fs.Close();
                            _fs = null;
                        }
                        RunAysnc(urlpath, IsVideoOn, IsAudioOn, TagTypes);
                    }
                }
                else
                {
                    _tokenSource.Cancel();
                    if (_statusThread != null)
                    {
                        _statusThread.Join();
                    }

                    if (_fs != null)
                    {
                        _fs.Flush();
                        _fs.Close();
                        _fs = null;
                    }
                    _statusThread = null;
                    IsIdle = true;

                    DownloadBtn.Content = "Download";
                    ProgressDownload.IsIndeterminate = false;
                }
            }
        }

        private void UrlTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UrlTextBox.Select(0, 0);
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            string path = _tempDownloadPath;
            Process.Start("explorer.exe", path);
        }
    }
}
