using FlvMonitor.Toolbox;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;

namespace FlvMonitor.Library
{
    public delegate void ProgressLengthHandler(long lenth);
    public delegate void ProviderCacheHandler(byte[] buffer, long length);

    public interface IDataProvider
    {
        event ProgressLengthHandler? ProgressChanged;
        event ProviderCacheHandler? ProviderDataChanged;

        string Description();

        long Position();

        bool RequestLength(long length);

        void Seek(long offset);

        uint ReadUInt8();

        uint ReadUInt24();

        uint ReadUInt32();

        byte[] ReadBytes(int length);

        int ReadBytes(byte[] buff, int offset, int length);

        UInt32 GetUInt32();
    }

    public class FileBaseDataProvider : IDataProvider, IDisposable
    {
        private string _fileName;
        private Stream _stream;
        private long _totalLength;

        public event ProgressLengthHandler? ProgressChanged;
        public event ProviderCacheHandler? ProviderDataChanged;

        public FileBaseDataProvider(string fileName)
        {
            _fileName = fileName;
            _stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _totalLength = _stream.Length;
        }
        private long Length() => _totalLength;

        public string Description() => Path.GetFileName(_fileName);
        public long Position() => _stream.Position;
        public bool RequestLength(long rl) { return (Length() - Position()) < rl; }
        public byte[] ReadBytes(int length)
        {
            byte[] buff = new byte[length];
            _stream.Read(buff, 0, length);
            return buff;
        }

        public int ReadBytes(byte[] buff, int offset, int length)
        {
            var len = _stream.Read(buff, offset, length);
            return len;
        }

        public uint ReadUInt24()
        {
            byte[] x = new byte[4];
            _stream.Read(x, 1, 3);
            return BitConverterBE.ToUInt32(x, 0);
        }

        public uint ReadUInt32()
        {
            Span<byte> x = stackalloc byte[4];
            _stream.Read(x);
            return BitConverterBE.ToUInt32(x, 0);
        }

        public uint ReadUInt8() => (uint)_stream.ReadByte();

        public void Seek(long offset)
        {
            _stream.Seek(offset, SeekOrigin.Begin);
        }

        public UInt32 GetUInt32()
        {
            Span<byte> x = stackalloc byte[4];
            _stream.Read(x);
            _stream.Seek(-4, SeekOrigin.Current);
            return BitConverterBE.ToUInt32(x, 0);
        }

        void IDisposable.Dispose()
        {
            if (_stream != null)
            {
                _stream.Close();
                _stream = null;
            }
            _fileName = null;
            GC.SuppressFinalize(this);
        }

        public void UpdateProgress(long length)
        {
            ProgressChanged?.Invoke(length);
        }
        private void DoDataChangedHanlder(byte[] buffer, long lenth)
        {
            ProviderDataChanged?.Invoke(buffer, lenth);
        }
    }

    public class StreamDataProvider : IDataProvider, IDisposable
    {
        private string _url;
        private long _TotalLength;
        private long _ReadPosition;

        private List<KeyValuePair<long, byte[]>> _datas = [];
        private const int SINGLE_BUFF_SIZE = 512*1024;
        private int _datas_index = 0;
        private long _datas_index_pos = 0;

        private CancellationToken _token;

        public event ProgressLengthHandler? ProgressChanged;
        public event ProviderCacheHandler? ProviderDataChanged;

