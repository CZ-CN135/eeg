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
        public double[] firCoefficients;
        public FIRFilter filter;
        public double LLMin = 0;
        public double LLMax = 1;
        public double RMSMin = 0;
        public double RMSMax = 10;
        public double Alpha = 0.2;
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
            
        }
        public float[] eeg_data_float= new float[8];
        public double[] eeg_data = new double[8];
        private double g_index;
      
        public bool clear_original_filter_txt_flag=false;          
        private static int WindowSize = 10;  
   
        double[][] save_data_buffer = new double[500][];         

        /// <summary>
        /// 离线处理数据
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        //采样率
        static double sampleRate = 1000;

        static double hpCut = 0.5;   // 高通截止 0.5 Hz
        static double hpA = Math.Exp(-2.0 * Math.PI * hpCut / sampleRate);

        static double lpCut = 40.0;  // 低通截止 40 Hz
        // 4阶 Butterworth 两段二阶的 Q（固定值）
        static double lpQ1 = 0.5411961;
        static double lpQ2 = 1.3065630;

        static double notchF0 = 50.0;
        static double notchQ = 25.0;  // 约等于 BW=2Hz 的量级（可按需要微调）

        // ===== 状态（8通道）=====
        double[] hp1_prevX = new double[8];
        double[] hp1_prevY = new double[8];
        double[] hp2_prevX = new double[8];
        double[] hp2_prevY = new double[8];

        double[][] medBuf = Enumerable.Range(0, 8).Select(_ => new double[5]).ToArray();
        int[] medCount = new int[8];
        int[] medIdx = new int[8];

        BiquadLPF[] lpf1= Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ1)).ToArray();
        BiquadLPF[] lpf2= Enumerable.Range(0, 8).Select(_ => new BiquadLPF(sampleRate, lpCut, lpQ2)).ToArray();
        BiquadNotch[] notch1;

       
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

            notch1 = Enumerable.Range(0, chCount).Select(_ => new BiquadNotch(sampleRate, notchF0, notchQ)).ToArray();

            g_index_1 = 0;
        }
        /// <summary>
        /// 离线癫痫
        /// </summary>
        /// <param name="Freqtextbox_filter"></param>
        /// <returns></returns>
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

                AlphaRelativeThreshold=Alpha,//带功率比阈值
                MinChannelsToTrigger = 2
            });
            _stage2.Start();
            int index = 1;
            _detector.OnWindowEvaluated += (s, e) =>
            {

                //NlogHelper.WriteInfoLog($"Stage1 评估窗口：{e.WindowStartSample} - {e.WindowEndSample},通道{1},RMS={e.RmsPerChannel[0]},LL={e.LlPerChannel[0]},计算次数={index}");
                NlogHelper.WriteInfoLog(e.RmsPerChannel[7].ToString());
                NlogHelper.WriteInfoLog(LLMin.ToString()+','+LLMax.ToString()+ ',' + RMSMin.ToString()+ ',' + RMSMax.ToString());
                //NlogHelper.WriteInfoLog(e.LlPerChannel[7].ToString());


            };
            // 关键：Stage1 命中后，把 600ms 数据块送入 Stage2 队列（Stage2 自己线程算）
            _detector.OnStage2WindowReady += (s, e) =>
            {
                _stage2.PushWindow(e.Window, e.WindowStartSample, e.WindowEndSample);
                //NlogHelper.WriteInfoLog($"Stage1 触发，送入 Stage2 窗口：{e.WindowStartSample} - {e.WindowEndSample}");
            };
            _stage2.OnStage2Evaluated += (s, e) =>
            {
                //NlogHelper.WriteInfoLog($"送入 Stage2 窗口：{e.WindowStartSample} - {e.WindowEndSample},通道1，带功率比={e.RatioPerChannel[0]}");
            };
            // Stage2 触发事件（注意：这是在 Stage2 线程里触发，UI要Dispatcher）
            _stage2.OnStage2Triggered += (s, e) =>
            {
                NlogHelper.WriteInfoLog($"Stage2 触发！有癫痫 窗口：{e.WindowStartSample} - {e.WindowEndSample}，满足通道数：{e.PassedChannels}");
                // 这里做“最终确认触发”
                // 如要更新UI，请 Dispatcher.BeginInvoke(...)
            };

        }
        private bool _detectorInited = false;
        public  double[][] LoadExcelAs2DArray(string Freqtextbox_filter)
        {
            // ① 打开文件选择框
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "选择 Excel 文件",
                Filter = "Excel 文件 (*.xlsx)|*.xlsx",
                Multiselect = false
            };

            if (ofd.ShowDialog() != true)
                return null;

            string filePath = ofd.FileName;

            // ② 设置 EPPlus 许可
            ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

            var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = package.Workbook.Worksheets[0]; // 第一个 Sheet

            int colCount = worksheet.Dimension.Columns;    // 列数 = 通道数
            int rowCount = worksheet.Dimension.Rows; // 行数 = 数据点数

            ResetFilterState(colCount);
            // ③ 创建二维数组 [通道, 数据点]
            double[,] data = new double[colCount, rowCount];

            for (int col = 1; col <= colCount; col++)
            {
                for (int row = 1; row <= rowCount; row++)
                {
                    object cellValue = worksheet.Cells[row, col].Value;

                    // ⚠️ 防止空单元格 / 非数字
                    if (cellValue == null)
                        data[col - 1, row - 1] = 0;
                    else
                        data[col - 1, row - 1] = Convert.ToDouble(cellValue);
                }
            }
            double[][] eeg_data_buffer = new double[colCount][];
            if (!_detectorInited)
            {
                double fs = sampleRate; // 或从数据包/设备配置解析
                InitAfterFsKnown(fs);
                _detectorInited = true;
            }
            for (int i=0; i < rowCount; i++)
            {
                for(int j=0; j < colCount; j++)
                {
                    eeg_data_float[j] = (float)(data[j, i]);
                }
                if (_detector != null)
                {
                    double[] frame = new double[8];
                    for (int ch = 0; ch < 8; ch++)
                        frame[ch] = eeg_data_float[ch];   // 或者用你滤波后的 filterdata（更稳）
                    _detector.PushFrame(frame);
                }
            }
            for (int i = 0; i < colCount; i++)
            {
                eeg_data_buffer[i] = new double[rowCount];
                for (int m=0; m < rowCount-1; m++)
                {
                    double temp = (Convert.ToDouble(data[i,m]));

                    // ① 前置：5点中值去尖峰（防振铃）
                    double med = Median5_Update(i, temp);
                    double x = (Math.Abs(temp - med) > 800) ? med : temp;
                    // --- 第1级 一阶高通：去基线漂移 ---
                    double yhp1 = hpA * (hp1_prevY[i] + x - hp1_prevX[i]);
                    hp1_prevX[i] = x;
                    hp1_prevY[i] = yhp1;

                    // --- 第2级 一阶高通：进一步增强滚降 ---
                    double yhp2 = hpA * (hp2_prevY[i] + yhp1 - hp2_prevX[i]);
                    hp2_prevX[i] = yhp1;
                    hp2_prevY[i] = yhp2;

                    // --- 50 Hz 双级陷波1级 ---
                    double y1 = notch1[i].Process(yhp2);
                    //double y2 = notch2[i].Process(y1);


                    //---新增：40 Hz 低通（两级，等效4阶） ---
                    double ylp1 = lpf1[i].Process(y1);
                    double ylp2 = lpf2[i].Process(ylp1);
                    //double ylp3 = lpf3[i].Process(ylp2);

                    eeg_data_buffer[i][m] = ylp2;
                    lineData[i].Append(g_index, eeg_data_buffer[i][m] - i * 10000);
                    g_index += 1.0/ Convert.ToDouble(Freqtextbox_filter);
                }
                g_index = 0;

            }
            NlogHelper.WriteInfoLog($"已处理8通道每通道{{colCount}}个数据");
            return eeg_data_buffer;
        }
        public void save_offline(double[][] eeg_data_buffer)
        {
            if (eeg_data_buffer == null || eeg_data_buffer.Length == 0)
                throw new Exception("eeg_data_buffer 为空");

            for (int ch = 0; ch < eeg_data_buffer.Length; ch++)
            {
                if (eeg_data_buffer[ch] == null)
                    throw new Exception($"eeg_data_buffer[{ch}] 未初始化");
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Title = "保存文件",
                FileName = "Record-" + DateTime.Now.ToString("yyyyMMdd-HH时mm分ss秒") + ".xlsx",
                DefaultExt = "xlsx",
                Filter = "Excel 文件 (*.xlsx)|*.xlsx"
            };

            if (saveFileDialog.ShowDialog() != true)
                return;

            string filePath = saveFileDialog.FileName;
            ExcelPackage.License.SetNonCommercialOrganization("My Noncommercial organization");

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var worksheet = package.Workbook.Worksheets.Add("EEG Data");

                int channelCount = eeg_data_buffer.Length;
                int sampleCount = eeg_data_buffer.Min(ch => ch.Length);

                object[][] allData = new object[sampleCount + 1][];

                // 表头
                allData[0] = new object[channelCount];
                for (int ch = 0; ch < channelCount; ch++)
                    allData[0][ch] = $"Ch{ch + 1}";

                // 数据
                for (int t = 0; t < sampleCount; t++)
                {
                    allData[t + 1] = new object[channelCount];
                    for (int ch = 0; ch < channelCount; ch++)
                    {
                        allData[t + 1][ch] = eeg_data_buffer[ch][t];
                    }
                }

                worksheet.Cells[1, 1].LoadFromArrays(allData);
                package.Save();
            }

            LogHelper.WriteInfoLog("EEG 数据保存成功");
            NlogHelper.WriteInfoLog("EEG 数据保存成功");
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
        // ===== 5点中值（前置）=====
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
       
        private void Button_Click(object sender, RoutedEventArgs e)
        {
        }
        double g_index_2 = 0;
        private void Button_Click_1(object sender, RoutedEventArgs e)
        {

           
        }

     
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
