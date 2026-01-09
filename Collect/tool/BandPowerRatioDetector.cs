using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Collect.tool
{
    public class BandPowerRatioDetector
    {
        public sealed class Config
        {
            // 原始总通道数（用于把结果映射回 8 通道）
            public int ChannelCount = 8;

            public double Fs = 500;

            // ===== 4 个频段（默认 EEG 常用）=====
            public double DeltaLow = 0.5;
            public double DeltaHigh = 4.0;

            public double ThetaLow = 4.0;
            public double ThetaHigh = 8.0;

            public double AlphaLow = 8.0;
            public double AlphaHigh = 13.0;

            public double BetaLow = 13.0;
            public double BetaHigh = 30.0;

            // ===== 触发条件：Alpha 相对带功率阈值 =====
            // alphaRel = Palpha / (Pdelta + Ptheta + Palpha + Pbeta)
            public double AlphaRelativeThreshold = 0.35;

            // 模式1：至少多少个“参与的通道(即传入window的通道)”满足 alphaRel 才触发
            public int MinChannelsToTrigger = 2;

            // ===== 模式2：下一次window，这些“上次模式1命中的通道”都要比上次高 X 才触发 =====
            // 20% -> 0.20
            public double Mode2IncreaseRatio = 0.20;
            public bool EnableMode2 = true;

            public int QueueCapacity = 32;

            // 如果你设 true：任意一次触发(模式1或2)后就不再继续处理
            // 若你希望“先触发模式1再触发模式2”，请保持 false
            public bool StopAfterTrigger = false;
        }

        public sealed class ResultEventArgs : EventArgs
        {
            public long WindowStartSample;
            public long WindowEndSample;

            // ✅ 仅针对“传入 Stage2 的通道”（长度 = window.Length）
            // 兼容字段：Ratio 实际表示 AlphaRelative
            public double[] RatioPerWindowChannel;   // == AlphaRelativePerWindowChannel

            // ✅ RatioPerWindowChannel[k] 对应原始通道号 ChannelIndices[k]
            public int[] ChannelIndices;

            // ✅ 映射回原始通道（长度 = cfg.ChannelCount），未参与的通道填 NaN
            public double[] RatioPerOriginalChannel; // == AlphaRelativePerOriginalChannel

            // ===== 四段功率 + alphaRel =====
            public double[] PDeltaPerWindowChannel;
            public double[] PThetaPerWindowChannel;
            public double[] PAlphaPerWindowChannel;
            public double[] PBetaPerWindowChannel;

            public double[] AlphaRelativePerWindowChannel;
            public double[] AlphaRelativePerOriginalChannel;

            // ✅ 在“参与的通道”里，有多少个 alphaRel >= 阈值
            public int PassedChannels;

            // ===== 新增：触发模式 =====
            // 0=未触发（仅用于Evaluated事件）
            // 1=模式1触发
            // 2=模式2触发
            public int TriggerMode;

            // 本次触发涉及的“原始通道号”
            public int[] TriggeredOriginalChannels;

            // 本次触发通道对应的“当前alphaRel”
            public double[] TriggeredAlphaRelative;

            // 仅模式2：这些通道在“上一次模式1基准”的 alphaRel（与 TriggeredOriginalChannels 一一对应）
            public double[] PreviousAlphaRelative;
        }

        public event EventHandler<ResultEventArgs> OnStage2Evaluated;
        public event EventHandler<ResultEventArgs> OnStage2Triggered;

        private readonly Config _cfg;
        private BlockingCollection<Job> _queue;
        private CancellationTokenSource _cts;
        private Task _worker;

        private volatile bool _stopProcessing;

        // ===== 模式2基准：来自“上一次模式1触发”的那组通道 =====
        private bool _mode2Pending;                 // 是否等待“下一次window”进行模式2比较
        private int[] _mode1BaselineCh;             // 原始通道号集合
        private double[] _mode1BaselineAlphaRel;    // 上一次对应通道的alphaRel

        private sealed class Job
        {
            public double[][] Window;     // [m][n]  m=参与通道数（变长）
            public int[] Indices;         // [m] 对应原始通道号
            public long Start;
            public long End;
        }

        public BandPowerRatioDetector(Config cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            if (_cfg.ChannelCount <= 0) throw new ArgumentException("ChannelCount must be > 0");
            if (_cfg.Fs <= 0) throw new ArgumentException("Fs must be > 0");
            if (_cfg.MinChannelsToTrigger < 1) throw new ArgumentException("MinChannelsToTrigger must be >= 1");
            if (_cfg.Mode2IncreaseRatio < 0) throw new ArgumentException("Mode2IncreaseRatio must be >= 0");

            ValidateBands(_cfg);
        }

        private static void ValidateBands(Config c)
        {
            void Check(double lo, double hi, string name)
            {
                if (lo < 0 || hi <= 0 || hi <= lo)
                    throw new ArgumentException($"{name} band invalid: [{lo}, {hi}]");
            }

            Check(c.DeltaLow, c.DeltaHigh, "Delta");
            Check(c.ThetaLow, c.ThetaHigh, "Theta");
            Check(c.AlphaLow, c.AlphaHigh, "Alpha");
            Check(c.BetaLow, c.BetaHigh, "Beta");

            if (c.AlphaRelativeThreshold < 0 || c.AlphaRelativeThreshold > 1.0)
                throw new ArgumentException("AlphaRelativeThreshold must be in [0,1]");
        }

        public void Start()
        {
            if (_worker != null) return;

            _queue = new BlockingCollection<Job>(new ConcurrentQueue<Job>(), _cfg.QueueCapacity);
            _cts = new CancellationTokenSource();
            _stopProcessing = false;

            _mode2Pending = false;
            _mode1BaselineCh = null;
            _mode1BaselineAlphaRel = null;

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

        /// <summary>
        /// 兼容旧接口：不传 ChannelIndices 时，默认认为 window[0]..window[m-1] 对应通道 0..m-1
        /// </summary>
        public void PushWindow(double[][] window, long startSample, long endSample)
        {
            if (window == null || window.Length == 0) return;
            var idx = new int[window.Length];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            PushWindow(window, idx, startSample, endSample);
        }

        /// <summary>
        /// ✅ 推荐接口：window 只包含“通过 Stage1 的通道”，indices 指明它们对应的原始通道号
        /// </summary>
        public void PushWindow(double[][] window, int[] channelIndices, long startSample, long endSample)
        {
            if (window == null || window.Length == 0) return;
            if (channelIndices == null || channelIndices.Length != window.Length) return;
            if (_queue == null || _queue.IsAddingCompleted) return;

            _queue.TryAdd(new Job
            {
                Window = window,
                Indices = channelIndices,
                Start = startSample,
                End = endSample
            });
        }

        private void WorkerLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_queue.IsCompleted)
            {
                Job job = null;
                try
                {
                    if (!_queue.TryTake(out job, 100, ct)) continue;
                }
                catch (OperationCanceledException) { break; }

                if (job?.Window == null || job.Window.Length == 0) continue;
                if (job.Indices == null || job.Indices.Length != job.Window.Length) continue;
                if (_stopProcessing) continue;

                int m = job.Window.Length;

                var pDelta = new double[m];
                var pTheta = new double[m];
                var pAlpha = new double[m];
                var pBeta = new double[m];
                var alphaRel = new double[m];

                var alphaRelOrig = new double[_cfg.ChannelCount];
                for (int i = 0; i < alphaRelOrig.Length; i++) alphaRelOrig[i] = double.NaN;

                int passed = 0;

                for (int k = 0; k < m; k++)
                {
                    var x = job.Window[k];

                    double d = 0, t = 0, a = 0, b = 0, rel = 0;

                    if (x != null && x.Length >= 8)
                    {
                        d = BandPowerFFT(x, _cfg.DeltaLow, _cfg.DeltaHigh, _cfg.Fs);
                        t = BandPowerFFT(x, _cfg.ThetaLow, _cfg.ThetaHigh, _cfg.Fs);
                        a = BandPowerFFT(x, _cfg.AlphaLow, _cfg.AlphaHigh, _cfg.Fs);
                        b = BandPowerFFT(x, _cfg.BetaLow, _cfg.BetaHigh, _cfg.Fs);

                        double total = d + t + a + b;
                        rel = (total <= 1e-12) ? 0.0 : (a / total);
                    }

                    pDelta[k] = d;
                    pTheta[k] = t;
                    pAlpha[k] = a;
                    pBeta[k] = b;
                    alphaRel[k] = rel;

                    int origCh = job.Indices[k];
                    if (origCh >= 0 && origCh < _cfg.ChannelCount)
                        alphaRelOrig[origCh] = rel;

                    if (rel >= _cfg.AlphaRelativeThreshold) passed++;
                }

                // ===== 先发 Evaluated（不带触发模式）=====
                var evalArgs = BuildArgs(job, pDelta, pTheta, pAlpha, pBeta, alphaRel, alphaRelOrig, passed,
                                        triggerMode: 0,
                                        trigCh: null, trigCur: null, trigPrev: null);
                OnStage2Evaluated?.Invoke(this, evalArgs);

                // ===== 模式2：只检查“上一轮模式1命中的那些通道”，并且只检查一次（下一次window）=====
                if (_cfg.EnableMode2 && _mode2Pending)
                {
                    // “后一次”机会无论成功失败，都消耗掉
                    _mode2Pending = false;

                    if (TryTriggerMode2(job, alphaRel, out int[] mode2Ch, out double[] prevRel, out double[] curRel))
                    {
                        var mode2Args = BuildArgs(job, pDelta, pTheta, pAlpha, pBeta, alphaRel, alphaRelOrig, passed,
                                                  triggerMode: 2,
                                                  trigCh: mode2Ch, trigCur: curRel, trigPrev: prevRel);

                        OnStage2Triggered?.Invoke(this, mode2Args);

                        if (_cfg.StopAfterTrigger) _stopProcessing = true;
                    }
                }

                // ===== 模式1：本次window中，超阈值通道数 >= MinChannelsToTrigger =====
                if (passed >= _cfg.MinChannelsToTrigger)
                {
                    // 选出本次超阈值的“原始通道号集合”，作为模式2基准
                    ExtractMode1Baseline(job, alphaRel, out int[] mode1Ch, out double[] mode1Rel);

                    // 保存为下一次window的比较基准（通道必须按原始通道对应）
                    _mode1BaselineCh = mode1Ch;
                    _mode1BaselineAlphaRel = mode1Rel;
                    _mode2Pending = true;

                    var mode1Args = BuildArgs(job, pDelta, pTheta, pAlpha, pBeta, alphaRel, alphaRelOrig, passed,
                                              triggerMode: 1,
                                              trigCh: mode1Ch, trigCur: mode1Rel, trigPrev: null);

                    OnStage2Triggered?.Invoke(this, mode1Args);

                    if (_cfg.StopAfterTrigger) _stopProcessing = true;
                }
            }
        }

        private ResultEventArgs BuildArgs(
            Job job,
            double[] pDelta, double[] pTheta, double[] pAlpha, double[] pBeta,
            double[] alphaRel, double[] alphaRelOrig,
            int passed,
            int triggerMode,
            int[] trigCh,
            double[] trigCur,
            double[] trigPrev)
        {
            // 兼容旧字段：Ratio = AlphaRelative
            return new ResultEventArgs
            {
                WindowStartSample = job.Start,
                WindowEndSample = job.End,

                RatioPerWindowChannel = (double[])alphaRel.Clone(),
                ChannelIndices = (int[])job.Indices.Clone(),
                RatioPerOriginalChannel = (double[])alphaRelOrig.Clone(),

                PDeltaPerWindowChannel = pDelta,
                PThetaPerWindowChannel = pTheta,
                PAlphaPerWindowChannel = pAlpha,
                PBetaPerWindowChannel = pBeta,

                AlphaRelativePerWindowChannel = alphaRel,
                AlphaRelativePerOriginalChannel = alphaRelOrig,

                PassedChannels = passed,

                TriggerMode = triggerMode,
                TriggeredOriginalChannels = trigCh,
                TriggeredAlphaRelative = trigCur,
                PreviousAlphaRelative = trigPrev
            };
        }

        /// <summary>
        /// 模式1基准：提取本次 alphaRel >= threshold 的通道（按“原始通道号”保存）及其 alphaRel
        /// </summary>
        private void ExtractMode1Baseline(Job job, double[] alphaRel, out int[] baselineCh, out double[] baselineRel)
        {
            var chList = new List<int>();
            var relList = new List<double>();

            for (int k = 0; k < job.Indices.Length; k++)
            {
                if (alphaRel[k] >= _cfg.AlphaRelativeThreshold)
                {
                    int origCh = job.Indices[k];
                    if (origCh < 0 || origCh >= _cfg.ChannelCount) continue;

                    chList.Add(origCh);
                    relList.Add(alphaRel[k]);
                }
            }

            baselineCh = chList.ToArray();
            baselineRel = relList.ToArray();
        }

        /// <summary>
        /// 模式2：下一次window里，要求“上一次模式1基准通道”的 alphaRel 都比上次对应通道高 (1+ratio)
        /// 注意：通道按原始通道号一一对应；若当前window缺少某个通道，则直接失败
        /// </summary>
        private bool TryTriggerMode2(Job job, double[] alphaRel, out int[] ch, out double[] prevRel, out double[] curRel)
        {
            ch = null;
            prevRel = null;
            curRel = null;

            if (_mode1BaselineCh == null || _mode1BaselineAlphaRel == null) return false;
            if (_mode1BaselineCh.Length == 0) return false;
            if (_mode1BaselineCh.Length != _mode1BaselineAlphaRel.Length) return false;

            // 当前window：构建 origCh -> currentAlphaRel 映射
            var map = new Dictionary<int, double>(job.Indices.Length);
            for (int k = 0; k < job.Indices.Length; k++)
            {
                int origCh = job.Indices[k];
                if (origCh < 0 || origCh >= _cfg.ChannelCount) continue;
                // 若重复，后者覆盖即可
                map[origCh] = alphaRel[k];
            }

            double factor = 1.0 + _cfg.Mode2IncreaseRatio;

            int n = _mode1BaselineCh.Length;
            var cur = new double[n];
            var prev = new double[n];

            for (int i = 0; i < n; i++)
            {
                int origCh = _mode1BaselineCh[i];

                // 必须找到对应通道
                if (!map.TryGetValue(origCh, out double now)) return false;

                double last = _mode1BaselineAlphaRel[i];

                // last 理论上 >= threshold，不会太小；但还是保护一下
                if (last <= 1e-12) return false;

                // 要求提升至少 20%：now >= last * 1.2
                if (now < last * factor) return false;

                cur[i] = now;
                prev[i] = last;
            }

            ch = (int[])_mode1BaselineCh.Clone();
            prevRel = prev;
            curRel = cur;
            return true;
        }

        // ===== 带功率：Hann窗 + 零填充到2^k + FFT + 频带积分 =====
        private static double BandPowerFFT(double[] x, double fLow, double fHigh, double fs)
        {
            int n = x.Length;
            int nfft = NextPow2(n);
            if (nfft < 64) nfft = 64;

            // 去均值
            double mean = 0;
            for (int i = 0; i < n; i++) mean += x[i];
            mean /= n;

            var buf = new Complex[nfft];

            // Hann 窗
            if (n == 1)
            {
                buf[0] = new Complex(x[0] - mean, 0.0);
                for (int i = 1; i < nfft; i++) buf[i] = Complex.Zero;
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    double w = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (n - 1));
                    buf[i] = new Complex((x[i] - mean) * w, 0.0);
                }
                for (int i = n; i < nfft; i++) buf[i] = Complex.Zero;
            }

            FFT(buf, inverse: false);

            int k1 = (int)Math.Floor(fLow * nfft / fs);
            int k2 = (int)Math.Ceiling(fHigh * nfft / fs);
            int kMax = nfft / 2;

            if (k1 < 0) k1 = 0;
            if (k2 > kMax) k2 = kMax;
            if (k2 < k1) return 0;

            double sum = 0.0;
            for (int k = k1; k <= k2; k++)
            {
                double re = buf[k].Real;
                double im = buf[k].Imaginary;
                sum += (re * re + im * im);
            }
            return sum;
        }

        private static int NextPow2(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }

        // 标准 Cooley-Tukey radix-2 FFT
        private static void FFT(Complex[] a, bool inverse)
        {
            int n = a.Length;

            // bit reversal
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;

                if (i < j)
                {
                    var tmp = a[i];
                    a[i] = a[j];
                    a[j] = tmp;
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2.0 * Math.PI / len * (inverse ? 1 : -1);
                Complex wlen = new Complex(Math.Cos(ang), Math.Sin(ang));

                for (int i = 0; i < n; i += len)
                {
                    Complex w = Complex.One;
                    int half = len >> 1;
                    for (int j = 0; j < half; j++)
                    {
                        Complex u = a[i + j];
                        Complex v = a[i + j + half] * w;
                        a[i + j] = u + v;
                        a[i + j + half] = u - v;
                        w *= wlen;
                    }
                }
            }

            if (inverse)
            {
                for (int i = 0; i < n; i++) a[i] /= n;
            }
        }
    }
}
