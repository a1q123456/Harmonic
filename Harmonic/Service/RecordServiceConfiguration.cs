namespace Harmonic.Service;

public class RecordServiceConfiguration
{
    public virtual string RecordPath { get; set; } = @"Record";
    public virtual string FilenameFormat { get; set; } = @"recorded-{streamName}";
}