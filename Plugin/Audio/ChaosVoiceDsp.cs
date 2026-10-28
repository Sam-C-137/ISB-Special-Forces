using System;

namespace ISBSpecialForces.Audio
{
    internal static class ChaosVoiceDsp
    {
        internal const double PitchRatio = .7937005259840998;

        private sealed class Stage
        {
            private readonly double b0, b1, b2, a1, a2;
            private double z1, z2;
            internal Stage(int rate, double frequency, int kind, double db = 0, double q = .7071067811865476)
            {
                double w = 2 * Math.PI * frequency / rate, c = Math.Cos(w), s = Math.Sin(w);
                if (kind == 3)
                {
                    double k = Math.Tan(w * .5);
                    b0 = b1 = k / (1 + k); a1 = (k - 1) / (k + 1);
                    return;
                }
                double alpha = s / (2 * q), a0 = 1 + alpha;
                a1 = -2 * c / a0; a2 = (1 - alpha) / a0;
                if (kind == 0) { b0 = (1 + c) / (2 * a0); b1 = -(1 + c) / a0; b2 = b0; }
                else if (kind == 1) { b0 = (1 - c) / (2 * a0); b1 = (1 - c) / a0; b2 = b0; }
                else
                {
                    double gain = Math.Pow(10, db / 40);
                    a0 = 1 + alpha / gain;
                    b0 = (1 + alpha * gain) / a0; b1 = -2 * c / a0; b2 = (1 - alpha * gain) / a0;
                    a1 = -2 * c / a0; a2 = (1 - alpha / gain) / a0;
                }
            }
            internal double Run(double x)
            {
                double y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
        }

        internal static float[] Render(float[] input, int channels, int rate)
        {
            if (input == null || channels < 1 || channels > 2 || input.Length % channels != 0 || rate < 8000 || rate > 192000)
                throw new ArgumentException("Invalid speech sample format.");
            int frames = input.Length / channels;
            var clean = new float[input.Length];
            double power = 0;
            for (int i = 0; i < input.Length; i++)
            {
                float x = input[i];
                clean[i] = float.IsNaN(x) || float.IsInfinity(x) ? 0 : x;
                power += clean[i] * (double)clean[i];
            }
            if (frames == 0 || power < 1e-18) return clean;
            var output = ShiftPitch(clean, channels, rate);
            for (int channel = 0; channel < channels; channel++) Color(output, channel, channels, rate);
            double sum = 0, peak = 0;
            for (int i = 0; i < output.Length; i++) sum += output[i] * (double)output[i];
            double gain = Math.Pow(10, -18d / 20) / Math.Max(1e-9, Math.Sqrt(sum / output.Length));
            for (int i = 0; i < output.Length; i++) peak = Math.Max(peak, Math.Abs(output[i] * gain));
            gain *= Math.Min(1, .95 / Math.Max(1e-9, peak));
            int fade = Math.Min(frames / 2, (int)(rate * .004));
            for (int frame = 0; frame < frames; frame++)
            {
                double edge = fade < 2 ? 1 : Math.Min(1, Math.Min(frame, frames - 1 - frame) / (double)(fade - 1));
                for (int channel = 0; channel < channels; channel++) output[frame * channels + channel] *= (float)(gain * edge);
            }
            return output;
        }

