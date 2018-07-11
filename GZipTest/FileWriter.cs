using System;
using System.IO;
using System.Threading;

namespace GZipTest
{
    /// <summary>
    /// Represents data and methods required to write data from collection into a file in order.
    /// Will try to maintain that collection as empty, pretty slow because it has to be done synchronously,
    /// since we don't know chunks' sizes, doesn't seem like we have anything to gain by asynchronous work.
    /// </summary>
    internal class FileWriter
    {
        private readonly string _fileName;
        private readonly MySafeDictionary<int, byte[]> _dict;
        private int currentIndexToWrite = 0;

        /// <summary>
        /// Ctor for FileWriter
        /// </summary>
        /// <param name="toFile">Output file name, expected to be a valid filename and you should have writing permissions for it</param>
        /// <param name="fromDictionary">Collection with data to write in order</param>
        public FileWriter(string toFile, MySafeDictionary<int, byte[]> fromDictionary)
        {
            _fileName = toFile;
            _dict = fromDictionary;
        }

        public int ChunksWritten
        {
            get
            {
                return currentIndexToWrite;
            }
        }

        /// <summary>
        /// Writes the chunks from collection in order into output file synchronously. Will never return unless an IO exception is occured (probably out of space at that point).
        /// </summary>
        public void Write()
        {
            currentIndexToWrite = 0;
            while (true)
            {
                if (_dict.Count == 0)
                {
                    Thread.Sleep(50);
                    continue;
                }

                if (!_dict.IsNext(currentIndexToWrite))
                {
                    Thread.Sleep(50);
                    continue;
                }
                byte[] chunk = _dict.TryPopNext(out int index);
                try
                {
                    using (FileStream fs = new FileStream(_fileName, FileMode.Append, FileAccess.Write))
                    {
                        fs.Write(chunk, 0, chunk.Length);
                        currentIndexToWrite++;
                    }
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"I/O exception occured when writing file to disk. Check if you have correct permissions and enough space.");
                    Console.WriteLine($"Exception message: {ex.Message}");
                    return;
                }
            }
        }
    }
}