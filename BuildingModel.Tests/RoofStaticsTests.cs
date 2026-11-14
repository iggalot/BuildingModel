using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Statics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// A sectioned roof's statics (owner 2026-11-12): every rafter, pair, hip, valley and header on its supports, what each
    /// carries passed down until it reaches the walls; the roof's whole load lands, and each member balances.
    /// </summary>
    [TestClass]
    public class RoofStaticsTests
    {
        private static readonly double Pitch612 = Math.Atan(0.5) * 180 / Math.PI;
        private const double Psf = 10;

        private static List<Wall> Around(params Rect2[] rects)
        {
            var list = new List<Wall>();
            foreach (var r in rects)
            {
                void W(double x0, double y0, double x1, double y1) => list.Add(new Wall { Id = "W" + (list.Count + 1), Start = new Point2(x0, y0), End = new Point2(x1, y1) });
                W(r.X0, r.Y0, r.X1, r.Y0); W(r.X1, r.Y0, r.X1, r.Y1); W(r.X0, r.Y1, r.X1, r.Y1); W(r.X0, r.Y0, r.X0, r.Y1);
            }
            return list;
        }

        private static RoofStatics Solve(RoofFrame f, List<Wall> walls)
        {
            Assert.IsNull(f.NotLaidOut);
            var st = RoofStatics.Of(f, walls, new List<Column>(), new List<Beam>());
            st.Solve(1, m => RoofStatics.AreaLoads(m, new[] { Psf }));
            return st;
        }

        /// <summary>Every member's supports hold what is on it (its own and what lands on it), and the walls get the roof.</summary>
        private static void Balances(RoofStatics st, double area)
        {
            Assert.AreEqual(0, st.Notes.Count, string.Join("\n", st.Notes));
            foreach (var m in st.Members)
            {
                double on = m.Own[0].Points.Sum(p => p.P) + m.Own[0].Lines().Sum(l => (l.B - l.A) * (l.WA + l.WB) / 2) + m.Points.Sum(p => p.P[0]);
                double held = m.Supports.Where(s => s.R != null).Sum(s => s.R[0]);
                Assert.AreEqual(on, held, 1e-6 * Math.Max(1, on), m.ToString());
            }
            Assert.AreEqual(area * Psf, st.Landings.Sum(l => l.P[0]), 1e-6 * area * Psf);
            Assert.IsTrue(st.Landings.All(l => l.Kind == RoofTargetKind.Wall));
        }

        /// <summary>
        /// A hip roof 30 × 20 on its four walls: the commons pairs across the ridge, the jacks on the hips, the hips on the
        /// corners and the ridge board, the board on the pairs. All 600 sq ft lands on the walls.
        /// </summary>
        [TestMethod]
        public void Hip_TheWholeRoofLands_AndEveryMemberBalances()
        {
            var f = RoofFrame.Of(null, new List<RoofSection> { new RoofSection { Walls = new Rect2(0, 0, 30, 20), Shape = RoofShape.Hip } }, Pitch612, 0, 2);
            var st = Solve(f, Around(new Rect2(0, 0, 30, 20)));
            Balances(st, 600);
            Assert.IsTrue(st.Members.Any(m => m.Kind == "rafter pair"));
            var hips = st.Members.Where(m => m.Kind == "hip rafter").ToList();
            Assert.AreEqual(4, hips.Count);
            foreach (var h in hips)
            {
                Assert.AreEqual(2, h.Supports.Count, h.ToString());
                Assert.IsTrue(h.Points.Count > 0, "the jacks land on it");
                Assert.IsTrue(h.Supports.Any(s => s.Kind == RoofTargetKind.Member && s.On.StartsWith("ridge")));
            }
            // the four hips alike (the roof symmetrical)
            var feet = hips.Select(h => h.Supports.First(s => s.Kind == RoofTargetKind.Wall).R[0]).ToList();
            Assert.IsTrue(feet.Max() - feet.Min() < 1e-6, string.Join(", ", feet));
        }

        /// <summary>
        /// The L: the wing's valley jacks on its valleys, all of it down. Overframed (owner 2026-11-14): the main roof framed
        /// through under the wing, each valley laid on it, held by every main rafter it crosses (x = 12 to 18 west, its top on the one under the wing's ridge; 18 to 24
        /// east), none ending on it; no member on one it holds up. A vault: cut in, each valley's top on the main rafters there.
        /// </summary>
        [TestMethod]
        public void Wing_TheValleysCarryTheirJacks_AndTheRoofLands()
        {
            var sections = new List<RoofSection>
            {
                new RoofSection { Name = "main", Walls = new Rect2(0, 0, 40, 20), Shape = RoofShape.Gable, Ridge = RidgeAxis.WestEast },
                new RoofSection { Name = "wing", Walls = new Rect2(10, -16, 26, 10), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth },
            };
            var walls = Around(new Rect2(0, 0, 40, 20));
            walls.Add(new Wall { Id = "WW", Start = new Point2(10, -16), End = new Point2(10, 0) });
            walls.Add(new Wall { Id = "WE", Start = new Point2(26, -16), End = new Point2(26, 0) });
            walls.Add(new Wall { Id = "WS", Start = new Point2(10, -16), End = new Point2(26, -16) });
            var f = RoofFrame.Of(null, sections, Pitch612, 0, 2);
            var st = Solve(f, walls);
            Balances(st, f.Faces.Sum(x => x.Area));
            foreach (var v in st.Members.Where(m => m.Kind == "valley rafter"))
            {
                Assert.IsTrue(v.Points.Count > 0, v + ": its jacks");
                Assert.IsTrue(v.Supports.All(s => s.Shares == null), v + ": overframed, no lever onto the rafters at its top");
                var under = v.Supports.Where(s => s.Kind == RoofTargetKind.Member).Select(s => st.Members.Single(m => m.Id == s.On)).ToList();
                var lines = under.Select(m => m.Rafters[0].Line).OrderBy(x => x).ToList();
                Assert.IsTrue(lines.SequenceEqual(v.Start.X < 18 ? new[] { 12.0, 14, 16, 18 } : new[] { 18.0, 20, 22, 24 }), v + ": " + string.Join(", ", lines));
                Assert.IsTrue(under.All(m => m.Supports.All(s => s.On != v.Id)), v + ": a main rafter under it ends on it");
            }

            var vault = new Building { Roof = new Roof { NoCeilingJoists = true } };
            f = RoofFrame.Of(vault, sections, Pitch612, 0, 2);
            st = Solve(f, walls);
            Balances(st, f.Faces.Sum(x => x.Area));
            foreach (var v in st.Members.Where(m => m.Kind == "valley rafter"))
            {
                var top = v.Supports.Single(s => s.Shares != null);
                Assert.IsTrue(top.Shares.Count >= 1 && Math.Abs(top.Shares.Sum(x => x.share) - 1) < 1e-9);
            }
        }

        /// <summary>
        /// A rafter on a wall's line (owner 2026-11-14: "a rafter doesnt lie along the length of the wall. If rafters are parallel
        /// to walls, the walls will be balloon frame up"): a gable 24 × 20 S-N, its end rafters on the gable end walls held all
        /// the way along them, a foot apart at most; the pair's own load lands on that wall, none of it on the side walls but
        /// its ends' share; the roof balances.
        /// </summary>
        [TestMethod]
        public void RafterOnAGableEndWall_TheWallHoldsItAllAlong()
        {
            var f = RoofFrame.Of(null, new List<RoofSection> { new RoofSection { Walls = new Rect2(0, 0, 24, 20), Shape = RoofShape.Gable, Ridge = RidgeAxis.WestEast } }, Pitch612, 0, 2);
            var walls = Around(new Rect2(0, 0, 24, 20));
            var st = Solve(f, walls);
            Balances(st, 24 * 20);
            var end = st.Members.Single(m => m.Kind == "rafter pair" && Math.Abs(m.Rafters[0].Line) < 1e-9);
            Assert.IsTrue(end.Supports.Count >= 21, end + ": " + end.Supports.Count);
            Assert.IsTrue(end.Supports.Zip(end.Supports.Skip(1), (a, b) => b.At - a.At).All(d => d <= 1 + 1e-9));
            Assert.IsTrue(end.Supports.All(s => s.On == "W4" && s.R[0] > -1e-9), string.Join(", ", end.Supports.Select(s => s.On + " " + s.R[0].ToString("0"))));
            Assert.IsTrue(end.Supports.All(s => s.Spread > 0));
            // the landings on W4 spread along it: the end rafter's strip, 1 ft × 20 ft × 10 psf
            Assert.AreEqual(1 * 20 * Psf, st.Landings.Where(l => l.From == end.Id).Sum(l => l.P[0]), 1e-6);
        }

        /// <summary>
        /// A purlin (owner 2026-11-12; IRC R802.4.5): a gable 40 × 24 at 6:12, a purlin under the south rafters at y = 6 (3 ft up),
        /// braced to the bearing wall at y = 4 (2 ft run for 3 ft rise: 56°, within 45° of plumb), its braces at 4 ft: 11 of
        /// them, each on that wall. Every south rafter is held there; the whole roof still lands, every member balanced. A wall
        /// 4 ft off (y = 2) is too far for a 3 ft rise: none found, said so.
        /// </summary>
        [TestMethod]
        public void Purlin_HoldsTheRafters_BracedToTheWallUnder()
        {
            var f = RoofFrame.Of(null, new List<RoofSection> { new RoofSection { Walls = new Rect2(0, 0, 40, 24), Shape = RoofShape.Gable, Ridge = RidgeAxis.WestEast } }, Pitch612, 0, 2);
            var walls = Around(new Rect2(0, 0, 40, 24));
            walls.Add(new Wall { Id = "WI", Start = new Point2(0, 4), End = new Point2(40, 4) });
            var purlin = new RoofPurlin { Id = "PL1", Start = new Point2(0, 6), End = new Point2(40, 6) };
            var st = RoofStatics.Of(f, walls, new List<Column>(), new List<Beam>(), new[] { purlin });
            st.Solve(1, m => RoofStatics.AreaLoads(m, new[] { Psf }));
            Balances(st, 40 * 24);
            var pm = st.Members.Single(m => m.Kind == "purlin");
            Assert.AreEqual(11, pm.Supports.Count);
            Assert.IsTrue(pm.Supports.All(s => s.On == "WI" && Math.Abs(s.BraceRise - 3) < 1e-9 && Math.Abs(s.BraceRun - 2) < 1e-9));
            var common = st.Members.Single(m => m.Kind == "rafter pair" && Math.Abs(m.Rafters[0].Line - 14) < 1e-9);
            Assert.IsTrue(common.Supports.Any(s => s.On == "PL1" && Math.Abs(s.Where.Y - 6) < 1e-9));
            // each south half on the wall, the bearing wall under it (y = 4: it holds the rafters too), the purlin and the ridge:
            // the purlin its reaction on supports 0, 4, 6, 12 under w = 10 psf over the 38 ft between the end rafters' strips (the
            // two on the gable end walls held all along them, owner 2026-11-14: none of theirs on it)
            var q = new BeamLoads(); q.Spread.Add((0, 12, 38 * Psf));
            Assert.AreEqual(BeamStatics.Reactions(12, new[] { 0.0, 4, 6, 12 }, new[] { q })[0][2], pm.Points.Sum(p => p.P[0]), 1e-6);

            walls[4] = new Wall { Id = "WI", Start = new Point2(0, 2), End = new Point2(40, 2) };
            st = RoofStatics.Of(f, walls, new List<Column>(), new List<Beam>(), new[] { purlin });
            Assert.IsTrue(st.Notes.Any(n => n.Contains("45°")), string.Join("\n", st.Notes));
        }

        /// <summary>
        /// The dormer: its header at y = 2 carries the three rafters cut there (x = 16, 18, 20), its ends on the trimmers; the
        /// dormer's rafters on its cheek walls over the main roof's rafters. All 800 sq ft lands on the walls.
        /// </summary>
        [TestMethod]
        public void Dormer_TheHeaderCarriesItsCutRafters_AndTheRoofLands()
        {
            var f = RoofFrame.Of(null, new List<RoofSection>
            {
                new RoofSection { Name = "main", Walls = new Rect2(0, 0, 40, 20), Shape = RoofShape.Gable, Ridge = RidgeAxis.WestEast },
                new RoofSection { Name = "dormer", Walls = new Rect2(14, 2, 22, 9), Shape = RoofShape.Gable, Ridge = RidgeAxis.SouthNorth, EaveRise = 2 },
            }, Pitch612, 0, 2);
            var st = Solve(f, Around(new Rect2(0, 0, 40, 20)));
            Balances(st, 800);
            var header = st.Members.Single(m => m.Kind == "header");
            Assert.AreEqual(3, header.Points.Count, string.Join("; ", header.Points.Select(p => p.From)));
            double cut = st.Members.Where(m => m.Supports.Any(s => s.On == header.Id)).Sum(m => m.Supports.Single(s => s.On == header.Id).R[0]);
            Assert.AreEqual(cut, header.Supports.Sum(s => s.R[0]), 1e-9);
            Assert.IsTrue(header.Supports.All(s => s.Shares != null && s.Shares.Count == 1), "each end on its trimmer");
        }
    }
}
