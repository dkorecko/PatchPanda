namespace PatchPanda.Units.Helpers
{
    public class PathHelperTests
    {
        private readonly Mock<IFileService> _fileService;

        public PathHelperTests()
        {
            _fileService = new Mock<IFileService>();
        }

        [Fact]
        public void ComputePathForEnvironment_ReturnsNull_ForNullOrWhitespace()
        {
            string? nullPath = null;
            string? empty = string.Empty;
            string? whitespace = "   ";

            _fileService.Setup(x => x.Exists(It.IsAny<string>())).Returns(false);

            Assert.Null(nullPath.ComputePathForEnvironment(_fileService.Object));
            Assert.Null(empty.ComputePathForEnvironment(_fileService.Object));
            Assert.Null(whitespace.ComputePathForEnvironment(_fileService.Object));
        }

        [Theory]
        [InlineData("C:\\folder\\file.txt", "/c/folder/file.txt")]
        [InlineData("C:\\folder\\sub\\", "/c/folder/sub")]
        [InlineData("D:\\", "/d")]
        [InlineData("/c/folder/file.txt", "C:\\folder\\file.txt")]
        public void ComputePathForEnvironment_ConvertsPaths_Correctly(string input, string expected)
        {
            _fileService
                .SetupSequence(x => x.Exists(It.IsAny<string>()))
                .Returns(false)
                .Returns(true);
            string? result = input.ComputePathForEnvironment(_fileService.Object);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("C:\\folder\\file.txt")]
        [InlineData("/c/folder/file.txt")]
        public void ComputePathForEnvironment_DoesNotExistEitherWay(string input)
        {
            _fileService
                .SetupSequence(x => x.Exists(It.IsAny<string>()))
                .Returns(false)
                .Returns(false);

            string? result = input.ComputePathForEnvironment(_fileService.Object);

            Assert.Null(result);
        }

        [Theory]
        [InlineData("C:\\folder\\file.txt", "C:\\folder\\file.txt")]
        [InlineData("/c/folder/file.txt", "/c/folder/file.txt")]
        public void ComputePathForEnvironment_NoNeedForChange(string input, string expected)
        {
            _fileService.SetupSequence(x => x.Exists(It.IsAny<string>())).Returns(true);

            string? result = input.ComputePathForEnvironment(_fileService.Object);

            Assert.Equal(result, expected);
        }
    }
}
