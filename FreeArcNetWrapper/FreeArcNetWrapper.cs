using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;


namespace FreeArcNetWrapper
{
    /// <summary>
    /// The class provides easy to use interface to call Arc.exe. It provides error hadling and progress.
    /// 
    /// The class is not multithreaded, use one FreeArcNetWrapper for each thread/asynchronouse operation.
    /// </summary>
    public class FreeArcNetWrapper : IDisposable
    {
        Process _process;
        bool _success = false;
        object _lock = new object();
        bool _isWorking = false;
        Thread _readingStandardOutputThread;
        Thread _readingStandardErrorThread;
        string _warning = string.Empty;

        bool _list = false;

        /// <summary>
        /// the list of unpacked files
        /// </summary>
        List<string> _decompressList = new List<string>();

        public string FreeArcExePath { get; set; }
        public PackerLevel PackerLevel { get; set; }

        public FreeArcNetWrapper(string freeArcExeDir)
        {
            FreeArcExePath = Path.Combine(freeArcExeDir, "Arc.exe");//!-!
        }

        #region ILongOperation Members

        public event EventHandler<ProgressEventArgs> Progress;

        #endregion


        /// <summary>
        /// compress file / directory
        /// warning: it is not reentrant
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="targetPath"></param>
        /// <param name="withDirName"></param>
        public void Compress(string sourcePath, string targetPath, bool withDirName)
        {
            try
            {
                CompressAsync(sourcePath, targetPath, withDirName, false);
                _process.WaitForExit();
                _readingStandardOutputThread.Join();
                _readingStandardErrorThread.Join();
                if (!string.IsNullOrEmpty(_warning))
                    throw new Exception(_warning);
                if (!_success)
                    throw new Exception("Freearc compression failed");//!-!
            }
            finally
            {
                AbortAsync();
            }
        }

        public List<string> Decompress(string sourcePath, string targetPath)
        {
            return Decompress(sourcePath, targetPath, null);
        }

        /// <summary>
        /// decompress archive to directory.
        /// warning: it is not reentrant
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="targetPath"></param>
        /// <param name="mask"></param>
        /// <returns></returns>
        public List<string> Decompress(string sourcePath, string targetPath, string mask)
        {
            try
            {
                DecompressAsync(sourcePath, targetPath, true, mask);
                _process.WaitForExit();
                _readingStandardErrorThread.Join();
                _readingStandardOutputThread.Join();
                if (!_success)
                    throw new Exception("Freearc decompression failed");//!-!
                return _decompressList;
            }
            finally
            {
                AbortAsync();
            }
        }

        string GetPackerParam()
        {
            return "-" + PackerLevel.ToString();
        }

        /// <summary>
        /// decompress asynchronously archive to directory.
        /// warning: it is not reentrant
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="targetPath"></param>
        /// <param name="withDirName">Zda do archivu pridat jmeno baleneho adresare</param>
        public void CompressAsync(string sourceDir, string targetPath, bool withDirName, bool list)
        {
            if (!File.Exists(sourceDir) && !Directory.Exists(sourceDir))
                throw new ArgumentException(sourceDir);

            string targetDir = Path.GetDirectoryName(targetPath);
            Directory.CreateDirectory(targetDir);

            if (withDirName)
            {
                if (!Directory.Exists(sourceDir))
                    throw new Exception("sourcePath is not dir");//!-!
                //nejdrive se vytvori prazdny adresar v docasnem adresari a ten se zabali.                                
                string baseArchivePath = Path.GetFileName(sourceDir);

                string tempdir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                Directory.CreateDirectory(Path.Combine(tempdir, baseArchivePath));
                string packerPar = GetPackerParam();

                string command1 = string.Format("create \"{0}\" {1} -r -dp\"{2}\"", targetPath, packerPar, tempdir);//!-!

                //Pak se k nemu prida obsah
                string command2 = string.Format("a \"{0}\" {1} -r -y -ap\"{2}\" -dp\"{3}\"", targetPath, packerPar, baseArchivePath, sourceDir);//!-!

                //jendotlive ulohy se v arc oddeluji strednikem a mezerami
                DoWork(command1 + " ; " + command2, null, list);

                //TODO smazat tempdir
            }
            else
            {
                //-y odpovida na vsechno yes
                //target musi byt v uvozovkach (kvuli mezerama  tak), ale source nesmi, jinak to nefunguje
                // -t otestuje archiv po zapakovani
                DoWork(string.Format("create \"{0}\" -r -dp\"{1}\"", targetPath, sourceDir), null, false);//!-!
            }
        }

