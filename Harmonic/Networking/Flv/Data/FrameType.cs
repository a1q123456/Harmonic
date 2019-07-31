using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Networking.Flv.Data
{
    public enum FrameType
    {
        KeyFrame = 1,
        InterFrame,
        DisposableInterFrame,
        GeneratedKeyFrame,
        VideoInfoOrCommandFrame
    }
}
