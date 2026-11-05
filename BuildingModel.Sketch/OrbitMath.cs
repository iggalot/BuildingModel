using System;
using System.Windows;
using System.Windows.Media.Media3D;

namespace BuildingModel.Sketch
{
    /// <summary>
    /// The 3D views' camera moves, the same in every app as on the plan: the middle button pans (drags the model with the
    /// pointer), the wheel zooms toward the point under the pointer. For a perspective camera looking at a target from a distance.
    /// </summary>
    public static class OrbitMath
    {
        /// <summary>The camera's right and up directions (unit), from where it looks.</summary>
        private static (Vector3D right, Vector3D up, Vector3D look) Axes(PerspectiveCamera cam)
        {
            var look = cam.LookDirection; look.Normalize();
            var right = Vector3D.CrossProduct(look, cam.UpDirection); right.Normalize();
            var up = Vector3D.CrossProduct(right, look); up.Normalize();
            return (right, up, look);
        }

        /// <summary>
        /// The target after a pan of (<paramref name="dx"/>, <paramref name="dy"/>) pixels in a view <paramref name="width"/> wide:
        /// the model follows the pointer at the target's depth.
        /// </summary>
        public static Point3D Pan(PerspectiveCamera cam, Point3D target, double distance, double dx, double dy, double width)
        {
            if (width <= 0) return target;
            var (right, up, _) = Axes(cam);
            double perPixel = 2 * distance * Math.Tan(cam.FieldOfView / 2 * Math.PI / 180) / width;   // the field of view is across the width
            return target - right * (dx * perPixel) + up * (dy * perPixel);
        }

        /// <summary>Where <paramref name="p"/> shows in a view of <paramref name="size"/> (pixels), or null when it is behind the camera.</summary>
        public static Point? Project(PerspectiveCamera cam, Point3D p, Size size)
        {
            double w = size.Width, h = size.Height;
            if (w <= 0 || h <= 0) return null;
            var z = -cam.LookDirection; z.Normalize();
            var x = Vector3D.CrossProduct(cam.UpDirection, z); x.Normalize();
            var y = Vector3D.CrossProduct(z, x);
            var d = p - cam.Position;
            double xc = Vector3D.DotProduct(d, x), yc = Vector3D.DotProduct(d, y), zc = Vector3D.DotProduct(d, z);
            if (zc > -1e-3) return null;
            double f = 1 / Math.Tan(cam.FieldOfView / 2 * Math.PI / 180);   // the field of view is across the width
            double nx = xc * f / -zc, ny = yc * f * (w / h) / -zc;
            return new Point((nx + 1) * w / 2, (1 - ny) * h / 2);
        }

        /// <summary>
        /// A zoom by <paramref name="factor"/> (under 1: in) toward the pointer at <paramref name="at"/> in a view of
        /// <paramref name="size"/>: the point under the pointer, at the target's depth, stays under it. Returns the new target and distance.
        /// </summary>
        public static (Point3D target, double distance) ZoomAt(PerspectiveCamera cam, Point3D target, double distance, double factor, Point at, Size size)
        {
            if (size.Width <= 0 || size.Height <= 0) return (target, distance * factor);
            var (right, up, look) = Axes(cam);
            double t = Math.Tan(cam.FieldOfView / 2 * Math.PI / 180);
            double nx = 2 * at.X / size.Width - 1, ny = 1 - 2 * at.Y / size.Height;
            var pos = target - look * distance;
            // the point under the pointer on the plane through the target, square to the view
            var p = pos + distance * (look + right * (nx * t) + up * (ny * t * size.Height / size.Width));
            // scale the camera and its target about it
            return (p + (target - p) * factor, distance * factor);
        }
    }
}
