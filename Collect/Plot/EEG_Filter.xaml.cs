using Collect.tool;
using Microsoft.Win32;
using OfficeOpenXml;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;
using ScottPlot;
using ScottPlot.ArrowShapes;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Colors = System.Windows.Media.Colors;
using System.Numerics;

namespace Collect.Plot
{
    /// <summary>
    /// EEG_Filter.xaml 的交互逻辑
    /// </summary>
    public partial class EEG_Filter : UserControl
    {
        private XyDataSeries<double, double>[] lineData;

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

        public bool IsFilter = false;
        public int Freq = 1000;
        public int Order = 4;
        public int EndFreq = 50;
        public EEG _eeg;
        public double[] firCoefficients;
        public FIRFilter filter;
        public double LLMin = 0;
        public double LLMax = 1;
        public double RMSMin = 0;
        public double RMSMax = 10;
        public double Delta = 0.2;
        public double Beta = 0.2;
        private XyDataSeries<double, double> deltaPowerSeries;
        private XyDataSeries<double, double> thetaPowerSeries;
        private XyDataSeries<double, double> alphaPowerSeries;
        private XyDataSeries<double, double> betaPowerSeries;
        private XyDataSeries<double, double> gammaPowerSeries;

        private struct BandPowers
        {
            public double Delta;
            public double Theta;
            public double Alpha;
            public double Beta;
            public double Gamma;
        }
        public EEG_Filter(EEG eeg)
        {
            _eeg=eeg;
            InitializeComponent();
            InitBandPowerChart();
            InitBandChannelCombo();
            band_delta.IsChecked=true;
            band_theta.IsChecked=true;
            band_alpha.IsChecked=true;
            band_beta.IsChecked=true;
            band_gamma.IsChecked=true;
            showdata.Items.Add(new ComboBoxItem
            {
                Content = "原始数据"
            });
            showdata.Items.Add(new ComboBoxItem
            {
                Content = "滤波数据"
            });
            timechange.Items.Add(new ComboBoxItem
            {
                Content = "ms"
            });
            timechange.Items.Add(new ComboBoxItem
            {
                Content = "s"
            });
            timechange.Items.Add(new ComboBoxItem
            {
                Content = "min"
            });
            timechange.Items.Add(new ComboBoxItem
            {
                Content = "h"
            });

            timechange.SelectedIndex = 1;

            showdata.SelectedIndex = 1;
            showdata.SelectionChanged += showdata_SelectionChanged;

            var xAxis = new NumericAxis() { AxisTitle = "Time (second)", VisibleRange = new DoubleRange(0, 2000) };
            var yAxis = new NumericAxis() { AxisTitle = "Value", Visibility = Visibility.Visible, VisibleRange = new DoubleRange(-1, 1) };

            sciChartSurface.XAxis = xAxis;
            sciChartSurface.YAxis = yAxis;
          
           // 创建 XyDataSeries 来托管图表的数据
           lineData = new XyDataSeries<double, double>[8];
            for (int i = 0; i < 8; i++)
            {
                //据系列可以存储的最大数据点数为5000，先进先出（FIFO）
                lineData[i] = new XyDataSeries<double, double>() { FifoCapacity = 20000};
                //lineData[i].AcceptsUnsortedData = true;
            }

            var colors = new[]
            {
                System.Windows.Media.Colors.Red,
                Colors.Orange,
                Colors.Cyan,
                Colors.Green,
                Colors.Blue,
                Colors.Orchid,
                Colors.Purple,
                Colors.Brown,
            };
            // 添加8条曲线
            for (int i = 0; i < 8; i++)
            {
                var lineSeries = new FastLineRenderableSeries()
                {
                    Stroke = colors[i],
                    StrokeThickness = 1,
                    //AntiAliasing = true,
                    AntiAliasing = false,
                  
                };
                sciChartSurface.RenderableSeries.Add(lineSeries);

            }
            // 将数据分配给8条曲线
            for (int i = 0; i < 8; i++)
            {
                sciChartSurface.RenderableSeries[i].DataSeries = lineData[i];
            }
            for (int i = 0; i < 500; i++)
            {
                //500行8列
                save_data_buffer[i] = new double[8];
            }
            channel_1.IsChecked = true;
            channel_2.IsChecked = true;
            channel_3.IsChecked = true;
            channel_4.IsChecked = true;
            channel_5.IsChecked = true;
            channel_6.IsChecked = true;
            channel_7.IsChecked = true;
            channel_8.IsChecked = true;
            
        }
        private void InitBandChannelCombo()
        {
            for (int i = 0; i < 8; i++)
            {
                cmbBandChannel.Items.Add(new ComboBoxItem
                {
                    Content = $"通道{i + 1}"
                });
            }

            cmbBandChannel.SelectedIndex = 0;
        }

        private void InitBandPowerChart()
        {
            deltaPowerSeries = new XyDataSeries<double, double>();
            thetaPowerSeries = new XyDataSeries<double, double>();
            alphaPowerSeries = new XyDataSeries<double, double>();
            betaPowerSeries = new XyDataSeries<double, double>();
            gammaPowerSeries = new XyDataSeries<double, double>();

            bandPowerSurface.XAxis = new NumericAxis
            {
                AxisTitle = "Time (second)"
            };

            bandPowerSurface.YAxis = new NumericAxis
            {
                AxisTitle = "Absolute Power",
                AutoRange = AutoRange.Always
            };

            bandPowerSurface.RenderableSeries.Clear();

            bandPowerSurface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                Stroke = Colors.Brown,
                StrokeThickness = 2,
                DataSeries = deltaPowerSeries
            });

