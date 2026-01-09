using Accord.Math;
using Accord.Statistics;
using Collect.Power;
using Microsoft.Win32;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Data.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Collect.Plot
{
    /// <summary>
    /// EEG_Pro.xaml 的交互逻辑
    /// </summary>
    public partial class EEG_Pro : UserControl
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

        readonly double[][] yy = new double[8][];//滤波前
        readonly double[][] yy1 = new double[8][];//反向滤波
        readonly double[][] yy2 = new double[8][];//正向滤波

        const int NL = 5;
        double[] BB = new double[5] { 0.00235720877285232, 0, -0.00471441754570465, 0, 0.00235720877285232 }; //0.002357, 0, -0.004714, 0, 0.002357 
        const int DL = 5;
        double[] AA = new double[5] { 1, -3.84341850795650, 5.55536743797448, -3.57936752958791, 0.867472133791667 };
        double[] buffer_in = new double[5];
        double[] buffer_out = new double[5];
        private XyDataSeries<double, double>[] lineData;
        public bool IsStm = false;
        
        // 频谱图相关
        private XyDataSeries<double, double>[] spectrumLineData;
        private Dictionary<int, PowerSpectralDensityAnalyzer.PSDResult> spectrumResults;
        private bool showSpectrum = false;

        //public void TCPWriteByte()
        //{
        //    eeg.client.WriteData("192.168.4.1", 4321);
        //}
        private double iir(double din)
        {
            for (int i = 0; i < NL - 1; i++)
                buffer_in[4 - i] = buffer_in[3 - i];
            buffer_in[0] = din;
            double value = 0;

            for (int i = 0; i < NL; i++)
                value += buffer_in[i] * BB[i];
            for (int i = 1; i < DL; i++)
                value -= buffer_out[i - 1] * AA[i];

            for (int i = 0; i < DL - 1; i++)
                buffer_out[4 - i] = buffer_out[3 - i];
            buffer_out[0] = value;

            return value;
        }
        BackgroundWorker backgroundWorker1 = new BackgroundWorker();
        public EEG_Pro(EEG eEG)
        {

            this.eeg = eEG;


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
            for (int i = 0; i < 8; i++)
            {
                yy[i] = new double[500];
            }
            for (int i = 0; i < 8; i++)
            {
                yy1[i] = new double[50];
                yy2[i] = new double[50];
            }
            for (int i = 0; i < 50; i++)
            {
                //50行8列
                save_data_buffer[i] = new double[8];
            }
            var xAxis = new NumericAxis() { AxisTitle = "Frequency (Hz)",  };
            var yAxis = new NumericAxis() { AxisTitle = "Amplitude（dB)", Visibility = Visibility.Visible};

            sciChartSurface.XAxis = xAxis;
            sciChartSurface.YAxis = yAxis;
            //// 创建 XyDataSeries 来托管图表的数据
            //lineData = new XyDataSeries<double, double>[8];
            //for (int i = 0; i < 8; i++)
            //{
            //    //据系列可以存储的最大数据点数为5000，先进先出（FIFO）
            //    lineData[i] = new XyDataSeries<double, double>() { FifoCapacity = 5000 };
            //    lineData[i].AcceptsUnsortedData = true;
            //}

            //var colors = new[]
            //{
            //    Colors.Red,
            //    Colors.Orange,
            //    Colors.Cyan,
            //    Colors.Green,
            //    Colors.Blue,
            //    Colors.Orchid,
            //    Colors.Purple,
            //    Colors.Brown,
            //};
            //// 添加8条曲线
            //for (int i = 0; i < 8; i++)
            //{
            //    var lineSeries = new FastLineRenderableSeries()
            //    {
            //        Stroke = colors[i],
            //        StrokeThickness = 1,
            //        AntiAliasing = true,
            //    };
            //    sciChartSurface.RenderableSeries.Add(lineSeries);

            //}
            //// 将数据分配给8条曲线
            //for (int i = 0; i < 8; i++)
            //{
            //    sciChartSurface.RenderableSeries[i].DataSeries = lineData[i];
            //}
            
            // 初始化频谱图相关
            InitializeSpectrumChart();
            spectrumResults = new Dictionary<int, PowerSpectralDensityAnalyzer.PSDResult>();

            //InitializeComponent();
            eeg.channel_1.Checked += channel_1_Checked;
            eeg.channel_1.Unchecked += channel_1_Unchecked;
            eeg.channel_2.Checked += channel_2_Checked;
            eeg.channel_2.Unchecked += channel_2_Unchecked;
            eeg.channel_3.Checked += channel_3_Checked;
            eeg.channel_3.Unchecked += channel_3_Unchecked;
            eeg.channel_4.Checked += channel_4_Checked;
            eeg.channel_4.Unchecked += channel_4_Unchecked;
            eeg.channel_5.Checked += channel_5_Checked;
            eeg.channel_5.Unchecked += channel_5_Unchecked;
            eeg.channel_6.Checked += channel_6_Checked;
            eeg.channel_6.Unchecked += channel_6_Unchecked;
            eeg.channel_7.Checked += channel_7_Checked;
            eeg.channel_7.Unchecked += channel_7_Unchecked;
            eeg.channel_8.Checked += channel_8_Checked;
            eeg.channel_8.Unchecked += channel_8_Unchecked;
            channel_1.IsChecked = true;
            channel_2.IsChecked = true;
            channel_3.IsChecked = true;
            channel_4.IsChecked = true;
            channel_5.IsChecked = true;
            channel_6.IsChecked = true;
            channel_7.IsChecked = true;
            channel_8.IsChecked = true;
           
        }



        double coryy = 0;
        double y0 = 0;
        double y = 0;

        short[] sdata_all = new short[8];

        private double g_index;
        private double g_scale;
        int g_old_ecg_time = 0;

        private static DoubleRange ComputeXAxisRange(double t)
        {
            if (t < WindowSize)
            {
                return new DoubleRange(0, WindowSize);
            }
            //t 值向上取整到最接近的整数
            return new DoubleRange(Math.Ceiling(t) - WindowSize + 5, Math.Ceiling(t) + 5);
        }

        private static double WindowSize = 10;
        private EEG eeg;

        //private void button_install_ecg_Click(object sender, RoutedEventArgs e)
        //{
        //    var button = sender as Button;
        //    var content = button.Content.ToString();
        //    if (content == "开始")
        //    {

        //        button.Content = "停止";

        //    }
        //    else
        //    {
        //        eeg.Ecg_ProEvent -= EEG_Ecg_ProEvent;
        //        button.Content = "开始";
        //        WriteEdf_Finish_multifile(0);
        //    }
        //}
        int yy_index = 0;

        double[][] save_data_buffer = new double[50][];
        List<double[][]> save_data_buffer_all = new List<double[][]>();
        private double[] pha;
        private int flagsti;
        private int NextPowerOfTwo(int length)
        {
            return (int)Math.Pow(2, Math.Ceiling(Math.Log(length, 2)));
        }
        private double[] FHTHilbert(double[] data)
        {
            int nextPowerOfTwo = NextPowerOfTwo(data.Length);

            double[] adjustedData = new double[nextPowerOfTwo];

            Array.Copy(data, adjustedData, data.Length);
            double[] pha = new double[nextPowerOfTwo];//complex[549].Phase;
                                                      // Forward operation
                                                      // Copy the input to a complex array which can be processed
                                                      //  in the complex domain by the FFT
            Complex[] cdata = new Complex[nextPowerOfTwo];
            for (int i = 0; i < nextPowerOfTwo; i++)
                cdata[i] = new Complex(adjustedData[i], 0.0);

            // Perform FFT
            FourierTransform.FFT(cdata, FourierTransform.Direction.Forward);
            TransformArray(cdata);

            // Reverse the FFT
            FourierTransform.FFT(cdata, FourierTransform.Direction.Backward);

            // Convert back to our initial double array
            for (int i = 0; i < nextPowerOfTwo; i++)
                pha[i] = cdata[i].Phase;
            //data[i] = cdata[i].Imaginary;

            return (pha);

        }
        private static Complex[] TransformArray(Complex[] array)
        {
            Complex[] array1 = array;
            int N = array.Length;
            int N2 = N / 2;
            for (int i = 1; i < N2; i++)
            {
                array1[i] *= 2.0;
            }
            for (int i = N2 + 1; i < N; i++)
            {
                array1[i] = Complex.Zero;
            }
            return (array1);
        }
        private int stimulation(double a, double b)
        {

            if (rangephase(a, b))//检测相位
            {
                flagsti = 1;
            }
            else
            {
                flagsti = 0;
            }
            return flagsti;
        }
        private bool rangephase(double a, double b)
        {
            bool rp;

            if (0 % 2 == 0)//波峰
            {
                rp = (a > -0.8) & (a < 0) & b > 2;

            }


            return rp;
        }
        

        public void button_save_fil_ecg()
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "保存文件";
            string date = "Record-" + "Filter-" + DateTime.Now.Year.ToString("0000") + DateTime.Now.Month.ToString("00") + DateTime.Now.Day.ToString("00") + "-" + DateTime.Now.Hour.ToString("00") + "时" + DateTime.Now.Minute.ToString("00") + "分" + DateTime.Now.Second.ToString("00") + "秒";
            saveFileDialog.FileName = date + ".edf";
            saveFileDialog.DefaultExt = "edf";
            saveFileDialog.Filter = "EDF 文件 (*.edf)|*.edf|所有文件 (*.*)|*.*";

            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;

                // 初始化EDF文件
                WriteEdf_File_multifile(0, filePath, 8 + 1, 50);

                unsafe
                {
                    for (int i = 0; i < save_data_buffer_all.Count; i++)
                    {
                        for (int k = 0; k < 50; k++)
                        {
                            fixed (double* p = save_data_buffer_all[i][k])
                            {

                                if (0 != WriteEdf_WriteData_multifile(0, p))
                                {
                                    // 写文件失败，可以在这里添加错误处理逻辑
                                    LogHelper.WriteErrorLog("滤波数据写入文件失败！");
                                    NlogHelper.WriteErrorLog("滤波数据写入文件失败！");
                                }
                            }
                        }

                    }
                    LogHelper.WriteInfoLog("滤波数据保存成功");
                    NlogHelper.WriteInfoLog("滤波数据保存成功");
                }

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

        #region 频谱图相关方法

        /// <summary>
        /// 初始化频谱图
        /// </summary>
        private void InitializeSpectrumChart()
        {
            try
            {
                // 创建频谱图数据系列
                spectrumLineData = new XyDataSeries<double, double>[8];
                for (int i = 0; i < 8; i++)
                {
                    spectrumLineData[i] = new XyDataSeries<double, double>() { FifoCapacity = 512 };
                    spectrumLineData[i].AcceptsUnsortedData = true;
                }
                var colors = new[]
            {
                Colors.Red,
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
                    sciChartSurface.RenderableSeries[i].DataSeries = spectrumLineData[i];
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"初始化频谱图失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 频谱分析事件处理
        /// </summary>
        private void EEG_SpectrumAnalysisEvent(object sender, SpectrumAnalysisEventArgs1 e)
        {
            try
            {
                // 保存频谱分析结果
                var psdResult = new PowerSpectralDensityAnalyzer.PSDResult
                {
                    Frequencies = e.Frequencies,
                    PowerSpectralDensity = e.PowerSpectralDensity,
                    PowerSpectralDensityDB = e.PowerSpectralDensity.Select(p => 10 * Math.Log10(p)).ToArray(),
                    SmoothedPSD = e.SmoothedPSD,
                    AbsolutePower = e.AbsolutePower,
                    RelativePower = e.RelativePower,
                    TotalPower = e.TotalPower
                };

                spectrumResults[e.ChannelIndex] = psdResult;

                // 更新频谱图显示
                UpdateSpectrumChart(e.ChannelIndex, psdResult);

                // 输出频谱分析信息
                Console.WriteLine($"通道{e.ChannelIndex + 1}频谱分析完成:");
                Console.WriteLine($"  总功率: {e.TotalPower:F4}");
                Console.WriteLine($"  Delta功率: {e.AbsolutePower[PowerSpectralDensityAnalyzer.FrequencyBand.Delta]:F4}");
                Console.WriteLine($"  Theta功率: {e.AbsolutePower[PowerSpectralDensityAnalyzer.FrequencyBand.Theta]:F4}");
                Console.WriteLine($"  Alpha功率: {e.AbsolutePower[PowerSpectralDensityAnalyzer.FrequencyBand.Alpha]:F4}");
                Console.WriteLine($"  Beta功率: {e.AbsolutePower[PowerSpectralDensityAnalyzer.FrequencyBand.Beta]:F4}");
                Console.WriteLine($"  LGamma功率: {e.AbsolutePower[PowerSpectralDensityAnalyzer.FrequencyBand.LGamma]:F4}");
                Console.WriteLine($"  HGamma功率: {e.AbsolutePower[PowerSpectralDensityAnalyzer.FrequencyBand.HGamma]:F4}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理频谱分析事件失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新频谱图显示
        /// </summary>
        private void UpdateSpectrumChart(int channelIndex, PowerSpectralDensityAnalyzer.PSDResult psdResult)
        {
            try
            {
                if (showSpectrum && channelIndex >= 0 && channelIndex < 8)
                {
                    // 清除旧数据
                    spectrumLineData[channelIndex].Clear();

                    // 添加新的频谱数据（只显示前100Hz）
                    int maxIndex = Math.Min(100, psdResult.SmoothedPSD.Length);
                    for (int i = 0; i < maxIndex; i++)
                    {
                        spectrumLineData[channelIndex].Append(psdResult.Frequencies[i], psdResult.SmoothedPSD[i]);
                    }

                    // 更新图表
                    this.Dispatcher.BeginInvoke((Action)delegate ()
                    {
                        if (sciChartSurface.RenderableSeries.Count > channelIndex)
                        {
                            sciChartSurface.RenderableSeries[channelIndex].DataSeries = spectrumLineData[channelIndex];
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新频谱图失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换频谱图显示模式
        /// </summary>
        public void ToggleSpectrumMode()
        {
            showSpectrum = !showSpectrum;
            
            if (showSpectrum)
            {
                // 切换到频谱图模式
                SwitchToSpectrumMode();
            }
            else
            {
                // 切换到时域图模式
                SwitchToTimeDomainMode();
            }
        }

        /// <summary>
        /// 切换到频谱图模式
        /// </summary>
        private void SwitchToSpectrumMode()
        {
            try
            {
                // 更新坐标轴标题
                this.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    sciChartSurface.XAxis.AxisTitle = "Frequency (Hz)";
                    sciChartSurface.YAxis.AxisTitle = "Power Spectral Density (dB)";
                    sciChartSurface.XAxis.VisibleRange = new DoubleRange(0, 100);
                    sciChartSurface.YAxis.VisibleRange = new DoubleRange(-100, 50);
                });

                // 显示最新的频谱数据
                foreach (var result in spectrumResults)
                {
                    UpdateSpectrumChart(result.Key, result.Value);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"切换到频谱图模式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换到时域图模式
        /// </summary>
        private void SwitchToTimeDomainMode()
        {
            try
            {
                // 恢复时域图显示
                this.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    sciChartSurface.XAxis.AxisTitle = "Time (second)";
                    sciChartSurface.YAxis.AxisTitle = "Value";
                    sciChartSurface.XAxis.VisibleRange = new DoubleRange(0, 10);
                    sciChartSurface.YAxis.VisibleRange = new DoubleRange(-10000, 10000);
                });

                // 恢复时域数据系列
                for (int i = 0; i < 8; i++)
                {
                    if (sciChartSurface.RenderableSeries.Count > i)
                    {
                        sciChartSurface.RenderableSeries[i].DataSeries = lineData[i];
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"切换到时域图模式失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取指定通道的频谱分析结果
        /// </summary>
        public PowerSpectralDensityAnalyzer.PSDResult GetSpectrumResult(int channelIndex)
        {
            if (spectrumResults.ContainsKey(channelIndex))
            {
                return spectrumResults[channelIndex];
            }
            return null;
        }

        /// <summary>
        /// 获取所有通道的频谱分析结果
        /// </summary>
        public Dictionary<int, PowerSpectralDensityAnalyzer.PSDResult> GetAllSpectrumResults()
        {
            return new Dictionary<int, PowerSpectralDensityAnalyzer.PSDResult>(spectrumResults);
        }

        /// <summary>
        /// 保存频谱分析结果
        /// </summary>
        public void SaveSpectrumResults(string filePath)
        {
            try
            {
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(filePath))
                {
                    writer.WriteLine("通道,频段,绝对功率,相对功率(%)");
                    
                    foreach (var result in spectrumResults)
                    {
                        int channelIndex = result.Key;
                        var psdResult = result.Value;
                        
                        foreach (var power in psdResult.AbsolutePower)
                        {
                            double relativePower = psdResult.RelativePower[power.Key];
                            writer.WriteLine($"{channelIndex + 1},{power.Key},{power.Value:F4},{relativePower:F2}");
                        }
                    }
                }
                
                Console.WriteLine($"频谱分析结果已保存到: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存频谱分析结果失败: {ex.Message}");
            }
        }

        #endregion
    }
}
