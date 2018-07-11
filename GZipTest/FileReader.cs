using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GZipTest
{
    // Reads a file in byte chunks into mysafedict
    // Has two modes, magicNumberMode = true means we don't have fixed chunksize and instead we work with gzip's header format to determine a chunk
    /// <summary>
    /// Represents data and methods required to read data from a file into a collection in order.
    /// Will try to maintain that collection between half full and full for most efficiency when reading, due to asynchronous nature
    /// works in two modes: 1) fixed chunk size 2) variable chunk size based on GZip magic numbers
    /// </summary>
    internal class FileReader
    {
        private readonly string _fileName;
        private readonly MySafeDictionary<int, byte[]> _dict;
        private readonly bool _magicNumberMode;
        private readonly int _chunkSize;
        private readonly int _maxChunks;
        private int currentChunkIndex = 0;

        /// <summary>
        /// Ctor for FileReader
        /// </summary>
        /// <param name="fromFile">File name to read data from</param>
        /// <param name="toDictionary">Collection to store data in</param>
        /// <param name="maxChunksInDict">How many chunks may the collection store at a time (used for not running out of memory)</param>
        /// <param name="chunkSize">It will read data from file in chunks of this size (Bytes). It will add data to collection in chunks of same size if not in magicNumerMode</param>
        /// <param name="magicNumberMode">If this is set to True, will assume that data has GZip headers and scan for those instead of conserving chunks as is.
        /// Will add data to collection in variable size chunks</param>
        public FileReader(string fromFile, MySafeDictionary<int, byte[]> toDictionary, int maxChunksInDict, int chunkSize, bool magicNumberMode = false)
        {
            _fileName = fromFile;
            _dict = toDictionary;
            _magicNumberMode = magicNumberMode;
            _chunkSize = chunkSize;
            _maxChunks = maxChunksInDict;
        }

        public int ChunksRead
        {
            get { return currentChunkIndex; }
        }

        /// <summary>
        /// Synchronous read. Obsolete, unused, left for reference.
        /// </summary>
        private void Read()
        {
            bool fileRead = false;
            long fileOffset = 0;
            byte[] leftovers = new byte[0];
            while (!fileRead)
            {
                if (_dict.Count >= _maxChunks)
                {
                    Thread.Sleep(50);
                    continue;
                }
                using (FileStream fs = new FileStream(_fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(fileOffset, SeekOrigin.Begin);
                    byte[] data = new byte[_chunkSize];
                    int bytesRead = fs.Read(data, 0, data.Length);
                    if (bytesRead == 0)
                    {
                        fileRead = true;
                        continue;
                    }
                    else if (bytesRead < _chunkSize)
                    {
                        byte[] smallerData = new byte[bytesRead];
                        Array.Copy(data, 0, smallerData, 0, bytesRead);
                        data = smallerData;
                        fileRead = true;
                    }
                    fileOffset += data.Length;
                    if (!_magicNumberMode)
                        _dict[currentChunkIndex++] = data;
                    else
                    {
                        // Here we need to consider leftovers and possibly more than 1 chunk at a time
                        using (MemoryStream ms = new MemoryStream())
                        {
                            ms.Write(leftovers, 0, leftovers.Length);
                            ms.Write(data, 0, data.Length);
                            data = ms.ToArray();
                        }
                        foreach (var slice in GZipSlicer(ref data))
                            _dict[currentChunkIndex++] = slice;
                        leftovers = data;
                    }
                }
            }
            // If we are here and we were in magic mode, we might still have leftovers and we need to write them as last chunk
            if (_magicNumberMode && leftovers.Length > 0)
                _dict[currentChunkIndex++] = leftovers;
        }

        /// <summary>
        /// Asynchronous read. It seems to perform better when decompressing, because between I/O calls we can fit a lot of CPU work that we need to do in order to determite chunks
        /// Still this is the bottleneck for decoding it seems. TODO: Improve?
        /// </summary>
        public void ReadAsync()
        {
            bool fileRead = false;
            long fileOffset = 0;
            byte[] leftovers = new byte[0];
            while (!fileRead)
            {
                // We only populate if we have less than half of the array, the we try to fill it up to max.
                // This is done to improve efficiency of the async code, works better when we do it for continuous number of chunks at a time
                if (_dict.Count >= _maxChunks / 2)
                {
                    Thread.Sleep(50);
                    continue;
                }
                using (FileStream fs = new FileStream(_fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
                {
                    fs.Seek(fileOffset, SeekOrigin.Begin);
                    byte[] nextData = new byte[_chunkSize];
                    var ar = fs.BeginRead(nextData, 0, nextData.Length, null, null);
                    // while next chunk is fetching, let's work with current data we have. It is stored in leftovers
                    byte[] data = leftovers;
                    if (data.Length > 0)
                    {
                        if (!_magicNumberMode)
                        {
                            _dict[currentChunkIndex++] = data;
                            leftovers = new byte[0];
                        }
                        else
                        {
                            foreach (var slice in GZipSlicer(ref data))
                                _dict[currentChunkIndex++] = slice;
                            leftovers = data;
                        }
                    }
                    // Ok all work with data done, hopefully now we can fetch the new data and continue on our way
                    int bytesRead = fs.EndRead(ar);
                    if (bytesRead == 0)
                    {
                        fileRead = true;
                        continue;
                    }
                    else if (bytesRead < _chunkSize)
                    {
                        byte[] smallerData = new byte[bytesRead];
                        Array.Copy(nextData, 0, smallerData, 0, bytesRead);
                        nextData = smallerData;
                        fileRead = true;
                    }
                    fileOffset += nextData.Length;
                    using (MemoryStream ms = new MemoryStream())
                    {
                        ms.Write(leftovers, 0, leftovers.Length);
                        ms.Write(nextData, 0, nextData.Length);
                        leftovers = ms.ToArray();
                    }
                    // We now return to the loop so that we can request another async read and process the data after
                }
            }
            // We are when we are all finished up reading. Our "leftovers" though is supposed to have at least one last chunk
            // This is the same code as in body of loop. Should make some methods out of all this to properly compact and reuse code
            if (!_magicNumberMode)
                _dict[currentChunkIndex++] = leftovers;
            else
            {
                foreach (var slice in GZipSlicer(ref leftovers))
                    _dict[currentChunkIndex++] = slice;
                // This is the last chunk, because we have no following header
                _dict[currentChunkIndex++] = leftovers;
            }
        }

        /// <summary>
        /// Will slice the data into GZip chunks based on GZip header
        /// </summary>
        /// <param name="data">the data to extract chunks from. The method will remove all valid chunks from it and change the array to contain only "leftover" bytes</param>
        /// <returns>List of valid GZip chunks each starting with a gzip header in order</returns>
        private List<byte[]> GZipSlicer(ref byte[] data)
        {
            List<byte[]> chunks = new List<byte[]>();
            int sliceStart = 0;
            // No point in looking at last 9 bytes, we need at least 10 in a row to get a proper header
            for (int i = 0; i < data.Length - 9; i++)
            {
                if (data[i] != 0x1F)
                    continue;
                // && data[i + 9] == 0x00 only for win
                if (data[i + 1] == 0x8B && data[i + 2] == 0x08 && (i != sliceStart))
                {
                    // We have found a new slice
                    byte[] slice = new byte[i - sliceStart];
                    Array.Copy(data, sliceStart, slice, 0, i - sliceStart);
                    chunks.Add(slice);
                    sliceStart = i;
                }
            }
            // Here we extracted all valid chunks from the data and we need to trim it
            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(data, sliceStart, data.Length - sliceStart);
                data = ms.ToArray();
            }
            return chunks;
        }
    }
}