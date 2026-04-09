using Accord.Math;
using Collect.EEG;
using Collect.tool;
using GalaSoft.MvvmLight.Threading;
using Microsoft.Win32;
using OfficeOpenXml;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
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


        const int CN = 8;
        public int PGA = 1;
        public double LLMin = 0;
        public double LLMax = 1;
        public double RMSMin = 0;
        public double RMSMax = 10;
        public double Delta = 0.2;
        public double Beta = 0.2;
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
        double g_old_ecg_time = 0;
        double g_scale = 1;
        string localIp = "127.0.0.1";
        int localPort = 45555;
        string remoteIp = "127.0.0.1";
        int remotePort = 45552;
        UdpClient udpsti = new UdpClient();
        //存储远程端点
        IPEndPoint remoteIpep;
        UdpClient udpdata = new UdpClient();
        private readonly object _saveBufferLock = new object();
        private bool _isSaving = false;

        // ===== 相对功率图数据 =====
        private XyDataSeries<double, double> deltaRelSeries;
        private XyDataSeries<double, double> thetaRelSeries;
        private XyDataSeries<double, double> alphaRelSeries;
        private XyDataSeries<double, double> betaRelSeries;

        // ===== 后台相对功率线程 =====
        private BlockingCollection<double> _powerQueue;
        private CancellationTokenSource _powerCts;
        private Task _powerTask;

        // ===== 参数 =====
        private const double RelPowerWindowSec = 4.0;   // 4秒窗
        private const double RelPowerStepSec = 0.5;     // 每0.5秒更新
        private const double RelPowerHistorySec = 12.0; // 显示最近12秒
        private const int PowerQueueCapacity = 20000;   // 防止队列无限长
        private double _powerTimeOffsetSec = 0.0;      // 历史时间偏移
        private double _lastPowerXSec = 0.0;           // 当前图上最后一个功率点时间
        private int _powerSessionId = 0;               // 防止旧线程残留UI更新

        private struct RelativePowers
        {
            public double DeltaPct;
            public double ThetaPct;
            public double AlphaPct;
            public double BetaPct;
        }
        private volatile int _powerChannelIndex = 0;   // 默认分析通道1
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
            for (int i = 0; i < 8; i++)
            {
                cmbPowerChannel.Items.Add(new ComboBoxItem
                {
                    Content = $"通道{i + 1}"
                });
            }
            cmbPowerChannel.SelectedIndex = 0;
            _powerChannelIndex = 0;
            CreateChart();
            InitRelativePowerChart();
            StartRelativePowerWorker();
            showdata.Items.Add(new ComboBoxItem
            {
                Content = "原始数据"
            });
            showdata.Items.Add(new ComboBoxItem
            {
                Content = "滤波数据"
            });
            runmode.Items.Add(new ComboBoxItem
            {
                Content = "开环"
            });
            runmode.Items.Add(new ComboBoxItem
            {
                Content = "闭环"
            });
            runmode.SelectedIndex = 0;
            showdata.SelectedIndex = 1;
            showdata.SelectionChanged += showdata_SelectionChanged;
            runmode.SelectionChanged += runmode_SelectionChanged;
            _showRawData = false;
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
        private void cmbPowerChannel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPowerChannel.SelectedIndex >= 0)
                _powerChannelIndex = cmbPowerChannel.SelectedIndex;
        }
        private void InitRelativePowerChart()
        {
            deltaRelSeries = new XyDataSeries<double, double>() { FifoCapacity = 400 };
            thetaRelSeries = new XyDataSeries<double, double>() { FifoCapacity = 400 };
            alphaRelSeries = new XyDataSeries<double, double>() { FifoCapacity = 400 };
            betaRelSeries = new XyDataSeries<double, double>() { FifoCapacity = 400 };

            powerSurface.RenderableSeries.Clear();

            powerSurface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                Stroke = System.Windows.Media.Colors.Brown,
                StrokeThickness = 2,
                DataSeries = deltaRelSeries
            });

            powerSurface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                Stroke = System.Windows.Media.Colors.Orange,
                StrokeThickness = 2,
                DataSeries = thetaRelSeries
            });

            powerSurface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                Stroke = System.Windows.Media.Colors.Green,
                StrokeThickness = 2,
                DataSeries = alphaRelSeries
            });

            powerSurface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                Stroke = System.Windows.Media.Colors.Blue,
                StrokeThickness = 2,
                DataSeries = betaRelSeries
            });

            powerSurface.XAxis.VisibleRange = new DoubleRange(0, RelPowerHistorySec);
            powerSurface.YAxis.VisibleRange = new DoubleRange(0, 100);
        }
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
                
                eeg_data_buffer[i] = new double[BaoLength];
                eeg_data_buffer_raw[i] = new double[BaoLength];

            }
            for (int i = 0; i < 500; i++)
            {
                //500行8列
                save_data_buffer[i] = new double[8];
                save_data_buffer_original[i] = new double[8];
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
        private const double WindowSize = 5;
        private void showdata_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem item = showdata.SelectedItem as ComboBoxItem;
            if (item == null || item.Content == null)
            {
                _showRawData = false;
                return;
            }

            _showRawData = (item.Content.ToString() == "原始数据");
        }
        private void runmode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem item = runmode.SelectedItem as ComboBoxItem;
            if (item == null || item.Content == null)
                return;

            string mode = item.Content.ToString();

            _isClosedLoopMode = (mode == "闭环");

            _madDetectorInited = false;
            _madDetector = null;

            LogHelper.WriteInfoLog($"当前运行模式：{mode}");
            NlogHelper.WriteInfoLog($"当前运行模式：{mode}");
        }
        private static DoubleRange ComputeXAxisRange(double t)
        {
            if (t < WindowSize)
            {
                return new DoubleRange(0, WindowSize);
            }
            //t 值向上取整到最接近的整数
            return new DoubleRange(Math.Ceiling(t) - WindowSize + 2.5, Math.Ceiling(t) + 2.5);
            //if (t < WindowSize)
            //    return new DoubleRange(0, WindowSize);

            //return new DoubleRange(t - WindowSize, t);
        }
       
        private double _lastPowerX = double.NegativeInfinity;

        private void StartRelativePowerWorker()
        {
            StopRelativePowerWorker();

            _powerSessionId++;

            // 关键：不要清空 deltaRelSeries/thetaRelSeries/...，这样才会接着画
            // 历史偏移 = 当前图最后一个X
            _powerTimeOffsetSec = _lastPowerXSec;

            _powerQueue = new BlockingCollection<double>(PowerQueueCapacity);
            _powerCts = new CancellationTokenSource();

            int sessionId = _powerSessionId;
            _powerTask = Task.Run(() => RelativePowerWorkerLoop(_powerCts.Token, sessionId));
        }
        private void StopRelativePowerWorker()
        {
            try
            {
                if (_powerCts != null)
                {
                    _powerCts.Cancel();
                    _powerQueue?.CompleteAdding();
                    _powerTask?.Wait(300);
                }
            }
            catch
            {
            }
            finally
            {
                _powerTask = null;
                _powerCts = null;
                _powerQueue = null;
            }
        }
        private void RelativePowerWorkerLoop(CancellationToken token, int sessionId)
        {
            int nWin = Math.Max(16, (int)Math.Round(sampleRate * RelPowerWindowSec));   // 4000
            int nStep = Math.Max(1, (int)Math.Round(sampleRate * RelPowerStepSec));     // 500

            double[] ring = new double[nWin];
            int ringPos = 0;
            int validCount = 0;
            int stepCounter = 0;
            long totalSamples = 0;

            while (!token.IsCancellationRequested)
            {
                double sample;
                try
                {
                    sample = _powerQueue.Take(token);
                }
                catch
                {
                    break;
                }

                ring[ringPos] = sample;
                ringPos = (ringPos + 1) % nWin;

                if (validCount < nWin) validCount++;
                totalSamples++;
                stepCounter++;

                if (validCount < nWin)
                    continue;

                if (stepCounter < nStep)
                    continue;

                stepCounter = 0;

                // 取最近4秒窗口
                double[] window = new double[nWin];
                int startIdx = ringPos; // ringPos 永远指向“最老位置”
                for (int i = 0; i < nWin; i++)
                {
                    int p = (startIdx + i) % nWin;
                    window[i] = ring[p];
                }

                RelativePowers rp = ComputeRelativePowers(window, sampleRate);
                double t = _powerTimeOffsetSec + totalSamples / sampleRate;

                Dispatcher.BeginInvoke((Action)(() =>
                {
                    // 旧会话残留UI消息，直接丢掉
                    if (sessionId != _powerSessionId) return;
                    if (deltaRelSeries == null) return;

                    // 只允许X递增，防止SciChart再报乱序
                    if (t <= _lastPowerXSec) return;

                    deltaRelSeries.Append(t, rp.DeltaPct);
                    thetaRelSeries.Append(t, rp.ThetaPct);
                    alphaRelSeries.Append(t, rp.AlphaPct);
                    betaRelSeries.Append(t, rp.BetaPct);

                    _lastPowerXSec = t;

                    powerSurface.RenderableSeries[0].IsVisible = band_delta.IsChecked == true;
                    powerSurface.RenderableSeries[1].IsVisible = band_theta.IsChecked == true;
                    powerSurface.RenderableSeries[2].IsVisible = band_alpha.IsChecked == true;
                    powerSurface.RenderableSeries[3].IsVisible = band_beta.IsChecked == true;

                    double left = Math.Max(0, t - RelPowerHistorySec);
                    double right = Math.Max(RelPowerHistorySec, t);
                    powerSurface.XAxis.VisibleRange = new DoubleRange(left, right);
                }));
            }
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
                    StartRelativePowerWorker();
                    InitSingleChannelMadDetector(sampleRate);
                    client.EcgEvent += new EcgTCPEventHandler(uav_control_CmdEvent);
                    return true;
                }

            }
            else
            {
                client.Stop();
                StopTimer();
                StopRelativePowerWorker();
                client.EcgEvent -= uav_control_CmdEvent;
                //WriteEdf_Finish_multifile(0);
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
        double[][] eeg_data_buffer_raw = new double[8][];      // 原始显示缓冲

        

        int index= 0;

        /// <summary>
        /// 修改滤波顺序
        /// </summary>
        /// <param name="ch"></param>
        /// <param name="x"></param>
        /// <returns></returns>
        public  double sampleRate = 1000;

        public double hpCut = 0.3;   // 高通截止 0.5 Hz
        //public  double hpA;

        public  double lpCut = 40.0;  // 低通截止 50 Hz
        // 4阶 Butterworth 两段二阶的 Q（固定值）
        public  double lpQ1 = 0.5411961;
        public   double lpQ2 = 1.3065630;
       
        public double notchF0 = 50.0;
        public double notchQ = 20.0;  // 约等于 BW=2Hz 的量级（可按需要微调）

        // ===== 状态（8通道）=====
        //double[] hp1_prevX = new double[8];
        //double[] hp1_prevY = new double[8];
        //double[] hp2_prevX = new double[8];
        //double[] hp2_prevY = new double[8];
        // ✅ 新增
        BiquadHPF[] hpf;

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
            //hpA = Math.Exp(-2.0 * Math.PI * hpCut / sampleRate);
            hpf = Enumerable.Range(0, 8)
              .Select(_ => new BiquadHPF(sampleRate, hpCut, 0.7071))
              .ToArray();
            lpf1 = Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ1)).ToArray();
            lpf2 = Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ2)).ToArray();
            notch1 = Enumerable.Range(0, 8).Select(_ => new BiquadNotch(sampleRate, notchF0, notchQ)).ToArray();
            notch2 = Enumerable.Range(0, 8).Select(_ => new BiquadNotch(sampleRate, notchF0, notchQ)).ToArray();
        }

        void ResetFilterState(int chCount)
        {
            // 1) 高通状态清零
            //Array.Clear(hp1_prevX, 0, hp1_prevX.Length);
            //Array.Clear(hp1_prevY, 0, hp1_prevY.Length);
            //Array.Clear(hp2_prevX, 0, hp2_prevX.Length);
            //Array.Clear(hp2_prevY, 0, hp2_prevY.Length);

            // 2) 中值滤波状态清零
            for (int ch = 0; ch < chCount; ch++)
            {
                Array.Clear(medBuf[ch], 0, medBuf[ch].Length);
                medCount[ch] = 0;
                medIdx[ch] = 0;
            }

            
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
   
        private SingleChannelMadSeizureDetector _madDetector;
        private bool _madDetectorInited = false;

        // 这两个阈值要换成你离线标定出来的真实值
        public double MadThreshold1 = 120.0;
        public double MadThreshold2 = 220.0;

        // 模式1 / 模式2 对应的刺激持续时间（单位 ms）
        public double MadShortStimMs = 200.0;
        public double MadLongStimMs = 500.0; 
        private void InitSingleChannelMadDetector(double fs)
        {
            _madDetector = new SingleChannelMadSeizureDetector(
                new SingleChannelMadSeizureDetector.Config
                {
                    Fs = fs,
                    WindowMs = 500,
                    StepMs = 250,
                    WarmupMs = 1000,         // 启动后前1秒不检测，可改成0
                    MadThreshold1 = MadThreshold1,
                    MadThreshold2 = MadThreshold2,
                    ShortStimMs = MadShortStimMs,
                    LongStimMs = MadLongStimMs
                });

            _madDetector.OnWindowEvaluated += (s, e) =>
            {
                // 这里只做日志，不做UI重操作
                // 你调试时打开，正式跑可以关掉
                // NlogHelper.WriteInfoLog(
                //     $"MAD窗: [{e.WindowStartSample},{e.WindowEndSample}] " +
                //     $"MAD={e.Mad:F3}, 超阈1={e.AboveThreshold1}, 连续计数={e.ConsecutiveAboveCount}");
            };

            _madDetector.OnSeizureTriggered += (s, e) =>
            {
                

                if (e.Mode == 1)
                {
                    client.EnqueueMode1Cmd();
                    NlogHelper.WriteInfoLog("闭环触发模式1");
                }
                else
                {
                    client.EnqueueMode2Cmd();
                    NlogHelper.WriteInfoLog("闭环触发模式2");
                }
            };

            _madDetector.Start();
        }
        
        public bool clear_peak_flag = false;
        private volatile bool _showRawData = false;
        private volatile bool _isClosedLoopMode = false;
        private int NextPowerOfTwo(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }

        private void FFTInPlace(Complex[] buffer)
        {
            int n = buffer.Length;

            int j = 0;
            for (int i = 1; i < n; i++)
            {
                int bit = n >> 1;
                while ((j & bit) != 0)
                {
                    j ^= bit;
                    bit >>= 1;
                }
                j ^= bit;

                if (i < j)
                {
                    Complex temp = buffer[i];
                    buffer[i] = buffer[j];
                    buffer[j] = temp;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                Complex wlen = new Complex(Math.Cos(ang), Math.Sin(ang));

                for (int i = 0; i < n; i += len)
                {
                    Complex w = Complex.One;
                    int half = len >> 1;

                    for (int k = 0; k < half; k++)
                    {
                        Complex u = buffer[i + k];
                        Complex v = buffer[i + k + half] * w;

                        buffer[i + k] = u + v;
                        buffer[i + k + half] = u - v;

                        w *= wlen;
                    }
                }
            }
        }
        private RelativePowers ComputeRelativePowers(double[] segment, double fs)
        {
            int n0 = segment.Length;
            int n = NextPowerOfTwo(n0);

            Complex[] x = new Complex[n];

            double mean = segment.Average();
            double sumW2 = 0.0;

            for (int i = 0; i < n0; i++)
            {
                double w = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (n0 - 1)); // Hann窗
                double v = (segment[i] - mean) * w;
                x[i] = new Complex(v, 0);
                sumW2 += w * w;
            }

            for (int i = n0; i < n; i++)
                x[i] = Complex.Zero;

            FFTInPlace(x);

            double df = fs / n;

            double delta = 0.0;
            double theta = 0.0;
            double alpha = 0.0;
            double beta = 0.0;

            for (int k = 0; k <= n / 2; k++)
            {
                double f = k * df;
                double pxx = x[k].Magnitude * x[k].Magnitude / (fs * sumW2);

                if (k > 0 && k < n / 2)
                    pxx *= 2.0;

                double area = pxx * df;

                if (f >= 0.5 && f < 4.0) delta += area;
                else if (f >= 4.0 && f < 8.0) theta += area;
                else if (f >= 8.0 && f < 13.0) alpha += area;
                else if (f >= 13.0 && f < 30.0) beta += area;
            }

            double total = delta + theta + alpha + beta;

            if (total <= 1e-12)
            {
                return new RelativePowers
                {
                    DeltaPct = 0,
                    ThetaPct = 0,
                    AlphaPct = 0,
                    BetaPct = 0
                };
            }

            return new RelativePowers
            {
                DeltaPct = delta / total * 100.0,
                ThetaPct = theta / total * 100.0,
                AlphaPct = alpha / total * 100.0,
                BetaPct = beta / total * 100.0
            };
        }
        void uav_control_CmdEvent(object sender, EcgTCPEventArgs e)
        {
            Numeeg += 33;
            //33数据包版本
            Buffer.BlockCopy(e.value, 2, eeg_data_byte, 0, 24);

            process_eegdata(eeg_data_byte);

            if (_isClosedLoopMode && !_madDetectorInited)
            {
                InitSingleChannelMadDetector(sampleRate);
                _madDetectorInited = true;
            }
            index++;
            int curIdx = buffer_index;

            for (int i = 0; i < 8; i++)
            {
                double temp = Convert.ToDouble(eeg_data_float[i]);

                double peakdata;
                if (clear_peak_flag)
                    peakdata = Median5_Update(i, temp);
                else
                    peakdata = temp;

                // --- 第1级 一阶高通：去基线漂移 ---
                //double yhp1 = hpA * (hp1_prevY[i] + peakdata - hp1_prevX[i]);
                //hp1_prevX[i] = peakdata;
                //hp1_prevY[i] = yhp1;

                //// --- 第2级 一阶高通：进一步增强滚降 ---
                //double yhp2 = hpA * (hp2_prevY[i] + yhp1 - hp2_prevX[i]);
                //hp2_prevX[i] = yhp1;
                //hp2_prevY[i] = yhp2;
                double yhp = hpf[i].Process(peakdata);

                // --- 50 Hz 双级陷波 ---
                double y1 = notch1[i].Process(yhp);
                double y2 = notch2[i].Process(y1);

                // --- 低通 ---
                double ylp1 = lpf1[i].Process(y2);
                double ylp2 = lpf2[i].Process(ylp1);

                double filterdata = ylp2;

                // 两份显示缓冲
                eeg_data_buffer_raw[i][curIdx] = temp;        // 原始数据
                eeg_data_buffer[i][curIdx] = filterdata;      // 滤波数据

                // 检测继续使用滤波后的数据
                if (_isClosedLoopMode && i == 0 && _madDetector != null)
                {
                    _madDetector.PushSample(filterdata);
                }
                if (i == _powerChannelIndex && _powerQueue != null)
                {
                    _powerQueue.TryAdd(filterdata);
                }
                // 保存逻辑
                if (index >= 1000)
                {
                    index = 1002;
                    save_data_buffer[buffer_save_index][i] = filterdata;

                    // 真正原始数据建议保存 temp，不要再写 yhp1
                    save_data_buffer_original[buffer_save_index][i] = yhp;
                }
            }

            // 根据当前显示模式追加曲线
            bool showRaw = _showRawData;

            for (int i = 0; i < 8; i++)
            {
                double showValue = showRaw ? eeg_data_buffer_raw[i][curIdx]
                                           : eeg_data_buffer[i][curIdx];

                lineData[i].Append(g_index, showValue - i * 10000);
            }

            buffer_index = (buffer_index + 1) % BaoLength;
            buffer_save_index++;
            buffer_save_index %= 500;
            g_index += 0.001;

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

                    lock (_saveBufferLock)
                    {
                        save_data_buffer_all.Add(copiedBuffer);
                        save_data_buffer_all_original.Add(copiedBuffer_original);
                    }
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
            if (Convert.ToInt32(g_index) - g_old_ecg_time > 2 || Convert.ToInt32(g_index) < WindowSize)
            {
                g_old_ecg_time = Convert.ToInt32(g_index);

                if (Convert.ToInt32(g_index) < WindowSize)
                {
                    g_old_ecg_time = Convert.ToInt32(WindowSize - 3);
                }
                this.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
                });
            }
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
       
        public async Task button_save_ecg_filter_ns2()
        {
            if (_isSaving)
            {
                MessageBox.Show("当前已有保存任务在进行，请稍后再试。");
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-filter-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒");
            saveFileDialog.FileName = date + ".ns2";
            saveFileDialog.DefaultExt = "ns2";
            saveFileDialog.Filter = "NS2 文件 (*.ns2)|*.ns2|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() != true)
                return;

            string filePath = saveFileDialog.FileName;

            List<double[][]> snapshot = SnapshotFilteredBlocks();
            if (snapshot.Count == 0)
            {
                MessageBox.Show("当前没有可保存的滤波数据。");
                return;
            }

            _isSaving = true;
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            try
            {
                await Task.Run(() => SaveNs2Blocks(filePath, snapshot));
                MessageBox.Show("滤波后 NS2 文件保存成功。");
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存滤波后 NS2 文件时出错: " + ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isSaving = false;
            }
        }


        private const int EXCEL_MAX_ROWS = 1000000; // Excel 单个 Sheet 最大行数
        private const int EXCEL_HEADER_ROWS = 1;   // 表头占 1 行
        public async Task button_save_ecg_filter_excel()
        {
            if (_isSaving)
            {
                MessageBox.Show("当前已有保存任务在进行，请稍后再试。");
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒");
            saveFileDialog.FileName = date + ".xlsx";
            saveFileDialog.DefaultExt = "xlsx";
            saveFileDialog.Filter = "Excel 文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() != true)
                return;

            string filePath = saveFileDialog.FileName;

            List<double[][]> snapshot = SnapshotFilteredBlocks();
            if (snapshot.Count == 0)
            {
                MessageBox.Show("当前没有可保存的滤波数据。");
                return;
            }

            _isSaving = true;
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            try
            {
                await Task.Run(() => SaveExcelBlocks(filePath, snapshot, "EEG_Data"));
                LogHelper.WriteInfoLog("Excel 数据保存成功（自动拆分 Sheet）");
                NlogHelper.WriteInfoLog("Excel 数据保存成功（自动拆分 Sheet）");
                MessageBox.Show("滤波后 Excel 文件保存成功。");
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存滤波后 Excel 文件时出错: " + ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isSaving = false;
            }
        }
       
        public async Task button_save_ecg_original_ns2()
        {
            if (_isSaving)
            {
                MessageBox.Show("当前已有保存任务在进行，请稍后再试。");
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-original-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒");
            saveFileDialog.FileName = date + ".ns2";
            saveFileDialog.DefaultExt = "ns2";
            saveFileDialog.Filter = "NS2 文件 (*.ns2)|*.ns2|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() != true)
                return;

            string filePath = saveFileDialog.FileName;

            List<double[][]> snapshot = SnapshotOriginalBlocks();
            if (snapshot.Count == 0)
            {
                MessageBox.Show("当前没有可保存的原始数据。");
                return;
            }

            _isSaving = true;
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            try
            {
                await Task.Run(() => SaveNs2Blocks(filePath, snapshot));
                MessageBox.Show("原始 NS2 文件保存成功，可在 MATLAB 用 openNSx 打开。");
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存原始 NS2 文件时出错: " + ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isSaving = false;
            }
        }

        public async Task button_save_ecg_original_excel()
        {
            if (_isSaving)
            {
                MessageBox.Show("当前已有保存任务在进行，请稍后再试。");
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-original-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒");
            saveFileDialog.FileName = date + ".xlsx";
            saveFileDialog.DefaultExt = "xlsx";
            saveFileDialog.Filter = "Excel 文件 (*.xlsx)|*.xlsx|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() != true)
                return;

            string filePath = saveFileDialog.FileName;

            List<double[][]> snapshot = SnapshotOriginalBlocks();
            if (snapshot.Count == 0)
            {
                MessageBox.Show("当前没有可保存的原始数据。");
                return;
            }

            _isSaving = true;
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            try
            {
                await Task.Run(() => SaveExcelBlocks(filePath, snapshot, "EEG_Original"));
                LogHelper.WriteInfoLog("原始 EEG Excel 保存成功（自动拆分 Sheet）");
                NlogHelper.WriteInfoLog("原始 EEG Excel 保存成功（自动拆分 Sheet）");
                MessageBox.Show("原始 Excel 文件保存成功。");
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存原始 Excel 文件时出错: " + ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isSaving = false;
            }
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
        private List<double[][]> SnapshotFilteredBlocks()
        {
            lock (_saveBufferLock)
            {
                return new List<double[][]>(save_data_buffer_all);
            }
        }

        private List<double[][]> SnapshotOriginalBlocks()
        {
            lock (_saveBufferLock)
            {
                return new List<double[][]>(save_data_buffer_all_original);
            }
        }

        private void SaveNs2Blocks(string filePath, List<double[][]> blocks)
        {
            const int channelCount = 8;
            const int samplingRate = 1000;
            const int timeResolution = 30000;
            const short minAnalog = -1000;
            const short maxAnalog = 1000;

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                // 1. FileTypeID
                writer.Write(Encoding.ASCII.GetBytes("NEURALCD"));

                // 2. FileSpec
                writer.Write((byte)2);
                writer.Write((byte)3);

                // 3. HeaderBytes placeholder
                long headerPos = writer.BaseStream.Position;
                writer.Write((uint)0);

                // 4. Sampling label
                writer.Write(Encoding.ASCII.GetBytes("EEG DATA".PadRight(16, '\0')));

                // 5. Comment
                writer.Write(Encoding.ASCII.GetBytes("Created by EEG acquisition system".PadRight(256, '\0')));

                // 6. Period & TimeRes
                writer.Write((uint)(timeResolution / samplingRate));
                writer.Write((uint)timeResolution);

                // 7. DateTime
                DateTime now = DateTime.Now;
                writer.Write((ushort)now.Year);
                writer.Write((ushort)now.Month);
                writer.Write((ushort)now.Day);
                writer.Write((ushort)now.Hour);
                writer.Write((ushort)now.Minute);
                writer.Write((ushort)now.Second);
                writer.Write((ushort)0);
                writer.Write((ushort)0);

                // 8. Channel count
                writer.Write((uint)channelCount);

                // 9. 扩展头
                for (int ch = 0; ch < channelCount; ch++)
                {
                    writer.Write(Encoding.ASCII.GetBytes("CC"));
                    writer.Write((ushort)(ch + 1));
                    writer.Write(Encoding.ASCII.GetBytes(("CH" + (ch + 1)).PadRight(16, '\0')));
                    writer.Write((byte)('A' + ch / 32));
                    writer.Write((byte)(ch % 32));
                    writer.Write((short)-32768);
                    writer.Write((short)32767);
                    writer.Write((short)minAnalog);
                    writer.Write((short)maxAnalog);
                    writer.Write(Encoding.ASCII.GetBytes("uV".PadRight(16, '\0')));
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((ushort)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((ushort)0);
                }

                // 10. 回填 HeaderBytes
                long headerEnd = writer.BaseStream.Position;
                writer.Seek((int)headerPos, SeekOrigin.Begin);
                writer.Write((uint)headerEnd);
                writer.Seek((int)headerEnd, SeekOrigin.Begin);

                // 11. 数据包头
                writer.Write((byte)1);
                writer.Write((uint)0);

                uint totalSamples = 0;
                foreach (var buf in blocks)
                    totalSamples += (uint)buf.Length;

                writer.Write(totalSamples);

                // 12. 数据区
                foreach (var buf in blocks)
                {
                    foreach (var row in buf)
                    {
                        for (int ch = 0; ch < channelCount; ch++)
                        {
                            int val = (int)row[ch];
                            if (val > short.MaxValue) val = short.MaxValue;
                            else if (val < short.MinValue) val = short.MinValue;

                            writer.Write((short)val);
                        }
                    }
                }
            }
        }

        private void SaveExcelBlocks(string filePath, List<double[][]> blocks, string sheetPrefix)
        {
            ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                int sheetIndex = 1;
                int currentRow = 1;

                ExcelWorksheet worksheet = CreateSheet(package, sheetPrefix, sheetIndex, ref currentRow);

                foreach (var buffer in blocks)
                {
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        if (currentRow > EXCEL_MAX_ROWS)
                        {
                            sheetIndex++;
                            worksheet = CreateSheet(package, sheetPrefix, sheetIndex, ref currentRow);
                        }

                        worksheet.Cells[currentRow, 1].LoadFromArrays(new[]
                        {
                    buffer[i].Cast<object>().ToArray()
                });

                        currentRow++;
                    }
                }

                package.Save();
            }
        }

        private ExcelWorksheet CreateSheet(ExcelPackage package, string sheetPrefix, int sheetIndex, ref int currentRow)
        {
            string sheetName = sheetPrefix + "_" + sheetIndex;
            var worksheet = package.Workbook.Worksheets.Add(sheetName);

            worksheet.Cells[1, 1].LoadFromArrays(new[]
            {
                new object[] { "Ch1", "Ch2", "Ch3", "Ch4", "Ch5", "Ch6", "Ch7", "Ch8" }
            });

            currentRow = EXCEL_HEADER_ROWS + 1;
            return worksheet;
        }
        public void Clear_Plot()
        {
            Numeeg = 0;
            _sw.Reset();
            EEG_time.Text= "00:00:00";
            Isclearplot_pro = true;
            buffer_index = 0;
            buffer_save_index = 0;
            g_index = 0;
            g_old_ecg_time = 0;
            eeg_data_buffer.Clear();
            save_data_buffer.Clear();
            save_data_buffer_original.Clear();
            lock (_saveBufferLock)
            {
                save_data_buffer_all.Clear();
                save_data_buffer_all_original.Clear();
            }
            for (int i = 0; i < 8; i++)
            {
                lineData[i].Clear();
            }
            deltaRelSeries?.Clear();
            thetaRelSeries?.Clear();
            alphaRelSeries?.Clear();
            betaRelSeries?.Clear();
            _powerTimeOffsetSec = 0.0;
            _lastPowerXSec = 0.0;
            _powerSessionId++;
            if (powerSurface != null)
            {
                powerSurface.XAxis.VisibleRange = new DoubleRange(0, RelPowerHistorySec);
            }
            LogHelper.WriteInfoLog("数据清除成功");
            NlogHelper.WriteInfoLog("数据清除成功");
        }   
        // 添加定时器相关字段
        private System.Windows.Threading.DispatcherTimer timer;
        private bool isTimerRunning = false;
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        // 初始化定时器
        private void InitializeTimer()
        {
            timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1); // 每5秒执行一次，可以根据需要调整
            timer.Tick += Timer_Tick;
            _sw.Reset();
        }
      

        // 定时器事件处理
        private void Timer_Tick(object sender, EventArgs e)
        {
            EEG_time.Text = _sw.Elapsed.ToString(@"hh\:mm\:ss");
            NumEEG.Text = Numeeg.ToString();

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
                _sw.Start();
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
                _sw.Stop();
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
    public sealed class BiquadHPF
    {
        private double b0, b1, b2, a1, a2;
        private double z1, z2;

        public BiquadHPF(double fs, double fc, double Q) => Update(fs, fc, Q);

        public void Update(double fs, double fc, double Q)
        {
            double w0 = 2.0 * Math.PI * (fc / fs);
            double cosw = Math.Cos(w0);
            double sinw = Math.Sin(w0);
            double alpha = sinw / (2.0 * Q);

            double B0 = (1 + cosw) / 2.0;
            double B1 = -(1 + cosw);
            double B2 = (1 + cosw) / 2.0;
            double A0 = 1 + alpha;
            double A1 = -2 * cosw;
            double A2 = 1 - alpha;

            b0 = B0 / A0; b1 = B1 / A0; b2 = B2 / A0;
            a1 = A1 / A0; a2 = A2 / A0;

            z1 = z2 = 0.0;
        }                                                                                   

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Process(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }

}