        /// <summary>
        /// decompress asynchronously archive to directory.
        /// warning: it is not reentrant
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="targetPath"></param>
        /// <param name="list"></param>
        /// <param name="mask"></param>
        public void DecompressAsync(string sourcePath, string targetPath, bool list, string mask)
        {
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                throw new ArgumentException(sourcePath);

            string targetDir = Path.GetDirectoryName(targetPath);
            if (targetDir != null)
                Directory.CreateDirectory(targetDir);

            if (mask == null)
                mask = string.Empty;
            else if (!string.IsNullOrEmpty(mask))//nastavenim mnasky se rozbali jen nektere soubory
                mask = "-n" + mask;//!-!

            //vylistuje seznam souboru
            string listCommandLine = string.Format("lb -fn \"{0}\"", sourcePath);//!-!

            //target musi byt v uvozovkach (kvuli mezerama  tak), ale source nesmi, jinak to nefunguje
            //-y odpovida na vsechno yes

            //-dp je cilovy adresar, normalne bych ho uvedl,
            // ale arc.exe zamrzne, kdyz je cesta obklopena uvozokama,
            // a pokud neni, nelze rozbalovat do adresare s mezerou
            // pokud se -dp neuvede, je pouzit pracovni adresar
            // proto toho vyuziju a vytvorim cilovy adresar a pak spustim proces v cilovem adresari
            // prijde mi, ze stejne tak to dela i addon pro total commander
            string command = string.Format("x \"{0}\" -y {1}", sourcePath, mask);//!-!
            if (list)
                command = listCommandLine + " ; " + command;
            Directory.CreateDirectory(targetPath);
            DoWork(command, targetPath, list);
        }

        /// <summary>
        /// main worker function
        /// </summary>
        /// <param name="commandline"></param>
        /// <param name="workingdir"></param>
        /// <param name="list">list to provide decompress files, it concerns only decopressing</param>
        void DoWork(string commandline, string workingdir, bool list)
        {
            lock (_lock)
            {
                if (_isWorking)
                    throw new Exception("FreeArcNetWrapper is still working.");//!-!
                _isWorking = true;
            }
            _decompressList.Clear();
            _warning = string.Empty;
            _success = false;
            _list = list;

            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = FreeArcExePath;
            start.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            start.CreateNoWindow = true;
            start.Arguments = commandline;
            if (workingdir != null)
                start.WorkingDirectory = workingdir;

            // This ensures that you get the output from the DOS application 
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.UseShellExecute = false;//musi byt false, pokud RedirectStandardOutput = true

            _readingStandardOutputThread = new Thread(new ThreadStart(ProgressThread));

            Trace.WriteLine(commandline);

            _process = Process.Start(start);

            _readingStandardErrorThread = new Thread(new ThreadStart(ErrorReadingThread));
            _readingStandardErrorThread.CurrentCulture = Thread.CurrentThread.CurrentCulture;
            _readingStandardErrorThread.CurrentUICulture = Thread.CurrentThread.CurrentUICulture;
            _readingStandardErrorThread.Start();

            _process.Exited += new EventHandler(_process_Exited);

            _readingStandardOutputThread.CurrentCulture = Thread.CurrentThread.CurrentCulture;
            _readingStandardOutputThread.CurrentUICulture = Thread.CurrentThread.CurrentUICulture;
            _readingStandardOutputThread.Start();
        }

        void _process_Exited(object sender, EventArgs e)
        {
            _isWorking = false;
        }

        void ErrorReadingThread()
        {
            const int bufferSize = 1024;

            char[] buffer = new char[bufferSize];
            int readed = 0;

            StringBuilder sb = new StringBuilder();

            string lastLine = string.Empty;

            // Now read the output of the DOS application
            while ((readed = _process.StandardError.Read(buffer, 0, bufferSize)) > 0)
            {
                string s = new string(buffer, 0, readed);
                sb.Append(s);
            }
            sb.Append(_process.StandardError.ReadToEnd());
            _warning = sb.ToString();
            if (!string.IsNullOrEmpty(_warning))
                Trace.WriteLine("FreeArc", _warning);//!-!
        }

        void ProgressThread()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                //Process process = (Process)o;
                const int bufferSize = 1024;

                char[] buffer = new char[bufferSize];
                int readed = 0;

                string lastLine = string.Empty;

