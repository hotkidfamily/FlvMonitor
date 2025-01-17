using System;

namespace FlvMonitor.Library
{
    public unsafe class VoiceAlgo
    {
        public static short CalclSamplesMeanPower(short* addr, int length)
        {
            long sum = 0;
            for (var j = 0; j < length; j++)
            {
                sum += *(addr+j);
            }
            return (short)(sum/length);
        }

        public static short CalcSamplesExtremaPower(short* addr, int length)
        {
            int max = short.MinValue, min = short.MaxValue;
            for (var j = 0; j < length; j++)
            {
                addr += j;
                max = Math.Max(*addr, max);
                min = Math.Min(*addr, min);
            }
            return (short)(Math.Abs(max) > Math.Abs(min) ? max : min);
        }
    }
}
