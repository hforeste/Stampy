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
        private List<KustoColumnMapping> _schema;
        private string _filePath;
        private bool _isColumnsWritten;

        public CsvFileLogger(string fileName, List<KustoColumnMapping> schema)
        {
            _schema = schema;
            _filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
            _fileStreamWriter = new StreamWriter(File.Open(_filePath, FileMode.Append, FileAccess.Write, FileShare.Read));
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
                //check if the columns exist already
                var line = string.Join(",", _schema.Select(c => c.ColumnName));

                using (var reader = new StreamReader(File.Open(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read)))
                {
                    var columnLine = await reader.ReadLineAsync();
                    if (!columnLine.Equals(line, StringComparison.CurrentCultureIgnoreCase))
                    {
                        await _fileStreamWriter.WriteLineAsync(line);
                    }
                }
                _isColumnsWritten = true;
            }

            await _fileStreamWriter.WriteLineAsync(message);
        }
    }
}
