using Microsoft.VisualBasic.Devices;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace GZipTest
{
    internal class Program
    {
        private static readonly int _chunkSize = 1 * 1024 * 1024; // 1 MB chunks
        private static int _maxThreads;
        private static int _maxInputChunks;
        private static int _maxOutputChunks;
        private static CompressionMode _mode;
        private static string from;
        private static string to;

        /// <summary>
        /// Will parse input, determine if it's valid. If not return false and display a message, if it is valid all of the global input variables will be populated with relevant info.
        /// </summary>
        /// <param name="args">arguments that were passed from command line</param>
        /// <returns>false if the input is no valid, </returns>
        private static bool ParseInput(string[] args)
        {
            // Checking for correct input
            if (args.Length != 3)
            {
                Console.WriteLine("The input is not correct. Expected: GZipTest.exe compress/decompress FromFile ToFile");
                return false;
            }

            if (args[0].ToLower() == "compress")
                _mode = CompressionMode.Compress;
            else if (args[0].ToLower() == "decompress")
                _mode = CompressionMode.Decompress;
            else
            {
                Console.WriteLine($"{args[0]} is not a valid command, use 'compress' or 'decompress'.");
                return false;
            }
            from = args[1];
            to = args[2];

            if (!File.Exists(from))
            {
                Console.WriteLine($"File {from} does not exist, cannot process it.");
                return false;
            }
            long fromLength = new FileInfo(from).Length;
            if (fromLength == 0)
            {
                Console.WriteLine($"File {from} Contatins 0 bytes, there is nothing to compress/decompress");
                return false;
            }
            if (_mode == CompressionMode.Decompress)
            {
                using (var fs = new FileStream(from, FileMode.Open, FileAccess.Read))
                {
                    var header = new byte[10];
                    var bytesRead = fs.Read(header, 0, header.Length);
                    if (bytesRead < 10)
                    {
                        Console.WriteLine($"Input file {from} has less than 10 bytes in it, it cannot have a proper GZip header. Check if input file is really a GZip archive if you want to decompress.");
                        return false;
                    }
                    else if (header[0] != 0x1F || header[1] != 0x8B || header[2] != 0x08)
                    {
                        Console.WriteLine($"Input file {from} is not a GZip archive, compressed by Deflate. Provide a valid GZip archive as input, if you want to decompress.");
                        return false;
                    }
                }
            }

            if (File.Exists(to))
            {
                Console.WriteLine($"File {to} exists and will be overwritten, are you sure? Y/N");
                string answer = Console.ReadLine();
                if (answer.ToLower() != "y")
                {
                    Console.WriteLine($"User cancelled request");
                    return false;
                }
                try
                {
                    File.Delete(to);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"When deleting {to} I/O exception was thrown. Maybe the file is in use? Exception message: {ex.Message}");
                    return false;
                }
                catch (UnauthorizedAccessException ex)
                {
                    Console.WriteLine($"When deleting {to} permission exception was thrown. Check if you have permissions. Couln't delete the file. Exception message: {ex.Message}");
                    return false;
                }
            }
            try
            {
                var fs = File.Create(to);
                fs.Dispose();
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"When creating {to} permission exception was thrown. Check if you have permissions. Couln't create the file. Exception message: {ex.Message}");
                return false;
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"When creating {to} argument exception was thrown. Check if this is a valid filename. Couln't create the file. Exception message: {ex.Message}");
                return false;
            }
            catch (IOException ex)
            {
                Console.WriteLine($"When creating {to} Input/Output exception was thrown. Couln't create the file. Exception message: {ex.Message}");
                return false;
            }
            return true;
        }

        // We await input of format GZipTest.exe compress/decompress FromFile ToFile
        private static void Main(string[] args)
        {
            if (!ParseInput(args))
            {
                Console.ReadLine();
                return;
            }
            // Let's figure out technical parameters
            ComputerInfo CI = new ComputerInfo();
            ulong mem = CI.AvailablePhysicalMemory;
            ulong vmem = CI.AvailableVirtualMemory;
            int cpuCount = Environment.ProcessorCount;
            _maxThreads = cpuCount;
            _maxInputChunks = Convert.ToInt32((mem * 0.45) / (_chunkSize));
            _maxOutputChunks = _maxInputChunks;
            // Seems like all input was correct, let's continue with actual work
            DateTime start = DateTime.Now;
            Console.WriteLine($"{_mode.ToString()}ing using {_maxThreads} threads and {_maxInputChunks} chunks per dictionary");
            var inputDictionary = new MySafeDictionary<int, byte[]>();
            var outputDictionary = new MySafeDictionary<int, byte[]>();
            var myCompressor = new Compressor(inputDictionary, outputDictionary, _maxThreads, _maxOutputChunks, _mode);
            var myReader = new FileReader(from, inputDictionary, _maxInputChunks, _chunkSize, _mode == CompressionMode.Compress ? false : true);
            var myWriter = new FileWriter(to, outputDictionary);
            Thread readThread = new Thread(myReader.ReadAsync);
            Thread writeThread = new Thread(myWriter.Write);
            readThread.Start();
            writeThread.Start();
            myCompressor.LaunchThreads();
            // Ok the work is being done. The first thing we should do is wait for the readThread to be done. The write thread might come down any minute if it runs out of space too
            while (readThread.IsAlive && writeThread.IsAlive)
            {
                // Console.WriteLine($"read = {myReader.ChunksRead}, inputcount = {inputDictionary.Count}, outputcount = {outputDictionary.Count}, written = {myWriter.ChunksWritten}");
                Thread.Sleep(100);
            }
            while (myWriter.ChunksWritten < myReader.ChunksRead && writeThread.IsAlive)
            {
                // Console.WriteLine($"Reading all done. read = {myReader.ChunksRead}, inputcount = {inputDictionary.Count}, outputcount = {outputDictionary.Count}, written = {myWriter.ChunksWritten}");
                Thread.Sleep(100);
            }
            myCompressor.StopThreads();
            if (!writeThread.IsAlive)
            {
                Console.ReadLine();
                return;
            }
            writeThread.Abort();
            long startSize = new FileInfo(from).Length;
            long endSize = new FileInfo(to).Length;
            Console.WriteLine($"{_mode.ToString()}ed {startSize} Bytes ({(startSize / (1024 * 1024)):N2} MB) into {endSize} Bytes ({(endSize / (1024 * 1024)):N2} MB) in {DateTime.Now.Subtract(start).TotalMilliseconds:N0} milliseconds ({(DateTime.Now.Subtract(start).TotalMilliseconds / (60 * 1000)):N2} minutes)");
            Console.ReadLine();
        }
    }
}