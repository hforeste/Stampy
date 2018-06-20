using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyCommon.Loggers
{
    public class CsvFileLogger : ITableLogger, IDisposable
    {
        private StreamWriter _fileStreamWriter;
        private List<KustoColumnMapping> _schema;
        private string _fileName;
        private bool _isColumnsWritten;

        public CsvFileLogger(string fileName, List<KustoColumnMapping> schema)
        {
            _fileName = fileName;
            _schema = schema;

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            _fileStreamWriter = new StreamWriter(File.Open(filePath, FileMode.Append, FileAccess.ReadWrite, FileShare.Read));
            _fileStreamWriter.AutoFlush = true;
        }

        public void Dispose()
        {
            _fileStreamWriter.Dispose();
        }

        public async Task WriteRow(string message)
        {
            if (!_isColumnsWritten)
            {
                var line = string.Join(",", _schema.Select(c => c.ColumnName));
                await _fileStreamWriter.WriteLineAsync(line);
                _isColumnsWritten = true;
            }

            await _fileStreamWriter.WriteLineAsync(message);
        }
    }
}
