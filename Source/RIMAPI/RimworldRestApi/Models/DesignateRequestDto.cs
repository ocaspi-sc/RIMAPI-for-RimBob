using System;
using System.Collections.Generic;

namespace RIMAPI.Models
{
    public class DesignateRequestDto
    {
        public int MapId { get; set; }
        public string Type { get; set; } // "Mine", "Deconstruct", "Harvest", "Hunt"
        public string Designation { get; set; } // RimBob-compatible alias for Type
        public PositionDto PointA { get; set; }
        public PositionDto PointB { get; set; }
        public DesignateRectDto Rect { get; set; }
    }

    public class DesignateRectDto
    {
        public int X1 { get; set; }
        public int Z1 { get; set; }
        public int X2 { get; set; }
        public int Z2 { get; set; }
    }

    public class UnforbidThingsRequestDto
    {
        public int MapId { get; set; }
        public List<string> ThingIds { get; set; } = new List<string>();
    }

    public class UnforbidThingsResponseDto
    {
        public int Requested { get; set; }
        public int Matched { get; set; }
        public int Changed { get; set; }
        public int AlreadyAllowed { get; set; }
        public int Missing { get; set; }
        public int NonMapTargets { get; set; }
        public int NonItemTargets { get; set; }
    }
}
