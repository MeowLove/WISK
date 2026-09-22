using System.Text;
using Wisk.Core;
using Xunit;

namespace Wisk.Core.Tests;

public sealed class ExtensionTrustPolicyTests
{
    [Fact]
    public void EmbeddedPolicyLoadsAndTrustsNoUnapprovedPublisher()
    {
        var publishers = ExtensionTrustPolicy.LoadEmbedded();

        Assert.Empty(publishers);
    }

    [Fact]
    public void AmbiguousPublisherPolicyIsRejected()
    {
        var json = Encoding.UTF8.GetBytes("""
            {"schemaVersion":"2.0","publishers":[],"Publishers":[]}
            """);

        Assert.Throws<System.Text.Json.JsonException>(() => ExtensionTrustPolicy.Deserialize(json));
    }

    [Fact]
    public void NullPublisherIsRejectedWithStablePolicyError()
    {
        var json = Encoding.UTF8.GetBytes("""
            {"schemaVersion":"2.0","publishers":[null]}
            """);

        var exception = Assert.Throws<InvalidOperationException>(() => ExtensionTrustPolicy.Deserialize(json));
        Assert.Equal("Embedded extension trust policy contains an invalid publisher.", exception.Message);
    }
}
