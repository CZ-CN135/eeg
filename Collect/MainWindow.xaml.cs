using Accord.Math;
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


        Plot.EEG_Pro eeg_pro;
        Plot.EEG_Filter eeg_filter;
        private Button btn_save;

        public MainWindow()
        {
            InitializeComponent();
            meua.IsVisible = false;
            actionRegion.IsVisible = false;
            logRegion.IsVisible = false;
            NlogHelper.ConfigureNLogForRichTextBox();
            eeg=new Plot.EEG();
            eeg_pro = new Plot.EEG_Pro(eeg);
            eeg_filter = new Plot.EEG_Filter(eeg);

            //tcp
            btn_save = new System.Windows.Controls.Button();
            btn_save.Content = "保存滤波ns2数据";
            btn_save.FontSize = 15;
            btn_save.Margin = new Thickness(0, 10, 0, 0);
            btn_save.Click += Btn_save_filter_filter_Click;

            btn_save_filter_excel = new System.Windows.Controls.Button();
            btn_save_filter_excel.Content = "保存滤波Excel数据";
            btn_save_filter_excel.FontSize = 15;
            btn_save_filter_excel.Margin= new Thickness(0, 10, 0, 0);
            btn_save_filter_excel.Click += Btn_save_filter_excel_Click;

            btn_save_filter_ns2 = new System.Windows.Controls.Button();
            btn_save_filter_ns2.Content = "保存原始ns2数据";
            btn_save_filter_ns2.FontSize = 15;
            btn_save_filter_ns2.Margin = new Thickness(0, 10, 0, 0);
            btn_save_filter_ns2.Click += Btn_save_original_ns2_Click;

            btn_save_filter = new System.Windows.Controls.Button();
            btn_save_filter.Content = "保原始Excel数据";
            btn_save_filter.FontSize = 15;
            btn_save_filter.Margin = new Thickness(0, 10, 0, 0);
            btn_save_filter.Click += Btn_save_original_excel_Click;

            btn_clear = new System.Windows.Controls.Button();
            btn_clear.Content = "清除";
            btn_clear.FontSize = 15;
            btn_clear.Margin = new Thickness(0, 10, 0, 0);
            btn_clear.Click += btn_clear_Click;

            comboBox1 = new ComboBox();
            comboBox1.FontSize = 15;
            comboBox1.Items.Add(new ComboBoxItem()
            {
                Content = "0.1"
            });
            comboBox1.Items.Add(new ComboBoxItem()
            {
                Content = "0.5"
            });
            comboBox1.Items.Add(new ComboBoxItem()
            {
                Content = "1"
            });
            comboBox1.Items.Add(new ComboBoxItem()
            {
                Content = "5"
            });
            comboBox1.Items.Add(new ComboBoxItem()
            {
                Content = "10"
            });
            comboBox1.Items.Add(new ComboBoxItem()
            {
                Content = "100"
            });
            comboBox1.SelectionChanged += ComboBox1_SelectionChanged;

            stackpanel1 = new StackPanel();
            stackpanel1.Orientation = Orientation.Vertical;

            stackpanel1.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "IP地址"
            });

            Iptextbox = new System.Windows.Controls.TextBox();
            Iptextbox.Text = "192.168.4.1";
            Iptextbox.FontSize = 15;
            stackpanel1.Children.Add(Iptextbox);

            stackpanel1.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "端口号"
            });

            Porttextbox = new System.Windows.Controls.TextBox();
            Porttextbox.Text = "4321";
            Porttextbox.FontSize = 15;
            stackpanel1.Children.Add(Porttextbox);

            stackpanel1.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "放大倍数"
            });
            

            Button btn_tcp = new System.Windows.Controls.Button();
            btn_tcp.Content = "开始";
            btn_tcp.FontSize = 15;
            btn_tcp.Margin = new Thickness(0, 10, 0, 0);
            btn_tcp.Click += Btn_tcp_Click;

            stackpanel1.Children.Add(comboBox1);
            stackpanel1.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "PGA"
            });

            PGAbox = new System.Windows.Controls.TextBox();
            PGAbox.Text = "24";
            PGAbox.FontSize = 15;
            stackpanel1.Children.Add(PGAbox);

            stackpanel1.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "滤波采样率"
            });
            fcbox= new System.Windows.Controls.TextBox();
            fcbox.Text = "1000";
            fcbox.FontSize = 15;
            stackpanel1.Children.Add(fcbox);

            stackpanel1.Children.Add(btn_tcp);
            stackpanel1.Children.Add(btn_save);
            stackpanel1.Children.Add(btn_save_filter_excel);
            stackpanel1.Children.Add(btn_save_filter_ns2);
            stackpanel1.Children.Add(btn_save_filter);
            stackpanel1.Children.Add(btn_clear);


            //com
            btn_save_com = new System.Windows.Controls.Button();
            btn_save_com.Content = "保存原始曲线数据";
            btn_save_com.FontSize = 15;
            btn_save_com.Margin = new Thickness(0, 10, 0, 0);
           

       

            btn_save_filter_com = new System.Windows.Controls.Button();
            btn_save_filter_com.Content = "保存滤波数据";
            btn_save_filter_com.FontSize = 15;
            btn_save_filter_com.Margin = new Thickness(0, 10, 0, 0);



            btn_clear_com = new System.Windows.Controls.Button();
            btn_clear_com.Content = "清除";
            btn_clear_com.FontSize = 15;
            btn_clear_com.Margin = new Thickness(0, 10, 0, 0);
            btn_clear_com.Click += btn_clear_Click;

            comboBox1_com = new ComboBox();
            comboBox1_com.FontSize = 15;
            comboBox1_com.Items.Add(new ComboBoxItem()
            {
                Content = "0.1"
            });
            comboBox1_com.Items.Add(new ComboBoxItem()
            {
                Content = "0.5"
            });
            comboBox1_com.Items.Add(new ComboBoxItem()
            {
                Content = "1"
            });
            comboBox1_com.Items.Add(new ComboBoxItem()
            {
                Content = "5"
            });
            comboBox1_com.Items.Add(new ComboBoxItem()
            {
                Content = "10"
            });
            comboBox1_com.Items.Add(new ComboBoxItem()
            {
                Content = "100"
            });
            comboBox1_com.SelectionChanged += ComboBox1_SelectionChanged;
            stackpanel2 = new StackPanel();
            stackpanel2.Orientation = Orientation.Vertical;

            stackpanel2.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "串口号"
            });

            comboBox = new ComboBox();
            comboBox.FontSize = 15;
            comboBox.DropDownOpened += ComboBox_DropDownOpened;
            stackpanel2.Children.Add(comboBox);

            stackpanel2.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "放大倍数"
            });


            Button com_tcp = new System.Windows.Controls.Button();
            com_tcp.Content = "开始";
            com_tcp.FontSize = 15;
            com_tcp.Margin = new Thickness(0, 10, 0, 0);
            com_tcp.Click += Com_tcp_Click;
            stackpanel2.Children.Add(comboBox1_com);
            stackpanel2.Children.Add(com_tcp);

            stackpanel2.Children.Add(btn_save_com);
            stackpanel2.Children.Add(btn_save_filter_com);
            stackpanel2.Children.Add(btn_clear_com);



            //pwm
            stackpanel3 = new StackPanel();
            stackpanel3.Orientation = Orientation.Vertical;

            stackpanel3.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "占空比(%)"
            });

            Dutytextbox = new System.Windows.Controls.TextBox();
            Dutytextbox.Text = "50";
            Dutytextbox.FontSize = 15;
            stackpanel3.Children.Add(Dutytextbox);

            stackpanel3.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "频率(Hz)"
            });

            Freqtextbox = new System.Windows.Controls.TextBox();
            Freqtextbox.Text = "200";
            Freqtextbox.FontSize = 15;
            stackpanel3.Children.Add(Freqtextbox);

            stackpanel3.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "时间(ms)"
            });


            Timetextbox = new System.Windows.Controls.TextBox();
            Timetextbox.Text = "200";
            Timetextbox.FontSize = 15;
            stackpanel3.Children.Add(Timetextbox);

            Button btn_send = new System.Windows.Controls.Button();
            btn_send.Content = "开始";
            btn_send.FontSize = 15;
            btn_send.Margin = new Thickness(0, 10, 0, 0);
            btn_send.Click += Btn_send_Click;
            stackpanel3.Children.Add(btn_send);

            //filter
            stackpanel4 = new StackPanel();
            stackpanel4.Orientation = Orientation.Vertical;
            stackpanel4.Children.Add(new TextBlock
            {
                FontSize = 15,
                Text = "采样频率(Hz)"
            });

            Freqtextbox_filter = new System.Windows.Controls.TextBox();
            Freqtextbox_filter.Text = "1000";
            Freqtextbox_filter.FontSize = 15;
            stackpanel4.Children.Add(Freqtextbox_filter);

            //stackpanel4.Children.Add(new TextBlock
            //{
            //    FontSize = 15,
            //    Text = "阶数"
            //});

            //Ordertextbox_filter = new System.Windows.Controls.TextBox();
            //Ordertextbox_filter.Text = "4";
            //Ordertextbox_filter.FontSize = 15;
            //stackpanel4.Children.Add(Ordertextbox_filter);

            //stackpanel4.Children.Add(new TextBlock
            //{
            //    FontSize = 15,
            //    Text = "截止频率(Hz)"
            //});

            //EndFreqtextbox_filter_com = new System.Windows.Controls.TextBox();
            //EndFreqtextbox_filter_com.Text = "50";
            //EndFreqtextbox_filter_com.FontSize = 15;
            //stackpanel4.Children.Add(EndFreqtextbox_filter_com);
            
            btn_filter = new System.Windows.Controls.Button();
            btn_filter.Content = "读取数据";
            btn_filter.FontSize = 15;
            btn_filter.Margin = new Thickness(0, 10, 0, 0);
            btn_filter.Click += Btn_filter_Click;
            stackpanel4.Children.Add(btn_filter);

            btn_save_offline = new System.Windows.Controls.Button();
            btn_save_offline.Content = "保存滤波数据";
            btn_save_offline.FontSize = 15;
            btn_save_offline.Margin = new Thickness(0, 10, 0, 0);
            btn_save_offline.Click += Btn_save_offline_Click; ;
            stackpanel4.Children.Add(btn_save_offline);

            //btn_clear_original_filter_txt = new System.Windows.Controls.Button();
            //btn_clear_original_filter_txt.Content = "清除文本数据";
            //btn_clear_original_filter_txt.FontSize = 15;
            //btn_clear_original_filter_txt.Margin = new Thickness(0, 10, 0, 0);
            //btn_clear_original_filter_txt.Click += Btn_clear_original_filter_txt_Click;
            //stackpanel4.Children.Add(btn_clear_original_filter_txt);

        }

        private void Btn_save_offline_Click(object sender, RoutedEventArgs e)
        {
            eeg_filter.save_offline(data);
        }

        private void Btn_clear_original_filter_txt_Click(object sender, RoutedEventArgs e)
        {
            eeg_filter.clear_original_filter_txt_flag=true;
        }
        double[][] data; 
        //开始滤波
        private void Btn_filter_Click(object sender, RoutedEventArgs e)
        {
            data=eeg_filter.LoadExcelAs2DArray(Freqtextbox_filter.Text);
            //var button = sender as Button;
            //var content = button.Content.ToString();
            //if (content == "开始")
            //{
            //    eeg_filter.IsFilter = true;
            //    eeg_filter.Freq = int.Parse(Freqtextbox_filter.Text);
            //    eeg_filter.Order = int.Parse(Ordertextbox_filter.Text);
            //    eeg_filter.EndFreq = int.Parse(EndFreqtextbox_filter_com.Text);
            //    NlogHelper.WriteInfoLog("滤波参数已就绪，开始滤波");
            //    button.Content = "结束";
            //}
            //else
            //{
            //    eeg_filter.IsFilter = false;
            //    NlogHelper.WriteWarnLog("停止滤波");
            //    button.Content = "开始";
            //}
        }

       



        private TextBox Iptextbox;
        private TextBox PGAbox;
        private TextBox fcbox;
        private System.Windows.Controls.TextBox Porttextbox;
        private ComboBox comboBox;
        private ComboBox comboBox1;
        private LoggingConfiguration config;
        private WpfRichTextBoxTarget target;
        private System.Windows.Controls.RichTextBox log;
        private TextBox Dutytextbox;
        private TextBox Freqtextbox;
        private TextBox Timetextbox;
        private TextBox Freqtextbox_filter;
        private TextBox Ordertextbox_filter;
        private TextBox EndFreqtextbox_filter_com;
        private StackPanel stackpanel1;
        private StackPanel stackpanel2;
        private StackPanel stackpanel3;
        private StackPanel stackpanel4;
        private Button btn_save_filter;
        private Button btn_clear;
        private Button btn_save_com;
        private Button btn_save_filter_com;
        private Button btn_save_filter_excel;
        private Button btn_save_filter_ns2;
        private Button btn_clear_com;
        private Button btn_filter;
        private Button btn_clear_original_filter_txt;
        private ComboBox comboBox1_com;
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
            if (tag == "EEG_Pro")
            {

                var existingEEGProDocumentPane = PlotGroup.Children
               .OfType<LayoutDocumentPane>()
               .FirstOrDefault(m => m.Children.Any(doc => doc.Title == "EEG_Pro"));
                if (existingEEGProDocumentPane == null)
                {
                    var EEG_ProDocumentPane = new LayoutDocumentPane();
                    PlotGroup.Children.Add(EEG_ProDocumentPane);

                    var EEG_ProDocument = new LayoutDocument();
                    EEG_ProDocument.Content = eeg_pro;

                    EEG_ProDocument.Title = "EEG_Spectrum";
                    EEG_ProDocumentPane.Children.Add(EEG_ProDocument);

                    EEG_ProDocument.Closed += (a, b) =>
                    {
                        EEG_ProDocumentPane.Children.Remove(EEG_ProDocument);
                    };
                }
                else
                {
                    foreach (var doc in existingEEGProDocumentPane.Children)
                    {
                        if (doc is LayoutDocument layoutDocument && layoutDocument.Title == "EEG_Pro")
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
        }

        //发送pwm参数
        private void Btn_send_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var content = button.Content.ToString();
            if (content == "开始")
            {
                eeg_pro.IsStm = true;
                eeg.client.freq2 = Freqtextbox.Text;
                eeg.client.duty2 = Dutytextbox.Text;
                eeg.client.time2 = Timetextbox.Text;
                NlogHelper.WriteInfoLog("PWM参数已就绪，开始给出刺激");
                button.Content = "结束";
            }
            else
            {
                eeg_pro.IsStm = false;
                NlogHelper.WriteWarnLog("停止给出刺激");
                button.Content = "开始";
            }
            
            
        }

       
        private void ComboBox1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            eeg.ComboBox_amplitude(comboBox1.SelectedIndex);
            eeg_pro.ComboBox_amplitude(comboBox1.SelectedIndex);
        }

        private void btn_clear_Click(object sender, RoutedEventArgs e)
        {
            eeg.Clear_Plot();

        }

        private void Btn_save_filter_filter_Click(object sender, RoutedEventArgs e)
        {
            eeg.button_save_ecg_filter_ns2();
        }
        private void Btn_save_filter_excel_Click(object sender, RoutedEventArgs e)
        {
            eeg.button_save_ecg_filter_excel();
        }
        private void Btn_save_original_ns2_Click(object sender, RoutedEventArgs e)
        {
            eeg.button_save_ecg_original_ns2();
        }
        private void Btn_save_original_excel_Click(object sender, RoutedEventArgs e)
        {
            eeg.button_save_ecg_original_excel();
        }

        private void Com_tcp_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var content = button.Content.ToString();
            bool sucess = eeg.Serial_install_ecg(content, comboBox.SelectedItem.ToString());
            if (sucess)
            {
                button.Content = "结束";
            }
            else
            {
                button.Content = "开始";
            }
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


        //TCP开始
        private void Btn_tcp_Click(object sender, RoutedEventArgs e)
        {

            var button = sender as Button;
            var content = button.Content.ToString();
            bool sucess = eeg.TCP_Install_ecg(content, Iptextbox.Text, int.Parse(Porttextbox.Text));
            eeg.PGA = Convert.ToInt16(PGAbox.Text);
            eeg.set_filter_params(Convert.ToDouble(fcbox.Text));
            if (sucess)
            {
                eeg.client.IsWri_start = true;
                button.Content = "结束";
            }
            else
            {
                eeg.client.IsWri_start = false;
                button.Content = "开始";
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
