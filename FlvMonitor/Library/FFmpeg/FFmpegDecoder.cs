﻿using FFmpeg.AutoGen;
using FFmpeg.AutoGen.Example;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace FlvMonitor.Library
{
    public unsafe class FFmpegDecoder
    {
        private string _tempPath;

        [DllImport("Kernel32.dll", EntryPoint = "RtlMoveMemory", SetLastError = false)]
        static extern void MoveMemory(IntPtr dest, IntPtr src, int size);

        public FFmpegDecoder(string path)
        {
            _tempPath = path;
        }

        public static AVCodecID FlvVideoTypeToFFmpeg(uint v)
        {
            AVCodecID desc;

            switch (v)
            {
            case 2:
                desc = AVCodecID.AV_CODEC_ID_H263;
                break;
            case 3:
                desc = AVCodecID.AV_CODEC_ID_SCREENPRESSO;
                break;
            case 4:
                desc = AVCodecID.AV_CODEC_ID_VP6;
                break;
            case 5:
                desc = AVCodecID.AV_CODEC_ID_VP6A;
                break;
            case 6:
                desc = AVCodecID.AV_CODEC_ID_SCREENPRESSO;
                break;
            case 7:
                desc = AVCodecID.AV_CODEC_ID_H264;
                break;
            case 12:
                desc = AVCodecID.AV_CODEC_ID_HEVC;
                break;
            default:
                desc = AVCodecID.AV_CODEC_ID_NONE;
                break;
            }
            return desc;
        }

        public static AVCodecID FlvAudioTypeToFFmpeg(uint v)
        {
            AVCodecID desc;

            switch (v)
            {
            case 0:
                desc = AVCodecID.AV_CODEC_ID_PCM_S16LE;
                break;
            case 1:
                desc = AVCodecID.AV_CODEC_ID_PCM_S16BE;
                break;
            case 2:
                desc = AVCodecID.AV_CODEC_ID_MP3;
                break;
            case 3:
                desc = AVCodecID.AV_CODEC_ID_PCM_S16LE;
                break;
            case 4:
                desc = AVCodecID.AV_CODEC_ID_NELLYMOSER;
                break;
            case 5:
                desc = AVCodecID.AV_CODEC_ID_NELLYMOSER;
                break;
            case 6:
                desc = AVCodecID.AV_CODEC_ID_NELLYMOSER;
                break;
            case 7:
                desc = AVCodecID.AV_CODEC_ID_PCM_ALAW;
                break;
            case 8:
                desc = AVCodecID.AV_CODEC_ID_PCM_MULAW;
                break;
            case 10:
                desc = AVCodecID.AV_CODEC_ID_AAC;
                break;
            case 11:
                desc = AVCodecID.AV_CODEC_ID_SPEEX;
                break;
            case 14:
                desc = AVCodecID.AV_CODEC_ID_MP3;
                break;
            case 9:
            case 15:
            default:
                desc = AVCodecID.AV_CODEC_ID_NONE;
                break;
            }

            return desc;
        }

        public struct DecodeContext
        {
            public AVCodecID CodecID;
            public AVBSFContext* FilterCtx = null;
            public AVCodecContext* CodecContext = null;
            public AVFrame* decFrame = null;
            public AVFrame* RGBFrame = null;
            public byte[] rgbdata = null;
            public int rgbdata_lenth = 0;
            public AVPacket* iPkt = null;
            public SwsContext* Scale = null;

            public SwrContext* SwrContext = null;
            public byte** dst_data = null;
            public long dst_data_capcity = 0;
            public long dst_data_linesize = 0;
            public int swr_dst_nb_channels = 1;
            public int swr_dst_sample_fmt = (int)AVSampleFormat.AV_SAMPLE_FMT_S16;
            public int swr_dst_sample_rate = 48000;

            public int width;
            public int height;

            public DecodeContext()
            {
                CodecID = AVCodecID.AV_CODEC_ID_NONE;
                CodecContext = null;
                decFrame = null;
                iPkt = null;
            }
        };

        private DecodeContext _context;
        public bool Ready { get; private set; }

        public bool CreateDecoder(AVCodecID codecID, Span<byte> extradata, int w, int h)
        {
            int ret = -1;
            do
            {
                var codec = ffmpeg.avcodec_find_decoder(codecID);
                if (codec == null)
                {
                    Debugger.Log(0, "s", $"Codec not found: {codecID}\n");
                    break;
                }

                var cc = ffmpeg.avcodec_alloc_context3(codec);
                if (cc == null)
                {
                    Debugger.Log(0, "s", $"Could not allocate video codec context: {codecID}\n");
                    break;
                }

                cc->extradata = (byte*)ffmpeg.av_mallocz((ulong)(extradata.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE));
                cc->extradata_size = extradata.Length;
                fixed (byte* ptr = extradata)
                    MoveMemory((IntPtr)cc->extradata, (IntPtr)ptr, extradata.Length);
                if((codec->capabilities & ffmpeg.AV_CODEC_CAP_SLICE_THREADS) != 0)
                {
                    cc->thread_type |= ffmpeg.FF_THREAD_SLICE;
                }
                if ((codec->capabilities & ffmpeg.AV_CODEC_CAP_FRAME_THREADS) != 0)
                {
                    cc->thread_type |= ffmpeg.FF_THREAD_FRAME;
                }

                /* open it */
                if ((ret = ffmpeg.avcodec_open2(cc, codec, null)) < 0)
                {
                    Debugger.Log(0, "s", "Could not open codec\n");
                    break;
                }

                var pkt = ffmpeg.av_packet_alloc();
                if (pkt == null)
                {
                    Debugger.Log(0, "s", "Could not allocate video packet\n");
                    break;
                }

                var frame = ffmpeg.av_frame_alloc();
                if (frame == null)
                {
                    Debugger.Log(0, "s", "Could not allocate video frame\n");
                    break;
                }

                AVBitStreamFilter* filter = null;
                AVBSFContext* bsf = null;

                if (codecID == AVCodecID.AV_CODEC_ID_H264)
                {
                    filter = ffmpeg.av_bsf_get_by_name("h264_mp4toannexb");
                    if (filter == null)
                    {
                        Debugger.Log(0, "s", "Could not find bsf h264_mp4toannexb\n");
                        break;
                    }
                }
                else if (codecID == AVCodecID.AV_CODEC_ID_HEVC)
                {
                    filter = ffmpeg.av_bsf_get_by_name("hevc_mp4toannexb");
                    if (filter == null)
                    {
                        Debugger.Log(0, "s", "Could not find bsf hevc_mp4toannexb\n");
                        break;
                    }
                }

                if (filter != null)
                {
                    if ((ret = ffmpeg.av_bsf_alloc(filter, &bsf)) < 0)
                    {
                        Debugger.Log(0, "s", "Could not allocate bsf\n");
                        break;
                    }

                    bsf->par_in->codec_id = AVCodecID.AV_CODEC_ID_HEVC;
                    if ((ret = ffmpeg.av_bsf_init(bsf)) < 0)
                    {
                        Debugger.Log(0, "s", "Could not init bsf\n");
                        break;
                    }
                }

                _context = new()
                {
                    CodecID = codecID,
                    CodecContext = cc,
                    iPkt = pkt,
                    decFrame = frame,
                    width = w,
                    height = h,
                    FilterCtx = bsf
                };
                Ready = true;

            } while (false);

            return (_context.CodecContext != null);
        }

        public void FreeCodec()
        {
            byte[] da = [];

            Decode(da, 0, 0);

            var c = _context;
            if (c.CodecContext != null)
                ffmpeg.avcodec_free_context(&c.CodecContext);

            if (c.decFrame != null)
                ffmpeg.av_frame_free(&c.decFrame);

            if (c.RGBFrame != null)
                ffmpeg.av_frame_free(&c.RGBFrame);

            if (c.iPkt != null)
                ffmpeg.av_packet_free(&c.iPkt);

            if (c.FilterCtx != null)
                ffmpeg.av_bsf_free(&c.FilterCtx);

            if (_context.Scale != null)
                ffmpeg.sws_freeContext(_context.Scale);

            if (_context.dst_data != null)
                ffmpeg.av_freep(_context.dst_data);

            if(_context.SwrContext != null)
                ffmpeg.swr_close(_context.SwrContext);
        }


        private bool SaveJPG(AVFrame* frame)
        {
            int ow = _context.width;
            int oh = _context.height;
            if (_context.width == -1 && _context.height == -1)
            {
                ow = frame->width;
                oh = frame->height;
            }
            else
            {
                if (_context.height != -1)
                {
                    ow = (_context.height * frame->width/ frame->height) & ~0x1;
                    oh = _context.height;
                }
                else
                {
                    ow = _context.width;
                    oh = (_context.width * frame->height/ frame->width) & ~0x1;
                }
            }

            if (frame->format != (int)AVPixelFormat.AV_PIX_FMT_RGBA)
            {
                if (_context.Scale == null
                    || _context.RGBFrame->width != ow
                    || _context.RGBFrame->height != oh)
                {
                    AVFrame* rgbF = ffmpeg.av_frame_alloc();
                    rgbF->width = ow;
                    rgbF->height = oh;
                    rgbF->format = (int)AVPixelFormat.AV_PIX_FMT_RGBA;
                    if (ffmpeg.av_frame_get_buffer(rgbF, 16) < 0)
                    {
                        return false;
                    }
                    _context.RGBFrame = rgbF;

                    _context.rgbdata_lenth = rgbF->linesize[0]*rgbF->height;
                    _context.rgbdata = new byte[_context.rgbdata_lenth];
                    var sws_ctx = ffmpeg.sws_getContext(frame->width, frame->height, (AVPixelFormat)frame->format,
                    rgbF->width, rgbF->height, (AVPixelFormat)rgbF->format, ffmpeg.SWS_BILINEAR, null, null, null);

                    if (sws_ctx == null)
                    {
                        return false;
                    }
                    _context.Scale = sws_ctx;
                }

                if (_context.Scale != null)
                {
                    AVFrame* rgb = _context.RGBFrame;
                    ffmpeg.sws_scale(_context.Scale, frame->data, frame->linesize, 0, frame->height,
                    rgb->data, rgb->linesize);

                    string op = Path.Join(_tempPath, $"{frame->pts}.png");
                    BitmapTool.SaveToBitmap(rgb->width, rgb->height, (IntPtr)rgb->data[0], rgb->linesize[0], op);
                }
            }
            return true;
        }
        

        private List<short> _sampleMeanfullValue(byte** dst_data, int data_len, int width)
        {
            int step = (int)Math.Floor(data_len*1.0/width);
            List<short> values = [];
            short* addr = (short*)dst_data[0];
            var v = addr;
            int calcedLen = 0;

            for (var i = 0; i < width - 1; i++)
            {
                v = addr + i * step;
                var meanValue = VoiceAlgo.CalclSamplesMeanPower(v, step);
                values.Add(meanValue);
                calcedLen += step;
            }

            var unCalcLen = data_len - calcedLen;
            var extremaValue = VoiceAlgo.CalclSamplesMeanPower(v, unCalcLen);
            values.Add(extremaValue);

            return values;
        }

        private bool CalcVoicePower(AVFrame* frame)
        {
            AVCodecContext* cc = _context.CodecContext;

            int src_nb_channels = cc->codec->ch_layouts->nb_channels;
            int src_sample_rate = cc->sample_rate;

            int dst_sample_rate = _context.swr_dst_sample_rate;
            int dst_nb_channels = (int)_context.swr_dst_nb_channels;
            var dst_sample_fmt = (AVSampleFormat)_context.swr_dst_sample_fmt;
            int ret = 0;

            if (_context.SwrContext == null)
            {
                SwrContext* swr_ctx = null;
                AVChannelLayout dst_ch_layout = new();
                ffmpeg.av_channel_layout_default(&dst_ch_layout, dst_nb_channels);
                ret = ffmpeg.swr_alloc_set_opts2(&swr_ctx,
                            &dst_ch_layout,
                            dst_sample_fmt,
                            dst_sample_rate,
                            cc->codec->ch_layouts,
                                (AVSampleFormat)frame->format,
                            src_sample_rate,
                            0,
                            null);
                if ((ret < 0) || (ret = ffmpeg.swr_init(swr_ctx))<0)
                {
                    Debugger.Log(0, "s", "Could not init swr\n");
                    return false;
                }

                byte** dst_data;
                int dst_linesize;
                long dst_nb_samples = ffmpeg.av_rescale_rnd(frame->nb_samples, dst_sample_rate, src_sample_rate, AVRounding.AV_ROUND_UP);
                if ((ret = ffmpeg.av_samples_alloc_array_and_samples(&dst_data,
                    &dst_linesize, src_nb_channels, (int)dst_nb_samples, dst_sample_fmt, 0)) < 0)
                {
                    Debugger.Log(0, "s", "Could not alloc resample dest buffer\n");
                    return false;
                }

                _context.dst_data_capcity = dst_nb_samples;
                _context.dst_data_linesize = dst_linesize;
                _context.dst_data = dst_data;
                _context.SwrContext = swr_ctx;
            }

            if (_context.SwrContext != null)
            {
                byte** dst_data = _context.dst_data;
                int dst_linesize = (int)_context.dst_data_linesize;

                int dst_nb_samples = (int)ffmpeg.av_rescale_rnd(
                    ffmpeg.swr_get_delay(_context.SwrContext, src_sample_rate) + frame->nb_samples, 
                    dst_sample_rate, src_sample_rate, AVRounding.AV_ROUND_UP);
                if (dst_nb_samples > _context.dst_data_capcity)
                {
                    ffmpeg.av_freep(_context.dst_data);
                    ret = ffmpeg.av_samples_alloc(dst_data, &dst_linesize, src_nb_channels, dst_nb_samples, dst_sample_fmt, 1);
                    if (ret < 0)
                    {
                        Debugger.Log(0, "s", "Could not alloc resample dest buffer\n");
                        return false;
                    }

                    _context.dst_data_capcity = dst_nb_samples;
                }

                ret = ffmpeg.swr_convert(_context.SwrContext, dst_data, (int)dst_nb_samples,
                    (byte**)&frame->data, frame->nb_samples);

                if(ret > 0)
                {
                    var width = _context.width * 2;
                    var height = _context.height;

                    var values = _sampleMeanfullValue(dst_data, ret, width);
                    string op = Path.Join(_tempPath, $"audio_{frame->pts}.png");
                    BitmapTool.CreateVoiceBitmap(ref values, width, height, op);
                }
            }

            return true;
        }

        private bool DecodeInternal(AVPacket* pkt)
        {
            AVFrame* frame = _context.decFrame;
            AVCodecContext* cc = _context.CodecContext;

            int ret = 0;
            ret = ffmpeg.avcodec_send_packet(cc, pkt);
            if (ret < 0)
            {
                Debugger.Log(0, "s", $"send_packet: {FFmpegHelper.av_err2str(ret)}\n");
                return false;
            }

            while (ret >= 0)
            {
                ret = ffmpeg.avcodec_receive_frame(cc, frame);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                {
                    return true;
                }
                else if (ret < 0)
                {
                    Debugger.Log(0, "s", $"receive_frame: {FFmpegHelper.av_err2str(ret)}\n");
                    return false;
                }

                if (cc->codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO)
                {
                    SaveJPG(frame);
                }
                else
                {
                    CalcVoicePower(frame);
                }
            }

            return true;
        }

        public  bool Decode(Span<byte> data, long dts, long pts)
        {
            AVCodecContext* cc = _context.CodecContext;
            AVBSFContext* bsf = _context.FilterCtx;
            AVPacket* pkt_ref;

            int ret = 0;

            AVPacket* pkt = _context.iPkt;
            if (bsf != null)
            {
                do
                {
                    pkt_ref = ffmpeg.av_packet_alloc();
                    if (data.Length != 0)
                    {
                        fixed (byte* ptr = data)
                            pkt->data = ptr;
                        pkt->size = data.Length;
                        pkt->dts = dts;
                        pkt->pts = pts;

                        ret = ffmpeg.av_packet_ref(pkt_ref, pkt);
                        if (ret < 0)
                        {
                            break;
                        }
                    }
                    else
                    {
                        pkt->data = null;
                        pkt->size = 0;
                    }

                    ret = ffmpeg.av_bsf_send_packet(bsf, pkt_ref);
                    if (ret < 0)
                    {
                        Debugger.Log(0, "s", $"bsf_send_packet: {FFmpegHelper.av_err2str(ret)}\n");
                        ffmpeg.av_packet_unref(pkt_ref);
                        break;
                    }

                    while (ret >= 0)
                    {
                        ret = ffmpeg.av_bsf_receive_packet(bsf, pkt_ref);
                        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                        {
                            ffmpeg.av_bsf_flush(bsf);
                            if (pkt_ref != null)
                                ffmpeg.av_packet_unref(pkt_ref);

                            DecodeInternal(pkt);
                            return true;
                        }
                        else if (ret < 0)
                        {
                            Debugger.Log(0, "s", $"bsf_receive_packet: {FFmpegHelper.av_err2str(ret)}\n");
                            return false;
                        }

                        DecodeInternal(pkt_ref);

                        if (pkt_ref != null)
                            ffmpeg.av_packet_unref(pkt_ref);
                    }
                } while (false);
            }
            else
            {
                if (data.Length == 0)
                {
                    pkt->data = null;
                    pkt->size = 0;
                }
                else
                {
                    fixed (byte* ptr = data)
                        pkt->data = ptr;
                    pkt->size = data.Length;
                    pkt->dts = dts;
                    pkt->pts = pts;
                }

                DecodeInternal(pkt);
            }

            return true;
        }
    }
}

