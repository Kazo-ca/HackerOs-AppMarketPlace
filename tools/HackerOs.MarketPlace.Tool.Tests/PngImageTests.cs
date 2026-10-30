namespace HackerOs.MarketPlace.Tests;

public sealed class PngImageTests
{
    [Fact]
    public void The_size_is_read_from_the_header()
    {
        Assert.True(PngImage.TryReadSize(TestCatalog.Png(256, 128), out int width, out int height));
        Assert.Equal((256, 128), (width, height));
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    public void Other_files_are_not_png(byte[] bytes) =>
        Assert.False(PngImage.TryReadSize(bytes, out _, out _));

    [Fact]
    public void A_zero_size_is_refused() =>
        Assert.False(PngImage.TryReadSize(TestCatalog.Png(0, 256), out _, out _));
}
