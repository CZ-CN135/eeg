using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Collect.EEG
{
    public delegate void EcgTCPEventHandler(object sender, EcgTCPEventArgs e);

    public class TCPClient
    {
        public event EcgTCPEventHandler EcgEvent;

        TcpClient client;
        public TcpListener tcpListener;
        string IPAdress;
        int Port;

        public string freq2;
        public string duty2;
        public string time2;

        Thread th = null;
        private volatile bool run = false;

        public bool g_bInstall = false;
        string g_err = "Init";

        byte ifA0 = 0;
        byte ifB0 = 0;
        int g_packet_length = 0;
        byte[] g_data = new byte[1024];

        // ========= 新增：发送队列（线程安全）=========
        private readonly ConcurrentQueue<byte[]> _txQueue = new ConcurrentQueue<byte[]>();

        /// <summary>
        /// 统一排队发送 3 字节命令：FF cmd FE
        /// </summary>
        public void EnqueueCommand(byte cmd)
        {
            _txQueue.Enqueue(new byte[] { 0xFF, cmd, 0xFE });
        }

        // 你要的几类命令（可读性更好）
        public void EnqueueStartCmd() => EnqueueCommand(0xF1); // FF F1 FE
        public void EnqueueStopCmd() => EnqueueCommand(0xF2); // FF F2 FE
        public void EnqueueMode1Cmd() => EnqueueCommand(0x04); // FF 04 FE
        public void EnqueueMode2Cmd() => EnqueueCommand(0x03); // FF 03 FE

        // ========= 你原来的解析（不改）=========
        int parse_data(byte data)
        {
            if (g_packet_length == 0)
            {
                if (data == 0xA0)
                {
                    g_data[g_packet_length] = data;
                    ifA0 = 1;
                    g_packet_length++;
                }
                else g_packet_length = 0;
                return -1;
            }

            if (g_packet_length == 1)
            {
                if (data == 0xB0)
                {
                    g_data[g_packet_length] = data;
                    ifB0 = 1;
                    g_packet_length++;
                }
                else g_packet_length = 0;
                return -1;
            }

            if (g_packet_length >= 2 && g_packet_length < 26)
            {
                g_data[g_packet_length] = data;
                g_packet_length++;
                return -1;
            }

            if (g_packet_length >= 26 && g_packet_length < 32)
            {
                if (data == 0x00)
                {
                    g_data[g_packet_length] = data;
                    g_packet_length++;
                }
                else g_packet_length = 0;
                return -1;
            }

            if (g_packet_length == 32)
            {
                if (data == 0xC0)
                {
                    g_data[g_packet_length] = data;
                    EcgEvent?.Invoke(this, new EcgTCPEventArgs(g_data));

                    g_packet_length = 0;
                    ifA0 = 0;
                    ifB0 = 0;
                }
                else g_packet_length = 0;
                return -1;
            }
            return -1;
        }

        // ========= 接收缓冲 =========
        Queue<byte> queue = new Queue<byte>();

        // 你原来的“5字节参数发送”保留
        public bool IsWri = false;

        public void Senddata(NetworkStream networkStream, string freq, string duty, string time)
        {
            string binaryString = Convert.ToString(int.Parse(time), 2).PadLeft(16, '0');
            string highBits = binaryString.Substring(0, 8);
            string lowBits = binaryString.Substring(8, 8);
            var timeH = Convert.ToByte(highBits, 2);
            var timeL = Convert.ToByte(lowBits, 2);

            var freq1 = Byte.Parse(freq);
            var duty1 = Byte.Parse(duty);

            byte[] sendBuffer = new byte[5] { 0xFF, freq1, duty1, timeH, timeL };
            networkStream.Write(sendBuffer, 0, sendBuffer.Length);
            IsWri = false;
        }

        void recvdata()
        {
            using (var stream = client.GetStream())
            {
                while (true)
                {
                    // ✅ 关键：Stop 时 run=false，但我们仍要把 txQueue 发完（至少要发出 StopCmd）
                    if (!run && _txQueue.IsEmpty)
                        break;

                    try
                    {
                        // ===== 1) 先把待发送命令全部发出去（模式1/2/开始/停止 都走这里）=====
                        while (_txQueue.TryDequeue(out var msg))
                        {
                            stream.Write(msg, 0, msg.Length);
                        }

                        // ===== 2) 你原来的 5 字节发送（如果还需要）=====
                        if (IsWri)
                        {
                            Senddata(stream, freq2, duty2, time2);
                            IsWri = false;
                        }

                        // ===== 3) 接收数据 =====
                        if (client.Available > 0)
                        {
                            byte[] buffer = new byte[client.Available];
                            int bytesRead = stream.Read(buffer, 0, buffer.Length);

                            if (bytesRead == 0)
                            {
                                NlogHelper.WriteInfoLog("未收到数据");
                                break;
                            }

                            lock (queue)
                            {
                                for (int i = 0; i < bytesRead; i++)
                                    queue.Enqueue(buffer[i]);
                            }
                        }

                        // ===== 4) 解析 =====
                        byte tempdata;
                        bool hasByte = false;
                        lock (queue)
                        {
                            if (queue.Count >= 1)
                            {
                                tempdata = queue.Dequeue();
                                hasByte = true;
                            }
                            else tempdata = 0;
                        }

                        if (hasByte)
                            parse_data(tempdata);

                        // 让出一点CPU（可选）
                      
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteErrorLog(ex.Message);
                        NlogHelper.WriteErrorLog(ex.Message);
                        break;
                    }
                }
            }
        }

        public bool Start(string ip, int port)
        {
            if (th != null)
            {
                g_err = "Thread which created for TCP port has beed created.";
                LogHelper.WriteInfoLog("TCP采集数据的线程已经创建");
                NlogHelper.WriteInfoLog("TCP采集数据的线程已经创建");
                return false;
            }

            this.IPAdress = ip;
            this.Port = port;

            try
            {
                client = new TcpClient();
                client.Connect(IPAddress.Parse(IPAdress), Port);
                LogHelper.WriteInfoLog("TCP与单片机连接成功");
                NlogHelper.WriteInfoLog("TCP与单片机连接成功");
            }
            catch (Exception ex)
            {
                g_err = ex.Message;
                LogHelper.WriteErrorLog(ex.Message);
                NlogHelper.WriteErrorLog(ex.Message);
                return false;
            }

            if (client.Connected)
            {
                run = true;
                th = new Thread(recvdata);
                th.IsBackground = true;
                th.Start();

                // ✅ TCP开始就发送：FF F1 FE
                EnqueueStartCmd();

                LogHelper.WriteInfoLog("TCP采集数据线程成功创建,并开启");
                NlogHelper.WriteInfoLog("TCP采集数据线程成功创建,并开启");
                g_bInstall = true;
                return true;
            }
            else
            {
                g_bInstall = false;
                LogHelper.WriteErrorLog("TCP与单片机断开连接");
                NlogHelper.WriteErrorLog("TCP与单片机断开连接");
                return false;
            }
        }

        public string GetLastError() => g_err;

        public bool Stop()
        {
            // ✅ TCP停止就发送：FF F2 FE（必须先排队，再 run=false，让线程把队列发完）
            EnqueueStopCmd();
            run = false;

            if (th != null)
            {
                th.Join();
                th = null;
                LogHelper.WriteInfoLog("TCP采集线程已停止并且销毁");
                NlogHelper.WriteInfoLog("TCP采集线程已停止并且销毁");
            }

            if (client != null && client.Connected)
            {
                try
                {
                    client.Close();
                    LogHelper.WriteInfoLog("TCP成功与单片机断开连接");
                    NlogHelper.WriteInfoLog("TCP成功与单片机断开连接");
                }
                catch (Exception ex)
                {
                    g_err = ex.Message;
                    LogHelper.WriteErrorLog(ex.Message);
                    NlogHelper.WriteErrorLog(ex.Message);
                    return false;
                }
            }
            else
            {
                g_err = "TCP Port has been closed.";
                LogHelper.WriteWarnLog("TCP断开已经被关闭");
                NlogHelper.WriteWarnLog("TCP断开已经被关闭");
                return false;
            }

            g_bInstall = false;
            return true;
        }
    }

    public class EcgTCPEventArgs : EventArgs
    {
        public string com;
        public int type;
        public byte[] value;

        public EcgTCPEventArgs(byte[] value)
        {
            this.value = value;
        }
    }
}
