using System;
using System.Collections.Generic;
using System.Text;

namespace Harmonic.Controllers;

[AttributeUsage(AttributeTargets.Class)]
public class NeverRegisterAttribute : Attribute
{

}