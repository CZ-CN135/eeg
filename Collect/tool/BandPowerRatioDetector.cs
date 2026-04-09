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
            public int ChannelCount = 8;
            public double Fs = 500;

            public double DeltaLow = 0.5;
            public double DeltaHigh = 4.0;

            public double ThetaLow = 4.0;
            public double ThetaHigh = 8.0;

            public double AlphaLow = 8.0;
            public double AlphaHigh = 13.0;

            public double BetaLow = 13.0;
            public double BetaHigh = 100.0;

            // 你当前用的：avgDeltaRel 低于阈值且 avgBetaRel 高于阈值
            public double DeltaRelativeThreshold = 0.35;
            public double BetaRelativeThreshold = 0.35;

            // 参与通道里至少多少个通道满足 (deltaRel<thr && betaRel>thr) 才允许触发
            public int MinChannelsToTrigger = 1;

            public double Mode2IncreaseRatio = 0.20; // 目前未参与判定（保留字段不删）
            public bool EnableMode2 = true;

            public int QueueCapacity = 32;

            public bool StopAfterTrigger = false;
        }

        public sealed class ResultEventArgs : EventArgs
        {
            public long WindowStartSample;
            public long WindowEndSample;

            // window通道 -> 原始通道映射（长度=m）
            public int[] ChannelIndices;

            // ===== 绝对带功率（长度=m）=====
            public double[] PDeltaPerWindowChannel;
            public double[] PThetaPerWindowChannel;
            public double[] PAlphaPerWindowChannel;
            public double[] PBetaPerWindowChannel;

            // ===== 相对带功率（长度=m）=====
            public double[] DeltaRelativePerWindowChannel;
            public double[] ThetaRelativePerWindowChannel;
            public double[] AlphaRelativePerWindowChannel;
            public double[] BetaRelativePerWindowChannel;

            // ===== 相对带功率：映射回原始通道（长度=ChannelCount，未参与=NaN）=====
            public double[] DeltaRelativePerOriginalChannel;
            public double[] ThetaRelativePerOriginalChannel;
            public double[] AlphaRelativePerOriginalChannel;
            public double[] BetaRelativePerOriginalChannel;

            // ===== 兼容字段（保留不乱用）：Ratio == AlphaRelative =====
            public double[] RatioPerWindowChannel;     // == AlphaRelativePerWindowChannel
            public double[] RatioPerOriginalChannel;   // == AlphaRelativePerOriginalChannel

            // ===== 本窗统计 =====
            public int ActiveChannels;     // 实际参与FFT计算且total>0的通道数
            public int PassedChannels;     // 满足 (deltaRel<thr && betaRel>thr) 的通道数
            public double AvgDeltaRelative;
            public double AvgBetaRelative;
            public double AvgThetaRelative;
            public double AvgAlphaRelative;

            // ===== 触发信息 =====
            // 0=未触发（仅Evaluated）
            // 1=趋势触发（与上一窗相比 delta↓ 且 beta↑）
            // 2=阈值触发（avgDeltaRel<thr 且 avgBetaRel>thr 且 passed>=MinChannelsToTrigger）
            public int TriggerMode;

            // 本次触发涉及的原始通道号（通常是满足passed条件的那几个）
            public int[] TriggeredOriginalChannels;

            public double[] TriggeredDeltaRelative;
            public double[] TriggeredBetaRelative;

            public double? PreviousAvgDeltaRelative;
            public double? PreviousAvgBetaRelative;
        }

        public event EventHandler<ResultEventArgs> OnStage2Evaluated;
        public event EventHandler<ResultEventArgs> OnStage2Triggered;

        private readonly Config _cfg;
        private BlockingCollection<Job> _queue;
        private CancellationTokenSource _cts;
        private Task _worker;
        private volatile bool _stopProcessing;

        // 上一窗平均值（用于趋势触发）
        private double? _previousAvgDeltaRel = null;
        private double? _previousAvgBetaRel = null;

        private sealed class Job
        {
            public double[][] Window; // [m][n]
            public int[] Indices;     // [m] 原始通道号
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

            if (c.DeltaRelativeThreshold < 0 || c.DeltaRelativeThreshold > 1.0)
                throw new ArgumentException("DeltaRelativeThreshold must be in [0,1]");
            if (c.BetaRelativeThreshold < 0 || c.BetaRelativeThreshold > 1.0)
                throw new ArgumentException("BetaRelativeThreshold must be in [0,1]");
        }

        public void Start()
        {
            if (_worker != null) return;

            _queue = new BlockingCollection<Job>(new ConcurrentQueue<Job>(), _cfg.QueueCapacity);
            _cts = new CancellationTokenSource();
            _stopProcessing = false;

            _previousAvgDeltaRel = null;
            _previousAvgBetaRel = null;

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

                var deltaRel = new double[m];
                var thetaRel = new double[m];
                var alphaRel = new double[m];
                var betaRel = new double[m];

                // 映射回原始通道（未参与=NaN）
                var deltaRelOrig = NewNaNArray(_cfg.ChannelCount);
                var thetaRelOrig = NewNaNArray(_cfg.ChannelCount);
                var alphaRelOrig = NewNaNArray(_cfg.ChannelCount);
                var betaRelOrig = NewNaNArray(_cfg.ChannelCount);

                double sumDeltaRel = 0.0, sumBetaRel = 0.0, sumThetaRel = 0.0, sumAlphaRel = 0.0;
                int activeChannels = 0;

                int passed = 0;
                var trigOrig = new List<int>();
                var trigDelta = new List<double>();
                var trigBeta = new List<double>();

                for (int k = 0; k < m; k++)
                {
                    var x = job.Window[k];

                    double d = 0, t = 0, a = 0, b = 0;
                    double deltaRelValue = 0, thetaRelValue = 0, alphaRelValue = 0, betaRelValue = 0;

                    if (x != null && x.Length >= 8)
                    {
                        d = BandPowerFFT(x, _cfg.DeltaLow, _cfg.DeltaHigh, _cfg.Fs);
                        t = BandPowerFFT(x, _cfg.ThetaLow, _cfg.ThetaHigh, _cfg.Fs);
                        a = BandPowerFFT(x, _cfg.AlphaLow, _cfg.AlphaHigh, _cfg.Fs);
                        b = BandPowerFFT(x, _cfg.BetaLow, _cfg.BetaHigh, _cfg.Fs);

                        double total = d + t + a + b;
                        if (total > 1e-12)
                        {
                            deltaRelValue = d / total;
                            thetaRelValue = t / total;
                            alphaRelValue = a / total;
                            betaRelValue = b / total;

                            sumDeltaRel += deltaRelValue;
                            sumThetaRel += thetaRelValue;
                            sumAlphaRel += alphaRelValue;
                            sumBetaRel += betaRelValue;
                            activeChannels++;
                        }
                    }

                    pDelta[k] = d;
                    pTheta[k] = t;
                    pAlpha[k] = a;
                    pBeta[k] = b;

                    deltaRel[k] = deltaRelValue;
                    thetaRel[k] = thetaRelValue;
                    alphaRel[k] = alphaRelValue;
                    betaRel[k] = betaRelValue;

                    // ===== deltaRelOrig 的含义：把“参与通道k”的结果，放到“原始通道号origCh”的位置上 =====
                    int origCh = job.Indices[k];
                    if (origCh >= 0 && origCh < _cfg.ChannelCount)
                    {
                        deltaRelOrig[origCh] = deltaRelValue;
                        thetaRelOrig[origCh] = thetaRelValue;
                        alphaRelOrig[origCh] = alphaRelValue;
                        betaRelOrig[origCh] = betaRelValue;
                    }

                    // per-channel passed：deltaRel<thr 且 betaRel>thr
                    if (deltaRelValue < _cfg.DeltaRelativeThreshold && betaRelValue > _cfg.BetaRelativeThreshold)
                    {
                        passed++;
                        if (origCh >= 0 && origCh < _cfg.ChannelCount)
                        {
                            trigOrig.Add(origCh);
                            trigDelta.Add(deltaRelValue);
                            trigBeta.Add(betaRelValue);
                        }
                    }
                }

                double avgDeltaRel = activeChannels > 0 ? sumDeltaRel / activeChannels : 0.0;
                double avgBetaRel = activeChannels > 0 ? sumBetaRel / activeChannels : 0.0;
                double avgThetaRel = activeChannels > 0 ? sumThetaRel / activeChannels : 0.0;
                double avgAlphaRel = activeChannels > 0 ? sumAlphaRel / activeChannels : 0.0;

                // ===== 先发 Evaluated =====
                var evalArgs = BuildArgs(
                    job,
                    pDelta, pTheta, pAlpha, pBeta,
                    deltaRel, thetaRel, alphaRel, betaRel,
                    deltaRelOrig, thetaRelOrig, alphaRelOrig, betaRelOrig,
                    activeChannels, passed, avgDeltaRel, avgBetaRel, avgThetaRel, avgAlphaRel,
                    triggerMode: 0,
                    trigCh: null, trigDelta: null, trigBeta: null,
                    prevAvgDelta: _previousAvgDeltaRel, prevAvgBeta: _previousAvgBetaRel
                );
                OnStage2Evaluated?.Invoke(this, evalArgs);

                // ===== 触发判定 =====
                bool thresholdHit = (avgDeltaRel < _cfg.DeltaRelativeThreshold) &&
                                    (avgBetaRel > _cfg.BetaRelativeThreshold) &&
                                    (passed >= _cfg.MinChannelsToTrigger);

                bool trendHit = _previousAvgDeltaRel.HasValue && _previousAvgBetaRel.HasValue &&
                                (avgDeltaRel < _previousAvgDeltaRel.Value) &&
                                (avgDeltaRel < _cfg.DeltaRelativeThreshold) &&
                                (avgBetaRel > _previousAvgBetaRel.Value) &&
                                (avgBetaRel > _cfg.BetaRelativeThreshold);

                int triggerMode = 0;
                if (thresholdHit) triggerMode = 2;
                else if (trendHit) triggerMode = 1;

                if (triggerMode != 0)
                {
                    // 若 passed 列表为空（极少见），兜底：用全部参与通道
                    int[] trigCh = trigOrig.Count > 0 ? trigOrig.ToArray() : (int[])job.Indices.Clone();
                    double[] trigD = trigDelta.Count > 0 ? trigDelta.ToArray() : (double[])deltaRel.Clone();
                    double[] trigB = trigBeta.Count > 0 ? trigBeta.ToArray() : (double[])betaRel.Clone();

                    var trigArgs = BuildArgs(
                        job,
                        pDelta, pTheta, pAlpha, pBeta,
                        deltaRel, thetaRel, alphaRel, betaRel,
                        deltaRelOrig, thetaRelOrig, alphaRelOrig, betaRelOrig,
                        activeChannels, passed, avgDeltaRel, avgBetaRel, avgThetaRel, avgAlphaRel,
                        triggerMode,
                        trigCh, trigD, trigB,
                        prevAvgDelta: _previousAvgDeltaRel, prevAvgBeta: _previousAvgBetaRel
                    );

                    OnStage2Triggered?.Invoke(this, trigArgs);

                    if (_cfg.StopAfterTrigger) _stopProcessing = true;
                }

                // ===== 更新上一窗平均值（用于下一窗趋势比较）=====
                _previousAvgDeltaRel = avgDeltaRel;
                _previousAvgBetaRel = avgBetaRel;
            }
        }

        private ResultEventArgs BuildArgs(
            Job job,
            double[] pDelta, double[] pTheta, double[] pAlpha, double[] pBeta,
            double[] deltaRel, double[] thetaRel, double[] alphaRel, double[] betaRel,
            double[] deltaRelOrig, double[] thetaRelOrig, double[] alphaRelOrig, double[] betaRelOrig,
            int activeChannels,
            int passed,
            double avgDeltaRel, double avgBetaRel, double avgThetaRel, double avgAlphaRel,
            int triggerMode,
            int[] trigCh,
            double[] trigDelta,
            double[] trigBeta,
            double? prevAvgDelta,
            double? prevAvgBeta)
        {
            return new ResultEventArgs
            {
                WindowStartSample = job.Start,
                WindowEndSample = job.End,

                ChannelIndices = (int[])job.Indices.Clone(),

                PDeltaPerWindowChannel = (double[])pDelta.Clone(),
                PThetaPerWindowChannel = (double[])pTheta.Clone(),
                PAlphaPerWindowChannel = (double[])pAlpha.Clone(),
                PBetaPerWindowChannel = (double[])pBeta.Clone(),

                DeltaRelativePerWindowChannel = (double[])deltaRel.Clone(),
                ThetaRelativePerWindowChannel = (double[])thetaRel.Clone(),
                AlphaRelativePerWindowChannel = (double[])alphaRel.Clone(),
                BetaRelativePerWindowChannel = (double[])betaRel.Clone(),

                DeltaRelativePerOriginalChannel = (double[])deltaRelOrig.Clone(),
                ThetaRelativePerOriginalChannel = (double[])thetaRelOrig.Clone(),
                AlphaRelativePerOriginalChannel = (double[])alphaRelOrig.Clone(),
                BetaRelativePerOriginalChannel = (double[])betaRelOrig.Clone(),

                // 兼容字段：Ratio = AlphaRelative
                RatioPerWindowChannel = (double[])alphaRel.Clone(),
                RatioPerOriginalChannel = (double[])alphaRelOrig.Clone(),

                ActiveChannels = activeChannels,
                PassedChannels = passed,
                AvgDeltaRelative = avgDeltaRel,
                AvgBetaRelative = avgBetaRel,
                AvgThetaRelative = avgThetaRel,
                AvgAlphaRelative = avgAlphaRel,

                TriggerMode = triggerMode,
                TriggeredOriginalChannels = trigCh,
                TriggeredDeltaRelative = trigDelta,
                TriggeredBetaRelative = trigBeta,
                PreviousAvgDeltaRelative = prevAvgDelta,
                PreviousAvgBetaRelative = prevAvgBeta
            };
        }

        private static double[] NewNaNArray(int n)
        {
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = double.NaN;
            return a;
        }

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
