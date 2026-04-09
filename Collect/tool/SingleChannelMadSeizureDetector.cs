using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Collect.tool
{
    public class SingleChannelMadSeizureDetector
    {
        public sealed class Config
        {
            public double Fs = 1000.0;              // 采样率 Hz
            public double WindowMs = 500.0;         // 500 ms 窗口
            public double StepMs = 250.0;           // 250 ms 步长
            public double WarmupMs = 1000.0;        // 上电后前 1s 不检测（可设为0）

            public double MadThreshold1 = 100.0;    // 阈值1：是否进入异常
            public double MadThreshold2 = 200.0;    // 阈值2：轻/重分级

            public double ShortStimMs = 200.0;      // 模式1：短刺激期间暂停检测
            public double LongStimMs = 500.0;       // 模式2：长刺激期间暂停检测

            public int QueueCapacity = 10000;
        }

        public sealed class WindowEvaluatedEventArgs : EventArgs
        {
            public long WindowStartSample;
            public long WindowEndSample;
            public double Mad;
            public bool AboveThreshold1;
            public int ConsecutiveAboveCount;
            public bool IsPaused;
        }

        public sealed class SeizureTriggeredEventArgs : EventArgs
        {
            public long FirstWindowEndSample;
            public long SecondWindowEndSample;

            public double FirstWindowMad;
            public double SecondWindowMad;
            public double MaxMad;

            /// <summary>
            /// 1 = 短刺激（模式1）
            /// 2 = 长刺激（模式2）
            /// </summary>
            public int Mode;

            public double PauseMs;
        }

        public event EventHandler<WindowEvaluatedEventArgs> OnWindowEvaluated;
        public event EventHandler<SeizureTriggeredEventArgs> OnSeizureTriggered;

        private readonly Config _cfg;

        private BlockingCollection<double> _queue;
        private CancellationTokenSource _cts;
        private Task _worker;

        // 环形缓冲区（单通道）
        private double[] _ring;
        private int _ringPos;
        private int _validCount;

        private int _nWin;
        private int _nStep;
        private int _nWarm;
        private long _sampleIndex;            // 全局样本序号，从0开始
        private long _nextEvalEndSample;      // 下一个要计算窗口的末尾样本号

        // 连续超阈值1状态
        private int _consecutiveAboveT1;
        private double _prevAboveMad;
        private long _prevAboveWinEnd;

        // 刺激暂停检测
        private int _pauseRemainSamples;

        public SingleChannelMadSeizureDetector(Config cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            ValidateAndInit();
        }

        private void ValidateAndInit()
        {
            if (_cfg.Fs <= 0) throw new ArgumentException("Fs must be > 0");
            if (_cfg.WindowMs <= 0) throw new ArgumentException("WindowMs must be > 0");
            if (_cfg.StepMs <= 0) throw new ArgumentException("StepMs must be > 0");
            if (_cfg.WarmupMs < 0) throw new ArgumentException("WarmupMs must be >= 0");
            if (_cfg.MadThreshold1 < 0) throw new ArgumentException("MadThreshold1 must be >= 0");
            if (_cfg.MadThreshold2 < 0) throw new ArgumentException("MadThreshold2 must be >= 0");

            _nWin = Math.Max(2, (int)Math.Round(_cfg.Fs * _cfg.WindowMs / 1000.0));
            _nStep = Math.Max(1, (int)Math.Round(_cfg.Fs * _cfg.StepMs / 1000.0));
            _nWarm = Math.Max(0, (int)Math.Round(_cfg.Fs * _cfg.WarmupMs / 1000.0));

            _ring = new double[_nWin];
            ResetRuntimeState(isInitial: true);
        }

        private void ResetRuntimeState(bool isInitial)
        {
            Array.Clear(_ring, 0, _ring.Length);
            _ringPos = 0;
            _validCount = 0;

            _consecutiveAboveT1 = 0;
            _prevAboveMad = 0.0;
            _prevAboveWinEnd = -1;

            _pauseRemainSamples = 0;

            if (isInitial)
            {
                _sampleIndex = -1;
                // 启动后：先暖机，再收满一个完整窗口，再开始第一次评估
                _nextEvalEndSample = _nWarm + _nWin - 1;
            }
            else
            {
                // 刺激结束后：从“当前时刻之后”重新收满一个窗口再检测
                _nextEvalEndSample = _sampleIndex + _nWin;
            }
        }

        public void Start()
        {
            if (_worker != null) return;

            _queue = new BlockingCollection<double>(new ConcurrentQueue<double>(), _cfg.QueueCapacity);
            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => WorkerLoop(_cts.Token), _cts.Token);
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _queue?.CompleteAdding();
                _worker?.Wait(500);
            }
            catch { }
            finally
            {
                _worker = null;
                _cts?.Dispose();
                _cts = null;

                _queue?.Dispose();
                _queue = null;
            }
        }

        public void PushSample(double sample)
        {
            if (_queue == null || _queue.IsAddingCompleted) return;
            _queue.TryAdd(sample);
        }

        private void WorkerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_queue.IsCompleted)
            {
                double sample;
                try
                {
                    if (!_queue.TryTake(out sample, 50, ct))
                        continue;
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                long curSample = ++_sampleIndex;

                // ===== 刺激暂停期间：不做检测，不写入窗口 =====
                if (_pauseRemainSamples > 0)
                {
                    _pauseRemainSamples--;

                    if (_pauseRemainSamples == 0)
                    {
                        // 刺激刚结束：清空窗口和连续计数，重新开始采集新窗口
                        ResetRuntimeState(isInitial: false);
                    }

                    continue;
                }

                // ===== 正常写入 ring =====
                _ring[_ringPos] = sample;
                _ringPos++;
                if (_ringPos >= _nWin) _ringPos = 0;

                if (_validCount < _nWin)
                    _validCount++;

                // 数据还没装满一个完整窗口
                if (_validCount < _nWin)
                    continue;

                // 还没到评估点
                if (curSample < _nextEvalEndSample)
                    continue;

                long winEnd = _nextEvalEndSample;
                long winStart = winEnd - _nWin + 1;

                double mad = ComputeCurrentWindowMad();

                bool aboveT1 = mad > _cfg.MadThreshold1;

                if (!aboveT1)
                {
                    _consecutiveAboveT1 = 0;
                    _prevAboveMad = 0.0;
                    _prevAboveWinEnd = -1;
                }
                else
                {
                    _consecutiveAboveT1++;
                }

                OnWindowEvaluated?.Invoke(this, new WindowEvaluatedEventArgs
                {
                    WindowStartSample = winStart,
                    WindowEndSample = winEnd,
                    Mad = mad,
                    AboveThreshold1 = aboveT1,
                    ConsecutiveAboveCount = _consecutiveAboveT1,
                    IsPaused = false
                });

                // ===== 连续两个窗口 MAD > 阈值1：确认发作 =====
                if (aboveT1)
                {
                    if (_consecutiveAboveT1 == 1)
                    {
                        _prevAboveMad = mad;
                        _prevAboveWinEnd = winEnd;
                    }
                    else if (_consecutiveAboveT1 >= 2)
                    {
                        double maxMad = Math.Max(_prevAboveMad, mad);

                        int mode = (maxMad > _cfg.MadThreshold2) ? 2 : 1;
                        double pauseMs = (mode == 2) ? _cfg.LongStimMs : _cfg.ShortStimMs;

                        OnSeizureTriggered?.Invoke(this, new SeizureTriggeredEventArgs
                        {
                            FirstWindowEndSample = _prevAboveWinEnd,
                            SecondWindowEndSample = winEnd,
                            FirstWindowMad = _prevAboveMad,
                            SecondWindowMad = mad,
                            MaxMad = maxMad,
                            Mode = mode,
                            PauseMs = pauseMs
                        });

                        // 进入暂停期
                        _pauseRemainSamples = Math.Max(1, (int)Math.Round(_cfg.Fs * pauseMs / 1000.0));

                        // 清连续计数，真正清窗口会在暂停结束时做
                        _consecutiveAboveT1 = 0;
                        _prevAboveMad = 0.0;
                        _prevAboveWinEnd = -1;
                    }
                }

                // 下一个窗口评估点
                _nextEvalEndSample += _nStep;
            }
        }

        private double ComputeCurrentWindowMad()
        {
            // 按时间顺序取出当前窗口
            double[] x = new double[_nWin];
            int oldestPos = _ringPos; // ringPos 指向“最老数据将被覆盖的位置”

            for (int k = 0; k < _nWin; k++)
            {
                int p = oldestPos + k;
                if (p >= _nWin) p -= _nWin;
                x[k] = _ring[p];
            }

            double med = Median(x);

            double[] absDev = new double[_nWin];
            for (int i = 0; i < _nWin; i++)
                absDev[i] = Math.Abs(x[i] - med);

            return Median(absDev);
        }

        private static double Median(double[] data)
        {
            double[] copy = new double[data.Length];
            Array.Copy(data, copy, data.Length);
            Array.Sort(copy);

            int n = copy.Length;
            if ((n & 1) == 1)
                return copy[n / 2];

            return 0.5 * (copy[n / 2 - 1] + copy[n / 2]);
        }

    }
}
