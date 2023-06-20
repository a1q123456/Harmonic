using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Service;

public class RecordServiceConfiguration
{
    public virtual string RecordPath { get; set; } = @"Record";
    public virtual string FilenameFormat { get; set; } = @"recorded-{streamName}";
}