using FlvMonitor.Library;
using System;
using System.Collections.Generic;

namespace FlvMonitor.Model
{
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

        public ParseListViewItem(FlvTag flv, int frameid, string image, long dtsd, long ptsd)
        {
            string format = @"hh\:mm\:ss\:fff";
            PTS = $"{TimeSpan.FromMilliseconds(flv.timestamp).ToString(format)} / {flv.timestamp}";
            Offset = flv.addr;
            FrameId = frameid;
            TagSize = flv.dataSize;
            if (flv.tagType == 8)
            {
                var soundFormat = FlvSpecs.strSoundFormat(flv.a.soundFormat) + "[" + flv.a.soundFormat + "]";
                //var soundRate = FlvSpecs.strSoundSampleRate(flv.a.soundRate) + "[" + flv.a.soundRate + "]";
                //var soundSize = flv.a.soundSize == 0 ? "8bits " : "16bits " + "[" + flv.a.soundSize + "]";
                //var soundType = flv.a.soundType == 0 ? "Mono " : "Stereo " + "[" + flv.a.soundType + "]";
                var aacPacketType = flv.a.aacPacketType == 0 ? "aac sequence header " : flv.a.aacPacketType == 1 ? "aac raw " : " ";
                aacPacketType += "[" + flv.a.aacPacketType + "]";

                CodecId = soundFormat;
                NalType = aacPacketType;
                TagType = $"🔈{flv.tagType}";
                AptsD = dtsd;
                Image = image;
            }
            else if (flv.tagType == 9)
            {
                CodecId = FlvSpecs.strVideoCodecID(flv.v.codecID) + "[" + flv.v.codecID + "]"; ;

                //detail.v.frametype = FlvSpecs.strVideoTagFrameType(frametype) + "[" + frametype + "]";
                //detail.v.codecID = FlvSpecs.strVideoCodecID(codecID) + "[" + codecID + "]";
                //detail.v.avcPacketType = FlvSpecs.strVideoAVCPacketType(avcPacketType) + "[" + avcPacketType + "]";

                List<string> types = [];
                foreach (var v in flv.v.NaluDetails)
                {
                    if (v != null)
                        types.Add(v.type);
                }
                NalType = string.Join(", ", types);
                TagType = $"🎥{flv.tagType}";

                long pts = flv.timestamp + flv.v.compositionTime;

                VdtsD = dtsd;
                VptsD = ptsd;

                //it.PTS = $"{TimeSpan.FromMilliseconds(flv.timestamp).ToString(format)} / {pts}";
                Image = image;
            }
            else
            {
                TagType = $"📄{flv.tagType}";
            }
        }
    }
}
