
using Collect.FIR;
using Collect.tool;
using MathNet.Filtering;
using MathNet.Filtering.FIR;
using MathNet.Filtering.IIR;
using Microsoft.Win32;
using NWaves.Audio;
using NWaves.Filters;
using NWaves.Filters.Base;
using NWaves.Filters.BiQuad;
using NWaves.Filters.Butterworth;
using NWaves.Filters.ChebyshevI;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;
using ScottPlot;
using ScottPlot.ArrowShapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Colors = System.Windows.Media.Colors;

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

        public EEG_Filter(EEG eeg)
        {
            _eeg=eeg;
            InitializeComponent();
           

            ch.Items.Add(new ComboBoxItem
            {
                Content = "1"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "2"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "3"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "4"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "5"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "6"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "7"
            });
            ch.Items.Add(new ComboBoxItem
            {
                Content = "8"
            });


            //for (int i = 0; i < 50; i++)
            //{
            //    //50行8列
            //    save_data_buffer[i] = new double[8];
            //}
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
                lineData[i].AcceptsUnsortedData = true;
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
                    AntiAliasing = true,
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
            _eeg.Ecg_FilterEvent += Ecg_FilterEvent;
        }
        public float[] eeg_data_float= new float[8];
        public double[] eeg_data = new double[8];
        private double g_index;
        private double g_scale=1;
        private double EEG_Length = 10;
        private double EEG_Length_COUNT = 0;
        public bool clear_original_filter_txt_flag=false;
        private bool single_filter_flag = true;

        private bool long_filter_flag = false;

        private List<double[]> EEG_FILTER_DATA = new List<double[]>();
        private int g_old_ecg_time;
        private static int WindowSize = 10;
        int Count = 0;
        
        // 为每个通道创建独立的滤波器实例，避免重复创建
        private FIRFilter[] channelFilters = new FIRFilter[8];
        private bool filtersInitialized = false;
        double[][] save_data_buffer = new double[500][];
        private int buffer_save_index;
        List<double[][]> save_data_buffer_all = new List<double[][]>();


         //工频干扰
        static double sampleRate = 500;
        //static double notchFreg = 50;
        //static double notchBandwidth = 2;
        static double f0 = 50.0;     // 60Hz 电网改成 60
        static double Q = 35.0;     // 30~40 之间调，Q 越大陷波越窄

        // --- 新增：去基线漂移的高通，推荐 fc = 0.5 Hz ---
        static double hpCut = 0.5;                              // 截止频率（-3 dB）
        static double hpA = Math.Exp(-2.0 * Math.PI * hpCut / sampleRate); // α = e^{-2π fc / fs}

        // —— 新增：低通 40 Hz（两级）——
        static double lpCut = 40.0;   // 低通截止
        static double lpQ = 1.0 / Math.Sqrt(2.0); // 二阶巴特沃斯
        BiquadLPF[] lpf1 = Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ)).ToArray();
        BiquadLPF[] lpf2 = Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ)).ToArray();

        // —— 新增：5 点中值滤波的缓冲与指针（每通道各一组）——
        double[][] medBuf = Enumerable.Range(0, 8).Select(_ => new double[5]).ToArray();
        int[] medCount = new int[8];   // 已填数量（≤5）
        int[] medIdx = new int[8];   // 写指针 0..4


        // 每个通道独立的一对notch（你已有）
        BiquadNotch[] notch1 = Enumerable.Range(0, 8).Select(_ => new BiquadNotch(sampleRate, f0, Q)).ToArray();
        BiquadNotch[] notch2 = Enumerable.Range(0, 8).Select(_ => new BiquadNotch(sampleRate, f0, Q)).ToArray();

        // --- 新增：一阶高通的状态（两级HP，效果更好） ---
        double[] hp1_prevX = new double[8];
        double[] hp1_prevY = new double[8];
        double[] hp2_prevX = new double[8];
        double[] hp2_prevY = new double[8];

        ////级联两个 Notch 提高抑制度（>30 dB 很容易）
        //BiquadNotch notch1 = new BiquadNotch(sampleRate, f0, Q);
        //BiquadNotch notch2 = new BiquadNotch(sampleRate, f0, Q);

        //// 每个通道独立的一对notch（防止状态串扰）
        //BiquadNotch[] notch1 = Enumerable.Range(0, 8)
        //    .Select(_ => new BiquadNotch(sampleRate, f0, Q)).ToArray();
        //BiquadNotch[] notch2 = Enumerable.Range(0, 8)
        //    .Select(_ => new BiquadNotch(sampleRate, f0, Q)).ToArray();



        //// 慢均值为类成员（不要在循环里new）
        //double[] dcMean = new double[8];
        //const double hpTauSec = 2.0;  // 2s 时间常数可调
        //double alpha = 1.0 / (sampleRate * hpTauSec);
        ////1 计算带照上下腿
        //static double fLow = notchFreg - notchBandwidth;
        //static double fHigh = notchFreg + notchBandwidth;

        //OnlineFilter notchFilter = OnlineFirFilter.CreateBandstop(
        //    ImpulseResponse.Finite,
        //    sampleRate,
        //    fLow,
        //    fHigh,
        //    256
        //);
        private void Ecg_FilterEvent(object sender, EcgFilterEventArgs e)
        {
            EEG_Length_COUNT++;
            if (IsFilter)
            {
                var filename = Directory.GetCurrentDirectory();
                string originalDataFile = System.IO.Path.Combine(filename, "Original_data.txt");
                string filterDataFile = System.IO.Path.Combine(filename, "Filter_data.txt");
                if (clear_original_filter_txt_flag)
                {
                    // 清空 Original_data.txt 文件内容
                    File.WriteAllText(originalDataFile, string.Empty);
                    File.WriteAllText(filterDataFile, string.Empty);
                    clear_original_filter_txt_flag = false;
                    IsFilter=false;
                    //如需复位滤波器状态可在此重新new
                    for (int k = 0; k < 8; k++)
                    {
                        notch1[k] = new BiquadNotch(sampleRate, f0, Q);
                        notch2[k] = new BiquadNotch(sampleRate, f0, Q);
                        // 新增：重置高通状态
                        hp1_prevX[k] = hp1_prevY[k] = 0.0;
                        hp2_prevX[k] = hp2_prevY[k] = 0.0;
                        lpf1[k] = new BiquadLPF(sampleRate, lpCut, lpQ);
                        lpf2[k] = new BiquadLPF(sampleRate, lpCut, lpQ);
                        Array.Clear(medBuf[k], 0, medBuf[k].Length);
                        medCount[k] = 0;
                        medIdx[k] = 0;
                    }
                }
               
                // 创建滤波器并处理该通道数据
                //var onlineFirFilter = OnlineFirFilter.CreateBandpass(ImpulseResponse.Finite, 1000, 0.3, 40, 1024);
                Buffer.BlockCopy(e.eeg_data, 0, eeg_data_float, 0, 8);
                //for(int i = 0; i < eeg_data_float.Length; i++)
                //{
                //    eeg_data[i] =Convert.ToDouble( eeg_data_float[i]);
                //}
              
                //if (long_filter_flag)
                //{
                //    EEG_FILTER_DATA.Add(eeg_data);
                //    //开始处理
                //    if (EEG_Length_COUNT >= EEG_Length)
                //    {
                //        // 对每个通道进行滤波处理
                //        for (int i = 0; i < 8; i++)
                //        {
                //            // 提取所有数组的索引[i]为一个新数组
                //            double[] channelData = new double[EEG_FILTER_DATA.Count];
                //            for (int j = 0; j < EEG_FILTER_DATA.Count; j++)
                //            {
                //                channelData[j] = Convert.ToDouble(EEG_FILTER_DATA[j][i]);
                //            }
                //            double[] filteredChannelData = onlineFirFilter.ProcessSamples(channelData);
                //            for (int j = 0; j < filteredChannelData.Length; j++)
                //            {
                //                lineData[i].Append(g_index, filteredChannelData[j]);
                //            }
                //            g_index += 0.002;

                //            //当前数据点与上次更新的数据差大于4x500个数据点或者当前数据少于WindowSizex500
                //            if (Convert.ToInt32(g_index) - g_old_ecg_time > 4 || Convert.ToInt32(g_index) < WindowSize)
                //            {
                //                g_old_ecg_time = Convert.ToInt32(g_index);

                //                if (Convert.ToInt32(g_index) < WindowSize)
                //                {
                //                    g_old_ecg_time = Convert.ToInt32(WindowSize - 5);
                //                }
                //                this.Dispatcher.BeginInvoke((Action)delegate ()
                //                {
                //                    sciChartSurface.XAxis.VisibleRange = ComputeXAxisRange(g_old_ecg_time);
                //                });
                //            }
                //            EEG_Length_COUNT = 0;
                //            // 现在filteredChannelData包含了该通道所有时间点的滤波后数据
                //            // 您可以在这里使用滤波后的数据进行后续处理
                //        }
                //    }

                //}
                if (single_filter_flag)
                {
                    if (EEG_Length_COUNT >= 500)
                    {
                       
                        for (int i = 0; i < 8; i++)
                        {
                            //double filterdata = onlineFirFilter.ProcessSample(Convert.ToDouble(eeg_data_float[i]));
                            //double filterdata = notchFilter.ProcessSample(Convert.ToDouble(eeg_data_float[i]));
                            //double x = Convert.ToDouble(eeg_data_float[i]);

                            //// 可选：去掉直流均值，减少边缘效应（强烈建议）
                            //const double hpTauSec = 2.0;               // 2s 的慢均值（可调）
                            //double[] dcMean = new double[8];    // 放到类里
                            //double alpha = 1.0 / (sampleRate * hpTauSec);
                            //dcMean[i] += (x - dcMean[i]) * alpha;
                            //double xAC = x - dcMean[i];

                            //// 串两级 notch 提升抑制深度
                            //double y = notch2.Process(notch1.Process(xAC));

                            //// 如需保持原有直流水平，可加回去（画原始计数值时更直观）
                            //double filterdata = y + dcMean[i];

                            double x = eeg_data_float[i];

                            // --- 第1级 一阶高通：去基线漂移 ---
                            // y[n] = a * (y[n-1] + x[n] - x[n-1])
                            double yhp1 = hpA * (hp1_prevY[i] + x - hp1_prevX[i]);
                            hp1_prevX[i] = x;
                            hp1_prevY[i] = yhp1;

                            // --- 第2级 一阶高通：进一步增强滚降 ---
                            double yhp2 = hpA * (hp2_prevY[i] + yhp1 - hp2_prevX[i]);
                            hp2_prevX[i] = yhp1;
                            hp2_prevY[i] = yhp2;



                            // --- 50 Hz 双级陷波 ---
                            double y1 = notch1[i].Process(yhp2);
                            double y2 = notch2[i].Process(y1);

                            // --- 新增：40 Hz 低通（两级，等效4阶） ---
                            double ylp1 = lpf1[i].Process(y2);
                            double ylp2 = lpf2[i].Process(ylp1);

                            // ---新增：5点中值去尖峰（实时） ---
                            double filterdata = Median5_Update(i, ylp2);

                            //// 慢均值跟踪（高通去直流/漂移）
                            //dcMean[i] += (x - dcMean[i]) * alpha;
                            //double xAC = x - dcMean[i];

                            //// 串两级陷波，提升抑制度
                            //double y1 = notch1[i].Process(xAC);
                            //double y2 = notch2[i].Process(y1);

                            //double filterdata = y2 + dcMean[i];
                            lineData[i].Append(g_index, filterdata);
                            if (i == 0)
                            {
                                File.AppendAllText(originalDataFile, x.ToString("G17") + " ");
                                File.AppendAllText(filterDataFile, filterdata.ToString("G17") + " ");
                            }
                            save_data_buffer[buffer_save_index][i] = filterdata;
                        }
                        g_index += 0.002;
                        buffer_save_index++;
                        buffer_save_index %= 500;
                        if (buffer_save_index == 499)
                        {
                            save_data_buffer_all.Add(save_data_buffer);
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
                    }
                   
                }





            }

        }
        public void button_save_ecg()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-" + DateTime.Now.Year.ToString("0000") + DateTime.Now.Month.ToString("00") + DateTime.Now.Day.ToString("00") + "-" + DateTime.Now.Hour.ToString("00") + "时" + DateTime.Now.Minute.ToString("00") + "分" + DateTime.Now.Second.ToString("00") + "秒";
            saveFileDialog.FileName = date + ".edf";
            saveFileDialog.DefaultExt = "edf";
            saveFileDialog.Filter = "EDF 文件 (*.edf)|*.edf|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;

                // 初始化EDF文件
                WriteEdf_File_multifile(0, filePath, 8, 500);

                unsafe
                {
                    for (int i = 0; i < save_data_buffer_all.Count; i++)
                    {
                        for (int k = 0; k < 500; k++)
                        {
                            fixed (double* p = save_data_buffer_all[i][k])
                            {

                                if (0 != WriteEdf_WriteData_multifile(0, p))
                                {
                                    // 写文件失败，可以在这里添加错误处理逻辑
                                    LogHelper.WriteErrorLog("原始数据写入文件失败！");
                                    NlogHelper.WriteErrorLog("原始数据写入文件失败！");
                                }
                            }
                        }
                    }
                    LogHelper.WriteInfoLog("原始数据保存成功");
                    NlogHelper.WriteInfoLog("原始数据保存成功");
                }

            }

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
        // 5点中值，实时更新：把当前样本写入环形缓冲，返回中值
        double Median5_Update(int ch, double x)
        {
            var buf = medBuf[ch];
            buf[medIdx[ch]] = x;
            medIdx[ch] = (medIdx[ch] + 1) % 5;
            if (medCount[ch] < 5) medCount[ch]++;

            // 拷贝已填元素并求中值
            int n = medCount[ch];
            if (n == 1) return x; // 初始化前期
            double[] w = new double[n];
            Array.Copy(buf, w, n);
            Array.Sort(w, 0, n);
            return w[n / 2];
        }

        //private void btn_sincefilt_Click(object sender, RoutedEventArgs e)
        //{
        //    freq1.Text = "50";
        //    freq2.Text = "150";


        //    SinceWave sinceWave1 = new SinceWave(Convert.ToDouble(freq1.Text));
        //    SinceWave sinceWave2 = new SinceWave(Convert.ToDouble(freq2.Text));
        //    double[] waveform1 = sinceWave1.GenerateWaveform(1000);
        //    double[] waveform2 = sinceWave2.GenerateWaveform(1000);

        //    double[] waveform = waveform1.Zip(waveform2, (a, b) => a + b).ToArray();
        //    //List<Double> doubles = new List<Double>();
        //    //doubles.AddRange(waveform1);
        //    //doubles.AddRange(waveform2);

        //    //var filter = new NWaves.Filters.Butterworth.BandPassFilter (0.3,100,4);

        //    var onlineFirFilter = OnlineFirFilter.CreateBandpass(ImpulseResponse.Finite, 1000, 0.5, 100, 1024);
        //    double[] fildata= onlineFirFilter.ProcessSamples(waveform);

        //    int taps = 1024;
        //    int gd = taps / 2;
        //    double[] xAligned = waveform.Skip(gd).ToArray();
        //    double[] yAligned = fildata.Skip(gd).ToArray();

        //    double GoertzelAmp(double[] s, int fs, double f)
        //    {
        //        int N = s.Length;
        //        double k = Math.Round(f * N / fs);
        //        double w = 2 * Math.PI * k / N, cw = Math.Cos(w), sw = Math.Sin(w), coeff = 2 * cw;
        //        double s0 = 0, s1 = 0, s2 = 0;
        //        for (int n = 0; n < N; n++) { s0 = s[n] + coeff * s1 - s2; s2 = s1; s1 = s0; }
        //        double re = s1 - s2 * cw, im = s2 * sw;
        //        return 2.0 * Math.Sqrt(re * re + im * im) / N;
        //    }
        //    int Fs = 1000;
        //    double A50_in = GoertzelAmp(xAligned, Fs, 50);
        //    double A50_out = GoertzelAmp(yAligned, Fs, 50);
        //    double A150_in = GoertzelAmp(xAligned, Fs, 150);
        //    double A150_out = GoertzelAmp(yAligned, Fs, 150);

        //    double delta50_dB = 20 * Math.Log10((A50_out + 1e-12) / (A50_in + 1e-12));
        //    double att150_dB = 20 * Math.Log10((A150_out + 1e-12) / (A150_in + 1e-12));
        //    Console.WriteLine($"50Hz 增益: {delta50_dB:F2} dB (≈0 dB 为理想)");
        //    Console.WriteLine($"150Hz 衰减: {att150_dB:F2} dB (越负越好)");



        //    //double[] filteredWaveform = lowPassFilter.ProcessSignalFull(waveform);
        //    //Console.WriteLine(filteredWaveform.Length);
        //    for (int i = 0; i < fildata.Length; i++)
        //    {

        //        lineData[0].Append(i, fildata[i]);
        //    }
        //}

        //private void btn_clear_Click(object sender, RoutedEventArgs e)
        //{
        //    lineData[0].Clear();
        //}
    }

    // Biquad 50 Hz Notch，RBJ cookbook 公式实现
    // 陷波滤波器
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
    // 二阶低通（RBJ Cookbook），Butterworth 用 Q=1/√2
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
