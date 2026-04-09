using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Collect.tool
{
    /// <summary>
    /// 读取“你当前这版程序生成的 NS2 文件”
    /// 还原为 double[channel][sample]
    /// </summary>
    public static class MyNs2Reader
    {
        public sealed class Ns2Result
        {
            public string FileTypeId { get; set; }              // 例如 "NEURALCD"
            public byte VersionMajor { get; set; }
            public byte VersionMinor { get; set; }

            public uint HeaderBytes { get; set; }
            public string SamplingLabel { get; set; }
            public string Comment { get; set; }

            public uint Period { get; set; }
            public uint TimeResolution { get; set; }
            public double SamplingRateHz { get; set; }

            public DateTime RecordTime { get; set; }

            public uint ChannelCount { get; set; }
            public string[] ChannelLabels { get; set; }

            public byte PacketMarker { get; set; }
            public uint Timestamp { get; set; }

            /// <summary>
            /// 每个通道的采样点数
            /// </summary>
            public uint SampleCountPerChannel { get; set; }

            /// <summary>
            /// Data[ch][sample]
            /// 例如：
            /// Data[0] -> 第1通道全部数据
            /// Data[0][99] -> 第1通道第100个采样点
            /// </summary>
            public double[][] Data { get; set; }
        }

        /// <summary>
        /// 读取 NS2 文件
        /// </summary>
        public static Ns2Result Read(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("filePath 不能为空");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("找不到文件", filePath);

            FileStream fs = null;
            BinaryReader br = null;

            try
            {
                fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                br = new BinaryReader(fs, Encoding.ASCII);

                Ns2Result result = new Ns2Result();

                // =========================
                // 1. 固定头
                // =========================
                result.FileTypeId = ReadFixedAscii(br, 8);   // "NEURALCD"
                result.VersionMajor = br.ReadByte();
                result.VersionMinor = br.ReadByte();

                result.HeaderBytes = br.ReadUInt32();
                result.SamplingLabel = ReadFixedAscii(br, 16);
                result.Comment = ReadFixedAscii(br, 256);

                result.Period = br.ReadUInt32();
                result.TimeResolution = br.ReadUInt32();

                ushort year = br.ReadUInt16();
                ushort month = br.ReadUInt16();
                ushort day = br.ReadUInt16();
                ushort hour = br.ReadUInt16();
                ushort minute = br.ReadUInt16();
                ushort second = br.ReadUInt16();
                ushort milli1 = br.ReadUInt16();   // 你写文件时一般是 0
                ushort milli2 = br.ReadUInt16();   // 你写文件时一般是 0

                try
                {
                    int y = year < 1 ? 1 : year;
                    int m = ClampInt(month, 1, 12);
                    int d = ClampInt(day, 1, 31);
                    int h = ClampInt(hour, 0, 23);
                    int min = ClampInt(minute, 0, 59);
                    int sec = ClampInt(second, 0, 59);

                    result.RecordTime = new DateTime(y, m, d, h, min, sec);
                }
                catch
                {
                    result.RecordTime = DateTime.MinValue;
                }

                result.ChannelCount = br.ReadUInt32();

                if (result.ChannelCount == 0)
                    throw new InvalidDataException("ChannelCount 读出来是 0，文件头可能不匹配");

                if (result.Period == 0)
                    throw new InvalidDataException("Period 为 0，无法计算采样率");

                result.SamplingRateHz = (double)result.TimeResolution / (double)result.Period;

                // =========================
                // 2. 扩展头（每通道 66 字节）
                // =========================
                string[] labels = new string[result.ChannelCount];

                int ch;
                for (ch = 0; ch < result.ChannelCount; ch++)
                {
                    string extTag = ReadFixedAscii(br, 2);   // "CC"
                    ushort electrodeId = br.ReadUInt16();
                    string label = ReadFixedAscii(br, 16);

                    byte connectorBank = br.ReadByte();
                    byte connectorPin = br.ReadByte();

                    short minDigi = br.ReadInt16();
                    short maxDigi = br.ReadInt16();
                    short minAnalog = br.ReadInt16();
                    short maxAnalog = br.ReadInt16();

                    string analogUnits = ReadFixedAscii(br, 16);

                    uint highFreqCorner = br.ReadUInt32();
                    uint highFreqOrder = br.ReadUInt32();
                    ushort highFilterType = br.ReadUInt16();

                    uint lowFreqCorner = br.ReadUInt32();
                    uint lowFreqOrder = br.ReadUInt32();
                    ushort lowFilterType = br.ReadUInt16();

                    if (string.IsNullOrEmpty(label))
                        labels[ch] = "CH" + (ch + 1).ToString();
                    else
                        labels[ch] = label;
                }

                result.ChannelLabels = labels;

                // =========================
                // 3. 跳到 HeaderBytes 指定位置
                // =========================
                fs.Seek(result.HeaderBytes, SeekOrigin.Begin);

                // =========================
                // 4. 数据包头
                // =========================
                result.PacketMarker = br.ReadByte();
                result.Timestamp = br.ReadUInt32();
                result.SampleCountPerChannel = br.ReadUInt32();

                uint sampleCount = result.SampleCountPerChannel;
                uint channelCount = result.ChannelCount;

                // =========================
                // 5. 分配 Data[channel][sample]
                // =========================
                double[][] data = new double[channelCount][];
                for (ch = 0; ch < channelCount; ch++)
                {
                    data[ch] = new double[sampleCount];
                }

                // =========================
                // 6. 读取数据区
                //    你的写法是交织存储：
                //    t1: ch1 ch2 ch3 ... chN
                //    t2: ch1 ch2 ch3 ... chN
                // =========================
                uint s;
                for (s = 0; s < sampleCount; s++)
                {
                    for (ch = 0; ch < channelCount; ch++)
                    {
                        if (fs.Position + 2 > fs.Length)
                        {
                            throw new EndOfStreamException(
                                "文件数据区提前结束，可能文件损坏或格式不匹配");
                        }

                        short raw = br.ReadInt16();

                        // 你保存时本来就是把数值直接转成 short 写入
                        // 所以这里直接转成 double 返回
                        data[ch][s] = (double)raw;
                    }
                }

                result.Data = data;
                return result;
            }
            finally
            {
                if (br != null) br.Close();
                if (fs != null) fs.Close();
            }
        }

        /// <summary>
        /// 读取固定长度 ASCII 字符串，并去掉尾部 \0 和空格
        /// </summary>
        private static string ReadFixedAscii(BinaryReader br, int length)
        {
            byte[] bytes = br.ReadBytes(length);
            string s = Encoding.ASCII.GetString(bytes);

            if (s == null) return string.Empty;

            return s.TrimEnd('\0', ' ');
        }

        /// <summary>
        /// 兼容旧版 .NET，没有 Math.Clamp 就自己写
        /// </summary>
        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
