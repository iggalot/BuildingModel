using System;
using System.Collections.Generic;

namespace BuildingModel.Edits
{
    /// <summary>
    /// The suite's one undo and redo: a list of snapshots (text: JSON of whatever the app edits), a name for each step, and
    /// which step was saved. Every app uses it: the Building Model and the Shear Wall Calculator through
    /// <see cref="ModelHistory"/> (the whole building), the Load Manager for its Loads section, the Wind Load Calculator for its
    /// wind inputs. An app takes a snapshot after each change (<see cref="Record"/>), puts back the one <see cref="Undo"/> or
    /// <see cref="Redo"/> gives, and calls <see cref="MarkSaved"/> when it saves: <see cref="IsDirty"/> is then the one
    /// "unsaved changes" of the app (an undo back to the saved step is clean again).
    /// </summary>
    public sealed class EditHistory
    {
        /// <summary>How many steps are kept; the oldest are dropped.</summary>
        public const int Limit = 200;

        private readonly List<string> states = new List<string>();
        private readonly List<string> labels = new List<string>();   // labels[i]: what step i did (null: not named)
        private int current = -1;
        private int saved = -1;                                       // the step saved (-1: none, or dropped)

        /// <summary>Raised after any change of the steps or of which is saved (to update Undo/Redo buttons and the title).</summary>
        public event EventHandler Changed;

        public bool CanUndo => current > 0;
        public bool CanRedo => current >= 0 && current < states.Count - 1;
        public int UndoSteps => Math.Max(0, current);
        public int RedoSteps => CanRedo ? states.Count - 1 - current : 0;

        /// <summary>The snapshot of the current step (null before <see cref="Reset"/>).</summary>
        public string Current => current >= 0 ? states[current] : null;

        /// <summary>The snapshot an undo would go back to (null: nothing to undo).</summary>
        public string Previous => CanUndo ? states[current - 1] : null;

        /// <summary>The snapshot a redo would go to (null: nothing to redo).</summary>
        public string Next => CanRedo ? states[current + 1] : null;

        /// <summary>What an undo would undo (the name it was recorded with; null: not named, or nothing to undo).</summary>
        public string UndoLabel => CanUndo ? labels[current] : null;

        /// <summary>What a redo would redo (null: not named, or nothing to redo).</summary>
        public string RedoLabel => CanRedo ? labels[current + 1] : null;

        /// <summary>True when the current step is not the one saved: the app has unsaved changes.</summary>
        public bool IsDirty => current >= 0 && current != saved;

        /// <summary>Forgets every step: <paramref name="state"/> is the start. <paramref name="isSaved"/> false: a start that is not saved yet (a new project).</summary>
        public void Reset(string state, bool isSaved = true)
        {
            states.Clear();
            labels.Clear();
            states.Add(state ?? "");
            labels.Add(null);
            current = 0;
            saved = isSaved ? 0 : -1;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Adds a step when <paramref name="state"/> differs from the current one; a new step drops the undone ones. True when added.</summary>
        public bool Record(string state, string what = null)
        {
            state = state ?? "";
            if (current < 0) { Reset(state); return false; }
            if (states[current] == state) return false;
            if (current < states.Count - 1)
            {
                states.RemoveRange(current + 1, states.Count - current - 1);
                labels.RemoveRange(current + 1, labels.Count - current - 1);
                if (saved > current) saved = -1; // the saved step was undone and is now dropped
            }
            states.Add(state);
            labels.Add(what);
            current = states.Count - 1;
            if (states.Count > Limit)
            {
                states.RemoveAt(0);
                labels.RemoveAt(0);
                current--;
                saved = saved > 0 ? saved - 1 : -1;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>
        /// Replaces the current step with <paramref name="state"/>, without a step of its own and keeping the steps to redo: for
        /// data an app fills in by itself after a change, an undo or a redo, which must not become a step or drop the redo steps.
        /// The saved mark stays (what is filled in by itself is not an edit). True when the step changed.
        /// </summary>
        public bool Amend(string state)
        {
            state = state ?? "";
            if (current < 0) { Reset(state); return true; }
            if (states[current] == state) return false;
            states[current] = state;
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>The current step is what is saved now.</summary>
        public void MarkSaved()
        {
            if (saved == current) return;
            saved = current;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Goes one step back. Returns the snapshot to put back (null: nothing to undo).</summary>
        public string Undo()
        {
            if (!CanUndo) return null;
            current--;
            Changed?.Invoke(this, EventArgs.Empty);
            return states[current];
        }

        /// <summary>Goes one step forward. Returns the snapshot to put back (null: nothing to redo).</summary>
        public string Redo()
        {
            if (!CanRedo) return null;
            current++;
            Changed?.Invoke(this, EventArgs.Empty);
            return states[current];
        }
    }
}