            bandPowerSurface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                Stroke = Colors.Orange,
                StrokeThickness = 2,
                DataSeries = thetaPowerSeries
            });

            bandPowerSurface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                Stroke = Colors.Green,
                StrokeThickness = 2,
                DataSeries = alphaPowerSeries
            });

            bandPowerSurface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                Stroke = Colors.Blue,
                StrokeThickness = 2,
                DataSeries = betaPowerSeries
            });

            bandPowerSurface.RenderableSeries.Add(new FastLineRenderableSeries
            {
                Stroke = Colors.Purple,
                StrokeThickness = 2,
                DataSeries = gammaPowerSeries
            });
        }
        public float[] eeg_data_float= new float[8];
        public double[] eeg_data = new double[8];
        private double g_index;
      
        public bool clear_original_filter_txt_flag=false;          
        private static int WindowSize = 10;  
   
        double[][] save_data_buffer = new double[500][];
        private double ConvertInputTimeToSeconds(double value)
        {
            ComboBoxItem item = timechange.SelectedItem as ComboBoxItem;
            string unit = (item == null || item.Content == null) ? "s" : item.Content.ToString();

            switch (unit)
            {
                case "ms":
                    return value / 1000.0;

                case "s":
                    return value;

                case "min":
                    return value * 60.0;

                case "h":
                    return value * 3600.0;

                default:
                    return value;
            }
        }
        /// <summary>
        /// 离线处理数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //采样率
        static double sampleRate = 1000;

        static double hpCut = 0.3;   // 高通截止 0.5 Hz
        static double hpA;

        static double lpCut = 200.0;  // 低通截止 40 Hz
        // 4阶 Butterworth 两段二阶的 Q（固定值）
        static double lpQ1 = 0.5411961;
        static double lpQ2 = 1.3065630;

        static double notchF0 = 50.0;
        static double notchQ = 20.0;  // 约等于 BW=2Hz 的量级（可按需要微调）

        // ===== 状态（8通道）=====
        double[] hp1_prevX = new double[8];
        double[] hp1_prevY = new double[8];
        double[] hp2_prevX = new double[8];
        double[] hp2_prevY = new double[8];

        double[][] medBuf = Enumerable.Range(0, 8).Select(_ => new double[5]).ToArray();
        int[] medCount = new int[8];
        int[] medIdx = new int[8];

        BiquadLPF[] lpf1;
        BiquadLPF[] lpf2;
        BiquadNotch[] notch1;
        BiquadNotch[] notch2;


        double g_index_1 = 0;

        // ===== 每次处理前都重置（关键！）=====
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
            hpA = Math.Exp(-2.0 * Math.PI * hpCut / sampleRate);
            lpf1 = Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ1)).ToArray();
            lpf2 = Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ2)).ToArray();
            notch1 = Enumerable.Range(0, 8).Select(_ => new BiquadNotch(sampleRate, notchF0, notchQ)).ToArray();
            notch2 = Enumerable.Range(0, 8).Select(_ => new BiquadNotch(sampleRate, notchF0, notchQ)).ToArray();
            g_index_1 = 0;
        }
        /// <summary>
        /// 离线癫痫
        /// </summary>
        /// <param name="Freqtextbox_filter"></param>
        /// <returns></returns>
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
                  
                    //NlogHelper.WriteInfoLog(
                    //    $"MAD检测触发模式1: MAD1={e.FirstWindowMad:F3}, MAD2={e.SecondWindowMad:F3}, Max={e.MaxMad:F3}");
                }
                else
                {
                    
                //    NlogHelper.WriteInfoLog(
                //        $"MAD检测触发模式2: MAD1={e.FirstWindowMad:F3}, MAD2={e.SecondWindowMad:F3}, Max={e.MaxMad:F3}");
                }
            };

            _madDetector.Start();
        }
      
        public bool clear_peak_flag = false;

        private bool _isOfflineLoading = false;

        private sealed class OfflineLoadResult
        {
            public double[][] RawData;        // 原始数据 [channel][sample]
            public double[][] FilteredData;   // 滤波数据 [channel][sample]
            public double Fs;
            public int ChannelCount;
        }
        private bool IsShowRawDataSelected()
        {
            ComboBoxItem item = showdata.SelectedItem as ComboBoxItem;
            if (item == null || item.Content == null)
                return false;

            return item.Content.ToString() == "原始数据";
        }
        //public double[][] LoadExcelAs2DArray(string Freqtextbox_filter)
        //{
        //    // ① 打开文件选择框
        //    OpenFileDialog ofd = new OpenFileDialog
        //    {
        //        Title = "选择 Excel 文件",
        //        Filter = "Excel 文件 (*.xlsx)|*.xlsx",
        //        Multiselect = false
        //    };

        //    if (ofd.ShowDialog() != true)
        //        return null;

        //    string filePath = ofd.FileName;

        //    // ② 设置 EPPlus 许可
        //    ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

        //    var package = new ExcelPackage(new FileInfo(filePath));
        //    var worksheet = package.Workbook.Worksheets[0]; // 第一个 Sheet

        //    int colCount = worksheet.Dimension.Columns; // 列数
        //    int rowCount = worksheet.Dimension.Rows;    // 行数

        //    // 只处理前8通道（你内部状态数组都是长度8，Excel>8会直接越界）
        //    int chCount = Math.Min(colCount, 8);

        //    // ⚠️ 每次离线处理前重置滤波器状态
        //    ResetFilterState(chCount);

        //    // 初始化检测器
        //    if (!_madDetectorInited)
        //    {
        //        InitSingleChannelMadDetector(sampleRate);
        //        _madDetectorInited = true;
        //    }

        //    // ③ 分配输出缓冲：[通道][采样点]
        //    double[][] eeg_data_buffer = new double[chCount][];
        //    for (int ch = 0; ch < chCount; ch++)
        //        eeg_data_buffer[ch] = new double[rowCount];

        //    // ④ 开始逐点处理：每一行=一个采样点
        //    g_index = 0;
        //    double fsFromUI = Convert.ToDouble(Freqtextbox_filter);   // 你的 UI 采样率输入
        //    double dt = 1.0 / fsFromUI;

        //    for (int t = 0; t < rowCount; t++)
        //    {
        //        // 这一帧（8通道）的滤波后数据，送入 detector
        //        double[] frameFiltered = new double[8]; // 固定8给detector（不足的通道留0）

        //        for (int ch = 0; ch < chCount; ch++)
        //        {
        //            object cellValue = worksheet.Cells[t + 1, ch + 1].Value;
        //            double temp = (cellValue == null) ? 0.0 : Convert.ToDouble(cellValue);

        //            double peakdata = 0;
        //            if (clear_peak_flag)
        //            {
        //                peakdata  = Median5_Update(ch, temp);

        //            }
        //            else
        //            {
        //                peakdata = temp;
        //            }
        //            // --- 第1级 一阶高通：去基线漂移 ---
        //            double yhp1 = hpA * (hp1_prevY[ch] + peakdata - hp1_prevX[ch]);
        //            hp1_prevX[ch] = peakdata;
        //            hp1_prevY[ch] = yhp1;

        //            // --- 第2级 一阶高通 ---
        //            double yhp2 = hpA * (hp2_prevY[ch] + yhp1 - hp2_prevX[ch]);
        //            hp2_prevX[ch] = yhp1;
        //            hp2_prevY[ch] = yhp2;

        //            // --- 50 Hz 双级陷波 ---
        //            double y1 = notch1[ch].Process(yhp2);
        //            double y2 = notch2[ch].Process(y1);

        //            // --- 40 Hz 双级低通（等效4阶） ---
        //            double ylp1 = lpf1[ch].Process(y2);
        //            double ylp2 = lpf2[ch].Process(ylp1);

        //            if (ch == 0 && _madDetector != null)
        //            {
        //                //_madDetector.PushSample(ylp2);
        //                _madDetector.PushSample(temp);
        //            }

        //            // 写入输出
        //            eeg_data_buffer[ch][t] = ylp2;

        //            // 画图（每个通道同一个时间坐标 g_index）
        //            //lineData[ch].Append(g_index, ylp2 - ch * 10000);

        //            lineData[ch].Append(g_index, temp - ch * 10000);

        //            frameFiltered[ch] = temp;


        //        }



        //        // 时间推进：每个采样点只加一次
        //        g_index += dt;
        //    }


        //    return eeg_data_buffer;
        //}
        public async Task<double[][]> LoadExcelAs2DArray(string Freqtextbox_filter)
        {
            if (_isOfflineLoading)
            {
                MessageBox.Show("当前已有离线任务在处理中，请稍后再试");
                return null;
            }

            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "选择 Excel 文件",
                Filter = "Excel 文件 (*.xlsx)|*.xlsx",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true)
                return null;

            string filePath = ofd.FileName;

            double fsFromUI;
            if (!double.TryParse(Freqtextbox_filter, out fsFromUI) || fsFromUI <= 0)
            {
                MessageBox.Show("请输入正确的采样率");
                return null;
            }

            _isOfflineLoading = true;
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            try
            {
                OfflineLoadResult result = await Task.Run(() => LoadExcelAs2DArrayCore(filePath, fsFromUI));
                if (result == null)
                    return null;

                CacheOfflineResult(result);

                // 原始和滤波后的长度本来就一样，这里用 FilteredData 即可
                UpdateLoadedFileInfo(result.FilteredData[0].Length, result.Fs);

                RefreshChartFromCurrentTextBoxRange();

                // 对外仍然返回滤波后的数据
                return result.FilteredData;
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取 Excel 失败：\n" + ex.Message);
                return null;
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isOfflineLoading = false;
            }
        }
        private OfflineLoadResult LoadExcelAs2DArrayCore(string filePath, double fsFromUI)
        {
            ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var worksheet = package.Workbook.Worksheets[0];

                int colCount = worksheet.Dimension.Columns;
                int rowCount = worksheet.Dimension.Rows;

                int chCount = Math.Min(colCount, 8);

                sampleRate = fsFromUI;

                ResetFilterState(chCount);

                if (_madDetector != null)
                {
                    _madDetector.Stop();
                    _madDetector = null;
                }
                _madDetectorInited = false;

                InitSingleChannelMadDetector(sampleRate);
                _madDetectorInited = true;

                double[][] raw_data_buffer = new double[chCount][];
                double[][] eeg_data_buffer = new double[chCount][];

                for (int ch = 0; ch < chCount; ch++)
                {
                    raw_data_buffer[ch] = new double[rowCount];
                    eeg_data_buffer[ch] = new double[rowCount];
                }

                for (int t = 0; t < rowCount; t++)
                {
                    for (int ch = 0; ch < chCount; ch++)
                    {
                        object cellValue = worksheet.Cells[t + 1, ch + 1].Value;
                        double temp = (cellValue == null) ? 0.0 : Convert.ToDouble(cellValue);
                        raw_data_buffer[ch][t] = temp;
                        double peakdata;
                        if (clear_peak_flag)
                            peakdata = Median5_Update(ch, temp);
                        else
                            peakdata = temp;

                        // --- 第1级 一阶高通：去基线漂移 ---
                        double yhp1 = hpA * (hp1_prevY[ch] + peakdata - hp1_prevX[ch]);
                        hp1_prevX[ch] = peakdata;
                        hp1_prevY[ch] = yhp1;

                        // --- 第2级 一阶高通 ---
                        double yhp2 = hpA * (hp2_prevY[ch] + yhp1 - hp2_prevX[ch]);
                        hp2_prevX[ch] = yhp1;
                        hp2_prevY[ch] = yhp2;

                        // --- 50 Hz 双级陷波 ---
                        double y1 = notch1[ch].Process(yhp2);
                        double y2 = notch2[ch].Process(y1);

                        // --- 双级低通 ---
                        double ylp1 = lpf1[ch].Process(y2);
                        double ylp2 = lpf2[ch].Process(ylp1);

                        // 只检测通道1
                        if (ch == 0 && _madDetector != null)
                        {
                            _madDetector.PushSample(ylp2);
                        }

                        eeg_data_buffer[ch][t] = ylp2;
                    }
                }

                OfflineLoadResult result = new OfflineLoadResult();
                result.RawData = raw_data_buffer;
                result.FilteredData = eeg_data_buffer;
                result.Fs = fsFromUI;
                result.ChannelCount = chCount;
                return result;
            }
        }
        //public double[][] LoadNs2As2DArray()
        //{
        //    // ① 打开文件选择框：选择 NS2
        //    OpenFileDialog ofd = new OpenFileDialog
        //    {
        //        Title = "选择 NS2 文件",
        //        Filter = "NS2 文件 (*.ns2)|*.ns2|所有文件 (*.*)|*.*",
        //        Multiselect = false
        //    };

        //    if (ofd.ShowDialog() != true)
        //        return null;

        //    string filePath = ofd.FileName;

        //    // ② 读取 NS2
        //    MyNs2Reader.Ns2Result ns2 = MyNs2Reader.Read(filePath);
        //    if (ns2 == null || ns2.Data == null || ns2.Data.Length == 0)
        //        return null;

        //    // NS2 里的数据格式：double[channel][sample]
        //    double[][] rawData = ns2.Data;

        //    int colCount = (int)ns2.ChannelCount;                 // 通道数
        //    int rowCount = (int)ns2.SampleCountPerChannel;        // 每通道采样点数

        //    // 只处理前8通道（因为你内部滤波状态数组长度是8）
        //    int chCount = Math.Min(colCount, 8);

        //    // ③ 使用 NS2 文件里的采样率
        //    double fsFromFile = ns2.SamplingRateHz;
        //    if (fsFromFile <= 0)
        //        fsFromFile = 1000;   // 兜底，正常情况下不会走到这里

        //    sampleRate = fsFromFile;

        //    // 每次离线处理前重置滤波状态
        //    ResetFilterState(chCount);

        //    // 如果之前已经有旧 detector，先停掉，避免状态残留
        //    if (_madDetector != null)
        //    {
        //        _madDetector.Stop();
        //        _madDetector = null;
        //    }
        //    _madDetectorInited = false;

        //    // 初始化单通道 MAD 检测器
        //    InitSingleChannelMadDetector(sampleRate);
        //    _madDetectorInited = true;

        //    // ④ 分配输出缓冲：[通道][采样点]
        //    double[][] eeg_data_buffer = new double[chCount][];
        //    for (int ch = 0; ch < chCount; ch++)
        //        eeg_data_buffer[ch] = new double[rowCount];

        //    // 如果你希望每次加载文件都重新画图，可以先清空曲线
        //    for (int ch = 0; ch < chCount; ch++)
        //    {
        //        if (lineData[ch] != null)
        //            lineData[ch].Clear();
        //    }

        //    // ⑤ 开始逐点处理：每个 t 是一个采样时刻
        //    g_index = 0;
        //    double dt = 1.0 / fsFromFile;

        //    for (int t = 0; t < rowCount; t++)
        //    {
        //        for (int ch = 0; ch < chCount; ch++)
        //        {
        //            // 从 NS2 读取该通道该时刻的原始数据
        //            double temp = rawData[ch][t];

        //            double peakdata = 0;
        //            if (clear_peak_flag)
        //            {
        //                peakdata = Median5_Update(ch, temp);
        //            }
        //            else
        //            {
        //                peakdata = temp;
        //            }

        //            // --- 第1级 一阶高通：去基线漂移 ---
        //            double yhp1 = hpA * (hp1_prevY[ch] + peakdata - hp1_prevX[ch]);
        //            hp1_prevX[ch] = peakdata;
        //            hp1_prevY[ch] = yhp1;

        //            // --- 第2级 一阶高通 ---
        //            double yhp2 = hpA * (hp2_prevY[ch] + yhp1 - hp2_prevX[ch]);
        //            hp2_prevX[ch] = yhp1;
        //            hp2_prevY[ch] = yhp2;

        //            // --- 50 Hz 双级陷波 ---
        //            double y1 = notch1[ch].Process(yhp2);
        //            double y2 = notch2[ch].Process(y1);

        //            // --- 双级低通 ---
        //            double ylp1 = lpf1[ch].Process(y2);
        //            double ylp2 = lpf2[ch].Process(ylp1);

        //            // 单通道癫痫检测：只检测通道1（ch == 0）
        //            if (ch == 0 && _madDetector != null)
        //            {
        //                _madDetector.PushSample(ylp2);
        //            }

        //            // 写入输出
        //            eeg_data_buffer[ch][t] = ylp2;

        //            // 画图
        //            if (lineData[ch] != null)
        //            {
        //                lineData[ch].Append(g_index, ylp2 - ch * 10000);
        //            }
        //        }

        //        // 时间推进：每个采样点只加一次
        //        g_index += dt;
        //    }

        //    return eeg_data_buffer;
        //}
        public async Task<double[][]> LoadNs2As2DArray()
        {
            if (_isOfflineLoading)
            {
                MessageBox.Show("当前已有离线任务在处理中，请稍后再试");
                return null;
            }

            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "选择 NS2 文件",
                Filter = "NS2 文件 (*.ns2)|*.ns2|所有文件 (*.*)|*.*",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true)
                return null;

            string filePath = ofd.FileName;

            _isOfflineLoading = true;
            Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;

            try
            {
                OfflineLoadResult result = await Task.Run(() => LoadNs2As2DArrayCore(filePath));
                if (result == null)
                    return null;

                CacheOfflineResult(result);

                UpdateLoadedFileInfo(result.FilteredData[0].Length, result.Fs);

                RefreshChartFromCurrentTextBoxRange();

                return result.FilteredData;
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取 NS2 失败：\n" + ex.Message);
                return null;
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _isOfflineLoading = false;
            }
        }
        private OfflineLoadResult LoadNs2As2DArrayCore(string filePath)
        {
            MyNs2Reader.Ns2Result ns2 = MyNs2Reader.Read(filePath);
            if (ns2 == null || ns2.Data == null || ns2.Data.Length == 0)
                return null;

            double[][] rawData = ns2.Data;

            int colCount = (int)ns2.ChannelCount;
            int rowCount = (int)ns2.SampleCountPerChannel;
            int chCount = Math.Min(colCount, 8);

            double fsFromFile = ns2.SamplingRateHz;
            if (fsFromFile <= 0)
                fsFromFile = 1000;

            sampleRate = fsFromFile;

            ResetFilterState(chCount);

            if (_madDetector != null)
            {
                _madDetector.Stop();
                _madDetector = null;
            }
            _madDetectorInited = false;

            InitSingleChannelMadDetector(sampleRate);
            _madDetectorInited = true;

            double[][] eeg_data_buffer = new double[chCount][];
            for (int ch = 0; ch < chCount; ch++)
                eeg_data_buffer[ch] = new double[rowCount];

            for (int t = 0; t < rowCount; t++)
            {
                for (int ch = 0; ch < chCount; ch++)
                {
                    double temp = rawData[ch][t];

                    double peakdata;
                    if (clear_peak_flag)
                        peakdata = Median5_Update(ch, temp);
                    else
                        peakdata = temp;

                    // --- 第1级 一阶高通：去基线漂移 ---
                    double yhp1 = hpA * (hp1_prevY[ch] + peakdata - hp1_prevX[ch]);
                    hp1_prevX[ch] = peakdata;
                    hp1_prevY[ch] = yhp1;

                    // --- 第2级 一阶高通 ---
                    double yhp2 = hpA * (hp2_prevY[ch] + yhp1 - hp2_prevX[ch]);
                    hp2_prevX[ch] = yhp1;
                    hp2_prevY[ch] = yhp2;

                    // --- 50 Hz 双级陷波 ---
                    double y1 = notch1[ch].Process(yhp2);
                    double y2 = notch2[ch].Process(y1);

                    // --- 双级低通 ---
                    double ylp1 = lpf1[ch].Process(y2);
                    double ylp2 = lpf2[ch].Process(ylp1);

                    if (ch == 0 && _madDetector != null)
                    {
                        _madDetector.PushSample(ylp2);
                    }

                    eeg_data_buffer[ch][t] = ylp2;
                }
            }

            OfflineLoadResult result = new OfflineLoadResult();
            result.RawData = rawData;
            result.FilteredData = eeg_data_buffer;
            result.Fs = fsFromFile;
            result.ChannelCount = chCount;
            return result;
        }
        //public void save_offline(double[][] eeg_data_buffer)
        //{
        //    if (eeg_data_buffer == null || eeg_data_buffer.Length == 0)
        //        throw new Exception("eeg_data_buffer 为空");

        //    for (int ch = 0; ch < eeg_data_buffer.Length; ch++)
        //    {
        //        if (eeg_data_buffer[ch] == null)
        //            throw new Exception($"eeg_data_buffer[{ch}] 未初始化");
        //    }

        //    SaveFileDialog saveFileDialog = new SaveFileDialog
        //    {
        //        Title = "保存文件",
        //        FileName = "Record-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒") + ".xlsx",
        //        DefaultExt = "xlsx",
        //        Filter = "Excel 文件 (*.xlsx)|*.xlsx"
        //    };

        //    if (saveFileDialog.ShowDialog() != true)
        //        return;

        //    string filePath = saveFileDialog.FileName;
        //    ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

        //    using (var package = new ExcelPackage(new FileInfo(filePath)))
        //    {
        //        var worksheet = package.Workbook.Worksheets.Add("EEG Data");

        //        int channelCount = eeg_data_buffer.Length;
        //        int sampleCount = eeg_data_buffer.Min(ch => ch.Length);

        //        object[][] allData = new object[sampleCount + 1][];

        //        // 表头
        //        allData[0] = new object[channelCount];
        //        for (int ch = 0; ch < channelCount; ch++)
        //            allData[0][ch] = $"Ch{ch + 1}";

        //        // 数据
        //        for (int t = 0; t < sampleCount; t++)
        //        {
        //            allData[t + 1] = new object[channelCount];
        //            for (int ch = 0; ch < channelCount; ch++)
        //            {
        //                allData[t + 1][ch] = eeg_data_buffer[ch][t];
        //            }
        //        }

        //        worksheet.Cells[1, 1].LoadFromArrays(allData);
        //        package.Save();
        //    }

        //    LogHelper.WriteInfoLog("EEG 数据保存成功");
        //    NlogHelper.WriteInfoLog("EEG 数据保存成功");
        //}
        public async Task save_offline()
        {
            if (!_hasLoadedData || _loadedFilteredData == null || _loadedFilteredData.Length == 0)
            {
                MessageBox.Show("当前没有可保存的离线滤波数据，请先读取 Excel 或 NS2 文件。");
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Title = "保存离线滤波数据",
                FileName = "Record-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒") + ".ns2",
                DefaultExt = "ns2",
                Filter = "NS2 文件 (*.ns2)|*.ns2|所有文件 (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() != true)
                return;

            string filePath = saveFileDialog.FileName;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;

                // 用缓存的滤波结果 + 缓存的采样率统一写成 NS2
                await Task.Run(() => SaveOfflineDataAsNs2(filePath, _loadedFilteredData, _loadedFs));
                //SaveOfflineDataAsNs2(filePath, _loadedFilteredData, _loadedFs);
                LogHelper.WriteInfoLog("离线滤波数据已保存为 NS2");
                NlogHelper.WriteInfoLog("离线滤波数据已保存为 NS2");
                MessageBox.Show("离线滤波数据保存成功（NS2）");
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存离线 NS2 文件时出错：\n" + ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        /// <summary>
        /// 动态显示数据范围
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        private double[][] _loadedRawData = null;
        private double[][] _loadedFilteredData = null;   // 全量滤波后的数据 [ch][sample]
        private double _loadedFs = 1000;                 // 当前加载数据的采样率
        private int _loadedChannelCount = 0;             // 当前加载的通道数
        private bool _hasLoadedData = false;             // 是否已经加载过数据

        private void RefreshChartByTimeRange(double startSec, double endSec)
        {
            if (!_hasLoadedData || _loadedChannelCount <= 0)
            {
                MessageBox.Show("请先加载 Excel 或 NS2 数据");
                return;
            }

            double[][] sourceData = IsShowRawDataSelected() ? _loadedRawData : _loadedFilteredData;

            if (sourceData == null || sourceData.Length == 0)
            {
                MessageBox.Show("当前没有可显示的数据");
                return;
            }

            int totalSamples = sourceData[0].Length;

            int startIndex = (int)Math.Round(startSec * _loadedFs);
            int endIndex = (int)Math.Round(endSec * _loadedFs);

            if (startIndex < 0) startIndex = 0;
            if (endIndex >= totalSamples) endIndex = totalSamples - 1;

            if (startIndex >= totalSamples)
            {
                MessageBox.Show("起始时间超过了数据总时长");
                return;
            }

            if (endIndex <= startIndex)
            {
                MessageBox.Show("这个时间范围内没有可显示的数据");
                return;
            }

            int pointCount = endIndex - startIndex + 1;

            // 如果你想显示 0~3 秒，1000Hz 就有 3000 点
            // 所以 FifoCapacity 至少要大于 pointCount
            for (int ch = 0; ch < _loadedChannelCount; ch++)
            {
                if (lineData[ch] != null && lineData[ch].FifoCapacity.HasValue)
                {
                    if (lineData[ch].FifoCapacity.Value < pointCount)
                    {
                        lineData[ch].FifoCapacity = pointCount + 100;
                    }
                }
            }

            using (sciChartSurface.SuspendUpdates())
            {
                // 先清空所有曲线
                for (int ch = 0; ch < 8; ch++)
                {
                    if (lineData[ch] != null)
                        lineData[ch].Clear();
                }

                // 再批量追加指定时间范围的数据
                for (int ch = 0; ch < _loadedChannelCount; ch++)
                {
                    double[] x = new double[pointCount];
                    double[] y = new double[pointCount];

                    for (int i = 0; i < pointCount; i++)
                    {
                        int srcIndex = startIndex + i;
                        x[i] = srcIndex / _loadedFs;                       // 横坐标：秒
                        y[i] = sourceData[ch][srcIndex] - ch * 10000;
                    }

                    lineData[ch].Append(x, y);   // 一次性批量更新
                }

                sciChartSurface.XAxis.VisibleRange = new DoubleRange(startSec, endSec);
            }
        }
        private void showdata_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_hasLoadedData)
            {
                RefreshChartFromCurrentTextBoxRange();
            }
        }
        private void CacheOfflineResult(OfflineLoadResult result)
        {
            _loadedRawData = result.RawData;
            _loadedFilteredData = result.FilteredData;
            _loadedFs = result.Fs;
            _loadedChannelCount = result.ChannelCount;
            _hasLoadedData = true;
        }
        private void RefreshChartFromCurrentTextBoxRange()
        {
            if (!_hasLoadedData)
                return;

            double[][] sourceData = IsShowRawDataSelected() ? _loadedRawData : _loadedFilteredData;
            if (sourceData == null || sourceData.Length == 0)
                return;

            double startInput;
            double endInput;

            bool ok1 = double.TryParse(txtStartTime.Text, out startInput);
            bool ok2 = double.TryParse(txtEndTime.Text, out endInput);

            double startSec;
            double endSec;

            if (!ok1 || !ok2)
            {
                startSec = 0;
                endSec = Math.Min(3.0, sourceData[0].Length / _loadedFs);
            }
            else
            {
                startSec = ConvertInputTimeToSeconds(startInput);
                endSec = ConvertInputTimeToSeconds(endInput);
            }

            RefreshChartByTimeRange(startSec, endSec);
        }
        private void UpdateLoadedFileInfo(int sampleCount, double fs)
        {
            if (sampleCount < 0) sampleCount = 0;
            if (fs <= 0) fs = 1;

            double totalSeconds = sampleCount / fs;
            TimeSpan ts = TimeSpan.FromSeconds(totalSeconds);

            NumSample.Text = sampleCount.ToString();
            TotalTime.Text = ts.ToString(@"hh\:mm\:ss\.fff");
        }
        private static DoubleRange ComputeXAxisRange(double t)
        {
            if (t < WindowSize)
            {
                return new DoubleRange(0, WindowSize);
            }
            //t 值向上取整到最接近的整数
            return new DoubleRange(Math.Ceiling(t) - WindowSize + 5, Math.Ceiling(t) + 5);
        }
        private void channel_1_Checked(object sender, RoutedEventArgs e)
        {
            channel_1.IsChecked = true;
            sciChartSurface.RenderableSeries[0].IsVisible = true;
        }

        private void channel_2_Checked(object sender, RoutedEventArgs e)
        {
            channel_2.IsChecked = true;
            sciChartSurface.RenderableSeries[1].IsVisible = true;
        }

        private void channel_3_Checked(object sender, RoutedEventArgs e)
        {
            channel_3.IsChecked = true;
            sciChartSurface.RenderableSeries[2].IsVisible = true;
        }

        private void channel_4_Checked(object sender, RoutedEventArgs e)
        {
            channel_4.IsChecked = true;
            sciChartSurface.RenderableSeries[3].IsVisible = true;
        }

        private void channel_5_Checked(object sender, RoutedEventArgs e)
        {
            channel_5.IsChecked = true;
            sciChartSurface.RenderableSeries[4].IsVisible = true;
        }

        private void channel_6_Checked(object sender, RoutedEventArgs e)
        {
            channel_6.IsChecked = true;
            sciChartSurface.RenderableSeries[5].IsVisible = true;
        }

        private void channel_7_Checked(object sender, RoutedEventArgs e)
        {
            channel_7.IsChecked = true;
            sciChartSurface.RenderableSeries[6].IsVisible = true;
        }

        private void channel_8_Checked(object sender, RoutedEventArgs e)
        {
            channel_8.IsChecked = true;
            sciChartSurface.RenderableSeries[7].IsVisible = true;
        }
        private void channel_1_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_1.IsChecked = false;
            sciChartSurface.RenderableSeries[0].IsVisible = false;
        }

        private void channel_2_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_2.IsChecked = false;
            sciChartSurface.RenderableSeries[1].IsVisible = false;
        }

        private void channel_3_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_3.IsChecked = false;
            sciChartSurface.RenderableSeries[2].IsVisible = false;
        }

        private void channel_4_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_4.IsChecked = false;
            sciChartSurface.RenderableSeries[3].IsVisible = false;
        }

        private void channel_5_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_5.IsChecked = false;
            sciChartSurface.RenderableSeries[4].IsVisible = false;
        }

        private void channel_6_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_6.IsChecked = false;
            sciChartSurface.RenderableSeries[5].IsVisible = false;
        }

        private void channel_7_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_7.IsChecked = false;
            sciChartSurface.RenderableSeries[6].IsVisible = false;
        }

        private void channel_8_Unchecked(object sender, RoutedEventArgs e)
        {
            channel_8.IsChecked = false;
            sciChartSurface.RenderableSeries[7].IsVisible = false;
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

        private void btn_update_Click(object sender, RoutedEventArgs e)
        {
            double startInput;
            double endInput;

            bool ok1 = double.TryParse(txtStartTime.Text, out startInput);
            bool ok2 = double.TryParse(txtEndTime.Text, out endInput);

            if (!ok1 || !ok2)
            {
                MessageBox.Show("请输入正确的时间范围");
                return;
            }

            double startSec = ConvertInputTimeToSeconds(startInput);
            double endSec = ConvertInputTimeToSeconds(endInput);

            RefreshChartByTimeRange(startSec, endSec);
        }
        private void SaveOfflineDataAsNs2(string filePath, double[][] eegData, double fs)
        {
            if (eegData == null || eegData.Length == 0)
                throw new Exception("eegData 为空");

            int channelCount = eegData.Length;
            int sampleCount = eegData.Min(ch => ch.Length);

            if (channelCount <= 0 || sampleCount <= 0)
                throw new Exception("离线数据为空，无法保存");

            int samplingRate = (int)Math.Round(fs);
            if (samplingRate <= 0)
                samplingRate = 1000;

            const int timeResolution = 30000;
            uint period = (uint)Math.Max(1, (int)Math.Round((double)timeResolution / samplingRate));
            const short minAnalog = -1000;
            const short maxAnalog = 1000;

            using (var fsStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fsStream))
            {
                // 1. FileTypeID
                writer.Write(Encoding.ASCII.GetBytes("NEURALCD"));   // 8 bytes

                // 2. FileSpec
                writer.Write((byte)2);   // major
                writer.Write((byte)3);   // minor

                // 3. HeaderBytes 占位
                long headerPos = writer.BaseStream.Position;
                writer.Write((uint)0);

                // 4. SamplingLabel
                writer.Write(Encoding.ASCII.GetBytes("EEG DATA".PadRight(16, '\0')));

                // 5. Comment
                writer.Write(Encoding.ASCII.GetBytes("Created by offline EEG filter".PadRight(256, '\0')));

                // 6. Period & TimeResolution
                writer.Write(period);
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

                // 8. ChannelCount
                writer.Write((uint)channelCount);

                // 9. 扩展头（每通道 66 字节）
                for (int ch = 0; ch < channelCount; ch++)
                {
                    writer.Write(Encoding.ASCII.GetBytes("CC"));  // 2 bytes
                    writer.Write((ushort)(ch + 1));               // Electrode ID
                    writer.Write(Encoding.ASCII.GetBytes(("CH" + (ch + 1)).PadRight(16, '\0')));
                    writer.Write((byte)('A' + ch / 32));          // ConnectorBank
                    writer.Write((byte)(ch % 32));                // ConnectorPin
                    writer.Write((short)-32768);                  // MinDigiValue
                    writer.Write((short)32767);                   // MaxDigiValue
                    writer.Write((short)minAnalog);               // MinAnalogValue
                    writer.Write((short)maxAnalog);               // MaxAnalogValue
                    writer.Write(Encoding.ASCII.GetBytes("uV".PadRight(16, '\0')));
                    writer.Write((uint)0);                        // HighFreqCorner
                    writer.Write((uint)0);                        // HighFreqOrder
                    writer.Write((ushort)0);                      // HighFilterType
                    writer.Write((uint)0);                        // LowFreqCorner
                    writer.Write((uint)0);                        // LowFreqOrder
                    writer.Write((ushort)0);                      // LowFilterType
                }

                // 10. 回填 HeaderBytes
                long headerEnd = writer.BaseStream.Position;
                writer.Seek((int)headerPos, SeekOrigin.Begin);
                writer.Write((uint)headerEnd);
                writer.Seek((int)headerEnd, SeekOrigin.Begin);

                // 11. 数据包头
                writer.Write((byte)1);             // marker
                writer.Write((uint)0);             // timestamp
                writer.Write((uint)sampleCount);   // 每通道采样点数

                // 12. 数据区：按 sample 交织写入
                for (int t = 0; t < sampleCount; t++)
                {
                    for (int ch = 0; ch < channelCount; ch++)
                    {
                        int val = (int)Math.Round(eegData[ch][t]);

                        if (val > short.MaxValue) val = short.MaxValue;
                        else if (val < short.MinValue) val = short.MinValue;

                        writer.Write((short)val);
                    }
                }
            }
        }

        private void btnBandPower_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasLoadedData || _loadedFilteredData == null)
            {
                MessageBox.Show("请先读取离线数据");
                return;
            }

            double startInput, endInput;
            if (!double.TryParse(txtStartTime.Text, out startInput) ||
                !double.TryParse(txtEndTime.Text, out endInput))
            {
                MessageBox.Show("请输入正确的时间范围");
                return;
            }

            double startSec = ConvertInputTimeToSeconds(startInput);
            double endSec = ConvertInputTimeToSeconds(endInput);

            if (endSec <= startSec)
            {
                MessageBox.Show("结束时间必须大于起始时间");
                return;
            }

            double winSec, stepSec;
            if (!double.TryParse(txtBandWindowSec.Text, out winSec) || winSec <= 0)
            {
                MessageBox.Show("窗长输入不正确");
                return;
            }

            if (!double.TryParse(txtBandStepSec.Text, out stepSec) || stepSec <= 0)
            {
                MessageBox.Show("步长输入不正确");
                return;
            }

            int ch = cmbBandChannel.SelectedIndex;
            if (ch < 0) ch = 0;

            // 推荐固定分析滤波数据，更稳
            DrawBandPowerCurves(_loadedFilteredData[ch], _loadedFs, startSec, endSec, winSec, stepSec);
        }
        private void DrawBandPowerCurves(double[] signal, double fs,
    double startSec, double endSec, double winSec, double stepSec)
        {
            int startIndex = Math.Max(0, (int)Math.Round(startSec * fs));
            int endIndex = Math.Min(signal.Length - 1, (int)Math.Round(endSec * fs));

            int winN = Math.Max(16, (int)Math.Round(winSec * fs));
            int stepN = Math.Max(1, (int)Math.Round(stepSec * fs));

            if (endIndex - startIndex + 1 < winN)
            {
                MessageBox.Show("当前时间段长度小于窗长，无法计算功率曲线");
                return;
            }

            using (bandPowerSurface.SuspendUpdates())
            {
                deltaPowerSeries.Clear();
                thetaPowerSeries.Clear();
                alphaPowerSeries.Clear();
                betaPowerSeries.Clear();
                gammaPowerSeries.Clear();

                for (int pos = startIndex; pos + winN - 1 <= endIndex; pos += stepN)
                {
                    double[] seg = new double[winN];
                    Array.Copy(signal, pos, seg, 0, winN);

                    BandPowers bp = ComputeAbsoluteBandPowers(seg, fs);

                    double centerTime = (pos + winN / 2.0) / fs;

                    deltaPowerSeries.Append(centerTime, bp.Delta);
                    thetaPowerSeries.Append(centerTime, bp.Theta);
                    alphaPowerSeries.Append(centerTime, bp.Alpha);
                    betaPowerSeries.Append(centerTime, bp.Beta);
                    gammaPowerSeries.Append(centerTime, bp.Gamma);
                }

                bandPowerSurface.XAxis.VisibleRange = new DoubleRange(startSec, endSec);
            }
        }
        private BandPowers ComputeAbsoluteBandPowers(double[] segment, double fs)
        {
            int n0 = segment.Length;
            int n = NextPowerOfTwo(n0);

            Complex[] x = new Complex[n];

            double mean = segment.Average();
            double sumW2 = 0.0;

            for (int i = 0; i < n0; i++)
            {
                double w = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (n0 - 1)); // Hann
                double v = (segment[i] - mean) * w;
                x[i] = new Complex(v, 0);
                sumW2 += w * w;
            }

            for (int i = n0; i < n; i++)
                x[i] = Complex.Zero;

            FFTInPlace(x);

            double df = fs / n;

            BandPowers bp = new BandPowers();

            for (int k = 0; k <= n / 2; k++)
            {
                double f = k * df;

                double pxx = x[k].Magnitude * x[k].Magnitude / (fs * sumW2);

                // 单边谱补偿
                if (k > 0 && k < n / 2)
                    pxx *= 2.0;

                double area = pxx * df;

                if (f >= 0.5 && f < 4.0) bp.Delta += area;
                else if (f >= 4.0 && f < 8.0) bp.Theta += area;
                else if (f >= 8.0 && f < 13.0) bp.Alpha += area;
                else if (f >= 13.0 && f < 30.0) bp.Beta += area;
                else if (f >= 30.0 && f < 60.0) bp.Gamma += area;
            }

            return bp;
        }
        private int NextPowerOfTwo(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }

        private void FFTInPlace(Complex[] buffer)
        {
            int n = buffer.Length;

            // 位反转
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

            // 蝶形
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
        private void band_delta_Checked(object sender, RoutedEventArgs e)
        {
            
                bandPowerSurface.RenderableSeries[0].IsVisible = true;
        }

        private void band_delta_Unchecked(object sender, RoutedEventArgs e)
        {
           
                bandPowerSurface.RenderableSeries[0].IsVisible = false;
        }

        private void band_theta_Checked(object sender, RoutedEventArgs e)
        {
           
                bandPowerSurface.RenderableSeries[1].IsVisible = true;
        }

        private void band_theta_Unchecked(object sender, RoutedEventArgs e)
        {
            
                bandPowerSurface.RenderableSeries[1].IsVisible = false;
        }

        private void band_alpha_Checked(object sender, RoutedEventArgs e)
        {
            
                bandPowerSurface.RenderableSeries[2].IsVisible = true;
        }

        private void band_alpha_Unchecked(object sender, RoutedEventArgs e)
        {
            
                bandPowerSurface.RenderableSeries[2].IsVisible = false;
        }

        private void band_beta_Checked(object sender, RoutedEventArgs e)
        {
            
                bandPowerSurface.RenderableSeries[3].IsVisible = true;
        }

        private void band_beta_Unchecked(object sender, RoutedEventArgs e)
        {
            
                bandPowerSurface.RenderableSeries[3].IsVisible = false;
        }

        private void band_gamma_Checked(object sender, RoutedEventArgs e)
        {
          
                bandPowerSurface.RenderableSeries[4].IsVisible = true;
        }

        private void band_gamma_Unchecked(object sender, RoutedEventArgs e)
        {
            
                bandPowerSurface.RenderableSeries[4].IsVisible = false;
        }
        // ===== 5点中值（前置）=====
        //double Median5_Update(int ch, double x)
        //{
        //    var buf = medBuf[ch];
        //    buf[medIdx[ch]] = x;
        //    medIdx[ch] = (medIdx[ch] + 1) % 5;
        //    if (medCount[ch] < 5) medCount[ch]++;

        //    int n = medCount[ch];
        //    if (n <= 1) return x;

        //    // 注意：环形缓冲直接Copy会乱序，但中值不依赖顺序，只依赖集合，因此可直接复制前n个“已写入的元素”
        //    // 为了更严谨：复制全5个再取前n个非0也行；这里用简单实现
        //    double[] w = new double[n];
        //    Array.Copy(buf, w, n);
        //    Array.Sort(w);
        //    return w[n / 2];
        //}
        // 5点中值，实时更新：把当前样本写入环形缓冲，返回中值
        //double Median5_Update(int ch, double x)
        //{
        //    var buf = medBuf[ch];
        //    buf[medIdx[ch]] = x;
        //    medIdx[ch] = (medIdx[ch] + 1) % 5;
        //    if (medCount[ch] < 5) medCount[ch]++;

        //    // 拷贝已填元素并求中值
        //    int n = medCount[ch];
        //    if (n == 1) return x; // 初始化前期
        //    double[] w = new double[n];
        //    Array.Copy(buf, w, n);
        //    Array.Sort(w, 0, n);
        //    return w[n / 2];
        //}




    }

    public  class FIRFilter
    {
        private readonly double[] coefficients;
        private readonly double[] buffer;
        private int offset;

        public FIRFilter(double[] coefficients)
        {
            this.coefficients = coefficients;
            this.buffer = new double[coefficients.Length];
            this.offset = 0;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        public double Process(double input)
        {
            buffer[offset] = input;

            double output = 0.0;
            int idx = offset;
            for (int i = 0; i < coefficients.Length; i++)
            {
                output += coefficients[i] * buffer[idx];
                if (--idx < 0) idx = buffer.Length - 1;
            }

            if (++offset >= buffer.Length) offset = 0;

            return output;
        }

        public double[] Process(double[] input)
        {
            double[] output = new double[input.Length];
            for (int i = 0; i < input.Length; i++)
                output[i] = Process(input[i]);
            return output;
        }
    }


}
