using System;
using System.Runtime.Serialization;

namespace BuildingModel.Geometry
{
    /// <summary>A point in plan (ft): x east, y north.</summary>
    [DataContract]
    public struct Point2 : IEquatable<Point2>
    {
        [DataMember(Order = 0)] public double X { get; set; }
        [DataMember(Order = 1)] public double Y { get; set; }

        public Point2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(Point2 o) => X == o.X && Y == o.Y;
        public override bool Equals(object obj) => obj is Point2 p && Equals(p);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    /// <summary>An axis-aligned rectangle in plan (ft); the corners are put in order (X0 ≤ X1, Y0 ≤ Y1).</summary>
    [DataContract]
    public struct Rect2 : IEquatable<Rect2>
    {
        [DataMember(Order = 0)] public double X0 { get; set; }
        [DataMember(Order = 1)] public double Y0 { get; set; }
        [DataMember(Order = 2)] public double X1 { get; set; }
        [DataMember(Order = 3)] public double Y1 { get; set; }

        public Rect2(double x0, double y0, double x1, double y1)
        {
            X0 = Math.Min(x0, x1);
            X1 = Math.Max(x0, x1);
            Y0 = Math.Min(y0, y1);
            Y1 = Math.Max(y0, y1);
        }

        public double Width => X1 - X0;
        public double Height => Y1 - Y0;
        public double Area => Width * Height;
        public bool IsEmpty => !(Width > 0) || !(Height > 0);

        /// <summary>True when the point is inside or on the edge.</summary>
        public bool Contains(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;

        /// <summary>Distance from this rectangle to a point (0 inside).</summary>
        public double DistanceTo(double x, double y)
        {
            double dx = Math.Max(0, Math.Max(X0 - x, x - X1));
            double dy = Math.Max(0, Math.Max(Y0 - y, y - Y1));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Distance between two rectangles (0 when they touch or overlap).</summary>
        public double DistanceTo(Rect2 o)
        {
            double dx = Math.Max(0, Math.Max(X0 - o.X1, o.X0 - X1));
            double dy = Math.Max(0, Math.Max(Y0 - o.Y1, o.Y0 - Y1));
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public bool Equals(Rect2 o) => X0 == o.X0 && Y0 == o.Y0 && X1 == o.X1 && Y1 == o.Y1;
        public override bool Equals(object obj) => obj is Rect2 r && Equals(r);
        public override int GetHashCode() => ((X0.GetHashCode() * 397 ^ Y0.GetHashCode()) * 397 ^ X1.GetHashCode()) * 397 ^ Y1.GetHashCode();
        public override string ToString() => $"[{X0:0.###}, {Y0:0.###}] - [{X1:0.###}, {Y1:0.###}]";
    }

    /// <summary>A straight piece of an outline (ft).</summary>
    public struct Segment2
    {
        public Point2 A { get; }
        public Point2 B { get; }

        public Segment2(Point2 a, Point2 b)
        {
            A = a;
            B = b;
        }

        public double Length => Math.Sqrt((B.X - A.X) * (B.X - A.X) + (B.Y - A.Y) * (B.Y - A.Y));
        public bool IsHorizontal => A.Y == B.Y;

        /// <summary>The segment's bounding box (a line, so zero width or height).</summary>
        public Rect2 Box => new Rect2(A.X, A.Y, B.X, B.Y);

        public override string ToString() => $"{A} - {B}";
    }
}
