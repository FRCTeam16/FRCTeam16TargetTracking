using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FRCTeam16TargetTracking
{
    public static class Conversions
    {
        public static double MMToFeet(double mm)
        {
            return mm * .003281;
        }

        public static double InchesToMM(double mm)
        {
            return mm / 0.039370;
        }
    }
}
