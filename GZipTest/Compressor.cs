using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GZipTest
{
    /// <summary>
    /// Represents a collection of data and methods needed to compress/decompress a collection of byte arrays and store it another collection.
    /// </summary>
    internal class Compressor
    {
        private readonly MySafeDictionary<int, byte[]> _inputDict;
        private readonly MySafeDictionary<int, byte[]> _outputDict;
        private readonly int _maxThreads;
        private readonly CompressionMode _mode;
        private readonly Thread[] _threads;
        private readonly int _throttleOutputCount;

        /// <summary>
        /// Create an instance of Compressor, with all the data it needs in order to process.
        /// </summary>
        /// <param name="fromDictionary">The data to compress/decompess in a collection</param>
        /// <param name="toDictionary">The collection to put resulting data in</param>
        /// <param name="maxThreads">How many worker threads to use while processing data</param>
        /// <param name="throttleOutputCount">Will throttle processing if resulting output collection has this many items. Useful if processing is too fast, writing too slow</param>
        /// <param name="mode">Compress or decompress</param>
        public Compressor(MySafeDictionary<int, byte[]> fromDictionary, MySafeDictionary<int, byte[]> toDictionary, int maxThreads, int throttleOutputCount, CompressionMode mode)
        {
            _inputDict = fromDictionary;
            _outputDict = toDictionary;
            _maxThreads = maxThreads;
            _mode = mode;
            _threads = new Thread[maxThreads];
            _throttleOutputCount = throttleOutputCount;
        }

        /// <summary>
        /// Represents single thread's workload
        /// </summary>
        private void Work()
        {
            while (true)
            {
                byte[] chunk = _inputDict.TryPopNext(out int index);
                if (index == -1)
                {
                    Thread.Sleep(50);
                    continue;
                }
                byte[] resultChunk;
                if (_mode == CompressionMode.Compress)
                    resultChunk = CompressionWorkload(chunk);
                else
                    resultChunk = DecompressionWorkload(chunk);
                while (_outputDict.Count >= _throttleOutputCount)
                    Thread.Sleep(50);
                _outputDict[index] = resultChunk;
            }
        }

        private byte[] CompressionWorkload(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipStream Compress = new GZipStream(ms, _mode))
                {
                    Compress.Write(data, 0, data.Length);
                }
                return ms.ToArray();
            }
        }

        private byte[] DecompressionWorkload(byte[] data)
        {
            using (MemoryStream compressedStream = new MemoryStream(data))
            {
                compressedStream.Seek(0, SeekOrigin.Begin);
                using (GZipStream decompressionStream = new GZipStream(compressedStream, _mode))
                {
                    using (MemoryStream decompressedStream = new MemoryStream())
                    {
                        Utilities.CopyStream(decompressionStream, decompressedStream);
                        return decompressedStream.ToArray();
                    }
                }
            }
        }

        /// <summary>
        /// Will launch _maxThreads amounth of thread to process the workload. The threads will never stop working/idling, you need to stop them manually for cleanup.
        /// </summary>
        public void LaunchThreads()
        {
            for (int i = 0; i < _maxThreads; i++)
            {
                _threads[i] = new Thread(Work);
                _threads[i].Start();
            }
        }

        /// <summary>
        /// Will abort all threads.
        /// </summary>
        public void StopThreads()
        {
            for (int i = 0; i < _maxThreads; i++)
                _threads[i].Abort();
        }
    }
}