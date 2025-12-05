namespace PatchPanda.Units.Helpers
{
    public class PathHelperTests
    {
        [Fact]
        public void GetLinuxPath_ReturnsNull_ForNullOrWhitespace()
        {
            string? nullPath = null;
            string? empty = string.Empty;
            string? whitespace = "   ";

            Assert.Null(nullPath.GetLinuxPath());
            Assert.Null(empty.GetLinuxPath());
            Assert.Null(whitespace.GetLinuxPath());
        }

        [Theory]
        [InlineData("C:\\folder\\file.txt", "/c/folder/file.txt")]
        [InlineData("C:\\folder\\sub\\", "/c/folder/sub")]
        [InlineData("D:\\", "/d")]
        [InlineData("/usr/local/bin//", "/usr/local/bin")]
        public void GetLinuxPath_ConvertsPaths_Correctly(string input, string expected)
        {
            string? result = input.GetLinuxPath();

            Assert.Equal(expected, result);
        }
    }
}
