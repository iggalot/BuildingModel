using System;
using System.Linq;
using BuildingModel.Checks;
using BuildingModel.Edits;
using BuildingModel.Geometry;
using BuildingModel.Model;
using BuildingModel.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BuildingModel.Tests
{
    /// <summary>
    /// The roof (flat, gable, hip) and the project defaults are part of the model (format version 3): the geometry the
    /// apps read (eave, ridge, mean roof height), the roof checks, and the defaults every app takes its starting values from.
    /// </summary>
    [TestClass]
    public class RoofAndDefaultsTests
    {
        private const double Tol = 1e-6;

        /// <summary>60 ft W-E by 40 ft S-N, two 10 ft stories: the eave at 20 ft.</summary>
        private static Building Box(out Level roof)
        {
            var b = Building.New();
            var l2 = BuildingEdits.AddStory(b, 10);
            roof = BuildingEdits.AddStory(b, 10);
            b.AddDiaphragm(l2, new Rect2(0, 0, 60, 40));
            b.AddDiaphragm(roof, new Rect2(0, 0, 60, 40));
            return b;
        }

        private static string[] Codes(Building b) => ModelChecks.Run(b).Select(i => i.Code).ToArray();

        [TestMethod]
        public void NewBuilding_DefaultsAreInTheModel_15ftStories_NoParapetOrEaves()
        {
            var b = Building.New();
            Assert.AreEqual(15, b.Defaults.StoryHeight);
            Assert.AreEqual(0, b.Defaults.Parapet);
            Assert.AreEqual(0, b.Defaults.Overhang);
            Assert.AreEqual(RoofShape.Flat, b.Roof.Shape);
            Assert.AreEqual(0, b.Roof.Parapet);
            Assert.AreEqual(0, b.Roof.Overhang);
            Assert.AreEqual(b.Defaults.RoofPitch, b.Roof.Pitch, "the slope it takes when made sloped");
        }

        [TestMethod]
        public void Defaults_AndRoof_RoundTripInTheFile_ProjectValuesWin()
        {
            var b = Box(out _);
            b.Defaults.StoryHeight = 12;
            b.Defaults.RoofPitch = 26.57;
            b.Roof = new Roof { Shape = RoofShape.Gable, Pitch = 30, Ridge = RidgeAxis.SouthNorth, Overhang = 2 };
            var back = ModelFile.FromJson(ModelFile.ToJson(new ModelDocument { Building = b })).Building;
            Assert.AreEqual(12, back.Defaults.StoryHeight);
            Assert.AreEqual(26.57, back.Defaults.RoofPitch);
            Assert.AreEqual(RoofShape.Gable, back.Roof.Shape);
            Assert.AreEqual(30, back.Roof.Pitch);
            Assert.AreEqual(RidgeAxis.SouthNorth, back.Roof.Ridge);
            Assert.AreEqual(2, back.Roof.Overhang);
        }

        /// <summary>A version 2 file (no roof, no defaults) opens with a flat roof and the program's starting values.</summary>
        [TestMethod]
        public void Version2File_FlatRoof_StartingDefaults()
        {
            var old = Box(out _);
            old.Roof = null;      // what an older file gives the reader: no roof, no defaults
            old.Defaults = null;
            var json = ModelFile.ToJson(new ModelDocument { Building = old, Version = 2 });
            var back = ModelFile.FromJson(json).Building;
            Assert.AreEqual(RoofShape.Flat, back.Roof.Shape);
            Assert.AreEqual(new ModelDefaults().StoryHeight, back.Defaults.StoryHeight);
            Assert.AreEqual(2, back.Diaphragms.Count, "the rest is read as before");
        }

        [TestMethod]
        public void Gable_RidgeAlongTheLongerSide_RiseAndMeanHeight()
        {
            var b = Box(out _);
            b.Roof = new Roof { Shape = RoofShape.Gable, Pitch = 45 };
            Assert.AreEqual(RidgeAxis.WestEast, b.EffectiveRidge(), "Auto: along the 60 ft side");
            Assert.AreEqual(40, b.SpanAcrossRidge(), Tol);
            Assert.AreEqual(20, b.RoofRise(), Tol, "tan 45° × 40 / 2");
            Assert.AreEqual(20, b.EaveHeight(), Tol);
            Assert.AreEqual(40, b.RidgeHeight(), Tol);
            Assert.AreEqual(30, b.MeanRoofHeight(), Tol);
            b.Roof.Ridge = RidgeAxis.SouthNorth;
            Assert.AreEqual(60, b.SpanAcrossRidge(), Tol, "the ridge chosen S-N: it rises across the 60 ft");
            Assert.AreEqual(30, b.RoofRise(), Tol);
        }

        [TestMethod]
        public void Hip_RisesOverTheShorterSide_FlatHasNoRise()
        {
            var b = Box(out _);
            b.Roof = new Roof { Shape = RoofShape.Hip, Pitch = 45, Ridge = RidgeAxis.SouthNorth };
            Assert.AreEqual(40, b.SpanAcrossRidge(), Tol);
            Assert.AreEqual(RidgeAxis.WestEast, b.EffectiveRidge(), "a hip's ridge is along the longer side");
            CollectionAssert.Contains(Codes(b), "HipRidge");
            b.Roof = new Roof { Shape = RoofShape.Flat, Pitch = 45, Parapet = 3 };
            Assert.AreEqual(0, b.RoofRise());
            Assert.AreEqual(20, b.MeanRoofHeight(), Tol, "a flat roof: h is the eave height");
            StringAssert.Contains(b.DescribeRoof(), "parapet 3 ft");
        }

        [TestMethod]
        public void RoofChecks_Slope_NegativeValues_ParapetOnSlope_NotRectangular()
        {
            var b = Box(out var roof);
            Assert.IsFalse(Codes(b).Any(c => c.StartsWith("Roof") || c == "ParapetOnSlopedRoof"), "a flat roof, nothing to say");
            b.Roof = new Roof { Shape = RoofShape.Gable, Pitch = 0 };
            CollectionAssert.Contains(Codes(b), "RoofPitch");
            b.Roof.Pitch = 95;
            CollectionAssert.Contains(Codes(b), "RoofPitch");
            b.Roof.Pitch = 30;
            Assert.IsFalse(Codes(b).Contains("RoofPitch"));
            b.Roof.Parapet = 2;
            CollectionAssert.Contains(Codes(b), "ParapetOnSlopedRoof");
            b.Roof = new Roof { Parapet = -1, Overhang = -2 };
            CollectionAssert.Contains(Codes(b), "RoofParapet");
            CollectionAssert.Contains(Codes(b), "RoofEaves");
            var issue = ModelChecks.Run(b).First(i => i.Code == "RoofEaves");
            Assert.AreEqual(Severity.Error, issue.Severity);
            Assert.IsTrue(issue.Ids.Contains(roof.Id), "shown on the top level");

            b.Roof = new Roof { Shape = RoofShape.Hip, Pitch = 30 };
            BuildingEdits.CutDiaphragms(b, roof, new Rect2(40, 20, 60, 40));
            CollectionAssert.Contains(Codes(b), "RoofNotRectangular");
            Assert.AreEqual(60, b.RoofPlan().Width, Tol, "the roof covers the rectangle around the L");
        }
    }
}
