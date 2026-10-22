using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Common.Settings;
using CleanArchitectureBase.Infrastructure.MediaStorage;
using Microsoft.Extensions.Options;

namespace CleanArchitectureBase.Application.UnitTests.Tests;

public class MediaPublicOriginsTests
{
    private static MediaSettings Settings(string? origins = "https://thanhkhac.id.vn, https://www.thanhkhac.id.vn/") => new()
    {
        PublicS3Endpoint = "http://100.86.165.118:8090",
        PublicOrigins = origins,
        S3Bucket = "aiquizz-media",
        S3Region = "us-east-1",
        S3AccessKey = "AKIDEXAMPLE",
        S3SecretKey = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY",
        PresignWindowMinutes = 5
    };

    [TearDown]
    public void Reset() => MediaRequestOrigin.Current = null;

    [TestCase("thanhkhac.id.vn", "https://thanhkhac.id.vn/")]
    [TestCase("THANHKHAC.ID.VN", "https://thanhkhac.id.vn/")]
    [TestCase("www.thanhkhac.id.vn", "https://www.thanhkhac.id.vn/")]
    [TestCase("100.86.165.118", "http://100.86.165.118:8090/")] // nginx chuyển Host không kèm cổng
    [TestCase("100.86.165.118:8090", "http://100.86.165.118:8090/")]
    public void Resolve_MatchesConfiguredHost(string requestHost, string expected)
    {
        new MediaPublicOrigins(Options.Create(Settings())).Resolve(requestHost)!.ToString().Should().Be(expected);
    }

    [TestCase("evil.example")]
    [TestCase("")]
    [TestCase(null)]
    public void Resolve_UnknownHost_ReturnsNull(string? requestHost)
    {
        new MediaPublicOrigins(Options.Create(Settings())).Resolve(requestHost).Should().BeNull();
    }

    [Test]
    public void IgnoresInvalidEntries()
    {
        var origins = new MediaPublicOrigins(Options.Create(Settings("ftp://x.vn, not a url, https://ok.vn")));
        origins.All.Select(o => o.Host).Should().Equal("100.86.165.118", "ok.vn");
    }

    [Test]
    public void Signer_UsesRequestOrigin_AndDefaultsToPublicEndpoint()
    {
        var signer = new S3PresignedUrlSigner(Options.Create(Settings()), TimeProvider.System);

        var byIp = signer.GetUrl("img/a.jpg", TimeSpan.FromMinutes(15));
        MediaRequestOrigin.Current = new Uri("https://thanhkhac.id.vn");
        var byDomain = signer.GetUrl("img/a.jpg", TimeSpan.FromMinutes(15));

        byIp.Should().StartWith("http://100.86.165.118:8090/aiquizz-media/img/a.jpg?");
        byDomain.Should().StartWith("https://thanhkhac.id.vn/aiquizz-media/img/a.jpg?");
        // SigV4 ký cả Host -> chữ ký khác nhau
        byDomain[(byDomain.IndexOf("X-Amz-Signature=", StringComparison.Ordinal))..]
            .Should().NotBe(byIp[(byIp.IndexOf("X-Amz-Signature=", StringComparison.Ordinal))..]);
    }
}
