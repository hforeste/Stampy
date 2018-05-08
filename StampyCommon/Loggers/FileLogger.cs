using System;
using System.Diagnostics;
using System.IO;

namespace StampyCommon.Helper
{
    public class FileLogger : ILogger
    {
        private string _fileName;

        public FileLogger()
        {

        }

        public FileLogger(string fileName)
        {
            FileName = fileName;
        }

        public string FileName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_fileName) || !_fileName.Contains(DateTime.Now.ToString("yyyy-MM-dd")))
                {
                    _fileName = string.Format("{0}-{1}.log", Process.GetCurrentProcess().ProcessName, DateTime.Now.ToString("yyyy-MM-dd"));
                }

                return _fileName;
            }
            private set
            {
                _fileName = value;
            }
        }

        public void WriteError(string message, Exception ex = null)
        {
            Write(LogLevel.ERROR, message, ex);
        }

        public void WriteError(string source, string message, Exception ex = null)
        {
            throw new NotImplementedException();
        }

        public void WriteInfo(string message)
        {
            Write(LogLevel.INFO, message);
        }

        public void WriteInfo(string source, string message)
        {
            throw new NotImplementedException();
        }

        private void Write(LogLevel level, string message, Exception ex = null)
        {
            using (var writer = new StreamWriter(File.Open(FileName, FileMode.OpenOrCreate | FileMode.Append, FileAccess.Write, FileShare.None)))
            {
                string content = ex == null ? string.Format("[{0}-{1}]:{2}", DateTime.Now.ToString("yyyy-MM-dd"), level.ToString(), message) : string.Format("[{0}-{1}]:{2} {3} {4}", DateTime.Now.ToString("yyyy-MM-dd"), level.ToString(), message, ex.GetType().ToString(), ex.Message);
                writer.WriteLine(content);
            }
        }
    }

    enum LogLevel
    {
        INFO, ERROR, WARNING
    }
}