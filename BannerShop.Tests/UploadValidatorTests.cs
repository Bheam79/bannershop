using BannerShop.Api.Services.BannerBuilder;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BannerShop.Tests;

/// <summary>
/// Unit tests for UploadValidator.ValidateStreamAsync (magic-byte + MIME checks).
/// Uses in-memory streams so no IFormFile mocking is needed.
/// </summary>
public class UploadValidatorTests
{
    private static UploadValidator MakeValidator(long maxBytes = 50L * 1024 * 1024)
    {
        var opts = Options.Create(new FileStorageOptions
        {
            LocalRoot = "/tmp/test",
            PublicBaseUrl = "https://example.com",
            MaxUploadBytes = maxBytes,
            AllowedMimeTypes = ["image/jpeg", "image/jpg", "image/png", "image/webp", "application/pdf"]
        });
        return new UploadValidator(opts);
    }

    // ── JPEG ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateStream_ValidJpeg_ReturnsValidTrueWithJpgExtension()
    {
        var validator = MakeValidator();
        // Minimal JPEG magic bytes: FF D8 FF + padding
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        using var stream = new MemoryStream(bytes);

        var (valid, error, ext) = await validator.ValidateStreamAsync(stream, "image/jpeg", bytes.Length);

        valid.Should().BeTrue();
        error.Should().BeNull();
        ext.Should().Be("jpg");
    }

    [Fact]
    public async Task ValidateStream_JpegWithJpgMime_IsAccepted()
    {
        var validator = MakeValidator();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        using var stream = new MemoryStream(bytes);

        var (valid, _, ext) = await validator.ValidateStreamAsync(stream, "image/jpg", bytes.Length);

        valid.Should().BeTrue();
        ext.Should().Be("jpg");
    }

    // ── PNG ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateStream_ValidPng_ReturnsValidTrueWithPngExtension()
    {
        var validator = MakeValidator();
        // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A + padding
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52 };
        using var stream = new MemoryStream(bytes);

        var (valid, error, ext) = await validator.ValidateStreamAsync(stream, "image/png", bytes.Length);

