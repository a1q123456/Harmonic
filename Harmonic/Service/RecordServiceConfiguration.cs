using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Service
{
    public class RecordServiceConfiguration : IServiceConfiguration
    {
        public string RecordPath { get; set; } = @"Record";
        public string FilenameFormat { get; set; } = @"recorded-{streamName}";
    }
}
