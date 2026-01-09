using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Collect.tool
{
    public class SeizureDetector
    {
        public sealed class Config
        {
            public int ChannelCount = 8;      // 通道数

            public double Fs = 500;           // 采样率 Hz
            public double WindowMs = 200;     // Stage1: 200ms
            public double StepMs = 50;        // Stage1: 50ms
            public double WarmupMs = 1000;    // 前 1s 不检测

            // Stage1 阈值范围（单位按你的输入 μV）
            // 触发条件：RMS 在 [RmsMin, RmsMax] 且 LL 在 [LlMin, LlMax]
            public double RmsMin = 80;
            public double RmsMax = double.PositiveInfinity;   // 不想设上限就保持无穷大
            public double LlMin = 2000;
            public double LlMax = double.PositiveInfinity;    // 不想设上限就保持无穷大

            // 至少有多少个通道满足条件才算 Stage1 触发
            public int MinChannelsToTrigger = 3;

            // Stage1 触发后是否停止继续检测（一般你做二级确认时建议 false）
            public bool StopAfterTrigger = false;

            public int QueueCapacity = 5000;

            // ===== Stage2 窗口参数 =====
            public double Stage2LookbackMs = 400;     // 向前回看多少 ms
            public double Stage2WindowMs = 600;       // Stage2 总窗口长度 ms（末尾对齐 Stage1 的 winEnd）
            public double HistoryMs = 2000;           // 历史缓冲保存时长（必须 >= Stage2WindowMs）

            // Stage2 候选窗口发送最小间隔（ms），防止阈值持续满足时疯狂发任务
            public double Stage2EmitMinIntervalMs = 100;
        }

        public sealed class DetectionEventArgs : EventArgs
        {
            public long WindowStartSample;
            public long WindowEndSample;
            public double[] RmsPerChannel;
            public double[] LlPerChannel;
            public int PassedChannels;
        }

        // ===== Stage2 数据块事件参数 =====
        // ✅ Window 只包含“通过 Stage1 条件”的通道数据
        // ✅ ChannelIndices[k] 表示 Window[k] 对应原始通道号（0..ChannelCount-1）
        public sealed class Stage2WindowEventArgs : EventArgs
        {
            public long WindowStartSample;
            public long WindowEndSample;

            public double[][] Window;        // [passedCh][n]
            public int[] ChannelIndices;     // [passedCh] -> 原始通道号
            public int Samples;
        }

        public event EventHandler<DetectionEventArgs> OnWindowEvaluated;
        public event EventHandler<DetectionEventArgs> OnSeizureTriggered;

        // Stage1 满足阈值时，把 Stage2WindowMs 数据块抛给二级检测
        public event EventHandler<Stage2WindowEventArgs> OnStage2WindowReady;

        private readonly Config _cfg;
        private BlockingCollection<double[]> _queue;
        private CancellationTokenSource _cts;
        private Task _worker;

        // ===== Stage1 ring：只保存最近 Nwin 个点 =====
        private double[,] _ring;      // [ch, idx] len=_nWin
        private int _ringPos;         // 0.._nWin-1

        private int _nWin, _nStep, _nWarm;
        private long _totalSamples;   // 已接收总样本数（帧数）
        private long _nextEvalEndSample;
        private volatile bool _triggered;

        // ===== 历史 ring：只用于 Stage2 提取 =====
        private double[,] _histRing;      // [ch, idx] len=_nHist
        private int _histPos;             // 0.._nHist-1
        private int _nHist;
        private long _lastSampleIndex;
        private long _lastStage2EmitSample;

        public SeizureDetector(Config cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            ValidateAndInit();
        }

        private void ValidateAndInit()
        {
            if (_cfg.ChannelCount <= 0) throw new ArgumentException("ChannelCount must be > 0");
            if (_cfg.Fs <= 0) throw new ArgumentException("Fs must be > 0");
            if (_cfg.WindowMs <= 0) throw new ArgumentException("WindowMs must be > 0");
            if (_cfg.StepMs <= 0) throw new ArgumentException("StepMs must be > 0");
            if (_cfg.WarmupMs < 0) throw new ArgumentException("WarmupMs must be >= 0");

            if (_cfg.RmsMin > _cfg.RmsMax) throw new ArgumentException("RmsMin must be <= RmsMax");
            if (_cfg.LlMin > _cfg.LlMax) throw new ArgumentException("LlMin must be <= LlMax");
            if (_cfg.MinChannelsToTrigger < 1) throw new ArgumentException("MinChannelsToTrigger must be >= 1");

            _nWin = (int)Math.Round(_cfg.Fs * _cfg.WindowMs / 1000.0);
            _nStep = (int)Math.Round(_cfg.Fs * _cfg.StepMs / 1000.0);
            _nWarm = (int)Math.Round(_cfg.Fs * _cfg.WarmupMs / 1000.0);

            if (_nWin < 2) _nWin = 2;
            if (_nStep < 1) _nStep = 1;

            _ring = new double[_cfg.ChannelCount, _nWin];
            _ringPos = 0;

            int lookback = (int)Math.Round(_cfg.Fs * _cfg.Stage2LookbackMs / 1000.0);
            int nStage2 = (int)Math.Round(_cfg.Fs * _cfg.Stage2WindowMs / 1000.0);

            int minStage2 = _nWin + Math.Max(0, lookback);
            if (nStage2 < minStage2) nStage2 = minStage2;

            int nHistByCfg = (int)Math.Round(_cfg.Fs * _cfg.HistoryMs / 1000.0);
            if (nHistByCfg < nStage2) nHistByCfg = nStage2;
            if (nHistByCfg < 16) nHistByCfg = 16;

            _nHist = nHistByCfg;
            _histRing = new double[_cfg.ChannelCount, _nHist];
            _histPos = 0;

            _totalSamples = 0;
            _lastSampleIndex = -1;
            _lastStage2EmitSample = long.MinValue;
            _triggered = false;

            _nextEvalEndSample = _nWarm + _nWin - 1;
        }

        public void Start()
        {
            if (_worker != null) return;

            _queue = new BlockingCollection<double[]>(
                new ConcurrentQueue<double[]>(),
                _cfg.QueueCapacity
            );
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
                _cts?.Dispose(); _cts = null;
                _queue?.Dispose(); _queue = null;
            }
        }

        public void PushFrame(double[] ch8)
        {
            if (ch8 == null || ch8.Length != _cfg.ChannelCount) return;
            if (_queue == null || _queue.IsAddingCompleted) return;
            _queue.TryAdd(ch8);
        }

        private void WorkerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_queue.IsCompleted)
            {
                double[] frame = null;
                try
                {
                    if (!_queue.TryTake(out frame, 50, ct)) continue;
                }
                catch (OperationCanceledException) { break; }

                if (frame == null) continue;

                long curSampleIndex = _totalSamples;

                // Stage1 ring
                for (int ch = 0; ch < _cfg.ChannelCount; ch++)
                    _ring[ch, _ringPos] = frame[ch];

                _ringPos++;
                if (_ringPos >= _nWin) _ringPos = 0;

                // 历史 ring（Stage2 用）
                for (int ch = 0; ch < _cfg.ChannelCount; ch++)
                    _histRing[ch, _histPos] = frame[ch];

                _histPos++;
                if (_histPos >= _nHist) _histPos = 0;

                _lastSampleIndex = curSampleIndex;
                _totalSamples++;

                if (_triggered && _cfg.StopAfterTrigger) continue;

                // 到点评估 Stage1
                if (curSampleIndex >= _nextEvalEndSample)
                {
                    long winEnd = _nextEvalEndSample;
                    long winStart = winEnd - _nWin + 1;

                    EvaluateCurrentWindow(winStart, winEnd);

                    _nextEvalEndSample += _nStep;
                }
            }
        }

        private void EvaluateCurrentWindow(long winStart, long winEnd)
        {
            var rms = new double[_cfg.ChannelCount];
            var ll = new double[_cfg.ChannelCount];

            // ✅ 记录满足条件的通道号
            var passedList = new List<int>(_cfg.ChannelCount);

            int oldestPos = _ringPos;

            for (int ch = 0; ch < _cfg.ChannelCount; ch++)
            {
                double sumSq = 0.0;
                double sumAbsDiff = 0.0;

                double prev = ReadRing(ch, oldestPos);
                sumSq += prev * prev;

                for (int k = 1; k < _nWin; k++)
                {
                    double x = ReadRing(ch, oldestPos + k);
                    sumSq += x * x;
                    sumAbsDiff += Math.Abs(x - prev);
                    prev = x;
                }

                double r = Math.Sqrt(sumSq / _nWin);
                double l = sumAbsDiff / (_nWin - 1);

                rms[ch] = r;
                ll[ch] = l;

                bool rmsInRange = (r >= _cfg.RmsMin) && (r <= _cfg.RmsMax);
                bool llInRange = (l >= _cfg.LlMin) && (l <= _cfg.LlMax);

                if (rmsInRange && llInRange)
                    passedList.Add(ch);
            }

            int passed = passedList.Count;

            var args = new DetectionEventArgs
            {
                WindowStartSample = winStart,
                WindowEndSample = winEnd,
                RmsPerChannel = rms,
                LlPerChannel = ll,
                PassedChannels = passed
            };

            OnWindowEvaluated?.Invoke(this, args);

            // ✅ 只把满足条件的通道传给 Stage2
            if (passed >= _cfg.MinChannelsToTrigger)
            {
                TryEmitStage2Window(winStart, winEnd, passedList.ToArray());
            }

            if (!_triggered && passed >= _cfg.MinChannelsToTrigger)
            {
                _triggered = true;
                OnSeizureTriggered?.Invoke(this, args);
            }
        }

        // ✅ 多了 channels 参数：只提取这些通道的 Stage2 窗口数据
        private void TryEmitStage2Window(long winStart, long winEnd, int[] channels)
        {
            if (channels == null || channels.Length == 0) return;

            // 限频（可选）
            if (_cfg.Stage2EmitMinIntervalMs > 0)
            {
                long minIntervalSamples = (long)Math.Round(_cfg.Fs * _cfg.Stage2EmitMinIntervalMs / 1000.0);
                if (_lastStage2EmitSample != long.MinValue && (winEnd - _lastStage2EmitSample) < minIntervalSamples)
                    return;
            }

            int lookback = (int)Math.Round(_cfg.Fs * _cfg.Stage2LookbackMs / 1000.0);
            int nStage2 = (int)Math.Round(_cfg.Fs * _cfg.Stage2WindowMs / 1000.0);
            int minStage2 = _nWin + Math.Max(0, lookback);
            if (nStage2 < minStage2) nStage2 = minStage2;
            if (nStage2 > _nHist) return;

            long s2End = winEnd;
            long s2Start = s2End - nStage2 + 1;
            if (s2Start < 0) return;

            long delta = _lastSampleIndex - s2End;
            if (delta < 0) return;
            if (delta >= _nHist) return;

            int endPos = _histPos - 1 - (int)delta;
            int startPos = endPos - (nStage2 - 1);

            // ✅ 只拷贝通过条件的通道
            var window = new double[channels.Length][];
            for (int k = 0; k < channels.Length; k++)
            {
                int ch = channels[k];
                if (ch < 0 || ch >= _cfg.ChannelCount) return;

                window[k] = new double[nStage2];
                for (int i = 0; i < nStage2; i++)
                {
                    window[k][i] = ReadHist(ch, startPos + i);
                }
            }

            _lastStage2EmitSample = s2End;

            OnStage2WindowReady?.Invoke(this, new Stage2WindowEventArgs
            {
                WindowStartSample = s2Start,
                WindowEndSample = s2End,
                Window = window,             // [passedCh][n]
                ChannelIndices = channels,   // 映射回原始通道号
                Samples = nStage2
            });
        }

        private double ReadRing(int ch, int pos)
        {
            int p = pos % _nWin;
            if (p < 0) p += _nWin;
            return _ring[ch, p];
        }

        private double ReadHist(int ch, int pos)
        {
            int p = pos % _nHist;
            if (p < 0) p += _nHist;
            return _histRing[ch, p];
        }
    }
}
