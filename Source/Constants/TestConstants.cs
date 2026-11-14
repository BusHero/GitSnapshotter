namespace Constants;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Nesting groups the test trait names and values.")]
public static class TestConstants
{
    public static class Traits
    {
        public static class Names
        {
            public const string CATEGORY = "Category";
        }

        public static class Values
        {
            public const string DISCOVERY = "Discovery";
        }
    }
}
