using System.IO;

namespace GZipTest
{
    static internal class Utilities
    {
        /// <summary>
        /// Use to copy a stream into another stream.
        /// </summary>
        /// <param name="input">The stream to copy from</param>
        /// <param name="output">The stream to copy to</param>
        public static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, bytesRead);
            }
        }
    }
}