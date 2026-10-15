using System;
using System.Collections.Generic;
using System.IO;

namespace ISBSSOFExtra
{
    internal sealed class AlarmLightPulse
    {
        private readonly float[] _values;
        private readonly int _rate;
        private readonly int _window;
        private readonly long _frames;
        private AlarmLightPulse(float[] values, int rate, int window, long frames)
        {
            _values = values;
            _rate = rate;
            _window = window;
            _frames = frames;
        }

        internal static AlarmLightPulse Read(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != 0x46464952)
                    return null;
                reader.ReadUInt32();
                if (reader.ReadUInt32() != 0x45564157)
                    return null;
                int format = 0, channels = 0, rate = 0, bits = 0;
                long offset = 0, length = 0;
                while (stream.Position + 8 <= stream.Length)
                {
                    uint kind = reader.ReadUInt32(), size = reader.ReadUInt32();
                    long start = stream.Position, end = Math.Min(stream.Length, start + size);
                    if (kind == 0x20746d66 && size >= 16)
                    {
                        format = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        rate = reader.ReadInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt16();
                        bits = reader.ReadUInt16();
                    }

                    if (kind == 0x61746164)
                    {
                        offset = start;
                        length = end - start;
                    }

                    stream.Position = Math.Min(stream.Length, end + (size & 1));
                }

                if (format != 1 || bits != 16 || channels < 1 || channels > 8 || rate < 8000 || length == 0)
                    return null;
                stream.Position = offset;
                int window = Math.Max(1, rate / 40), bytesPerFrame = channels * 2;
                long frames = length / bytesPerFrame;
                var levels = new List<float>();
                var buffer = new byte[window * bytesPerFrame];
                long remaining = frames * bytesPerFrame;
                while (remaining > 0)
                {
                    int count = (int)Math.Min(buffer.Length, remaining), read = 0;
                    while (read < count)
                    {
                        int n = stream.Read(buffer, read, count - read);
                        if (n == 0)
                            throw new EndOfStreamException();
                        read += n;
                    }

                    double energy = 0;
                    for (int i = 0; i + 1 < count; i += 2)
                    {
                        double sample = (short)(buffer[i] | (buffer[i + 1] << 8)) / 32768d;
                        energy += sample * sample;
                    }

                    levels.Add((float)Math.Sqrt(energy / (count / 2)));
                    remaining -= count;
                }

                var values = levels.ToArray();
                var smooth = new float[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    int first = Math.Max(0, i - 2), last = Math.Min(values.Length - 1, i + 2);
                    for (int j = first; j <= last; j++)
                        smooth[i] += values[j];
                    smooth[i] /= last - first + 1;
                }

                var sorted = (float[])smooth.Clone();
                Array.Sort(sorted);
                float peak = sorted[(int)((sorted.Length - 1) * .95)];
                var gates = new float[values.Length];
                bool on = false;
                int high = 0, low = 0;
                for (int i = 0; i < smooth.Length; i++)
                {
                    float bottom = float.MaxValue, top = 0f;
                    for (int j = Math.Max(0, i - 80); j <= Math.Min(smooth.Length - 1, i + 80); j++)
                    {
                        bottom = Math.Min(bottom, smooth[j]);
                        top = Math.Max(top, smooth[j]);
                    }

                    float span = Math.Max(top - bottom, peak * .15f);
                    bool strong = top > Math.Max(.00001f, peak * .3f) && smooth[i] >= bottom + span * .70f;
                    bool weak = smooth[i] < bottom + span * .45f || top <= .00001f;
                    high = strong ? high + 1 : 0;
                    low = weak ? low + 1 : 0;
                    if (!on && high >= 2)
                    {
                        on = true;
                        gates[i - 1] = 1f;
                    }
                    else if (on && low >= 4)
                    {
                        on = false;
                        for (int j = i - 3; j < i; j++)
                            gates[j] = 0f;
                    }

                    gates[i] = on ? 1f : 0f;
                }

                for (int i = 0; i < gates.Length;)
                {
                    int start = i;
                    float state = gates[i];
                    while (i < gates.Length && gates[i] == state)
                        i++;
                    if (state == 0f && start > 0 && i < gates.Length && i - start < 6)
                        for (int j = start; j < i; j++)
                            gates[j] = 1f;
                }

                for (int i = 0; i < gates.Length;)
                {
                    int start = i;
                    float state = gates[i];
                    while (i < gates.Length && gates[i] == state)
                        i++;
                    if (state == 1f && i - start < 8)
                        for (int j = start; j < i; j++)
                            gates[j] = 0f;
                }

                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = 0f;
                    for (int j = i - 2; j <= i + 2; j++)
                        values[i] += gates[Math.Max(0, Math.Min(gates.Length - 1, j))] / 5f;
                }

                return new AlarmLightPulse(values, rate, window, frames);
            }
        }

        internal float Value(double seconds)
        {
            if (_values.Length == 0 || seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                return 0f;
            double frame = (seconds * _rate % _frames) / _window - .5d;
            int index = (int)Math.Floor(frame);
            float fraction = (float)(frame - index);
            float a = _values[Math.Max(0, Math.Min(_values.Length - 1, index))];
            float b = _values[Math.Max(0, Math.Min(_values.Length - 1, index + 1))];
            return (float)Math.Round((a + (b - a) * fraction) * 24f) / 24f;
        }
    }
}
