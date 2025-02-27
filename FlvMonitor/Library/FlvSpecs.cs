﻿using FlvMonitor.Toolbox;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FlvMonitor.Library
{
    public class NaluDetail
    {
        public string type = "";
        public long offset;
    }
    public struct VideoTag
    {
        public uint frametype;
        public uint codecID;
        public uint avcPacketType;
        public int compositionTime;

        public long NALUs;
        public NaluDetail[] NaluDetails;
    };
    public struct AudioTag
    {
        public uint soundFormat;
        public uint soundRate;
        public uint soundSize;
        public uint soundType;
        public uint aacPacketType;
    };
    public struct FlvTag
    {
        public long addr;
        public uint tagType;
        public uint dataSize;
        public uint timestamp;
        public uint streamID;
        public VideoTag v;
        public AudioTag a;
        public byte[] data;
        public uint previousTagSize;
    }
    internal class FlvSpecs
    {
        private IDataProvider _fs;

        private int _nalLengthSize = 4;
        private bool _hevc_in_annexb = true;
        private long _last_video_dts = long.MaxValue;
        private long _last_video_pts = long.MaxValue;
        private long _last_audio_pts = long.MaxValue;
        private CancellationToken _token;

        enum H264NalType : int
        {
            NAL_UNSPECIFIED = 0,
            NAL_SLICE = 1,
            NAL_DPA = 2,
            NAL_DPB = 3,
            NAL_DPC = 4,
            NAL_IDR_SLICE = 5,
            NAL_SEI = 6,
            NAL_SPS = 7,
            NAL_PPS = 8,
            NAL_AUD = 9,
            NAL_END_SEQUENCE = 10,
            NAL_END_STREAM = 11,
            NAL_FILLER_DATA = 12,
            NAL_SPS_EXT = 13,
            NAL_PREFIX = 14,
            NAL_SUB_SPS = 15,
            NAL_DPS = 16,
            NAL_RESERVED17 = 17,
            NAL_RESERVED18 = 18,
            NAL_AUXILIARY_SLICE = 19,
            NAL_EXTEN_SLICE = 20,
            NAL_DEPTH_EXTEN_SLICE = 21,
            NAL_RESERVED22 = 22,
            NAL_RESERVED23 = 23,
            NAL_UNSPECIFIED24 = 24,
            NAL_UNSPECIFIED25 = 25,
            NAL_UNSPECIFIED26 = 26,
            NAL_UNSPECIFIED27 = 27,
            NAL_UNSPECIFIED28 = 28,
            NAL_UNSPECIFIED29 = 29,
            NAL_UNSPECIFIED30 = 30,
            NAL_UNSPECIFIED31 = 31,
        };

        enum AVCPayloadOffset : int
        {
            lengthSizeMinusOne = 4,
            SPSSize= 5,
        };

        enum HEVCNalType : int
        {
            TRAIL_N = 0,
            TRAIL_R = 1,
            TSA_N = 2,
            TSA_R = 3,
            STSA_N = 4,
            STSA_R = 5,
            RADL_N = 6,
            RADL_R = 7,
            RASL_N = 8,
            RASL_R = 9,
            VCL_N10 = 10,
            VCL_R11 = 11,
            VCL_N12 = 12,
            VCL_R13 = 13,
            VCL_N14 = 14,
            VCL_R15 = 15,
            BLA_W_LP = 16,
            BLA_W_RADL = 17,
            BLA_N_LP = 18,
            IDR_W_RADL = 19,
            IDR_N_LP = 20,
            CRA_NUT = 21,
            RSV_IRAP_VCL22 = 22,
            RSV_IRAP_VCL23 = 23,
            RSV_VCL24 = 24,
            RSV_VCL25 = 25,
            RSV_VCL26 = 26,
            RSV_VCL27 = 27,
            RSV_VCL28 = 28,
            RSV_VCL29 = 29,
            RSV_VCL30 = 30,
            RSV_VCL31 = 31,
            VPS = 32,
            SPS = 33,
            PPS = 34,
            AUD = 35,
            EOS_NUT = 36,
            EOB_NUT = 37,
            FD_NUT = 38,
            SEI_PREFIX = 39,
            SEI_SUFFIX = 40,
            RSV_NVCL41 = 41,
            RSV_NVCL42 = 42,
            RSV_NVCL43 = 43,
            RSV_NVCL44 = 44,
            RSV_NVCL45 = 45,
            RSV_NVCL46 = 46,
            RSV_NVCL47 = 47,
            UNSPEC48 = 48,
            UNSPEC49 = 49,
            UNSPEC50 = 50,
            UNSPEC51 = 51,
            UNSPEC52 = 52,
            UNSPEC53 = 53,
            UNSPEC54 = 54,
            UNSPEC55 = 55,
            UNSPEC56 = 56,
            UNSPEC57 = 57,
            UNSPEC58 = 58,
            UNSPEC59 = 59,
            UNSPEC60 = 60,
            UNSPEC61 = 61,
            UNSPEC62 = 62,
            UNSPEC63 = 63,
        };
        enum HVCCPayloadOffset : int
        {
            lengthSizeMinusOne = 21,
            numOfArrays = 22,
        };

        public FlvSpecs(IDataProvider fs, CancellationToken token)
        {
            _token = token;
            _fs = fs;
        }

        public long parseFileHeader()
        {
            _fs.Seek(0);
            if (_fs.RequestLength(4) || _fs.ReadUInt32() != 0x464C5601)
            {
                if (_fs.RequestLength(4) && _fs.ReadUInt32() == 0x66747970)
                {
                    throw new Exception($"{_fs.Description()} is a MP4 file. YAMB or MP4Box can be used to extract streams.");
                }
                else
                {
                    throw new Exception($"{_fs.Description()} isn't a FLV file.");
                }
            }

            var flags = _fs.ReadUInt8();
            var dataOffset = _fs.ReadUInt32();

            _fs.Seek(dataOffset);

            var prevTagSize = _fs.ReadUInt32();
            _ = ParseHEVCMuxerType(ref _hevc_in_annexb);

            return dataOffset + 4;
        }

        /*
         * Parse Video Tag to guess either annexb or standard mp4 format
         * return: True -> parse end
         *         False -> next Tag
         */
        private bool PreParseTag(ref int VideoTags, ref bool bAnnexb)
        {
            uint tagType, dataSize, timeStamp, streamID, mediaInfo, pkgType = 0, codecId = 0;
            byte[] data;
            long curTagpos = 0;
            uint tagSize = 0;

            if (_fs.RequestLength(11))
            {
                return false;
            }
            curTagpos = _fs.Position();
            // Read tag header
            tagType = _fs.ReadUInt8();
            dataSize = _fs.ReadUInt24();
            timeStamp = _fs.ReadUInt24();
            timeStamp |= _fs.ReadUInt8() << 24;
            streamID = _fs.ReadUInt24();

            tagSize = dataSize + 11;

            // Read tag data
            if (dataSize == 0)
            {
                return true;
            }
            if (_fs.RequestLength(dataSize))
            {
                return false;
            }

            mediaInfo = _fs.ReadUInt8();
            UInt32 composition = _fs.GetUInt32();
            dataSize -= 1;
            data = _fs.ReadBytes((int)dataSize);

            if ((tagType == 0x9) && ((mediaInfo >> 4) != 5))
            {
                VideoTags++;
                pkgType = (mediaInfo >> 4) & 0x0f;
                codecId = mediaInfo & 0x0f;

                if (codecId == 12)
                {
                    /* 
                     * first 4 byte = AVCPacketType + CompositionTime
                     * var AVCPacketType = data[0]; 
                     */
                    bool annexb = data[4] == 0x00 && data[5] == 0x00 && ((data[6] == 0x00 && data[7] == 0x01) || (data[6] == 0x01));

                    bAnnexb = annexb;
                }
                else
                {
                    bAnnexb = false;
                }

                return true;
            }
            return false;
        }

        public bool ParseHEVCMuxerType(ref bool bAnnexb)
        {
            var offset = _fs.Position();
            int trueCnt = 0;
            uint prevTagSize;
            bool bAnnexb1 = false;
            int videoTagParseCnt = 0;
            int maxVideoTagParseCnt = 100; /* max parse tag is 100 video frames */
            while (_fs.RequestLength(4))
            {
                if (PreParseTag(ref videoTagParseCnt, ref bAnnexb1))
                {
                    if (bAnnexb1) trueCnt++;
                };

                if (videoTagParseCnt > maxVideoTagParseCnt)
                    break;

                if (_fs.RequestLength(4))
                    break;

                prevTagSize = _fs.ReadUInt32();
            }

            bAnnexb = videoTagParseCnt == trueCnt ? true : false;

            _fs.Seek(offset);
            return true;
        }

        public bool parseTag(long offset, out FlvTag detail)
        {
            detail = new();
            if (_token.IsCancellationRequested) { 
                return false; 
            }

            uint tagType, dataSize, timeStamp, streamID;

            if (_fs.RequestLength(11))
            {
                return false;
            }

            detail.addr = _fs.Position();
            var tag = _fs.ReadBytes(11);

            tagType = tag[0];
            dataSize = (uint)tag[1] << 16 | (uint)tag[2] << 8 | (uint)tag[3];
            timeStamp = (uint)tag[4] << 16 | (uint)tag[5] << 8 | (uint)tag[6];
            timeStamp |= (uint)tag[7] << 24;
            streamID = (uint)tag[8] << 16 | (uint)tag[9] << 8 | (uint)tag[10];

            // Read tag data
            if (dataSize == 0)
            {
                return true;
            }
            if (_fs.RequestLength(dataSize))
            {
                return false;
            }

            byte[] data = new byte[tag.Length + dataSize];
            var read_len = _fs.ReadBytes(data, tag.Length, (int)dataSize);
            Array.Copy(tag, data, tag.Length);

            if (tagType == 9)
            {
                Span<byte> videotag = new(data, tag.Length, 5);
                uint mediaInfo, avcPacketType;
                mediaInfo = videotag[0];
                uint composition = (uint)videotag[1] << 24 | (uint)videotag[2] << 16 | (uint)videotag[3] << 8 | (uint)videotag[4];
                avcPacketType = (composition >> 24) & 0xff;
                uint tempv = composition & 0x00ffffff;
                int compositionTime = (Int32)((tempv & 0x00800000) << 8 | (tempv & 0x007fffff));

                uint frametype = (mediaInfo >> 4) & 0x0f;
                uint codecID = mediaInfo & 0x0f;

                detail.v.frametype = frametype;
                detail.v.codecID = codecID;
                detail.v.avcPacketType = avcPacketType;
                detail.v.compositionTime = compositionTime;

                if (_last_video_dts == long.MaxValue)
                {
                    _last_video_dts = timeStamp;
                }
                if (_last_video_pts == long.MaxValue)
                {
                    _last_video_pts = timeStamp + compositionTime;
                }

                if (codecID == 7)
                {
                    parserH264VideoPacket(ref data, ref detail);
                }
                else
                {
                    parserHEVCVideoPacket(ref data, ref detail);
                }
            }
            else if (tagType == 8)
            {
                uint mediaInfo, format, rate, size, type, pkttype = uint.MaxValue;
                Span<byte> audiotag = new(data, tag.Length, 2);
                mediaInfo = audiotag[0];

                format = (mediaInfo >> 4) & 0x0f;
                rate = (mediaInfo >> 2) & 0x03;
                size = (mediaInfo >> 1) & 0x01;
                type = mediaInfo & 0x01;
                if(format == 10)
                {
                    pkttype = audiotag[1];
                }

                detail.a.soundFormat = format;
                detail.a.soundRate = rate;
                detail.a.soundSize = size;
                detail.a.soundType = type;
                detail.a.aacPacketType = pkttype;

                /*
                detail.a.soundFormat = strSoundFormat(format) + "[" + format + "]";
                detail.a.soundRate = strSoundSampleRate(rate) + "[" + rate + "]";
                detail.a.soundSize = size == 0? "8bits " : "16bits " + "[" + size + "]";
                detail.a.soundType = type == 0 ? "Mono " : "Stereo " + "[" + type + "]";
                detail.a.aacPacketType = pkttype == 0 ? "aac sequence header " : pkttype == 1 ? "aac raw " : " ";
                detail.a.aacPacketType += "[" + pkttype + "]";
                */

                if (_last_audio_pts == long.MaxValue)
                {
                    _last_audio_pts = timeStamp;
                }
                parserAudioPacket(ref data, ref detail);
            }

            uint previousTagSize = _fs.ReadUInt32();
            detail.tagType = tagType;
            detail.dataSize = dataSize;
            detail.timestamp = timeStamp;
            detail.streamID = streamID;
            detail.data = data;
            detail.previousTagSize = previousTagSize;

            return true;
        }

        private bool parserAudioPacket(ref byte[] data, ref FlvTag detail)
        {
            return false;
        }
        private bool parserH264VideoPacket(ref byte[] data, ref FlvTag detail)
        {
            if (data == null)
                return false;

            int nalus = 0;
            int dataOffset = 16; // 11 bytes tag size + 5 bytes video tag 

            if (data[dataOffset - 4] == 0)
            { // Headers AVCDecoderConfigurationRecord
                if (data.Length < 10 + dataOffset) return false;

                int offset, spsCount, ppsCount;

                offset = dataOffset + (int)AVCPayloadOffset.lengthSizeMinusOne;
                _nalLengthSize = (data[offset++] & 0x03) + 1;
                spsCount = data[offset++] & 0x1F;
                detail.v.NaluDetails = new NaluDetail[256];

                ppsCount = -1;
                while (offset <= data.Length - 2)
                {
                    if ((spsCount == 0) && (ppsCount == -1))
                    {
                        ppsCount = data[offset++];
                        continue;
                    }

                    if (spsCount > 0) spsCount--;
                    else if (ppsCount > 0) ppsCount--;
                    else break;

                    int len = (int)BitConverterBE.ToUInt16(data, offset);
                    offset += 2;
                    if (offset + len > data.Length) break;
                    string naltype = naltype = ((H264NalType)(data[offset] & 0x1f)).ToString();
                    detail.v.NaluDetails[nalus++] = new NaluDetail() { type = naltype, offset = offset };
                    offset += len;
                }
            }
            else
            { // Video data
                int offset = dataOffset;

                detail.v.NaluDetails = new NaluDetail[256];
                while (offset <= data.Length - _nalLengthSize)
                {
                    int len = (_nalLengthSize == 2) ?
                        (int)BitConverterBE.ToUInt16(data, offset) :
                        (int)BitConverterBE.ToUInt32(data, offset);
                    offset += _nalLengthSize;

                    string naltype = ((H264NalType)(data[offset] & 0x1f)).ToString();

                    detail.v.NaluDetails[nalus++] = new NaluDetail() { type = naltype, offset = offset };

                    if (offset + len > data.Length) break;
                    offset += len;
                }
            }
            detail.v.NALUs = nalus;

            return true;
        }


        private bool parserHEVCVideoPacket(ref byte[] data, ref FlvTag detail)
        {
            if (data == null)
                return false;

            int nalus = 0;

            if (_hevc_in_annexb) // annexb format
            {
                Span<byte> v = new(data);
                List<int> indexs = [];
                for(var i = 2; i< v.Length; i++)
                {
                    if (v[i] == 1)
                    {
                        if ((v[i-1] == 0) && (v[i-2] == 0))
                        {
                            indexs.Add(i-2);
                            i+=2;
                            continue;
                        }
                    }
                }

                nalus = indexs.Count(); ;
                detail.v.NaluDetails = new NaluDetail[nalus];
                int j = 0;
                foreach (long i in indexs) 
                {
                    long offset = i + 3;
                    string naltype = ((HEVCNalType)((data[offset] >> 1) & 0x3f)).ToString();
                    detail.v.NaluDetails[j++] = new NaluDetail() { type = naltype, offset = offset }; 
                }
            }
            else
            {
                int dataOffset = 16; // 11 bytes tag size + 5 bytes video tag 
                var AVCPacketType = data[12]; // 11 bytes tag size + 1 offset

                if (AVCPacketType == 0)
                {
                    int offset, nalArrays;

                    offset = (int)HVCCPayloadOffset.lengthSizeMinusOne + dataOffset;
                    _nalLengthSize = (data[offset++] & 0x03) + 1;

                    offset = (int)HVCCPayloadOffset.numOfArrays + dataOffset;
                    nalArrays = data[offset++];

                    detail.v.NaluDetails = new NaluDetail[nalArrays];

                    for (int i = 0; i < nalArrays; i++)
                    {
                        int nalType = data[offset++] & 0x3f;
                        int numNalus = (int)BitConverterBE.ToUInt16(data, offset);
                        offset += 2;

                        for (int j = 0; j < numNalus; j++)
                        {
                            int len = (int)BitConverterBE.ToUInt16(data, offset);
                            offset += 2;

                            string naltype = ((HEVCNalType)((data[offset] >> 1) & 0x3f)).ToString();
                            detail.v.NaluDetails[i] = new NaluDetail() { type = naltype, offset = offset };

                            offset += len;
                            nalus++;

                            if (offset >= data.Length)
                                break;
                        }
                        if (offset >= data.Length)
                            break;
                    }
                }
                else
                { // Video data
                    int offset = dataOffset;

                    int i = 0;

                    detail.v.NaluDetails = new NaluDetail[256];
                    while (offset <= data.Length - _nalLengthSize)
                    {
                        int len = (_nalLengthSize == 2) ?
                            (int)BitConverterBE.ToUInt16(data, offset) :
                            (int)BitConverterBE.ToUInt32(data, offset);
                        offset += _nalLengthSize;

                        string naltype = ((HEVCNalType)((data[offset] >> 1) & 0x3f)).ToString();

                        detail.v.NaluDetails[i++] = new NaluDetail() { type = naltype, offset = offset };

                        if (offset + len > data.Length) break;
                        offset += len;
                        nalus++;
                    }
                }
            }

            detail.v.NALUs = nalus;

            return true;
        }
        struct FormatDesc
        {
            public int v;
            public string desc;
        };
        public static string strAudioTagFrameType(uint type, uint v)
        {
            string[] types =
            {
                "AAC sequence header ","AAC raw "
            };
            if (v < 2 && type == 10)
            {
                return types[v];
            }
            else
            {
                return "(unknown) ";
            }
        }
        public static string strSoundSampleRate(uint v)
        {
            FormatDesc[] Descs =
            {
                new() { v = 0, desc = "5.5 KHz " },
                new() { v = 1, desc = "11 KHz " },
                new() { v = 2, desc = "22 KHz " },
                new() { v = 3, desc = "44 KHz " }
            };

            foreach (FormatDesc desc in Descs)
            {
                if (v == desc.v)
                {
                    return desc.desc;
                }
            }

            return "";
        }

        public static string strSoundFormat(uint v)
        {
            FormatDesc[] descs =
            {
                new() { v = 0, desc = "Linear PCM, platform endian " },
                new() { v = 1, desc = "ADPCM " },
                new() { v = 2, desc = "MP3 " },
                new() { v = 3, desc = "Linear PCM, little endian " },
                new() { v = 4, desc = "Nellymoser 16 kHz mono " },
                new() { v = 5, desc = "Nellymoser 8 kHz mono " },
                new() { v = 6, desc = "Nellymoser " },
                new() { v = 7, desc = "G.711 A-law logarithmic PCM " },
                new() { v = 8, desc = "G.711 mu-law logarithmic PCM " },
                new() { v = 9, desc = "reserved " },
                new() { v = 10, desc = "aac " },
                new() { v = 11, desc = "Speex " },
                new() { v = 14, desc = "MP3 8 kHz " },
                new() { v = 15, desc = "Device-specific sound " }
            };

            foreach (FormatDesc desc in descs)
            {
                if (v == desc.v)
                {
                    return desc.desc;
                }
            }

            return "";
        }

        public static string strVideoTagFrameType(uint v)
        {
            string[] types = 
            {
                "key frame ",
                "inter frame ",
                "disposable inter frame (H.263 only) ",
                "generated key frame ",
                "video info/command frame "
            };
            if(v < 5 && v > 0)
            {
                return types[v-1];
            }
            else
            {
                return "(unknown) ";
            }
        }

        public static string strVideoCodecID(uint v)
        {
            string[] types =
            {
                "Sorenson H.263 ",
                "Screen video ",
                "On2 VP6 ",
                "On2 VP6 with alpha channel ",
                "Screen video version 2 ",
                "h264 ",
                "h265 "
            };
            if (v < 9 && v > 1)
            {
                return types[v-2];
            }
            else if(v == 12)
            {
                return "h265 ";
            }
            else
            {
                return "(unknown) ";
            }
        }

        public static string strVideoAVCPacketType(uint v)
        {
            string[] types =
            {
                "sequence header ",
                "NALU ",
                "end of sequence "
            };
            if (v < 2)
            {
                return types[v];
            }
            else
            {
                return "(unknown) ";
            }
        }
        /*
        private void Seek(long offset)
        {
            _fs.Seek(offset, SeekOrigin.Begin);
            _fileOffset = offset;
        }

        private uint ReadUInt8()
        {
            _fileOffset += 1;
            return (uint)_fs.ReadByte();
        }

        private long CurReadPosition()
        {
            return _fs.Position;
        }

        private uint ReadUInt24()
        {
            byte[] x = new byte[4];
            _fs.Read(x, 1, 3);
            _fileOffset += 3;
            return BitConverterBE.ToUInt32(x, 0);
        }

        private uint ReadUInt32()
        {
            Span<byte> x = stackalloc byte[4];
            _fs.Read(x);
            _fileOffset += 4;
            return BitConverterBE.ToUInt32(x, 0);
        }

        private byte[] ReadBytes(int length)
        {
            byte[] buff = new byte[length];
            _fs.Read(buff, 0, length);
            _fileOffset += length;
            return buff;
        }

        private int ReadBytes(byte[] buff, int offset, int length)
        {
            var len = _fs.Read(buff, offset, length);
            _fileOffset += length;
            return len;
        }

        private UInt32 GetUInt32()
        {
            Span<byte> x = stackalloc byte[4];
            _fs.Read(x);
            _fs.Seek(-4, SeekOrigin.Current);
            return BitConverterBE.ToUInt32(x, 0);
        }
        */
    }
}
