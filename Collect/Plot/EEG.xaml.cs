using Accord.Audio.Filters;
using Accord.Math;
using Accord.Statistics.Kernels;
using Collect.EEG;
using Collect.Power;
using Collect.tool;
using GalaSoft.MvvmLight.Threading;
using MathNet.Filtering;
using MathNet.Filtering.FIR;
using Microsoft.Win32;
using NWaves.Filters.BiQuad;
using NWaves.Utils;
using OfficeOpenXml;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;
using ScottPlot;
using ScottPlot.Colormaps;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Xceed.Wpf.Toolkit;
using static Collect.Power.PowerSpectralDensityAnalyzer;
//using Collect.FIR;

namespace Collect.Plot
{
  

    /// <summary>
    /// EEG.xaml 的交互逻辑
    /// </summary>
    public partial class EEG : UserControl
    {
        
        //调用外部dll
        [DllImport("SciChart.Show.dll")]
        public static extern int WriteEdf_File_multifile(int id, string filename, int number_of_signals, int number_of_each_data_record);
        [DllImport("SciChart.Show.dll")]
        public static extern unsafe int WriteEdf_WriteData_multifile(int id, double* data);
        [DllImport("SciChart.Show.dll")]
        public static extern int WriteEdf_Finish_multifile(int id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void DeleRecvFun(int count);

        [DllImport("DeviceSetting.dll")]
        private static extern int OpenDevice(DeleRecvFun fun, int value);
        [DllImport("DeviceSetting.dll")]
        private static extern int CloseDevice();
        [DllImport("DeviceSetting.dll")]
        private static extern int SetValue(int value);

        private static DeleRecvFun drf;


        public TCPClient client = new TCPClient();
        SerialControl ecg_control = new SerialControl();

        const int CN = 8;
        public int PGA = 1;
        public double LLMin = 0;
        public double LLMax = 1;
        public double RMSMin = 0;
        public double RMSMax = 10;
        public double Alpha= 0;
        const int NL = 5;
        double[] NUM = new double[] { 0.9480807851293,   -3.070235355059,    4.381800263529,   -3.070235355059,
     0.9480807851293 };
        const int DL = 5;
        double[] DEN = new double[] { 1,   -3.152118327708,    4.379102839633,    -2.98835238241,
     0.8988589941553 };

        //二维动态数组,5行的数组
        double[][] buffer_in = new double[CN][];
        double[][] buffer_out = new double[CN][];

       
        short g_ecg_data = 0;
        int g_old_ecg_time = 0;
        double g_scale = 1;
        string localIp = "127.0.0.1";
        int localPort = 45555;
        string remoteIp = "127.0.0.1";
        int remotePort = 45552;
        UdpClient udpsti = new UdpClient();
        //存储远程端点
        IPEndPoint remoteIpep;
        UdpClient udpdata = new UdpClient();
        public EEG()
        {
            //8行5列数组,CN=8,DN=5
            for (int i = 0; i < CN; i++)
            {
                buffer_in[i] = new double[NL];
                buffer_out[i] = new double[DL];
            }
            //buffer_in所有元素初始化为0
            for (int i = 0; i < CN; i++)
            {
                for (int j = 0; j < NL; j++)
                    buffer_in[i][j] = 0;
                for (int j = 0; j < DL; j++)
                    buffer_out[i][j] = 0;
            }

            //用于初始化与UI线程相关，用于管理UI线程的类
            DispatcherHelper.Initialize();
            InitializeComponent();
            CreateChart();

            // 初始化定时器
            InitializeTimer();

            channel_1.IsChecked = true;
            channel_2.IsChecked = true;
            channel_3.IsChecked = true;
            channel_4.IsChecked = true;
            channel_5.IsChecked = true;
            channel_6.IsChecked = true;
            channel_7.IsChecked = true;
            channel_8.IsChecked = true;
            try
            {
                //将udpdata绑定到本地IP地址127.0.0.1和端口localPort
                udpdata.Connect("127.0.0.1", localPort);
                //解析字符串"127.0.0.1"为IP地址
                IPAddress ip = IPAddress.Parse("127.0.0.1");
                //定义由IP地址和端口号组成的网络端点
                IPEndPoint remoteIpep = new IPEndPoint(ip, remotePort);
                udpsti = new UdpClient(remoteIpep);



            }
            catch (Exception)
            {
                //MessageBox.Show("错误", "请检查网络");
            }

            ResetFilterState(8);
        }
        XyDataSeries<double, double>[] lineData;
        private double g_index = 0;
        private int channel_num = 8;

        private void CreateChart()
        {
            var xAxis = new NumericAxis() { AxisTitle = "Time (second)", VisibleRange = new DoubleRange(0, 2000) };
            var yAxis = new NumericAxis() { AxisTitle = "Value", Visibility = Visibility.Visible, VisibleRange = new DoubleRange(-1, 1) };

            sciChartSurface.XAxis = xAxis;
            sciChartSurface.YAxis = yAxis;


            // 创建 XyDataSeries 来托管图表的数据
            lineData = new XyDataSeries<double, double>[8];

            for (int i = 0; i < 8; i++)
            {
                //据系列可以存储的最大数据点数为5000，先进先出（FIFO）
                lineData[i] = new XyDataSeries<double, double>() { FifoCapacity = 5000 };
                //lineData[i].AcceptsUnsortedData = true;
            }

            for (int i = 0; i < 8; i++)
            {
                //8行100列
                //eeg_data_buffer[i] = new short[BaoLength];
                eeg_data_buffer[i] = new double[BaoLength];
                eeg_data_buffer_baseline[i] = new short[BaoLength];
            }
            for (int i = 0; i < 500; i++)
            {
                //500行8列
                save_data_buffer[i] = new double[8];
                save_data_buffer_original[i] = new double[8];
            }

            // 初始化频谱分析器
            psdAnalyzer = new PowerSpectralDensityAnalyzer();

            // 初始化小波去噪缓冲区
            for (int i = 0; i < 8; i++)
            {
                eeg_data_buffer_WaveletDenoise[i] = new double[BaoLength];
            }

            var colors = new[]
            {
                System.Windows.Media.Colors.Red,
                System.Windows.Media.Colors.Orange,
                System.Windows.Media.Colors.Cyan,
                System.Windows.Media.Colors.Green,
                System.Windows.Media.Colors.Blue,
                System.Windows.Media.Colors.Orchid,
                System.Windows.Media.Colors.Purple,
                System.Windows.Media.Colors.Brown,


            };
            // 添加8条曲线
            for (int i = 0; i < channel_num; i++)
            {
                var lineSeries = new FastLineRenderableSeries()
                {
                    Stroke = colors[i],
                    StrokeThickness = 1,
                    AntiAliasing = true,
                };
                sciChartSurface.RenderableSeries.Add(lineSeries);

            }
            //XyScatterRenderableSeries ScatterSeries = new XyScatterRenderableSeries();
            // 将数据分配给8条曲线
            for (int i = 0; i < channel_num; i++)
            {
                sciChartSurface.RenderableSeries[i].DataSeries = lineData[i];
            }

            sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(0);

        }
        private const double WindowSize = 10;
        private static DoubleRange ComputeXAxisRange(double t)
        {
            if (t < WindowSize)
            {
                return new DoubleRange(0, WindowSize);
            }
            //t 值向上取整到最接近的整数
            return new DoubleRange(Math.Ceiling(t) - WindowSize + 5, Math.Ceiling(t) + 5);
        }

        //tcp开始停止按钮
        public bool TCP_Install_ecg(string btn_con, string ip, int port)
        {
            if (btn_con == "开始")
            {
                bool is_open = client.Start(ip, port);
                if (is_open == false)
                {
                    return false;
                }
                else
                {
                    StartTimer();
                    client.EcgEvent += new EcgTCPEventHandler(uav_control_CmdEvent);
                    return true;
                }

            }
            else
            {
                client.Stop();
                StopTimer();
                client.EcgEvent -= uav_control_CmdEvent;
                //WriteEdf_Finish_multifile(0);
                return false;
            }

        }
        public bool Serial_install_ecg(string btn_con, string com)
        {
            if (btn_con == "开始")
            {
                if (com != null)
                {
                    //comboBox_gyroscope1.SelectedItem.ToString()
                    bool is_open = ecg_control.Start(com);
                    if (is_open == false)
                    {
                        return false;
                    }

                    else
                    {
                        string filePath = "setting.ini";
                        try
                        {
                            using (StreamWriter writer = new StreamWriter(filePath))
                            {
                                writer.WriteLine(com);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteErrorLog(ex.Message);
                            NlogHelper.WriteErrorLog(ex.Message);
                        }
                       
                        return true;
                    }


                }
                else
                {
                    return false;
                }
            }
            else
            {
                ecg_control.Stop();
        

                WriteEdf_Finish_multifile(0);
                return false;
            }

        }

        int buffer_index = 0;
        double[][] save_data_buffer = new double[500][];
        double[][] save_data_buffer_original = new double[500][];
        List<double[][]> save_data_buffer_all = new List<double[][]>();
        List<double[][]> save_data_buffer_all_original = new List<double[][]>();
       
        float[] eeg_data_float = new float[8];
        uint[] eeg_data_uint = new uint[8];
        byte[] eeg_data_byte = new byte[24];

        int BaoLength = 50;
       
        void process_eegdata(byte[] eeg_data_byte)
        {
            for (int i = 0; i < 8; i++)
            {
                eeg_data_uint[i] = Convert.ToUInt32((eeg_data_byte[0 + 3 * i] << 16) | (eeg_data_byte[1 + 3 * i] << 8) | eeg_data_byte[2 + 3 * i]);
            }
            for (int i = 0; i < 8; i++)
            {                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         
                float datai = 0;
                if ((eeg_data_uint[i] & 0x800000) != 0) 
                {
                    datai = Convert.ToSingle((16777216 - eeg_data_uint[i]) * (-4500000.0) / (8388607 * PGA));
                    eeg_data_float[i] = datai;
                }
                else
                {
                    datai = Convert.ToSingle((eeg_data_uint[i] * 4500000.0) / (8388607 * PGA));
                    eeg_data_float[i] = datai;
                }
            }
           
        }
        public double fs; // 采样率Hz，根据实际情况修改

        int buffer_save_index = 0;
        double[][] eeg_data_buffer = new double[8][];
        short[][] eeg_data_buffer_baseline = new short[8][];
        double[][] eeg_data_buffer_WaveletDenoise = new double[8][];

        // 频谱分析相关
        private PowerSpectralDensityAnalyzer psdAnalyzer;
        
        //工频干扰
        //static double sampleRate = 500;
        static double notchFreg = 50;
        static double notchBandwidth = 2;

        //1 计算带照上下腿
        static double fLow = notchFreg - notchBandwidth;
        static double fHigh = notchFreg + notchBandwidth;

       
        static string filename1 = Directory.GetCurrentDirectory();
        static double hpCut_save = 0.016;
        static double hpCut_show = 0.5;
        
        // 每个通道独立的一对notch（你已有）
        static double f0 = 50.0;     // 60Hz 电网改成 60
        static double Q = 35.0;     // 30~40 之间调，Q 越大陷波越窄
        

       
        int index= 0;




        /// <summary>
        /// 修改滤波顺序
        /// </summary>
        /// <param name="ch"></param>
        /// <param name="x"></param>
        /// <returns></returns>
        public  double sampleRate = 1000;

        public double hpCut = 0.3;   // 高通截止 0.5 Hz
        public  double hpA;

        public  double lpCut = 40.0;  // 低通截止 50 Hz
        // 4阶 Butterworth 两段二阶的 Q（固定值）
        public  double lpQ1 = 0.5411961;
        public   double lpQ2 = 1.3065630;
       
        public double notchF0 = 50.0;
        public double notchQ = 20.0;  // 约等于 BW=2Hz 的量级（可按需要微调）

        // ===== 状态（8通道）=====
        double[] hp1_prevX = new double[8];
        double[] hp1_prevY = new double[8];
        double[] hp2_prevX = new double[8];
        double[] hp2_prevY = new double[8];

        double[][] medBuf = Enumerable.Range(0, 8).Select(_ => new double[5]).ToArray();
        int[] medCount = new int[8];
        int[] medIdx = new int[8];

        BiquadLPF[] lpf1 ;
        BiquadLPF[] lpf2 ;
        BiquadNotch[] notch1;
        BiquadNotch[] notch2;
        public void set_filter_params(double fs)
        {
            sampleRate = fs;
            hpA = Math.Exp(-2.0 * Math.PI * hpCut / sampleRate);
            lpf1 = Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ1)).ToArray();
            lpf2 = Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ2)).ToArray();
        }

        void ResetFilterState(int chCount)
        {
            // 1) 高通状态清零
            Array.Clear(hp1_prevX, 0, hp1_prevX.Length);
            Array.Clear(hp1_prevY, 0, hp1_prevY.Length);
            Array.Clear(hp2_prevX, 0, hp2_prevX.Length);
            Array.Clear(hp2_prevY, 0, hp2_prevY.Length);

            // 2) 中值滤波状态清零
            for (int ch = 0; ch < chCount; ch++)
            {
                Array.Clear(medBuf[ch], 0, medBuf[ch].Length);
                medCount[ch] = 0;
                medIdx[ch] = 0;
            }

            notch1 = Enumerable.Range(0, 8).Select(_ => new BiquadNotch(sampleRate, notchF0, notchQ)).ToArray();
            notch2 = Enumerable.Range(0, 8).Select(_ => new BiquadNotch(sampleRate, notchF0, notchQ)).ToArray();
        }
        double Median5_Update(int ch, double x)
        {
            var buf = medBuf[ch];
            buf[medIdx[ch]] = x;
            medIdx[ch] = (medIdx[ch] + 1) % 5;
            if (medCount[ch] < 5) medCount[ch]++;

            int n = medCount[ch];
            if (n <= 1) return x;

            // 注意：环形缓冲直接Copy会乱序，但中值不依赖顺序，只依赖集合，因此可直接复制前n个“已写入的元素”
            // 为了更严谨：复制全5个再取前n个非0也行；这里用简单实现
            double[] w = new double[n];
            Array.Copy(buf, w, n);
            Array.Sort(w);
            return w[n / 2];
        }
        /// <summary>
        /// 癫痫检测
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private SeizureDetector _detector;
        private BandPowerRatioDetector _stage2;
        public void InitAfterFsKnown(double fs)
        {
            // Stage1
            _detector = new SeizureDetector(new SeizureDetector.Config
            {
                Fs = sampleRate,
                ChannelCount = 8, //通道数
                WindowMs = 200, //第一检测区窗口大小
                StepMs = 50,//第一检测区步数
                WarmupMs = 1000,//前1s热身
                LlMin = LLMin,//LL最小值
                LlMax = LLMax,//LL最大值
                RmsMin = RMSMin,//RMS最小值
                RmsMax = RMSMax,//RMS最大值
                //RmsThreshold = 80,//RMS阈值
                //LlThreshold = 2000,//LL阈值
                MinChannelsToTrigger = 1,//最小触发通道数
                StopAfterTrigger = false,//触发后是否停止检测

                Stage2LookbackMs = 400, // 向前 400ms
                Stage2WindowMs = 600,   // 总窗口 600ms
                HistoryMs = 2000,       // 历史缓冲 2s
                Stage2EmitMinIntervalMs = 100// 最小间隔 100ms
            });
            _detector.Start();

            // Stage2
            _stage2 = new BandPowerRatioDetector(new BandPowerRatioDetector.Config
            {
                Fs = sampleRate,
                ChannelCount = 8,

                AlphaRelativeThreshold = Alpha,//带功率比阈值
                MinChannelsToTrigger = 2
            });
            _stage2.Start();

            // 关键：Stage1 命中后，把 600ms 数据块送入 Stage2 队列（Stage2 自己线程算）
            _detector.OnStage2WindowReady += (s, e) =>
            {
                _stage2.PushWindow(e.Window,e.ChannelIndices, e.WindowStartSample, e.WindowEndSample);
            };

            // Stage2 触发事件（注意：这是在 Stage2 线程里触发，UI要Dispatcher）
            _stage2.OnStage2Triggered += (s, e) =>
            {
                if (e.TriggerMode == 1)
                {
                    client.EnqueueMode1Cmd();
                    NlogHelper.WriteInfoLog("触发模式1");
                    // 模式1：本次超阈值通道集合
                    // e.TriggeredOriginalChannels 里就是原始通道号(0..7)
                }
                else if (e.TriggerMode == 2)
                {
                    client.EnqueueMode2Cmd();
                    NlogHelper.WriteInfoLog("触发模式2");
                    // 模式2：与上次模式1那组通道一一对应
                    // e.TriggeredOriginalChannels[i] 对应：
                    //   e.PreviousAlphaRelative[i] -> 上次(模式1基准)
                    //   e.TriggeredAlphaRelative[i] -> 本次
                }
            };

        }
        private bool _detectorInited = false;
        void uav_control_CmdEvent(object sender, EcgTCPEventArgs e)
        {
           
            Numeeg += 33;
            //33数据包版本
            Buffer.BlockCopy(e.value, 2, eeg_data_byte, 0, 24);

            process_eegdata(eeg_data_byte);
            if (!_detectorInited)
            {
                double fs = sampleRate; // 或从数据包/设备配置解析
                InitAfterFsKnown(fs);
                _detectorInited = true;
            }
            // 推给检测线程：原始 8通道（μV）
            if (_detector != null)
            {
                double[] frame = new double[8];
                for (int ch = 0; ch < 8; ch++)
                    frame[ch] = eeg_data_float[ch];   // 或者用你滤波后的 filterdata（更稳）
                _detector.PushFrame(frame);
            }
            index++;
            for (int i = 0; i < 8; i++)
            {
                double temp = (Convert.ToDouble(eeg_data_float[i]));

              
                // --- 第1级 一阶高通：去基线漂移 ---
                double yhp1 = hpA * (hp1_prevY[i] + temp - hp1_prevX[i]);
                hp1_prevX[i] = temp;
                hp1_prevY[i] = yhp1;

                // --- 第2级 一阶高通：进一步增强滚降 ---
                double yhp2 = hpA * (hp2_prevY[i] + yhp1 - hp2_prevX[i]);
                hp2_prevX[i] = yhp1;
                hp2_prevY[i] = yhp2;



                // --- 50 Hz 双级陷波1级 ---
                double y1 = notch1[i].Process(yhp2);
                double y2 = notch2[i].Process(y1);

                //带通
                double ylp1 = lpf1[i].Process(y2);
                double ylp2 = lpf2[i].Process(ylp1);

                double filterdata = Median5_Update(i, ylp2);
                
                
                eeg_data_buffer[i][buffer_index] = filterdata;
                

                if (index >= 1000)
                {
                    index = 1002;
                    save_data_buffer[buffer_save_index][i] = filterdata;
                    save_data_buffer_original[buffer_save_index][i] = yhp1;
                }
            }

            buffer_index++;
            buffer_save_index++;
            buffer_index %= BaoLength;

            for (int i = 0; i < 8; i++)
            {
                lineData[i].Append(g_index, eeg_data_buffer[i][buffer_index] - i * 10000);
            }
            g_index += 0.002;

            buffer_save_index %= 500;
            if (index >= 1000)
            {
                if (buffer_save_index == 499)
                {
                    // 创建一个新的数据快照（值拷贝）
                    var copiedBuffer = new double[500][];
                    var copiedBuffer_original = new double[500][];
                    for (int i = 0; i < 500; i++)
                    {
                        copiedBuffer[i] = new double[8];
                        copiedBuffer_original[i] = new double[8];
                        Array.Copy(save_data_buffer[i], copiedBuffer[i], 8);
                        Array.Copy(save_data_buffer_original[i], copiedBuffer_original[i], 8);
                    }

                    save_data_buffer_all.Add(copiedBuffer);
                    save_data_buffer_all_original.Add(copiedBuffer_original);
                    // 原始 buffer 重置
                    save_data_buffer = new double[500][];
                    save_data_buffer_original = new double[500][];
                    for (int i = 0; i < 500; i++)
                    {
                        save_data_buffer[i] = new double[8];
                        save_data_buffer_original[i] = new double[8];
                    }
                    buffer_save_index = 0;
                }
            }
            //当前数据点与上次更新的数据差大于4x500个数据点或者当前数据少于WindowSizex500
            if (Convert.ToInt32(g_index) - g_old_ecg_time > 4 || Convert.ToInt32(g_index) < WindowSize)
            {
                g_old_ecg_time = Convert.ToInt32(g_index);

                if (Convert.ToInt32(g_index) < WindowSize)
                {
                    g_old_ecg_time = Convert.ToInt32(WindowSize - 5);
                }
                this.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
                });
            }
            this.Dispatcher.BeginInvoke((Action)delegate ()
            {
                NumEEG.Text = Numeeg.ToString();
            });
        }
       
        int Numeeg = 0;
  

        private void channel_1_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[0].IsVisible = true;
        }

        private void channel_2_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[1].IsVisible = true;
        }

        private void channel_3_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[2].IsVisible = true;
        }

        private void channel_4_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[3].IsVisible = true;
        }

        private void channel_5_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[4].IsVisible = true;
        }

        private void channel_6_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[5].IsVisible = true;
        }

        private void channel_7_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[6].IsVisible = true;
        }

        private void channel_8_Checked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[7].IsVisible = true;
        }
        private void channel_1_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[0].IsVisible = false;
        }

        private void channel_2_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[1].IsVisible = false;
        }

        private void channel_3_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[2].IsVisible = false;
        }

        private void channel_4_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[3].IsVisible = false;
        }

        private void channel_5_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[4].IsVisible = false;
        }

        private void channel_6_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[5].IsVisible = false;
        }

        private void channel_7_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[6].IsVisible = false;
        }

        private void channel_8_Unchecked(object sender, RoutedEventArgs e)
        {
            sciChartSurface.RenderableSeries[7].IsVisible = false;
        }
        public void button_save_ecg_filter_ns2()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒");
            saveFileDialog.FileName = date + ".ns2";
            saveFileDialog.DefaultExt = "ns2";
            saveFileDialog.Filter = "NS2 文件 (*.ns2)|*.ns2|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;

                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    using (var writer = new BinaryWriter(fs))
                    {
                        const int channelCount = 8;
                        const int samplingRate = 1000;
                        const int timeResolution = 30000;
                        const double quantization = 22.35e-9; // 量化步长 nV/bit
                        const short minAnalog = -1000;
                        const short maxAnalog = 1000;

                        // --- 1. 写入 FileTypeID ---
                        writer.Write(Encoding.ASCII.GetBytes("NEURALCD")); // 8 bytes

                        // --- 2. FileSpec ---
                        writer.Write((byte)2); // major
                        writer.Write((byte)3); // minor

                        // --- 3. HeaderBytes (placeholder, update later) ---
                        long headerPos = writer.BaseStream.Position;
                        writer.Write((uint)0); // placeholder

                        // --- 4. Sampling label (16 bytes) ---
                        writer.Write(Encoding.ASCII.GetBytes("EEG DATA".PadRight(16, '\0')));

                        // --- 5. Comment (256 bytes) ---
                        writer.Write(Encoding.ASCII.GetBytes("Created by EEG acquisition system".PadRight(256, '\0')));

                        // --- 6. Period & TimeRes ---
                        writer.Write((uint)(timeResolution / samplingRate)); // Period
                        writer.Write((uint)timeResolution); // TimeRes

                        // --- 7. DateTime (8×uint16 = 16 bytes) ---
                        DateTime now = DateTime.Now;
                        writer.Write((ushort)now.Year);
                        writer.Write((ushort)now.Month);
                        writer.Write((ushort)now.Day);
                        writer.Write((ushort)now.Hour);
                        writer.Write((ushort)now.Minute);
                        writer.Write((ushort)now.Second);
                        writer.Write((ushort)0);
                        writer.Write((ushort)0);

                        // --- 8. Channel count ---
                        writer.Write((uint)channelCount);

                        // --- 9. 扩展头部 (每个通道66字节) ---
                        for (int ch = 0; ch < channelCount; ch++)
                        {
                            writer.Write(Encoding.ASCII.GetBytes("CC"));        // 2 bytes
                            writer.Write((ushort)(ch + 1));                     // Electrode ID
                            writer.Write(Encoding.ASCII.GetBytes($"CH{ch + 1}".PadRight(16, '\0'))); // Label
                            writer.Write((byte)('A' + ch / 32));                // ConnectorBank
                            writer.Write((byte)(ch % 32));                      // ConnectorPin
                            writer.Write((short)-32768);                        // MinDigiValue
                            writer.Write((short)32767);                         // MaxDigiValue
                            writer.Write((short)minAnalog);                     // MinAnalogValue
                            writer.Write((short)maxAnalog);                     // MaxAnalogValue
                            writer.Write(Encoding.ASCII.GetBytes("uV".PadRight(16, '\0'))); // AnalogUnits
                            writer.Write((uint)0);                              // HighFreqCorner
                            writer.Write((uint)0);                              // HighFreqOrder
                            writer.Write((ushort)0);                            // HighFilterType
                            writer.Write((uint)0);                              // LowFreqCorner
                            writer.Write((uint)0);                              // LowFreqOrder
                            writer.Write((ushort)0);                            // LowFilterType
                        }

                        // --- 10. 更新 HeaderBytes ---
                        long headerEnd = writer.BaseStream.Position;
                        writer.Seek((int)headerPos, SeekOrigin.Begin);
                        writer.Write((uint)headerEnd);
                        writer.Seek((int)headerEnd, SeekOrigin.Begin);

                        // --- 11. 数据包头 (标记 + 时间戳 + 数据点数) ---
                        writer.Write((byte)1);       // 标记
                        writer.Write((uint)0);       // 时间戳
                        uint totalSamples = 0;
                        foreach (var buf in save_data_buffer_all)
                            totalSamples += (uint)buf.Length;
                        writer.Write(totalSamples);  // DataPoints

                        // --- 12. 写入数据 (int16 格式) ---
                        foreach (var buf in save_data_buffer_all)
                        {
                            foreach (var row in buf)
                            {
                                for (int ch = 0; ch < channelCount; ch++)
                                {
                                    // 假设输入为 μV
                                    int val = (int)row[ch];
                                    if (val > short.MaxValue) val = short.MaxValue;
                                    else if (val < short.MinValue) val = short.MinValue;
                                    writer.Write((short)val);
                                }
                            }
                        }
                    }

                 
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"保存 NS2 文件时出错: {ex.Message}");
                }
            }
        }

        //public void button_save_ecg_filter_excel()
        //{
        //    SaveFileDialog saveFileDialog = new SaveFileDialog();
        //    saveFileDialog.Title = "保存文件";
        //    string date = "Record-" + DateTime.Now.Year.ToString("0000") + DateTime.Now.Month.ToString("00") + DateTime.Now.Day.ToString("00") + "-" + DateTime.Now.Hour.ToString("00") + "时" + DateTime.Now.Minute.ToString("00") + "分" + DateTime.Now.Second.ToString("00") + "秒";
        //    saveFileDialog.FileName = date + ".xlsx";
        //    saveFileDialog.DefaultExt = "xlsx";
        //    saveFileDialog.Filter = "Excel 文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*";

        //    if (saveFileDialog.ShowDialog() == true)
        //    {
        //        string filePath = saveFileDialog.FileName;
        //        ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

        //        using (var package = new ExcelPackage(filePath))
        //        {
        //            var worksheet = package.Workbook.Worksheets.Add("EEG Data");


        //            // 计算总数据行数
        //            int totalDataPoints = save_data_buffer_all.Sum(buffer => buffer.Length);
        //            var allData = new object[totalDataPoints + 1][]; // +1 用于标题行

        //            // 设置标题行
        //            allData[0] = new object[] { "Ch1", "Ch2", "Ch3", "Ch4", "Ch5", "Ch6", "Ch7", "Ch8" };

        //            int dataIndex = 1;

        //            // 合并所有缓冲区的数据
        //            for (int bufferIndex = 0; bufferIndex < save_data_buffer_all.Count; bufferIndex++)
        //            {
        //                var buffer = save_data_buffer_all[bufferIndex];
        //                int dataPointCount = buffer.Length;
        //                int channelCount = buffer[0].Length;

        //                for (int dataPoint = 0; dataPoint < dataPointCount; dataPoint++)
        //                {
        //                    allData[dataIndex] = new object[channelCount];
        //                    for (int channel = 0; channel < channelCount; channel++)
        //                    {
        //                        allData[dataIndex][channel] = buffer[dataPoint][channel];
        //                    }
        //                    dataIndex++;
        //                }
        //            }

        //            // 一次性写入所有数据
        //            worksheet.Cells[1, 1].LoadFromArrays(allData);
        //            package.Save();

        //        }
        //        LogHelper.WriteInfoLog("文本数据保存成功");
        //        NlogHelper.WriteInfoLog("文本数据保存成功");
        //    }
        //}
        private const int EXCEL_MAX_ROWS = 1000000; // Excel 单个 Sheet 最大行数
        private const int EXCEL_HEADER_ROWS = 1;   // 表头占 1 行
        public void button_save_ecg_filter_excel()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒");
            saveFileDialog.FileName = date + ".xlsx";
            saveFileDialog.DefaultExt = "xlsx";
            saveFileDialog.Filter = "Excel 文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() != true)
                return;

            string filePath = saveFileDialog.FileName;
            ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                int sheetIndex = 1;
                int currentRow = 1;

                // 创建第一个 Sheet
                ExcelWorksheet worksheet = CreateNewSheet(package, sheetIndex, ref currentRow);

                // 遍历所有 EEG 数据缓冲
                foreach (var buffer in save_data_buffer_all)
                {
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        // 如果当前 Sheet 行数即将超过上限，创建新 Sheet
                        if (currentRow > EXCEL_MAX_ROWS)
                        {
                            sheetIndex++;
                            worksheet = CreateNewSheet(package, sheetIndex, ref currentRow);
                        }
                        worksheet.Cells[currentRow, 1].LoadFromArrays(new[]
                        {
                            buffer[i].Cast<object>().ToArray()
                        });
                        //// 将 EEG 数据转换为整数（保留整数部分）
                        //var intData = buffer[i].Select(value => (int)Math.Floor(value)).ToArray();

                        //// 写一行整数类型的 EEG 数据（8 通道）
                        //worksheet.Cells[currentRow, 1].LoadFromArrays(new object[][] { intData.Cast<object>().ToArray() });

                        currentRow++;
                    }
                }

                package.Save();
            }

            LogHelper.WriteInfoLog("Excel 数据保存成功（自动拆分 Sheet）");
            NlogHelper.WriteInfoLog("Excel 数据保存成功（自动拆分 Sheet）");
        }
        private ExcelWorksheet CreateNewSheet(ExcelPackage package, int sheetIndex, ref int currentRow)
        {
            string sheetName = $"EEG_Data_{sheetIndex}";
            var worksheet = package.Workbook.Worksheets.Add(sheetName);

            // 写表头
            worksheet.Cells[1, 1].LoadFromArrays(new[]
            {
                new object[] { "Ch1", "Ch2", "Ch3", "Ch4", "Ch5", "Ch6", "Ch7", "Ch8" }
            });

            currentRow = EXCEL_HEADER_ROWS + 1; // 数据从第 2 行开始
            return worksheet;
        }
        public void button_save_ecg_original_ns2()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-original" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒");
            saveFileDialog.FileName = date + ".ns2";
            saveFileDialog.DefaultExt = "ns2";
            saveFileDialog.Filter = "NS2 文件 (*.ns2)|*.ns2|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;

                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                    using (var writer = new BinaryWriter(fs))
                    {
                        const int channelCount = 8;
                        const int samplingRate = 1000;
                        const int timeResolution = 30000;
                        const double quantization = 22.35e-9; // 量化步长 nV/bit
                        const short minAnalog = -1000;
                        const short maxAnalog = 1000;

                        // --- 1. 写入 FileTypeID ---
                        writer.Write(Encoding.ASCII.GetBytes("NEURALCD")); // 8 bytes

                        // --- 2. FileSpec ---
                        writer.Write((byte)2); // major
                        writer.Write((byte)3); // minor

                        // --- 3. HeaderBytes (placeholder, update later) ---
                        long headerPos = writer.BaseStream.Position;
                        writer.Write((uint)0); // placeholder

                        // --- 4. Sampling label (16 bytes) ---
                        writer.Write(Encoding.ASCII.GetBytes("EEG DATA".PadRight(16, '\0')));

                        // --- 5. Comment (256 bytes) ---
                        writer.Write(Encoding.ASCII.GetBytes("Created by EEG acquisition system".PadRight(256, '\0')));

                        // --- 6. Period & TimeRes ---
                        writer.Write((uint)(timeResolution / samplingRate)); // Period
                        writer.Write((uint)timeResolution); // TimeRes

                        // --- 7. DateTime (8×uint16 = 16 bytes) ---
                        DateTime now = DateTime.Now;
                        writer.Write((ushort)now.Year);
                        writer.Write((ushort)now.Month);
                        writer.Write((ushort)now.Day);
                        writer.Write((ushort)now.Hour);
                        writer.Write((ushort)now.Minute);
                        writer.Write((ushort)now.Second);
                        writer.Write((ushort)0);
                        writer.Write((ushort)0);

                        // --- 8. Channel count ---
                        writer.Write((uint)channelCount);

                        // --- 9. 扩展头部 (每个通道66字节) ---
                        for (int ch = 0; ch < channelCount; ch++)
                        {
                            writer.Write(Encoding.ASCII.GetBytes("CC"));        // 2 bytes
                            writer.Write((ushort)(ch + 1));                     // Electrode ID
                            writer.Write(Encoding.ASCII.GetBytes($"CH{ch + 1}".PadRight(16, '\0'))); // Label
                            writer.Write((byte)('A' + ch / 32));                // ConnectorBank
                            writer.Write((byte)(ch % 32));                      // ConnectorPin
                            writer.Write((short)-32768);                        // MinDigiValue
                            writer.Write((short)32767);                         // MaxDigiValue
                            writer.Write((short)minAnalog);                     // MinAnalogValue
                            writer.Write((short)maxAnalog);                     // MaxAnalogValue
                            writer.Write(Encoding.ASCII.GetBytes("uV".PadRight(16, '\0'))); // AnalogUnits
                            writer.Write((uint)0);                              // HighFreqCorner
                            writer.Write((uint)0);                              // HighFreqOrder
                            writer.Write((ushort)0);                            // HighFilterType
                            writer.Write((uint)0);                              // LowFreqCorner
                            writer.Write((uint)0);                              // LowFreqOrder
                            writer.Write((ushort)0);                            // LowFilterType
                        }

                        // --- 10. 更新 HeaderBytes ---
                        long headerEnd = writer.BaseStream.Position;
                        writer.Seek((int)headerPos, SeekOrigin.Begin);
                        writer.Write((uint)headerEnd);
                        writer.Seek((int)headerEnd, SeekOrigin.Begin);

                        // --- 11. 数据包头 (标记 + 时间戳 + 数据点数) ---
                        writer.Write((byte)1);       // 标记
                        writer.Write((uint)0);       // 时间戳
                        uint totalSamples = 0;
                        foreach (var buf in save_data_buffer_all_original)
                            totalSamples += (uint)buf.Length;
                        writer.Write(totalSamples);  // DataPoints

                        // --- 12. 写入数据 (int16 格式) ---
                        foreach (var buf in save_data_buffer_all_original)
                        {
                            foreach (var row in buf)
                            {
                                for (int ch = 0; ch < channelCount; ch++)
                                {
                                    // 假设输入为 μV
                                    int val = (int)row[ch];
                                    if (val > short.MaxValue) val = short.MaxValue;
                                    else if (val < short.MinValue) val = short.MinValue;
                                    writer.Write((short)val);
                                }
                            }
                        }
                    }

                    System.Windows.MessageBox.Show("NS2 文件保存成功，可在 MATLAB 用 openNSx 打开。");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"保存 NS2 文件时出错: {ex.Message}");
                }
            }
        }

        //public void button_save_ecg_original_excel()
        //{
        //    SaveFileDialog saveFileDialog = new SaveFileDialog();
        //    saveFileDialog.Title = "保存文件";
        //    string date = "Record-original-" + DateTime.Now.Year.ToString("0000") + DateTime.Now.Month.ToString("00") + DateTime.Now.Day.ToString("00") + "-" + DateTime.Now.Hour.ToString("00") + "时" + DateTime.Now.Minute.ToString("00") + "分" + DateTime.Now.Second.ToString("00") + "秒";
        //    saveFileDialog.FileName = date + ".xlsx";
        //    saveFileDialog.DefaultExt = "xlsx";
        //    saveFileDialog.Filter = "Excel 文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*";

        //    if (saveFileDialog.ShowDialog() == true)
        //    {
        //        string filePath = saveFileDialog.FileName;
        //        ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

        //        using (var package = new ExcelPackage(filePath))
        //        {
        //            var worksheet = package.Workbook.Worksheets.Add("EEG Data");


        //            // 计算总数据行数
        //            int totalDataPoints = save_data_buffer_all_original.Sum(buffer => buffer.Length);
        //            var allData = new object[totalDataPoints + 1][]; // +1 用于标题行

        //            // 设置标题行
        //            allData[0] = new object[] { "Ch1", "Ch2", "Ch3", "Ch4", "Ch5", "Ch6", "Ch7", "Ch8" };

        //            int dataIndex = 1;

        //            // 合并所有缓冲区的数据
        //            for (int bufferIndex = 0; bufferIndex < save_data_buffer_all_original.Count; bufferIndex++)
        //            {
        //                var buffer = save_data_buffer_all_original[bufferIndex];
        //                int dataPointCount = buffer.Length;
        //                int channelCount = buffer[0].Length;

        //                for (int dataPoint = 0; dataPoint < dataPointCount; dataPoint++)
        //                {
        //                    allData[dataIndex] = new object[channelCount];
        //                    for (int channel = 0; channel < channelCount; channel++)
        //                    {
        //                        allData[dataIndex][channel] = buffer[dataPoint][channel];
        //                    }
        //                    dataIndex++;
        //                }
        //            }

        //            // 一次性写入所有数据
        //            worksheet.Cells[1, 1].LoadFromArrays(allData);
        //            package.Save();
        //        }

        //        LogHelper.WriteInfoLog("文本数据保存成功");
        //        NlogHelper.WriteInfoLog("文本数据保存成功");


        //    }

        //}
        public void button_save_ecg_original_excel()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-original-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒");
            saveFileDialog.FileName = date + ".xlsx";
            saveFileDialog.DefaultExt = "xlsx";
            saveFileDialog.Filter = "Excel 文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() != true)
                return;

            string filePath = saveFileDialog.FileName;
            ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                int sheetIndex = 1;
                int currentRow = 1;

                // 创建第一个 Sheet
                ExcelWorksheet worksheet = CreateNewOriginalSheet(package, sheetIndex, ref currentRow);

                // 遍历所有“原始数据”缓冲区
                foreach (var buffer in save_data_buffer_all_original)
                {
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        // 如果当前 Sheet 行数超过上限，自动新建 Sheet
                        if (currentRow > EXCEL_MAX_ROWS)
                        {
                            sheetIndex++;
                            worksheet = CreateNewOriginalSheet(package, sheetIndex, ref currentRow);
                        }

                        // 写一行 8 通道原始 EEG
                        worksheet.Cells[currentRow, 1].LoadFromArrays(new[]
                        {
                            buffer[i].Cast<object>().ToArray()
                        });
                        //// 将 EEG 数据转换为整数（保留整数部分）
                        //var intData = buffer[i].Select(value => (int)Math.Floor(value)).ToArray();

                        //// 写一行整数类型的 EEG 数据（8 通道）
                        //worksheet.Cells[currentRow, 1].LoadFromArrays(new object[][] { intData.Cast<object>().ToArray() });

                        currentRow++;
                    }
                }

                package.Save();
            }

            LogHelper.WriteInfoLog("原始 EEG Excel 保存成功（自动拆分 Sheet）");
            NlogHelper.WriteInfoLog("原始 EEG Excel 保存成功（自动拆分 Sheet）");
        }
        private ExcelWorksheet CreateNewOriginalSheet(ExcelPackage package,int sheetIndex,ref int currentRow)
        {
            string sheetName = $"EEG_Original_{sheetIndex}";
            var worksheet = package.Workbook.Worksheets.Add(sheetName);

            // 表头
            worksheet.Cells[1, 1].LoadFromArrays(new[]
            {
                new object[] { "Ch1", "Ch2", "Ch3", "Ch4", "Ch5", "Ch6", "Ch7", "Ch8" }
             });

            currentRow = EXCEL_HEADER_ROWS + 1; // 数据从第 2 行开始
            return worksheet;
        }
        public void ComboBox_amplitude(int scale)
        {
            if (scale == 0)
                g_scale = 0.1;
            if (scale == 1)
                g_scale = 0.5;
            if (scale == 2)
                g_scale = 1.0;
            if (scale == 3)
                g_scale = 5.0;
            if (scale == 4)
                g_scale = 10.0;
            if (scale == 5)
                g_scale = 100.0;

        }
        public bool Isclearplot_pro { get; set; }
        public void Clear_Plot()
        {
            Numeeg = 0;
            EEG_time.Text= "00:00:00";
            Isclearplot_pro = true;
            buffer_index = 0;
            buffer_save_index = 0;
            g_index = 0;
            g_old_ecg_time = 0;
            eeg_data_buffer.Clear();
            save_data_buffer.Clear();
            save_data_buffer_original.Clear();
            save_data_buffer_all.Clear();
            save_data_buffer_all_original.Clear();
            for (int i = 0; i < 8; i++)
            {
                lineData[i].Clear();
            }
            LogHelper.WriteInfoLog("数据清除成功");
            NlogHelper.WriteInfoLog("数据清除成功");
        }   
        // 添加定时器相关字段
        private System.Windows.Threading.DispatcherTimer timer;
        private bool isTimerRunning = false;
        // 初始化定时器
        private void InitializeTimer()
        {
            timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1); // 每5秒执行一次，可以根据需要调整
            timer.Tick += Timer_Tick;
        }
        private TimeSpan _elapsedTime = TimeSpan.Zero;

        // 定时器事件处理
        private void Timer_Tick(object sender, EventArgs e)
        {
            _elapsedTime = _elapsedTime.Add(TimeSpan.FromSeconds(1));

            // 格式化为 HH:mm:ss 显示
            EEG_time.Text = _elapsedTime.ToString(@"hh\:mm\:ss");

        }
      
     

        // 启动定时器
        public void StartTimer()
        {
            if (timer == null)
            {
                InitializeTimer();
            }

            if (!isTimerRunning)
            {
                timer.Start();
                isTimerRunning = true;
                LogHelper.WriteInfoLog("定时器已启动");
                NlogHelper.WriteInfoLog("定时器已启动");
            }
        }

        // 停止定时器
        public void StopTimer()
        {
            if (timer != null && isTimerRunning)
            {
                timer.Stop();
                isTimerRunning = false;
                LogHelper.WriteInfoLog("定时器已停止");
                NlogHelper.WriteInfoLog("定时器已停止");
            }
        }

        // 设置定时器间隔
        public void SetTimerInterval(TimeSpan interval)
        {
            if (timer != null)
            {
                timer.Interval = interval;
                LogHelper.WriteInfoLog($"定时器间隔已设置为: {interval.TotalSeconds}秒");
                NlogHelper.WriteInfoLog($"定时器间隔已设置为: {interval.TotalSeconds}秒");
            }
        }


        private void channel_1_8_Checked(object sender, RoutedEventArgs e)
        {
            channel_1.IsChecked = true;
            channel_2.IsChecked = true;
            channel_3.IsChecked = true;
            channel_4.IsChecked = true;
            channel_5.IsChecked = true;
            channel_6.IsChecked = true;
            channel_7.IsChecked = true;
            channel_8.IsChecked = true;
            sciChartSurface.RenderableSeries[0].IsVisible = true;
            sciChartSurface.RenderableSeries[1].IsVisible = true;
            sciChartSurface.RenderableSeries[2].IsVisible = true;
            sciChartSurface.RenderableSeries[3].IsVisible = true;
            sciChartSurface.RenderableSeries[4].IsVisible = true;
            sciChartSurface.RenderableSeries[5].IsVisible = true;
            sciChartSurface.RenderableSeries[6].IsVisible = true;
            sciChartSurface.RenderableSeries[7].IsVisible = true;
        }

        private void channel_1_8_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_1.IsChecked = false;
            channel_2.IsChecked = false;
            channel_3.IsChecked = false;
            channel_4.IsChecked = false;
            channel_5.IsChecked = false;
            channel_6.IsChecked = false;
            channel_7.IsChecked = false;
            channel_8.IsChecked = false;
            sciChartSurface.RenderableSeries[0].IsVisible = false;
            sciChartSurface.RenderableSeries[1].IsVisible = false;
            sciChartSurface.RenderableSeries[2].IsVisible = false;
            sciChartSurface.RenderableSeries[3].IsVisible = false;
            sciChartSurface.RenderableSeries[4].IsVisible = false;
            sciChartSurface.RenderableSeries[5].IsVisible = false;
            sciChartSurface.RenderableSeries[6].IsVisible = false;
            sciChartSurface.RenderableSeries[7].IsVisible = false;
        }
    }
    public sealed class BiquadNotch
    {
        private double b0, b1, b2, a1, a2;
        private double z1, z2; // Direct Form I (也可用Transposed)

        public BiquadNotch(double fs, double f0, double Q)
        {
            Update(fs, f0, Q);
        }

        public void Update(double fs, double f0, double Q)
        {
            double w0 = 2.0 * Math.PI * (f0 / fs); // 归一化角频率
            double cosw = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2.0 * Q);  // 带宽参数


            // 原始系数
            double B0 = 1.0;// 分子系数
            double B1 = -2.0 * cosw;
            double B2 = 1.0;
            double A0 = 1.0 + alpha;// 分母系数
            double A1 = -2.0 * cosw;
            double A2 = 1.0 - alpha;

            // 归一化
            b0 = B0 / A0; b1 = B1 / A0; b2 = B2 / A0;
            a1 = A1 / A0; a2 = A2 / A0;

            // 清状态（如需热切换时可保留）
            z1 = z2 = 0.0;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Process(double x)
        {
            // Direct Form I
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

    public sealed class BiquadLPF
    {
        private double b0, b1, b2, a1, a2;
        private double z1, z2; // Transposed Direct Form II

        public BiquadLPF(double fs, double fc, double Q) => Update(fs, fc, Q);

        public void Update(double fs, double fc, double Q)
        {
            double w0 = 2.0 * Math.PI * (fc / fs);
            double cosw = Math.Cos(w0);
            double sinw = Math.Sin(w0);
            double alpha = sinw / (2.0 * Q);

            double B0 = (1 - cosw) / 2.0;
            double B1 = 1 - cosw;
            double B2 = (1 - cosw) / 2.0;
            double A0 = 1 + alpha;
            double A1 = -2 * cosw;
            double A2 = 1 - alpha;

            b0 = B0 / A0; b1 = B1 / A0; b2 = B2 / A0;
            a1 = A1 / A0; a2 = A2 / A0;

            z1 = z2 = 0.0;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public double Process(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }
   
    
}
