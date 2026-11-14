using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using BuildingModel.Geometry;
using BuildingModel.Model;

namespace BuildingModel.Checks
{
    /// <summary>
    /// An assumption the program made that the engineer of record should agree with (owner 2026-11-14, UI audit A7): not an
    /// error and not a warning, a fourth tier (ⓘ, blue), never mixed with the failures. Each one can be ticked reviewed; the
    /// tick is saved with the project (<see cref="ReviewSection"/>) and printed in the report.
    /// </summary>
    public sealed class ReviewItem
    {
        /// <summary>Stable within its app ("holdowns-no-dead-load"): the tick is kept under it.</summary>
        public string Id { get; set; }

        /// <summary>The app that made the assumption and keeps its tick (<see cref="SuiteApps"/>).</summary>
        public string App { get; set; }

        /// <summary>A few words for the list ("Hold-downs with no dead load").</summary>
        public string Title { get; set; }

        /// <summary>
        /// The assumption with its values ("the gap taken as the thickest sheathing, 0.5 in"). A tick holds for this text only:
        /// when the values change, the item asks to be reviewed again.
        /// </summary>
        public string Text { get; set; }

        /// <summary>The members it is about (empty: the whole design), for the plan.</summary>
        public List<string> Ids { get; set; } = new List<string>();

        public string LevelId { get; set; }
        public Point2? At { get; set; }

        public override string ToString() => $"Review {App}/{Id}: {Text}";
    }

    /// <summary>
    /// One app's ticks on its review list, saved in the project under "Review.&lt;app&gt;" (each app writes only its own key,
    /// the suite's rule). A tick is the item's text when it was ticked and the date.
    /// </summary>
    [DataContract]
    public sealed class ReviewSection
    {
        public static string Key(string app) => "Review." + app;

        [DataMember(Order = 1)] public List<ReviewTick> Ticks { get; set; } = new List<ReviewTick>();

        /// <summary>Ticked reviewed, for its text as it is now.</summary>
        public ReviewTick TickOf(ReviewItem item) =>
            Ticks?.FirstOrDefault(t => t.Id == item?.Id && t.Text == item?.Text);

        public bool IsReviewed(ReviewItem item) => TickOf(item) != null;

        /// <summary>Ticks it (today) or clears it. Any older tick of the same item goes.</summary>
        public void Mark(ReviewItem item, bool reviewed, DateTime? on = null)
        {
            if (item == null) return;
            Ticks = Ticks ?? new List<ReviewTick>();
            Ticks.RemoveAll(t => t.Id == item.Id);
            if (reviewed) Ticks.Add(new ReviewTick { Id = item.Id, Text = item.Text, On = (on ?? DateTime.Today).ToString("yyyy-MM-dd") });
        }

        public bool IsEmpty => Ticks == null || Ticks.Count == 0;

        private static DataContractJsonSerializer Serializer() => new DataContractJsonSerializer(typeof(ReviewSection));

        /// <summary>The app's ticks (none when it has none, or they cannot be read).</summary>
        public static ReviewSection Read(Building b, string app)
        {
            string json = b?.GetData(Key(app));
            if (string.IsNullOrEmpty(json)) return new ReviewSection();
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (ReviewSection)Serializer().ReadObject(ms) ?? new ReviewSection();
            }
            catch (SerializationException) { return new ReviewSection(); }
        }

        public string ToJson()
        {
            using (var ms = new MemoryStream())
            {
                Serializer().WriteObject(ms, this);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        /// <summary>Writes it into the building's data (none ticked: removes the key).</summary>
        public static void Write(Building b, string app, ReviewSection s) => b?.SetData(Key(app), s == null || s.IsEmpty ? null : s.ToJson());
    }

    [DataContract]
    public sealed class ReviewTick
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        /// <summary>The item's text when it was ticked.</summary>
        [DataMember(Order = 2)] public string Text { get; set; }
        /// <summary>yyyy-MM-dd.</summary>
        [DataMember(Order = 3)] public string On { get; set; }
    }
}