                // Now read the output of the DOS application
                while ((readed = _process.StandardOutput.Read(buffer, 0, bufferSize)) > 0)
                {
                    string s = new string(buffer, 0, readed);

                    if (s.Contains("\r") || s.Contains("\n"))
                    {
                        string[] lines = s.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0)
                            lines[0] = lastLine + lines[0];

                        foreach (string line in lines)
                            ProcessLastLine(line);

                        if (lines.Length > 0 && !s.EndsWith("\n") && !s.EndsWith("\r"))//!-!
                            lastLine = lines[lines.Length - 1];
                        else
                            lastLine = string.Empty;
                    }
                    else
                    {
                        lastLine += s;
                        ProcessLastLine(lastLine);
                    }

                    sb.Append(s);
                }
                sb.Append(_process.StandardOutput.ReadToEnd());

                string output = sb.ToString();

                if (string.IsNullOrEmpty(output))
                {
                    _success = false;
                    Trace.WriteLine("arcwrapper:empty console output");//!-!
                    return;
                }

                if (_list)
                {
                    //list the files
                    //all lines in front of empty line are path                    
                    string searchStr = "\n\r\n";
                    int indexOf = output.IndexOf(searchStr);
                    if (indexOf < 0)
                        throw new ArgumentException(string.Format("Failed to find {0} in {1}", searchStr, output));//!-!
                    string pathList = string.Empty;
                    if (indexOf > -1)
                        pathList = output.Substring(0, indexOf);

                    _decompressList.AddRange(pathList.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));
                }


                string[] linesAll = output.Split(
                    new string[] { Environment.NewLine }
                    , StringSplitOptions.RemoveEmptyEntries);

                //kdyz se to uspesne zabali, konzole vypise na predposlednim radku ALL OK
                //a pak to odradkuje prazdnym radkem, ale ten uz se orezal pomoci StringSplitOptions.RemoveEmptyEntries
                _success = linesAll.Length > 0 && linesAll[linesAll.Length - 1] == "All OK";//!-!
                if (!_success)
                    Trace.WriteLine(sb.ToString());
            }
            catch (Exception ex)
            {
                _success = false;
                Trace.WriteLine(ex);
                Trace.WriteLine(sb.ToString());
            }
        }

        /// <summary>
        /// hleda informace o tom, kolik se udelalo procent
        /// ve vystupu je jedna radka, ktera obsahuje slovo processed a
        /// pak nasleduje cislo procenta a 4x backspace a cislo procenta a 4x backspace atd...
        /// </summary>
        /// <param name="lastline"></param>
        void ProcessLastLine(string lastline)
        {
            if (lastline.Contains(Environment.NewLine))
                throw new ArgumentException(); //tady to parsuje uz jedinou radku

            const string processed = "Processed";//!-!
            int index = lastline.IndexOf(processed);
            if (index < 0)
                return;
            string progressString = lastline.Substring(index + processed.Length);

            int lastPercent = lastline.LastIndexOf('%');
            //pred procentem se hleda mezera nebo backspace

            progressString = lastline.Substring(0, lastPercent);

            int separator = progressString.LastIndexOfAny(new char[] { ' ', '\b' });

            string percentString = progressString.Substring(separator + 1);
            float percent;
            if (float.TryParse(percentString, NumberStyles.Any, CultureInfo.InvariantCulture, out percent))
            {
                OnProgress(new ProgressEventArgs(percent));
            }
            else
            {
                OnProgress(new ProgressEventArgs(0));//???
            }
        }

        protected virtual void OnProgress(ProgressEventArgs args)
        {
            if (Progress != null)
                Progress(this, args);
        }

        /// <summary>
        /// Nelze zabijet zabijenim vlakna, protoze by zustal viset spusteny process
        /// </summary>
        /// <returns></returns>
        public void AbortAsync()
        {
            try
            {
                if (_process != null)
                {
                    if (!_process.HasExited)
                    {
                        try
                        {
                            _process.Kill();
                        }
                        catch (InvalidOperationException)//hodi to vyjimku, kdyz se mezitim ukonci sam
                        {
                        }
                    }
                    _process = null;
                }

                Thread t = _readingStandardOutputThread;
                _readingStandardOutputThread = null;
                if (t != null)
                {
                    t.Abort();
                }
                t = _readingStandardErrorThread;
                _readingStandardErrorThread = null;
                if (t != null)
                {
                    t.Abort();
                }
            }
            finally
            {
                _isWorking = false;
            }
        }

        #region IDisposable Members

        public void Dispose()
        {
            //TODO clean up workdir
            AbortAsync();
        }

        #endregion
    }
}
