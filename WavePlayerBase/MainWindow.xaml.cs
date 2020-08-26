using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace WavePlayerBase
{
    public partial class MainWindow : Window
    {
        private AudioWaveOutPlayer Player;
        private AudioWaveFormat Format;
        private Stream AudioStream;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void ButtonOpen_Click(object sender, System.EventArgs e)
        {
            OpenFileDialog mainOpenFileDialog = new OpenFileDialog
            {
                Title = "Select a valid 'Waveform Audio File' (.wav)",
                Filter = "Waveform Audio Files|*.wav;",
                DefaultExt = ".wav",
                Multiselect = false
            };

            bool? dialogResult = mainOpenFileDialog.ShowDialog();
            if (dialogResult == true)
            {
                CloseFile();
                try
                {
                    AudioWaveStream stream = new AudioWaveStream(mainOpenFileDialog.FileName);
                    if (stream.Length <= 0)
                    {
                        throw new Exception("Invalid 'Waveform Audio File' (.wav) file");
                    }
                    Format = stream.Format;
                    if (Format.FormatID != (short)AudioWaveFormats.PulseCodeModulation && Format.FormatID != (short)AudioWaveFormats.Float)
                    {
                        throw new Exception("Only 'Pulse Code Modulation' (PCM) files are supported");
                    }
                    AudioStream = stream;
                }
                catch (Exception ex)
                {
                    CloseFile();
                    MessageBox.Show(ex.Message);
                }
            }
        }
        private void CloseFile()
        {
            Stop();
            if (AudioStream != null)
            {
                try
                {
                    AudioStream.Close();
                }
                finally
                {
                    AudioStream = null;
                }
            }
        }
        private void ButtonPlay_Click(object sender, System.EventArgs e)
        {
            long zeroLong = 0L;
            int deviceID = -1;
            int bufferSize = 16384;
            int bufferCount = 3;

            Stop();
            if (AudioStream != null)
            {
                AudioStream.Position = zeroLong;
                BufferFillEventHandler fillerEvent = new BufferFillEventHandler(Filler);
                Player = new AudioWaveOutPlayer(deviceID, Format, bufferSize, bufferCount, fillerEvent);
            }
        }
        private void Filler(IntPtr dataPointer, int size)
        {
            int zero = 0;
            byte zeroByte = (byte)0;

            byte[] bytes = new byte[size];
            if (AudioStream != null)
            {
                int position = zero;
                while (position < size)
                {
                    int positionToGet = size - position;
                    int foundPosition = AudioStream.Read(bytes, position, positionToGet);
                    if (foundPosition < positionToGet)
                    {
                        AudioStream.Position = zero;
                    }
                    position += foundPosition;
                }
            }
            else
            {
                for (int counter = zero; counter < bytes.Length; counter++)
                {
                    bytes[counter] = zeroByte;
                }
            }
            Marshal.Copy(bytes, zero, dataPointer, size);
        }
        private void ButtonStop_Click(object sender, System.EventArgs e)
        {
            Stop();
        }
        private void Stop()
        {
            if (Player != null)
            {
                try
                {
                    Player.Dispose();
                }
                finally
                {
                    Player = null;
                }
            }
        }
    }

    #region Windows Multimedia API

    internal class WindowsMultimediaAPI
    {
        private const string WindowsMultimediaAPIDLLName = "winmm.dll";

        public const int MMSYSERR_NOERROR = 0;
        public const int MM_WOM_OPEN = 0x3BB;
        public const int MM_WOM_CLOSE = 0x3BC;
        public const int MM_WOM_DONE = 0x3BD;
        public const int CALLBACK_FUNCTION = 0x00030000;
        public const int TIME_MS = 0x0001;
        public const int TIME_SAMPLES = 0x0002;
        public const int TIME_BYTES = 0x0004;

        public delegate void WaveDelegate(IntPtr pointer, int messageNumber, int user, ref AudioWaveHeader waveHeader, int parameter);

#pragma warning disable IDE1006

        [DllImport(WindowsMultimediaAPIDLLName)]
        public static extern int waveOutGetNumDevs();
        [DllImport(WindowsMultimediaAPIDLLName)]
        public static extern int waveOutPrepareHeader(IntPtr waveOutPointer, ref AudioWaveHeader waveOutHeader, int size);
        [DllImport(WindowsMultimediaAPIDLLName)]
        public static extern int waveOutUnprepareHeader(IntPtr waveOutPointer, ref AudioWaveHeader waveOutHeader, int size);
        [DllImport(WindowsMultimediaAPIDLLName)]
        public static extern int waveOutWrite(IntPtr waveOutPointer, ref AudioWaveHeader waveOutHeader, int size);
        [DllImport(WindowsMultimediaAPIDLLName)]
        public static extern int waveOutOpen(out IntPtr waveOutPointer, int deviceID, AudioWaveFormat format, WaveDelegate callback, int instance, int flags);
        [DllImport(WindowsMultimediaAPIDLLName)]
        public static extern int waveOutReset(IntPtr waveOutPointer);
        [DllImport(WindowsMultimediaAPIDLLName)]
        public static extern int waveOutClose(IntPtr waveOutPointer);

#pragma warning restore IDE1006

        [StructLayout(LayoutKind.Sequential)]
        public struct AudioWaveHeader
        {
            public IntPtr DataPointer;
            public IntPtr NextPointer;
            public IntPtr UserPointer;
            public int BufferLength;
            public int BytesRecorded;
            public int FlagNumber;
            public int LoopNumber;
            public int ReservedNumber;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public class AudioWaveFormat
    {
        public short FormatID;
        public short NumberOfChannels;
        public int SamplesPerSecond;
        public int BytesPerSecond;
        public short BlocksAlignedNumber;
        public short BitsPerSample;

        public AudioWaveFormat(int samplesPerSecond, int bitsPerSample, int numberOfChannels)
        {
            short eightShort = 8;

            FormatID = (short)AudioWaveFormats.PulseCodeModulation;
            NumberOfChannels = (short)numberOfChannels;
            SamplesPerSecond = samplesPerSecond;
            BitsPerSample = (short)bitsPerSample;
            BlocksAlignedNumber = (short)(NumberOfChannels * (BitsPerSample / eightShort));
            BytesPerSecond = SamplesPerSecond * BlocksAlignedNumber;
        }
    }

    public enum AudioWaveFormats
    {
        PulseCodeModulation = 1,
        Float = 3
    }

    #endregion

    #region Audio Wave Out

    public delegate void BufferFillEventHandler(IntPtr data, int size);

    public class AudioWaveOutPlayer : IDisposable
    {
        private IntPtr WaveOutPointer;
        private AudioWaveOutBuffer MainBuffer;
        private AudioWaveOutBuffer CurrentBuffer;
        private Thread MainThread;
        private BufferFillEventHandler FillProcedure;
        private bool IsFinished;
        private readonly byte Zero;

        private readonly WindowsMultimediaAPI.WaveDelegate BufferProcedure = new WindowsMultimediaAPI.WaveDelegate(AudioWaveOutBuffer.WaveOutProcedure);

        public AudioWaveOutPlayer(int deviceID, AudioWaveFormat audioWaveFormat, int bufferSize, int bufferCount, BufferFillEventHandler fillProcedure)
        {
            short eightShort = 8;
            byte oneTwentyEightByte = (byte)128;
            byte zeroByte = (byte)0;
            int instanceNumber = 0;

            Zero = audioWaveFormat.BitsPerSample == eightShort ? oneTwentyEightByte : zeroByte;
            FillProcedure = fillProcedure;
            int waveOutOpenNumber = WindowsMultimediaAPI.waveOutOpen(out WaveOutPointer, deviceID, audioWaveFormat, BufferProcedure, instanceNumber, WindowsMultimediaAPI.CALLBACK_FUNCTION);
            AudioWaveOutCheck.CheckErrorNumber(waveOutOpenNumber);
            AllocateBuffers(bufferSize, bufferCount);
            MainThread = new Thread(new ThreadStart(ThreadProcedure));
            MainThread.Start();
        }

        public void Dispose()
        {
            if (MainThread != null)
            {
                try
                {
                    IsFinished = true;
                    if (WaveOutPointer != IntPtr.Zero)
                    {
                        WindowsMultimediaAPI.waveOutReset(WaveOutPointer);
                    }
                    MainThread.Join();
                    FillProcedure = null;
                    FreeBuffers();
                    if (WaveOutPointer != IntPtr.Zero)
                    {
                        WindowsMultimediaAPI.waveOutClose(WaveOutPointer);
                    }
                }
                finally
                {
                    MainThread = null;
                    WaveOutPointer = IntPtr.Zero;
                }
            }
            GC.SuppressFinalize(this);
        }
        private void ThreadProcedure()
        {
            int zero = 0;

            while (!IsFinished)
            {
                Advance();
                if (FillProcedure != null && !IsFinished)
                {
                    FillProcedure(CurrentBuffer.DataPointer, CurrentBuffer.Size);
                }
                else
                {
                    byte zeroByte = Zero;
                    byte[] buffer = new byte[CurrentBuffer.Size];
                    for (int counter = zero; counter < buffer.Length; counter++)
                    {
                        buffer[counter] = zeroByte;
                    }
                    Marshal.Copy(buffer, zero, CurrentBuffer.DataPointer, buffer.Length);
                }
                CurrentBuffer.Play();
            }
            WaitForAllBuffers();
        }
        private void AllocateBuffers(int bufferSize, int bufferCount)
        {
            int zero = 0;
            int one = 1;

            FreeBuffers();
            if (bufferCount > zero)
            {
                MainBuffer = new AudioWaveOutBuffer(WaveOutPointer, bufferSize);
                AudioWaveOutBuffer previousAudioWaveOutBuffer = MainBuffer;
                try
                {
                    for (int counter = one; counter < bufferCount; counter++)
                    {
                        AudioWaveOutBuffer audioWaveOutBuffer = new AudioWaveOutBuffer(WaveOutPointer, bufferSize);
                        previousAudioWaveOutBuffer.NextBuffer = audioWaveOutBuffer;
                        previousAudioWaveOutBuffer = audioWaveOutBuffer;
                    }
                }
                finally
                {
                    previousAudioWaveOutBuffer.NextBuffer = MainBuffer;
                }
            }
        }
        private void FreeBuffers()
        {
            CurrentBuffer = null;
            if (MainBuffer != null)
            {
                AudioWaveOutBuffer firstAudioWaveOutBuffer = MainBuffer;
                MainBuffer = null;

                AudioWaveOutBuffer currentAudioWaveOutBuffer = firstAudioWaveOutBuffer;
                do
                {
                    AudioWaveOutBuffer nextAudioWaveOutBuffer = currentAudioWaveOutBuffer.NextBuffer;
                    currentAudioWaveOutBuffer.Dispose();
                    currentAudioWaveOutBuffer = nextAudioWaveOutBuffer;
                }
                while (currentAudioWaveOutBuffer != firstAudioWaveOutBuffer);
            }
        }
        private void Advance()
        {
            CurrentBuffer = CurrentBuffer == null ? MainBuffer : CurrentBuffer.NextBuffer;
            CurrentBuffer.WaitFor();
        }
        private void WaitForAllBuffers()
        {
            AudioWaveOutBuffer waveOutBuffer = MainBuffer;
            while (waveOutBuffer.NextBuffer != MainBuffer)
            {
                waveOutBuffer.WaitFor();
                waveOutBuffer = waveOutBuffer.NextBuffer;
            }
        }
    }

    internal class AudioWaveOutBuffer : IDisposable
    {
        private AutoResetEvent PlayEvent = new AutoResetEvent(false);
        private WindowsMultimediaAPI.AudioWaveHeader Header;
        public AudioWaveOutBuffer NextBuffer;
        private readonly IntPtr WaveOutPointer;
        private readonly byte[] HeaderData;
        private GCHandle HeaderHandle;
        private GCHandle HeaderDataHandle;
        private bool IsPlaying;

        public AudioWaveOutBuffer(IntPtr waveOutPointer, int size)
        {
            WaveOutPointer = waveOutPointer;

            HeaderHandle = GCHandle.Alloc(Header, GCHandleType.Pinned);
            Header.UserPointer = (IntPtr)GCHandle.Alloc(this);
            HeaderData = new byte[size];
            HeaderDataHandle = GCHandle.Alloc(HeaderData, GCHandleType.Pinned);
            Header.DataPointer = HeaderDataHandle.AddrOfPinnedObject();
            Header.BufferLength = size;
            AudioWaveOutCheck.CheckErrorNumber(WindowsMultimediaAPI.waveOutPrepareHeader(WaveOutPointer, ref Header, Marshal.SizeOf(Header)));
        }

        internal static void WaveOutProcedure(IntPtr pointer, int message, int user, ref WindowsMultimediaAPI.AudioWaveHeader header, int parameter)
        {
            if (message == WindowsMultimediaAPI.MM_WOM_DONE)
            {
                try
                {
                    GCHandle garbageCollectorHandle = (GCHandle)header.UserPointer;
                    AudioWaveOutBuffer audioWaveOutBuffer = (AudioWaveOutBuffer)garbageCollectorHandle.Target;
                    audioWaveOutBuffer.OnCompleted();
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (Header.DataPointer != IntPtr.Zero)
            {
                WindowsMultimediaAPI.waveOutUnprepareHeader(WaveOutPointer, ref Header, Marshal.SizeOf(Header));
                HeaderHandle.Free();
                Header.DataPointer = IntPtr.Zero;
            }
            PlayEvent.Close();
            if (HeaderDataHandle.IsAllocated)
            {
                HeaderDataHandle.Free();
            }
            GC.SuppressFinalize(this);
        }
        public int Size
        {
            get { return Header.BufferLength; }
        }
        public IntPtr DataPointer
        {
            get { return Header.DataPointer; }
        }
        public bool Play()
        {
            lock (this)
            {
                PlayEvent.Reset();
                IsPlaying = WindowsMultimediaAPI.waveOutWrite(WaveOutPointer, ref Header, Marshal.SizeOf(Header)) == WindowsMultimediaAPI.MMSYSERR_NOERROR;
                return IsPlaying;
            }
        }
        public void WaitFor()
        {
            if (IsPlaying)
            {
                IsPlaying = PlayEvent.WaitOne();
            }
            else
            {
                int sleepTime = 0;
                Thread.Sleep(sleepTime);
            }
        }
        public void OnCompleted()
        {
            PlayEvent.Set();
            IsPlaying = false;
        }
    }

    internal class AudioWaveOutCheck
    {
        public static void CheckErrorNumber(int errorNumber)
        {
            if (errorNumber != WindowsMultimediaAPI.MMSYSERR_NOERROR)
            {
                throw new Exception(errorNumber.ToString());
            }
        }
    }

    #endregion

    #region Audio Wave Stream

    public class AudioWaveStream : Stream, IDisposable
    {
        private Stream MainStream;
        private long DataPosition;
        private long MainLength;

        public override long Length { get { return MainLength; } }

        public override long Position
        {
            get { return MainStream.Position - DataPosition; }
            set { Seek(value, SeekOrigin.Begin); }
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }

        public AudioWaveFormat Format { get; private set; }

        public AudioWaveStream(string fileName) : this(new FileStream(fileName, FileMode.Open)) { }
        public AudioWaveStream(Stream stream)
        {
            MainStream = stream;
            ReadHeader();
        }
        public new void Dispose()
        {
            if (MainStream != null)
            {
                MainStream.Close();
            }
            GC.SuppressFinalize(this);
        }

        private void ReadHeader()
        {
            long zeroLong = 0L;
            int sixteen = 16;
            int samplesPerSecondDefault = 22050;
            int bitsPerSampleDefault = 16;
            int numberOfChannelsDefault = 2;

            BinaryReader binaryReader = new BinaryReader(MainStream);

            if (ReadChunk(binaryReader) != "RIFF")
            {
                throw new Exception("Invalid file format");
            }

            binaryReader.ReadInt32();

            if (ReadChunk(binaryReader) != "WAVE")
            {
                throw new Exception("Invalid file format");
            }

            if (ReadChunk(binaryReader) != "fmt ")
            {
                throw new Exception("Invalid file format");
            }

            if (binaryReader.ReadInt32() != sixteen)
            {
                throw new Exception("Invalid file format");
            }

            Format = new AudioWaveFormat(samplesPerSecondDefault, bitsPerSampleDefault, numberOfChannelsDefault)
            {
                FormatID = binaryReader.ReadInt16(),
                NumberOfChannels = binaryReader.ReadInt16(),
                SamplesPerSecond = binaryReader.ReadInt32(),
                BytesPerSecond = binaryReader.ReadInt32(),
                BlocksAlignedNumber = binaryReader.ReadInt16(),
                BitsPerSample = binaryReader.ReadInt16()
            };

            while (MainStream.Position < MainStream.Length && ReadChunk(binaryReader) != "data") ;

            if (MainStream.Position >= MainStream.Length)
            {
                throw new Exception("Invalid file format");
            }

            MainLength = binaryReader.ReadInt32();
            DataPosition = MainStream.Position;

            Position = zeroLong;
        }
        private string ReadChunk(BinaryReader reader)
        {
            int zero = 0;
            int four = 4;
            byte[] byteChunk = new byte[four];
            reader.Read(byteChunk, zero, byteChunk.Length);
            return System.Text.Encoding.ASCII.GetString(byteChunk);
        }

        public override long Seek(long position, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    MainStream.Position = position + DataPosition;
                    break;
                case SeekOrigin.Current:
                    MainStream.Seek(position, SeekOrigin.Current);
                    break;
                case SeekOrigin.End:
                    MainStream.Position = DataPosition + MainLength - position;
                    break;
            }
            return this.Position;
        }

        public override void SetLength(long length)
        {
            throw new InvalidOperationException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int toRead = (int)Math.Min(count, MainLength - Position);
            return MainStream.Read(buffer, offset, toRead);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        public override void Close()
        {
            Dispose();
        }
        public override void Flush() { }
    }

    #endregion
}