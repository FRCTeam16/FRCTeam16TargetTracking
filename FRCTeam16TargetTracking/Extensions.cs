using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FRCTeam16TargetTracking
{
    public static class Extensions
    {
        public static bool IsNumber(this object value)
        {
            if (value is sbyte) return true;
            if (value is byte) return true;
            if (value is short) return true;
            if (value is ushort) return true;
            if (value is int) return true;
            if (value is uint) return true;
            if (value is long) return true;
            if (value is ulong) return true;
            if (value is float) return true;
            if (value is double) return true;
            if (value is decimal) return true;
            return false;
        }
    }
}
