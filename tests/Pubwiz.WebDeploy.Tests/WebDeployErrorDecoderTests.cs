namespace Pubwiz.WebDeploy.Tests;

public class WebDeployErrorDecoderTests
{
    /// <summary>A real MSDeploy.Exception header returned by Web Deploy 4.0 (test VM) for a request without MSDeploy.SyncOptions.</summary>
    private const string MissingSyncOptionsHeader =
        "H4sIAAAAAAAEALVTTW/TQBC13dZpDYFKFYjj3AqSZTUKLVxySFOkRJC2cgI5cGG9njhuNrtmPyC5ceAX8wdgHVMriqhUgVhLu29m1/PezOw6ruM4P+0o13I89Oz0cZhTKZSY6miCSXSBDDOic8FD+IBSWdB5FbWik+gkhJ5h2kjscDRaEhbCtUlYTt/iaizmyDvtVjJtvz49I2n77CW2T/dKpnCboGBitUCuN+CbJcWiJG3aHw56jCh1SRbYGKJSJMPdC6LJowHnKOuTjT6y4n387nCkCZ2PJaE40jLn2dMYF0Ljtvtwwz3gKS4f16GGqGcibfRjVDZBvzKbE6KV4OeGzlErfySMpOi6nue5rmM/z9vfL+vn79jp6A+p+Lt247vb5WC4MkUhpMYUJH42qDR8JcpiivkXTCMYz7DemSFJUcLxcFQFjUYrTq/WIdUxLIw9kiAUEpVls3AqJIKe5aqOQAmvjghq62fjB0Hg71k1P3YAgGi4R0e6WWn3CU8ZlgKeb211lXWWbQFyi0K4NIyRhOGnFhS2hYM0hKr4YBTK2lC5xrK9L5rBX+mJqzwnQs5R3kPYP/L8F4ZzzHJ+XXXoDp4qv/j2wmxaIaxF9Ahjib3PtuMVCOEquUGqAZf2ga6V+f7v1+4crNGTo29+w67P7pIaBA9+AeSaFyAqBAAA";

    [Fact]
    public void Decode_ReadsTypeAndMessageFromARealHeader()
    {
        var error = WebDeployErrorDecoder.Decode(MissingSyncOptionsHeader);

        Assert.NotNull(error);
        Assert.Equal("DeploymentException", error.ExceptionType);
        Assert.Equal("An unsupported request was received. The request header 'MSDeploy.SyncOptions' must be present before this request can be processed.", error.Message);
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("aGVsbG8=")]
    public void Decode_ReturnsNullForInvalidHeaders(string header)
    {
        Assert.Null(WebDeployErrorDecoder.Decode(header));
    }
}
