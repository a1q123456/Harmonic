using System.IO;

namespace Harmonic.Service;

public class RecordService
{
    private readonly RecordServiceConfiguration _configuration;

    public RecordService(RecordServiceConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GetRecordFilename(string streamName)
    {
        return Path.Combine(_configuration.RecordPath, _configuration.FilenameFormat.Replace("{streamName}", streamName));
    }

}