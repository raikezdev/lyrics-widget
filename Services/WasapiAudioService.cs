using System;
using NAudio.Wave;

namespace LyricsWidget.Services
{
    public class WasapiAudioService : IDisposable
    {
        private WasapiLoopbackCapture? _capture;
        private bool _isDisposed;
        private readonly int _bandsCount = 16;
        private readonly float[] _bandLevels;
        private float _peakLevel;
        private readonly object _lock = new();

        public WasapiAudioService(int bandsCount = 16)
        {
            _bandsCount = bandsCount;
            _bandLevels = new float[_bandsCount];
        }

        public void Start()
        {
            try
            {
                _capture = new WasapiLoopbackCapture();
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WASAPI loopback initialization failed: {ex.Message}");
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (!_isDisposed)
            {
                System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
                {
                    if (!_isDisposed)
                    {
                        try
                        {
                            _capture?.Dispose();
                            Start();
                        }
                        catch { }
                    }
                });
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0 || _capture == null) return;

            var waveFormat = _capture.WaveFormat;
            int bytesPerSample = waveFormat.BitsPerSample / 8;
            int channels = waveFormat.Channels;
            int sampleCount = e.BytesRecorded / bytesPerSample;

            if (sampleCount == 0) return;

            float sumSquares = 0f;
            int usableSamples = Math.Min(sampleCount, 1024);
            float[] monoSamples = new float[usableSamples / channels];
            int monoIndex = 0;

            if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                for (int i = 0; i < usableSamples - channels; i += channels)
                {
                    int offset = i * bytesPerSample;
                    if (offset + sizeof(float) <= e.BytesRecorded)
                    {
                        float sample = BitConverter.ToSingle(e.Buffer, offset);
                        if (monoIndex < monoSamples.Length)
                        {
                            monoSamples[monoIndex++] = sample;
                        }
                        sumSquares += sample * sample;
                    }
                }
            }
            else if (waveFormat.BitsPerSample == 16)
            {
                for (int i = 0; i < usableSamples - channels; i += channels)
                {
                    int offset = i * 2;
                    if (offset + 2 <= e.BytesRecorded)
                    {
                        short sample16 = BitConverter.ToInt16(e.Buffer, offset);
                        float sample = sample16 / 32768.0f;
                        if (monoIndex < monoSamples.Length)
                        {
                            monoSamples[monoIndex++] = sample;
                        }
                        sumSquares += sample * sample;
                    }
                }
            }

            float rms = (float)Math.Sqrt(sumSquares / Math.Max(1, monoIndex));
            float currentPeak = Math.Clamp(rms * 3.5f, 0f, 1f);

            lock (_lock)
            {
                _peakLevel = (_peakLevel * 0.7f) + (currentPeak * 0.3f);
                ComputeBands(monoSamples, monoIndex);
            }
        }

        private void ComputeBands(float[] samples, int count)
        {
            if (count < 16) return;

            int segmentSize = Math.Max(1, count / _bandsCount);

            for (int b = 0; b < _bandsCount; b++)
            {
                int start = b * segmentSize;
                int end = Math.Min(start + segmentSize, count);
                float bandSum = 0f;

                for (int s = start; s < end; s++)
                {
                    bandSum += Math.Abs(samples[s]);
                }

                float avg = bandSum / Math.Max(1, (end - start));
                float freqBoost = 1.0f + (b * 0.18f);
                float currentVal = Math.Clamp(avg * 4.5f * freqBoost, 0f, 1f);

                if (currentVal > _bandLevels[b])
                {
                    _bandLevels[b] = currentVal;
                }
                else
                {
                    _bandLevels[b] = (_bandLevels[b] * 0.75f) + (currentVal * 0.25f);
                }
            }
        }

        public void GetLatestAudioData(float[] destinationBands, out float peak)
        {
            lock (_lock)
            {
                peak = _peakLevel;
                int copyLen = Math.Min(destinationBands.Length, _bandLevels.Length);
                Array.Copy(_bandLevels, destinationBands, copyLen);
            }
        }

        public void Stop()
        {
            try
            {
                _capture?.StopRecording();
            }
            catch { }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                Stop();
                _capture?.Dispose();
            }
            catch { }
        }
    }
}
