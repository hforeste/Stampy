using System;
using System.Collections.Generic;

namespace StampyWorker.Jobs
{
    internal class ProcessAction
    {
        private Guid _id;
        internal Guid Id { get { return _id; } }
        public string ProgramPath { get; set; }
        public string WorkingDirectory { get; set; }
        public string Arguments { get; set; }

        public ProcessAction()
        {
            _id = Guid.NewGuid();
        }
    }
}