        valid.Should().BeTrue();
        error.Should().BeNull();
        ext.Should().Be("png");
    }

    // ── PDF ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateStream_ValidPdf_ReturnsValidTrueWithPdfExtension()
    {
        var validator = MakeValidator();
        // PDF magic bytes: "%PDF" (25 50 44 46) + padding
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(bytes);

        var (valid, error, ext) = await validator.ValidateStreamAsync(stream, "application/pdf", bytes.Length);

        valid.Should().BeTrue();
        error.Should().BeNull();
        ext.Should().Be("pdf");
    }

    // ── WEBP ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateStream_ValidWebP_ReturnsValidTrueWithWebpExtension()
    {
        var validator = MakeValidator();
        // WEBP: "RIFF" at 0, "WEBP" at 8 (need at least 12 bytes)
        var bytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20 };
        using var stream = new MemoryStream(bytes);

        var (valid, error, ext) = await validator.ValidateStreamAsync(stream, "image/webp", bytes.Length);

        valid.Should().BeTrue();
        error.Should().BeNull();
        ext.Should().Be("webp");
    }

    // ── Rejection cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateStream_EmptyStream_ReturnsError()
    {
        var validator = MakeValidator();
        using var stream = new MemoryStream();

        var (valid, error, ext) = await validator.ValidateStreamAsync(stream, "image/jpeg", 0);

        valid.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateStream_FileTooLarge_ReturnsError()
    {
        var validator = MakeValidator(maxBytes: 10); // tiny limit
        var bytes = new byte[100];
        using var stream = new MemoryStream(bytes);

        var (valid, error, _) = await validator.ValidateStreamAsync(stream, "image/jpeg", 100);

        valid.Should().BeFalse();
        error.Should().Contain("large");
    }

    [Fact]
    public async Task ValidateStream_DisallowedMimeType_ReturnsError()
    {
        var validator = MakeValidator();
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F };
        using var stream = new MemoryStream(bytes);

        var (valid, error, _) = await validator.ValidateStreamAsync(stream, "text/plain", bytes.Length);

        valid.Should().BeFalse();
        error.Should().Contain("Unsupported");
    }

    [Fact]
    public async Task ValidateStream_MismatchedMagicBytes_ReturnsError()
    {
        var validator = MakeValidator();
        // Say it's a PNG but give JPEG bytes
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        using var stream = new MemoryStream(bytes);

        var (valid, error, _) = await validator.ValidateStreamAsync(stream, "image/png", bytes.Length);

        valid.Should().BeFalse();
        error.Should().Contain("contents");
    }

    [Fact]
    public async Task ValidateStream_TooShortForMagicBytes_ReturnsError()
    {
        var validator = MakeValidator();
        // Just 1 byte — can't verify magic
        var bytes = new byte[] { 0xFF };
        using var stream = new MemoryStream(bytes);

        var (valid, _, _) = await validator.ValidateStreamAsync(stream, "image/jpeg", 1);

        valid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateStream_RewindsSeekableStream()
    {
        var validator = MakeValidator();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        using var stream = new MemoryStream(bytes);

        await validator.ValidateStreamAsync(stream, "image/jpeg", bytes.Length);

        // After validation, the seekable stream should be rewound
        stream.Position.Should().Be(0);
    }

    // ── ExtByMime static dictionary ────────────────────────────────────────────

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/jpg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    [InlineData("application/pdf", "pdf")]
    public void ExtByMime_KnownTypes_MapsCorrectly(string mime, string expectedExt)
    {
        UploadValidator.ExtByMime.Should().ContainKey(mime);
        UploadValidator.ExtByMime[mime].Should().Be(expectedExt);
    }

    // ── IFormFile overload (ValidateAsync) ────────────────────────────────────

    private static IFormFile MakeFormFile(byte[] bytes, string contentType, string fileName = "test.jpg")
    {
        var stream = new MemoryStream(bytes);
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(bytes.Length);
        fileMock.Setup(f => f.ContentType).Returns(contentType);
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.OpenReadStream()).Returns(stream);
        return fileMock.Object;
    }

    [Fact]
    public async Task ValidateAsync_NullFile_ReturnsError()
    {
        var validator = MakeValidator();
        var (valid, error, _) = await validator.ValidateAsync(null);
        valid.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateAsync_EmptyFile_ReturnsError()
    {
        var validator = MakeValidator();
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.Length).Returns(0);
        var (valid, error, _) = await validator.ValidateAsync(fileMock.Object);
        valid.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateAsync_FileTooLarge_ReturnsError()
    {
        var validator = MakeValidator(maxBytes: 10);
        var bytes = new byte[100];
        var file = MakeFormFile(bytes, "image/jpeg");
        var (valid, error, _) = await validator.ValidateAsync(file);
        valid.Should().BeFalse();
        error.Should().Contain("large");
    }

    [Fact]
    public async Task ValidateAsync_DisallowedMimeType_ReturnsError()
    {
        var validator = MakeValidator();
        var bytes = new byte[20];
        var file = MakeFormFile(bytes, "text/plain");
        var (valid, error, _) = await validator.ValidateAsync(file);
        valid.Should().BeFalse();
        error.Should().Contain("Unsupported");
    }

    [Fact]
    public async Task ValidateAsync_ValidJpeg_ReturnsValidTrue()
    {
        var validator = MakeValidator();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        var file = MakeFormFile(bytes, "image/jpeg");
        var (valid, error, ext) = await validator.ValidateAsync(file);
        valid.Should().BeTrue();
        ext.Should().Be("jpg");
    }

    [Fact]
    public async Task ValidateAsync_WrongMagicBytes_ReturnsError()
    {
        var validator = MakeValidator();
        // Says it's PNG but has JPEG bytes
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01 };
        var file = MakeFormFile(bytes, "image/png", "test.png");
        var (valid, error, _) = await validator.ValidateAsync(file);
        valid.Should().BeFalse();
        error.Should().Contain("contents");
    }
}
