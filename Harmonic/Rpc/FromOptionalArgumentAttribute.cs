using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Rpc;

[AttributeUsage(AttributeTargets.Parameter)]
public class FromOptionalArgumentAttribute : Attribute
{
}