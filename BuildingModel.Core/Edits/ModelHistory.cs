using System;
using System.Collections.Generic;
using System.Linq;
using BuildingModel.Model;
using BuildingModel.Serialization;

namespace BuildingModel.Edits
{
    /// <summary>
    /// Undo and redo for the whole building (every level, and the apps' data in the bags): a snapshot is taken after
    /// every change (<see cref="Record"/>); undo puts the previous snapshot back into the same building object.
    /// </summary>
    public sealed class ModelHistory
    {
        /// <summary>How many steps are kept; the oldest are dropped.</summary>
        public const int Limit = 200;

        private readonly List<string> states = new List<string>();
        private int current = -1;

        public bool CanUndo => current > 0;
        public bool CanRedo => current >= 0 && current < states.Count - 1;
        public int UndoSteps => Math.Max(0, current);
        public int RedoSteps => CanRedo ? states.Count - 1 - current : 0;

        /// <summary>Forgets every step; the building as it is now is the start (new or opened model).</summary>
        public void Reset(Building b)
        {
            states.Clear();
            states.Add(ModelFile.BuildingToJson(b));
            current = 0;
        }

        /// <summary>Adds a step when the building differs from the last step; a new step drops the undone ones. True when added.</summary>
        public bool Record(Building b)
        {
            string s = ModelFile.BuildingToJson(b);
            if (current >= 0 && states[current] == s) return false;
            if (current < states.Count - 1) states.RemoveRange(current + 1, states.Count - current - 1);
            states.Add(s);
            current = states.Count - 1;
            if (states.Count > Limit)
            {
                states.RemoveAt(0);
                current--;
            }
            return true;
        }

        /// <summary>Puts the previous step back. Returns what was undone ("W3 added"), or null when there is nothing to undo.</summary>
        public string Undo(Building b)
        {
            if (!CanUndo) return null;
            string what = Describe(states[current - 1], states[current]);
            current--;
            Assign(b, ModelFile.BuildingFromJson(states[current]));
            return what;
        }

        /// <summary>Puts the next undone step back. Returns what was redone, or null when there is nothing to redo.</summary>
        public string Redo(Building b)
        {
            if (!CanRedo) return null;
            string what = Describe(states[current], states[current + 1]);
            current++;
            Assign(b, ModelFile.BuildingFromJson(states[current]));
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
