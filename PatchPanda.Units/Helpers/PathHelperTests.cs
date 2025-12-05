namespace PatchPanda.Units.Helpers
{
    public class PathHelperTests
    {
        [Fact]
        public void ComputePathForEnvironment_ReturnsNull_ForNullOrWhitespace()
        {
            string? nullPath = null;
            string? empty = string.Empty;
            string? whitespace = "   ";

            Assert.Null(nullPath.ComputePathForEnvironment());
            Assert.Null(empty.ComputePathForEnvironment());
            Assert.Null(whitespace.ComputePathForEnvironment());
        }

        [Theory]
        [InlineData("C:\\folder\\file.txt", "/c/folder/file.txt")]
        [InlineData("C:\\folder\\sub\\", "/c/folder/sub")]
        [InlineData("D:\\", "/d")]
        [InlineData("/usr/local/bin//", "/usr/local/bin")]
        public void GetLinuxPath_ConvertsPaths_Correctly(string input, string expected)
        {
            string? result = input.ComputePathForEnvironment();

            Assert.Equal(expected, result);
        }
    }
}