        internal static float[] ShiftPitch(float[] input, int channels, int rate)
        {
            int frames = input.Length / channels;
            int stretchedFrames = (int)Math.Ceiling(frames / PitchRatio);
            var stretched = new float[stretchedFrames * channels];
            for (int frame = 0; frame < stretchedFrames; frame++)
            {
                double position = frame * PitchRatio;
                int first = Math.Min(frames - 1, (int)position), second = Math.Min(frames - 1, first + 1);
                double mix = position - first;
                for (int channel = 0; channel < channels; channel++)
                    stretched[frame * channels + channel] = (float)(input[first * channels + channel] * (1 - mix) + input[second * channels + channel] * mix);
            }
            int hop = Math.Max(32, rate / 100), window = hop * 4, overlap = window - hop, search = hop;
            var output = new float[input.Length];
            if (frames < window) { Array.Copy(stretched, output, output.Length); return output; }
            Array.Copy(stretched, output, window * channels);
            int previous = 0;
            for (int target = hop; target < frames; target += hop)
            {
                int expected = (int)Math.Round(target / PitchRatio);
                int lower = Math.Max(previous + 1, expected - search), upper = Math.Min(stretchedFrames - window, expected + search);
                int best = Math.Max(0, Math.Min(stretchedFrames - window, expected));
                if (lower <= upper)
                {
                    double score = double.NegativeInfinity;
                    int reference = previous + hop;
                    for (int candidate = lower; candidate <= upper; candidate += 4)
                    {
                        double value = Correlation(stretched, reference, candidate, overlap, channels, 4);
                        if (value > score) { score = value; best = candidate; }
                    }
                    int coarse = best;
                    score = double.NegativeInfinity;
                    for (int candidate = Math.Max(lower, coarse - 3); candidate <= Math.Min(upper, coarse + 3); candidate++)
                    {
                        double value = Correlation(stretched, reference, candidate, overlap, channels, 2);
                        if (value > score) { score = value; best = candidate; }
                    }
                }
                int count = Math.Min(window, frames - target);
                for (int frame = 0; frame < count; frame++)
                {
                    double mix = frame >= overlap ? 1 : frame / (double)overlap;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        int destination = (target + frame) * channels + channel;
                        output[destination] = (float)(output[destination] * (1 - mix) + stretched[(best + frame) * channels + channel] * mix);
                    }
                }
                previous = best;
            }
            return output;
        }

        private static double Correlation(float[] data, int reference, int candidate, int frames, int channels, int step)
        {
            double cross = 0, a = 0, b = 0;
            for (int frame = 0; frame < frames; frame += step)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    double x = data[(reference + frame) * channels + channel], y = data[(candidate + frame) * channels + channel];
                    cross += x * y; a += x * x; b += y * y;
                }
            }
            return a < 1e-12 || b < 1e-12 ? -Math.Abs(candidate - reference) * 1e-12 : cross / Math.Sqrt(a * b);
        }

        private static void Color(float[] data, int channel, int channels, int rate)
        {
            int frames = data.Length / channels;
            var hp = new Stage(rate, 300, 0);
            var peak1 = new Stage(rate, 650, 2, 10, 2.5);
            var peak2 = new Stage(rate, 1250, 2, 7, 2);
            var peak3 = new Stage(rate, 2250, 2, -5, 1.6);
            var lp = new Stage(rate, 2000, 1);
            int d1 = (int)Math.Round(rate * .0005), d2 = (int)Math.Round(rate * .0013), d3 = (int)Math.Round(rate * .0029);
            var cavity = new double[d3 + 1];
            var envelope = new double[frames];
            var noise = new double[frames];
            var nh = new Stage(rate, 650, 0);
            var nl = new Stage(rate, 3200, 1);
            var final = new Stage(rate, 2600, 3);
            double a = Math.Exp(-1d / (rate * .008)), env = 0, sum = 0, noisePower = 0;
            uint state = 137;
            for (int frame = 0; frame < frames; frame++)
            {
                double dry = peak3.Run(peak2.Run(peak1.Run(hp.Run(data[frame * channels + channel]))));
                cavity[frame % cavity.Length] = dry;
                double wet = dry;
                if (frame >= d1) wet += .24 * cavity[(frame - d1) % cavity.Length];
                if (frame >= d2) wet -= .16 * cavity[(frame - d2) % cavity.Length];
                if (frame >= d3) wet += .12 * cavity[(frame - d3) % cavity.Length];
                wet = lp.Run(wet);
                env = a * env + (1 - a) * wet * wet;
                envelope[frame] = Math.Sqrt(Math.Max(0, env));
                wet *= Math.Min(1, Math.Pow(.09 / Math.Max(envelope[frame], 1e-5), .7));
                data[frame * channels + channel] = (float)wet;
                sum += wet * wet;
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                noise[frame] = nl.Run(nh.Run(state / (double)uint.MaxValue * 2 - 1));
                noisePower += noise[frame] * noise[frame];
            }
            double level = Math.Max(1e-9, Math.Sqrt(sum / Math.Max(1, frames))), noiseLevel = Math.Max(1e-9, Math.Sqrt(noisePower / Math.Max(1, frames)));
            for (int frame = 0; frame < frames; frame++)
            {
                double wet = Math.Tanh(data[frame * channels + channel] / level * .70) * .3;
                wet += noise[frame] / noiseLevel * envelope[frame] * .085;
                data[frame * channels + channel] = (float)final.Run(wet);
            }
        }
    }
}
