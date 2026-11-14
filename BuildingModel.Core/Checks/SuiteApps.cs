namespace BuildingModel.Checks
{
    /// <summary>
    /// The suite's apps by the IDs the hub and the project locks use, and their names: where an issue is fixed
    /// (<see cref="ModelIssue.FixIn"/>) and whose review list an assumption is on (<see cref="ReviewItem.App"/>).
    /// </summary>
    public static class SuiteApps
    {
        public const string Building = "building", Wind = "wind", Loads = "loads", ShearWalls = "shearwalls";

        /// <summary>"the Building Model", "the Wind Load Calculator", "the Load Manager", "the Shear Wall Calculator".</summary>
        public static string Name(string app) =>
            app == Building ? "the Building Model" : app == Wind ? "the Wind Load Calculator" : app == Loads ? "the Load Manager"
            : app == ShearWalls ? "the Shear Wall Calculator" : app;
    }
}
