using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ISBSSOFExtra
{
    internal sealed class PcmWavData
    {
        internal readonly float[] Samples;
        internal readonly int Channels;
        internal readonly int Frequency;
        internal PcmWavData(float[] samples, int channels, int frequency)
        {
            Samples = samples;
            Channels = channels;
            Frequency = frequency;
        }
    }

    internal static class PcmWavDecoder
    {
        private static readonly SemaphoreSlim DecodeSlot = new SemaphoreSlim(1, 1);
        internal static async Task<PcmWavData> DecodeAsync(string path, CancellationToken cancellationToken)
        {
            await DecodeSlot.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(() => Decode(path, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                DecodeSlot.Release();
            }
        }

        internal static PcmWavData Decode(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream, Encoding.ASCII))
            {
                if (ReadFourCc(reader) != "RIFF")
                    throw new InvalidDataException("Missing RIFF header.");
                reader.ReadUInt32();
                if (ReadFourCc(reader) != "WAVE")
                    throw new InvalidDataException("Missing WAVE header.");
                ushort format = 0, channels = 0, bits = 0;
                int frequency = 0, dataSize = 0;
                long dataPosition = -1;
                while (stream.Position + 8 <= stream.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var chunk = ReadFourCc(reader);
                    long size = reader.ReadUInt32();
                    long next = stream.Position + size;
                    if (next > stream.Length)
                        throw new InvalidDataException("WAV chunk exceeds the file length.");
                    if (chunk == "fmt ")
                    {
                        if (size < 16)
                            throw new InvalidDataException("Invalid WAV format chunk.");
                        format = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        frequency = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadUInt16();
                        bits = reader.ReadUInt16();
                    }
                    else if (chunk == "data")
                    {
                        dataSize = checked((int)size);
                        dataPosition = stream.Position;
                    }

                    if (dataPosition >= 0 && format != 0)
                        break;
                    stream.Position = Math.Min(next + (size & 1L), stream.Length);
                }

                if (channels == 0 || frequency <= 0 || dataPosition < 0 || dataSize == 0)
                    throw new InvalidDataException("Invalid WAV audio data.");
                if (!(format == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32)) && !(format == 3 && bits == 32))
                    throw new NotSupportedException("Unsupported WAV sample format.");
                int bytesPerSample = bits / 8;
                int frameSize = checked(channels * bytesPerSample);
                if (dataSize % frameSize != 0)
                    throw new InvalidDataException("WAV contains incomplete sample frames.");
                var samples = new float[dataSize / bytesPerSample];
                var buffer = new byte[4096 * bytesPerSample];
                stream.Position = dataPosition;
                int sampleIndex = 0;
                while (sampleIndex < samples.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int sampleCount = Math.Min(4096, samples.Length - sampleIndex);
                    int bytes = sampleCount * bytesPerSample;
                    int read = 0;
                    while (read < bytes)
                    {
                        int count = stream.Read(buffer, read, bytes - read);
                        if (count == 0)
                            throw new EndOfStreamException("Truncated WAV audio data.");
                        read += count;
                    }

                    for (int offset = 0; offset < bytes; offset += bytesPerSample)
                    {
                        float value;
                        if (format == 3)
                            value = BitConverter.ToSingle(buffer, offset);
                        else if (bits == 8)
                            value = (buffer[offset] - 128f) / 128f;
                        else if (bits == 16)
                            value = BitConverter.ToInt16(buffer, offset) / 32768f;
                        else if (bits == 24)
                        {
                            int sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                            if ((sample & 0x800000) != 0)
                                sample |= unchecked((int)0xFF000000);
                            value = sample / 8388608f;
                        }
                        else
                            value = BitConverter.ToInt32(buffer, offset) / 2147483648f;
                        samples[sampleIndex++] = float.IsNaN(value) ? 0f : Math.Max(-1f, Math.Min(1f, value));
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                return new PcmWavData(samples, channels, frequency);
            }
        }

        private static string ReadFourCc(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
                throw new EndOfStreamException("Truncated WAV header.");
            return Encoding.ASCII.GetString(bytes);
        }
    }
}
