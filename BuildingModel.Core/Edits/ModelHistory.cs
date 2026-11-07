using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Model;
using BuildingModel.Serialization;

namespace BuildingModel.Edits
{
    /// <summary>
    /// Undo and redo for the whole building (every level, and the apps' data in the bags): the suite's <see cref="EditHistory"/>
    /// with the building as its snapshot. A snapshot is taken after every change (<see cref="Record"/>); undo puts the previous
    /// one back into the same building object. <see cref="IsDirty"/> / <see cref="MarkSaved"/>: the app's unsaved changes.
    /// </summary>
    public sealed class ModelHistory
    {
        /// <summary>How many steps are kept; the oldest are dropped.</summary>
        public const int Limit = EditHistory.Limit;

        /// <summary>The steps themselves (the suite's one undo mechanism).</summary>
        public EditHistory Steps { get; } = new EditHistory();

        public event EventHandler Changed { add => Steps.Changed += value; remove => Steps.Changed -= value; }

        public bool CanUndo => Steps.CanUndo;
        public bool CanRedo => Steps.CanRedo;
        public int UndoSteps => Steps.UndoSteps;
        public int RedoSteps => Steps.RedoSteps;

        /// <summary>True when the building is not as last saved (<see cref="MarkSaved"/>), by the steps.</summary>
        public bool IsDirty => Steps.IsDirty;

        /// <summary>The building as it is now is what is saved.</summary>
        public void MarkSaved() => Steps.MarkSaved();

        /// <summary>Forgets every step; the building as it is now is the start (new or opened model).</summary>
        public void Reset(Building b, bool isSaved = true) => Steps.Reset(ModelFile.BuildingToJson(b), isSaved);

        /// <summary>
        /// Adds a step when the building differs from the last step; a new step drops the undone ones. <paramref name="what"/>:
        /// its name for undo ("3 walls declared shear walls"); without one, undo names it from what changed. True when added.
        /// </summary>
        public bool Record(Building b, string what = null) => Steps.Record(ModelFile.BuildingToJson(b), what);

        /// <summary>
        /// Replaces the current step with the building as it is now, without a step of its own and keeping the steps to
        /// redo: for data an app fills in by itself after a change, an undo or a redo (a new wall's default design), which
        /// must not become an undo step or drop the redo steps. True when the step changed.
        /// </summary>
        public bool Amend(Building b) => Steps.Amend(ModelFile.BuildingToJson(b));

        /// <summary>True when the building's geometry (not only the apps' data) differs from the last step.</summary>
        public bool GeometryChanged(Building b) =>
            Steps.Current != null && ModelMerge.GeometryJson(b) != ModelMerge.GeometryJson(ModelFile.BuildingFromJson(Steps.Current));

        /// <summary>Puts the last step back into <paramref name="b"/> (an edit that is refused), without a step of its own.</summary>
        public void Revert(Building b)
        {
            if (Steps.Current != null) Assign(b, ModelFile.BuildingFromJson(Steps.Current));
        }

        /// <summary>Puts the previous step back. Returns what was undone ("W3 added"), or null when there is nothing to undo.</summary>
        public string Undo(Building b)
        {
            if (!CanUndo) return null;
            string what = Steps.UndoLabel ?? Describe(Steps.Previous, Steps.Current);
            Assign(b, ModelFile.BuildingFromJson(Steps.Undo()));
            return what;
        }

        /// <summary>Puts the next undone step back. Returns what was redone, or null when there is nothing to redo.</summary>
        public string Redo(Building b)
        {
            if (!CanRedo) return null;
            string what = Steps.RedoLabel ?? Describe(Steps.Current, Steps.Next);
            Assign(b, ModelFile.BuildingFromJson(Steps.Redo()));
            return what;
        }

        /// <summary>Makes <paramref name="target"/> the same as <paramref name="source"/> (the host keeps its reference to the building).</summary>
        public static void Assign(Building target, Building source)
        {
            target.Name = source.Name;
            target.Levels = source.Levels;
            target.Diaphragms = source.Diaphragms;
            target.Openings = source.Openings;
            target.Walls = source.Walls;
            target.Notes = source.Notes;
            target.Beams = source.Beams;
            target.Columns = source.Columns;
            target.Roof = source.Roof;
            target.Defaults = source.Defaults;
            target.FlippedJoints = source.FlippedJoints;
            target.Data.Clear();
            foreach (var kv in source.Data) target.Data[kv.Key] = kv.Value;
        }

        /// <summary>What changed between two snapshots, in words: "W3 added", "D1 changed", "4 walls deleted".</summary>
        public static string Describe(string before, string after)
        {
            var cs = ChangeSet.Between(ModelFile.BuildingFromJson(before), ModelFile.BuildingFromJson(after));
            return cs.Summary();
        }
    }
}
