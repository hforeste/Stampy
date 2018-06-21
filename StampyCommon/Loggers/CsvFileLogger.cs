using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyCommon.Loggers
{
    internal class CsvFileLogger : ITableLogger, IDisposable
    {
        private StreamWriter _fileStreamWriter;
        private StreamReader _fileStreamReader;
        private List<KustoColumnMapping> _schema;
        private string _filePath;
        private bool _isColumnsWritten;

        public CsvFileLogger(string fileName, List<KustoColumnMapping> schema)
        {
            _schema = schema;
            _filePath = Path.Combine(Directory.GetCurrentDirectory(), $"{fileName}.csv");
            var stream = File.Open(_filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            _fileStreamWriter = new StreamWriter(stream);
            _fileStreamReader = new StreamReader(stream);
        }

        public void Dispose()
        {
            _fileStreamWriter.Dispose();
            _fileStreamReader.Dispose();
        }

        public Task WriteRow(string message)
        {
            if (!_isColumnsWritten)
            {
                //check if the columns exist already
                var line = string.Join(",", _schema.Select(c => c.ColumnName));

                var columnLine = _fileStreamReader.ReadLine();
                if (!string.Equals(columnLine, line, StringComparison.CurrentCultureIgnoreCase))
                {
                    _fileStreamWriter.WriteLine(line);
                    _fileStreamWriter.Flush();
                }

                _isColumnsWritten = true;
            }
            _fileStreamWriter.WriteLine(message);
            _fileStreamWriter.Flush();

            return Task.FromResult(new object());
        }
    }
}
