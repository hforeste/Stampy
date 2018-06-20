using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StampyCommon.Loggers
{
    interface ITableLogger
    {
        Task WriteRow(string message);
    }
}