        private async void Open(string urlpath, CancellationToken token)
        {
            lock (this)
            {
                _datas.Clear();
                _datas_index = 0;
                _datas_index_pos = 0;
                _TotalLength = 0;
                _ReadPosition = 0;
            }

            using (HttpClient client = new())
            {
                try
                {
                    long readSize = 0;
                    HttpResponseMessage response = await client.GetAsync(urlpath, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();

                    using (Stream stream = await response.Content.ReadAsStreamAsync(token))
                    {
                        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
                        int bytesRead;
                        int bufferCopied = 0;
                        byte[] np = ArrayPool<byte>.Shared.Rent(SINGLE_BUFF_SIZE);

                        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                        {
                            readSize += bytesRead;
                            UpdateProgress(readSize);
                            DoDataChangedHanlder(buffer, bytesRead);
                            if (SINGLE_BUFF_SIZE - bufferCopied >= bytesRead)
                            {
                                Array.Copy(buffer, 0, np, bufferCopied, bytesRead);
                                bufferCopied += bytesRead;
                            }
                            else
                            {
                                int firstCopied = SINGLE_BUFF_SIZE - bufferCopied;
                                Array.Copy(buffer, 0, np, bufferCopied, firstCopied);
                                lock (this) {
                                    _datas.Add(new KeyValuePair<long, byte[]>(SINGLE_BUFF_SIZE, np));
                                    _TotalLength += SINGLE_BUFF_SIZE;
                                }
                        
                                int remain = bytesRead - firstCopied;

                                np = ArrayPool<byte>.Shared.Rent(SINGLE_BUFF_SIZE);
                                Array.Copy(buffer, firstCopied, np, 0, remain);
                                bufferCopied = remain;
                            }
                        }

                        //_datas.Add(new(bytesRead, buffer));
                        //_TotalLength += bytesRead;
                        //buffer = ArrayPool<byte>.Shared.Rent(8192);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"发生错误: {ex.Message}");
                }
            }
        }

        public StreamDataProvider(string url, CancellationToken token)
        {
            _url = url;
            _token = token;
            Open(url, _token);
        }
        private long Length() => _TotalLength;

        public string Description() => _url;
        
        public long Position() => _ReadPosition;

        public bool RequestLength(long rl)
        {
            while (((Length() - Position()) <= rl) && !_token.IsCancellationRequested)
            {
                Thread.Sleep(100);
            }
            return (Length() - Position()) < rl;
        }

        public byte[] ReadBytes(int length)
        {
            byte[] buff = new byte[length];
            _Read(buff, 0, length);
            return buff;
        }

        public int ReadBytes(byte[] buff, int offset, int length)
        {
            var len = _Read(buff, offset, length);
            return len;
        }

        public uint ReadUInt24()
        {
            byte[] x = new byte[4];
            _Read(x, 1, 3);
            return BitConverterBE.ToUInt32(x, 0);
        }

        public uint ReadUInt32()
        {
            Span<byte> x = stackalloc byte[4];
            _Read(x);
            return BitConverterBE.ToUInt32(x, 0);
        }

        public uint ReadUInt8() => (uint)ReadByte();

        public void Seek(long offset)
        {
            _Seek(offset, SeekOrigin.Begin);
        }

        public UInt32 GetUInt32()
        {
            Span<byte> x = stackalloc byte[4];
            _Read(x);
            _Seek(-4, SeekOrigin.Current);
            return BitConverterBE.ToUInt32(x, 0);
        }

        private void _datas_index_plus()
        {
            _datas_index++;
            _datas_index_pos = 0;
        }
        private void _datas_index_minus()
        {
            _datas_index--;
            _datas_index_pos = 0;
        }

        private int _readimp(byte[] buffer, int offset, int count)
        {
            int ret = 0;
            lock (this)
            {
                long sum = _ReadPosition;
                int dst_offset = offset;

                for (int i = _datas_index; i < _datas.Count; i++)
                {
                    var src_len = _datas[i].Key - _datas_index_pos;
                    var copy_len = Math.Min(count, (int)src_len);

                    Array.Copy(_datas[i].Value, _datas_index_pos, buffer, dst_offset, copy_len);

                    _datas_index_pos += copy_len;
                    sum += copy_len;
                    count -= copy_len;
                    dst_offset += copy_len;
                    if (count <= 0)
                    {
                        break;
                    }
                    _datas_index_plus();
                }
                ret = (int)(sum - _ReadPosition);
                _ReadPosition = sum;
            }
            return ret;
        }
        private int _Read(byte[] buffer, int offset, int count)
        {
            int ret = 0;

            if (!RequestLength(count))
            {
                ret = _readimp(buffer, offset, count);
            }

            return ret;
        }

        private int _Read(Span<byte> buffer)
        {
            byte[] sharedBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                int numRead = _Read(sharedBuffer, 0, buffer.Length);
                new ReadOnlySpan<byte>(sharedBuffer, 0, numRead).CopyTo(buffer);
                return numRead;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(sharedBuffer);
            }
        }

        private int ReadByte()
        {
            var oneByteArray = new byte[1];
            int r = _Read(oneByteArray, 0, 1);
            return r == 0 ? -1 : oneByteArray[0];
        }

        private bool _offset_to_position(long offset)
        {
            lock (this)
            {
                long sum = 0;
                long target = offset;
                for (int i = 0; i<_datas.Count; i++)
                {
                    var len = _datas[i].Key;
                    if (target <= (sum + len))
                    {
                        _datas_index = i;
                        _datas_index_pos = target - sum;
                        break;
                    }
                    sum += len;
                }
                _ReadPosition = target;
            }

            return true;
        }

        private void _Seek(long offset, SeekOrigin o)
        {
            switch (o)
            {
            case SeekOrigin.Begin:
            {
                _offset_to_position(offset);
            }
            break;
            case SeekOrigin.Current:
            {
                if (offset >= 0)
                {
                    _offset_to_position(_ReadPosition + offset);
                }
                else
                {
                    _offset_to_position(Math.Max(0, _ReadPosition + offset));
                }
            }
            break;
            case SeekOrigin.End:
            {
                _offset_to_position(_TotalLength + offset);
            }
            break;
            }
        }

        void IDisposable.Dispose()
        {
            lock (this)
            {
                _datas.Clear();
                _ReadPosition = 0;
                _datas_index = 0;
                _datas_index_pos = 0;
                _TotalLength = 0;
            }
            GC.SuppressFinalize(this);
        }

        private void UpdateProgress(long length)
        {
            ProgressChanged?.Invoke(length);
        }

        private void DoDataChangedHanlder(byte[] buffer, long lenth)
        {
            ProviderDataChanged?.Invoke(buffer, lenth);
        }
    }
}
