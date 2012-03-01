using System;
using System.IO;

namespace FreeArcNetWrapper
{
    class Program
    {
        static void Main(string[] args)
        {
            string workDir = Directory.GetCurrentDirectory();

            using (FreeArcNetWrapper wrapper = new FreeArcNetWrapper(Path.Combine(workDir, "..\\..\\..\\FreeArcNetWrapper\\")))
            {
                wrapper.Progress += new EventHandler<ProgressEventArgs>(wrapper_Progress);

                try
                {
                    string source = Path.Combine(workDir, "..\\..\\..\\test\\compress");
                    string target = Path.Combine(workDir, "..\\..\\..\\test\\archive.arc");

                    wrapper.Compress(source, target, true);

                    string decompressDir = Path.Combine(workDir, "..\\..\\..\\test\\decompress\\");

                    Directory.CreateDirectory(decompressDir);

                    wrapper.Decompress(target, decompressDir);

                    Console.WriteLine("Done");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                Console.ReadKey();
            }
        }

        static void wrapper_Progress(object sender, ProgressEventArgs e)
        {
            Console.WriteLine(e.ToString());            
        }
    }
}
