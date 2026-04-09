using Collect.Helper;
using Collect.Plot;
using NLog;
using NLog.Config;
using System;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xceed.Wpf.AvalonDock.Layout;

namespace Collect
{

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>

    public partial class MainWindow : Window
    {
        private Logger logger = null;
        public Plot.EEG eeg;

        Plot.EEG_Filter eeg_filter;
        

        public MainWindow()
        {
            InitializeComponent();
            meua.IsVisible = true;
            actionRegion.IsVisible = true;
            logRegion.IsVisible = true;
            NlogHelper.ConfigureNLogForRichTextBox();
            eeg=new Plot.EEG();
            
            eeg_filter = new Plot.EEG_Filter(eeg);

            //AP
            //APstackpanel
            stackpanel1 = new StackPanel();
            stackpanel1.Orientation = Orientation.Vertical;

            //IP地址textblock
            stackpanel1.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "IP地址"
            });
            //IP地址textbox
            Iptextbox = new System.Windows.Controls.TextBox();
            Iptextbox.Text = "192.168.4.1";
            Iptextbox.FontSize = 15;
            stackpanel1.Children.Add(Iptextbox);

            //端口号textblock
            stackpanel1.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "端口号"
            });
            //端口号textbox
            Porttextbox = new System.Windows.Controls.TextBox();
            Porttextbox.Text = "4321";
            Porttextbox.FontSize = 15;
            stackpanel1.Children.Add(Porttextbox);

            //PGATextBlock
            stackpanel1.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "PGA"
            });
            //PGATextBox
            PGAbox = new System.Windows.Controls.TextBox();
            PGAbox.Text = "24";
            PGAbox.FontSize = 15;
            stackpanel1.Children.Add(PGAbox);

            //滤波采样率TextBlock
            stackpanel1.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "滤波采样率"
            });
            //滤波采样率TextBox
            fcbox = new System.Windows.Controls.TextBox();
            fcbox.Text = "1000";
            fcbox.FontSize = 15;
            stackpanel1.Children.Add(fcbox);

            //AP开始按钮
            Button btn_tcp = new System.Windows.Controls.Button();
            btn_tcp.Content = "开始";
            btn_tcp.FontSize = 15;
            btn_tcp.Margin = new Thickness(0, 10, 0, 0);
            btn_tcp.Click += Btn_tcp_Click;

            
            //去除尖峰按钮
            Button btn_clear_peak = new System.Windows.Controls.Button();
            btn_clear_peak.Content = "开始去除尖峰";
            btn_clear_peak.FontSize = 15;
            btn_clear_peak.Margin = new Thickness(0, 10, 0, 0);
            btn_clear_peak.Click += Btn_clear_peak_Click;

            //保存滤波ns2数据按钮
            Button btn_save_filter_ns2 = new System.Windows.Controls.Button();
            btn_save_filter_ns2.Content = "保存滤波ns2数据";
            btn_save_filter_ns2.FontSize = 15;
            btn_save_filter_ns2.Margin = new Thickness(0, 10, 0, 0);
            btn_save_filter_ns2.Click += Btn_save_filter_filter_Click;
            //保存滤波Excel数据按钮
            Button btn_save_filter_excel = new System.Windows.Controls.Button();
            btn_save_filter_excel.Content = "保存滤波Excel数据";
            btn_save_filter_excel.FontSize = 15;
            btn_save_filter_excel.Margin = new Thickness(0, 10, 0, 0);
            btn_save_filter_excel.Click += Btn_save_filter_excel_Click;
            //保存原始ns2数据按钮
            Button btn_save_original_ns2 = new System.Windows.Controls.Button();
            btn_save_original_ns2.Content = "保存原始ns2数据";
            btn_save_original_ns2.FontSize = 15;
            btn_save_original_ns2.Margin = new Thickness(0, 10, 0, 0);
            btn_save_original_ns2.Click += Btn_save_original_ns2_Click;
            //保存原始Excel数据按钮
            Button btn_save_original_excel = new System.Windows.Controls.Button();
            btn_save_original_excel.Content = "保原始Excel数据";
            btn_save_original_excel.FontSize = 15;
            btn_save_original_excel.Margin = new Thickness(0, 10, 0, 0);
            btn_save_original_excel.Click += Btn_save_original_excel_Click;
            //清除按钮
            Button btn_clear = new System.Windows.Controls.Button();
            btn_clear.Content = "清除曲线";
            btn_clear.FontSize = 15;
            btn_clear.Margin = new Thickness(0, 10, 0, 0);
            btn_clear.Click += btn_clear_Click;

            Button btn_clear_log = new System.Windows.Controls.Button();
            btn_clear_log.Content = "清除日志";
            btn_clear_log.FontSize = 15;
            btn_clear_log.Margin = new Thickness(0, 10, 0, 0);
            btn_clear_log.Click += Btn_clear_log_Click;

            //添加控件到APstackpanel
            stackpanel1.Children.Add(btn_tcp);
            stackpanel1.Children.Add(btn_clear_peak);
            stackpanel1.Children.Add(btn_save_filter_ns2);
            stackpanel1.Children.Add(btn_save_filter_excel);
            stackpanel1.Children.Add(btn_save_original_ns2);
            stackpanel1.Children.Add(btn_save_original_excel);
            stackpanel1.Children.Add(btn_clear);
            stackpanel1.Children.Add(btn_clear_log);



            //Offline
            //OffLine stackpanel
            stackpanel4 = new StackPanel();
            stackpanel4.Orientation = Orientation.Vertical;

            //采样频率textblock
            stackpanel4.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "采样频率(Hz)"
            });
            //采样频率textbox
            Freqtextbox_filter = new System.Windows.Controls.TextBox();
            Freqtextbox_filter.Text = "1000";
            Freqtextbox_filter.FontSize = 15;
            stackpanel4.Children.Add(Freqtextbox_filter);

            //读取xlsx数据
            btn_filter = new System.Windows.Controls.Button();
            btn_filter.Content = "读取xlsx数据";
            btn_filter.FontSize = 15;
            btn_filter.Margin = new Thickness(0, 10, 0, 0);
            btn_filter.Click += Btn_filter_Click;
            stackpanel4.Children.Add(btn_filter);

            //读取ns2数据
            btn_offline_ns2 = new System.Windows.Controls.Button();
            btn_offline_ns2.Content = "读取ns2数据";
            btn_offline_ns2.FontSize = 15;
            btn_offline_ns2.Margin= new Thickness(0, 10, 0, 0);
            btn_offline_ns2.Click += Btn_offline_ns2_Click;
            stackpanel4.Children.Add(btn_offline_ns2);

            //去除尖峰按钮
            Button btn_clear_peak_offline = new System.Windows.Controls.Button();
            btn_clear_peak_offline.Content = "开始去除尖峰";
            btn_clear_peak_offline.FontSize = 15;
            btn_clear_peak_offline.Margin = new Thickness(0, 10, 0, 0);
            btn_clear_peak_offline.Click += Btn_clear_peak_offline_Click;
            stackpanel4.Children.Add(btn_clear_peak_offline);
            //保存滤波数据
            btn_save_offline = new System.Windows.Controls.Button();
            btn_save_offline.Content = "保存滤波NS2数据";
            btn_save_offline.FontSize = 15;
            btn_save_offline.Margin = new Thickness(0, 10, 0, 0);
            btn_save_offline.Click += Btn_save_offline_Click; ;
            stackpanel4.Children.Add(btn_save_offline);

            Button btn_clear_log_offline = new System.Windows.Controls.Button();
            btn_clear_log_offline.Content = "清除日志";
            btn_clear_log_offline.FontSize = 15;
            btn_clear_log_offline.Margin = new Thickness(0, 10, 0, 0);
            btn_clear_log_offline.Click += Btn_clear_log_offline_Click;
            stackpanel4.Children.Add(btn_clear_log_offline);

            //ThreShold
            //ThreShold stackpanel
            ThreSholdstackpanel = new StackPanel();
            ThreSholdstackpanel.Orientation = Orientation.Vertical;


            //阈值1
            ThreSholdstackpanel.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "阈值1"
            });
            //阈值1textbox
            ThreShold1textbox = new System.Windows.Controls.TextBox();
            ThreShold1textbox.Text = "0";
            ThreShold1textbox.FontSize = 15;
            ThreSholdstackpanel.Children.Add(ThreShold1textbox);

            //阈值2
            ThreSholdstackpanel.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "阈值2"
            });
            //阈值2textbox
            ThreShold2textbox = new System.Windows.Controls.TextBox();
            ThreShold2textbox.Text = "10000";
            ThreShold2textbox.FontSize = 15;
            ThreSholdstackpanel.Children.Add(ThreShold2textbox);

            //短刺激时长
            ThreSholdstackpanel.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "短刺激不应期"
            });
            //Alpha相对带功率textbox
            MadShortstimtextbox = new System.Windows.Controls.TextBox();
            MadShortstimtextbox.Text = "0.2";
            MadShortstimtextbox.FontSize = 15;
            ThreSholdstackpanel.Children.Add(MadShortstimtextbox);

            //长刺激时长
            ThreSholdstackpanel.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "长刺激不应期"
            });
            //Alpha相对带功率textbox
            MadLongstimtextbox = new System.Windows.Controls.TextBox();
            MadLongstimtextbox.Text = "0.2";
            MadLongstimtextbox.FontSize = 15;
            ThreSholdstackpanel.Children.Add(MadLongstimtextbox);

            Button btn_set_param = new System.Windows.Controls.Button();
            btn_set_param.Content = "确定";
            btn_set_param.FontSize = 15;
            btn_set_param.Margin = new Thickness(0, 10, 0, 0);
            btn_set_param.Click += Btn_set_param_Click; 
            ThreSholdstackpanel.Children.Add(btn_set_param);
        }
        private bool is_set_param = false;
        private void Btn_set_param_Click(object sender, RoutedEventArgs e)
        {
            is_set_param=true;
            NlogHelper.WriteInfoLog($"设置参数：短刺激不应期={MadShortstimtextbox.Text}ms，长刺激不应期={MadLongstimtextbox.Text}ms，阈值1={ThreShold1textbox.Text}，阈值2={ThreShold2textbox.Text}");
        }

        private void Btn_clear_log_offline_Click(object sender, RoutedEventArgs e)
        {
            LogRichTextBox.Document.Blocks.Clear();
        }

        private void Btn_clear_log_Click(object sender, RoutedEventArgs e)
        {
            LogRichTextBox.Document.Blocks.Clear();
        }

        private async void Btn_save_offline_Click(object sender, RoutedEventArgs e)
        {
            await eeg_filter.save_offline();
        
        }

        private void Btn_clear_original_filter_txt_Click(object sender, RoutedEventArgs e)
        {
            eeg_filter.clear_original_filter_txt_flag=true;
        }
        double[][] xlsxdata;
        double[][] ns2data;
        //开始滤波
        private async void Btn_filter_Click(object sender, RoutedEventArgs e)
        {
            eeg_filter.MadThreshold1= Convert.ToDouble(ThreShold1textbox.Text);
            eeg_filter.MadThreshold2 = Convert.ToDouble(ThreShold2textbox.Text);
            eeg_filter.MadShortStimMs = Convert.ToDouble(MadShortstimtextbox.Text);
            eeg_filter.MadLongStimMs = Convert.ToDouble(MadLongstimtextbox.Text);

            xlsxdata = await eeg_filter.LoadExcelAs2DArray(Freqtextbox_filter.Text);
            if (xlsxdata == null)
                return;

            //data =eeg_filter.LoadExcelAs2DArray(Freqtextbox_filter.Text);
        }
        private async void Btn_offline_ns2_Click(object sender, RoutedEventArgs e)
        {
            eeg_filter.MadThreshold1 = Convert.ToDouble(ThreShold1textbox.Text);
            eeg_filter.MadThreshold2 = Convert.ToDouble(ThreShold2textbox.Text);
            eeg_filter.MadShortStimMs = Convert.ToDouble(MadShortstimtextbox.Text);
            eeg_filter.MadLongStimMs = Convert.ToDouble(MadLongstimtextbox.Text);
            //data1= eeg_filter.LoadNs2As2DArray();
            ns2data = await eeg_filter.LoadNs2As2DArray();
            if (ns2data == null)
                return;
        }
        private void Btn_clear_peak_offline_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var content = button.Content.ToString();
            if (content == "开始去除尖峰")
            {
                eeg_filter.clear_peak_flag = true;
                NlogHelper.WriteInfoLog("开始去除尖峰");
                button.Content = "结束去除尖峰";
            }
            else
            {
                eeg_filter.clear_peak_flag = false;
                NlogHelper.WriteWarnLog("停止去除尖峰");
                button.Content = "开始去除尖峰";
            }
        }
        private TextBox Iptextbox;
        private TextBox PGAbox;
        private TextBox fcbox;
        private System.Windows.Controls.TextBox Porttextbox;
        private ComboBox comboBox;
        private ComboBox comboBox1;
   
        private TextBox Freqtextbox_filter;
    
        private TextBox MadShortstimtextbox;
        private TextBox MadLongstimtextbox;
        private TextBox ThreShold1textbox;
        private TextBox ThreShold2textbox;

        private StackPanel stackpanel1;
        private StackPanel stackpanel2;
        private StackPanel stackpanel3;
        private StackPanel stackpanel4;
        private StackPanel ThreSholdstackpanel;
        private Button btn_filter;
        private Button btn_offline_ns2;
        private Button btn_save_offline;

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var Item = sender as ListBox;
            ListBoxItem item = Item.SelectedItem as ListBoxItem;
            var tag = item.Tag.ToString();
            var EEGDocumentPane = new LayoutDocumentPane();
            if (tag == "EEG")
            {
                var existingEEGDocumentPane = PlotGroup.Children
                .OfType<LayoutDocumentPane>()
                .FirstOrDefault(m => m.Children.Any(doc => doc.Title == "EEG"));

                if (existingEEGDocumentPane == null)
                {
                    PlotGroup.Children.Add(EEGDocumentPane);
                    var EEGDocument = new LayoutDocument();
                    EEGDocument.Content = eeg;
                    EEGDocument.Title = "EEG";
                    EEGDocumentPane.Children.Add(EEGDocument);

                    EEGDocument.Closed += (a, b) =>
                    {
                        EEGDocumentPane.Children.Remove(EEGDocument);
                    };
                }
                else
                {
                    foreach (var doc in existingEEGDocumentPane.Children)
                    {
                        if (doc is LayoutDocument layoutDocument && layoutDocument.Title == "EEG")
                        {
                            layoutDocument.IsSelected = true;
                            break;
                        }
                    }

                }

            }
            
            if (tag == "EEG_OffLine")
            {
                var existingEEGFilterDocumentPane = PlotGroup.Children
                .OfType<LayoutDocumentPane>()
                .FirstOrDefault(m => m.Children.Any(doc => doc.Title == "EEG_OffLine"));
                if (existingEEGFilterDocumentPane == null)
                {
                    var EEG_FilterDocumentPane = new LayoutDocumentPane();
                    PlotGroup.Children.Add(EEG_FilterDocumentPane);
                    var EEG_FilterDocument = new LayoutDocument();
                    EEG_FilterDocument.Content = eeg_filter;
                    EEG_FilterDocument.Title = "EEG_OffLine";
                    EEG_FilterDocumentPane.Children.Add(EEG_FilterDocument);

                    EEG_FilterDocument.Closed += (a, b) =>
                    {
                        EEG_FilterDocumentPane.Children.Remove(EEG_FilterDocument);
                    };
                }
                else
                {
                    foreach (var doc in existingEEGFilterDocumentPane.Children)
                    {
                        if (doc is LayoutDocument layoutDocument && layoutDocument.Title == "EEG_OffLine")
                        {
                            layoutDocument.IsSelected = true;
                            break;
                        }
                    }
                }
            }


        }

        private void listbox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var Item = sender as ListBox;
            ListBoxItem item = Item.SelectedItem as ListBoxItem;
            var tag = item.Tag.ToString();

            if (tag == "tcp")
            {
                groupbox.Visibility = Visibility.Visible;
                groupbox.Content = stackpanel1;
            }
            if (tag == "com")
            {
                groupbox.Visibility = Visibility.Visible;
                groupbox.Content = stackpanel2;
            }
            if (tag == "pwm")
            {
                groupbox.Visibility = Visibility.Visible;
                groupbox.Content = stackpanel3;
            }
            if (tag == "OffLine")
            {
                groupbox.Visibility = Visibility.Visible;
                groupbox.Content = stackpanel4;
            }
            if(tag=="ThreShold")
            {
                groupbox.Visibility = Visibility.Visible;
                groupbox.Content = ThreSholdstackpanel;
            }
        }

        //TCP开始
        private void Btn_tcp_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var content = button.Content.ToString();

            eeg.PGA = Convert.ToInt16(PGAbox.Text);
            eeg.set_filter_params(Convert.ToDouble(fcbox.Text));

            if (is_set_param)
            {
                is_set_param = false;
                eeg.MadShortStimMs = Convert.ToDouble(MadShortstimtextbox.Text);
                eeg.MadLongStimMs = Convert.ToDouble(MadLongstimtextbox.Text);
                eeg.MadThreshold1 = Convert.ToDouble(ThreShold1textbox.Text);
                eeg.MadThreshold2 = Convert.ToDouble(ThreShold2textbox.Text);
            }

            bool sucess = eeg.TCP_Install_ecg(content, Iptextbox.Text, int.Parse(Porttextbox.Text));

            if (sucess)
                button.Content = "结束";
            else
                button.Content = "开始";
        }

        private void Btn_clear_peak_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var content = button.Content.ToString();
            if(content=="开始去除尖峰")
            {
                eeg.clear_peak_flag = true;
                NlogHelper.WriteInfoLog("开始去除尖峰");
                button.Content = "结束去除尖峰";
            }
            else
            {
                eeg.clear_peak_flag = false;
                NlogHelper.WriteWarnLog("停止去除尖峰");
                button.Content = "开始去除尖峰";
            }
        }

        

        private void ComboBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            eeg.ComboBox_amplitude(comboBox1.SelectedIndex);
            
        }

        private void btn_clear_Click(object sender, RoutedEventArgs e)
        {
            eeg.Clear_Plot();
        }

        private async void Btn_save_filter_filter_Click(object sender, RoutedEventArgs e)
        {
            await eeg.button_save_ecg_filter_ns2();
        }
        private async void Btn_save_filter_excel_Click(object sender, RoutedEventArgs e)
        {
            await eeg.button_save_ecg_filter_excel();
        }
        private async void Btn_save_original_ns2_Click(object sender, RoutedEventArgs e)
        {
            await eeg.button_save_ecg_original_ns2();
        }
        private async void Btn_save_original_excel_Click(object sender, RoutedEventArgs e)
        {
            await eeg.button_save_ecg_original_excel();
        }

        private void ComboBox_DropDownOpened(object sender, EventArgs e)
        {
            string filePath = "setting.ini";
            string content = "";
            try
            {
                using (StreamReader reader = new StreamReader(filePath))
                {
                    content = reader.ReadLine();
                }
            }
            catch (FileNotFoundException)
            {
                content = "";
            }
            catch (Exception ex)
            {
                LogHelper.WriteErrorLog(ex.Message);
                NlogHelper.WriteErrorLog(ex.Message);
                return;
            }

            comboBox.Items.Clear();
            //检查是否含有串口  
            string[] str = SerialPort.GetPortNames();
            if (str == null)
            {
                //MessageBox.Show("本机没有串口！", "Error");
                return;
            }

            //添加串口项目  
            foreach (string s in System.IO.Ports.SerialPort.GetPortNames())
            {//获取有多少个COM口  
                comboBox.Items.Add(s);
                LogHelper.WriteInfoLog($"搜索到串口：{s}");
                NlogHelper.WriteInfoLog($"搜索到串口：{s}");
                //显示虚拟串口
                if (s == content)
                {
                    comboBox.SelectedItem = s;
                }
            }
        }

        private void btn_menu_Click(object sender, RoutedEventArgs e)
        {
            meua.IsVisible = true;
        }

        private void btn_action_Click(object sender, RoutedEventArgs e)
        {
            actionRegion.IsVisible = true;
        }

        private void btn_log_Click(object sender, RoutedEventArgs e)
        {
            logRegion.IsVisible = true;
        }

        private void MainWindow1_Closed(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void ListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 判断是否点击了 ListBoxItem
            var item = FindParent<ListBoxItem>((DependencyObject)e.OriginalSource);
            if (item != null && item.IsSelected)
            {
                // 强制触发重新选择
                item.IsSelected = false;
                item.IsSelected = true; // 这会触发 SelectionChanged
            }
        }
        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null && !(child is T))
                child = VisualTreeHelper.GetParent(child);
            return child as T;
        }
    }



}
