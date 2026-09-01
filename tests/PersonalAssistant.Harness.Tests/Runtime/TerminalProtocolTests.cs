using PersonalAssistant.Harness.Runtime;
using Xunit;

namespace PersonalAssistant.Harness.Tests.Runtime;

public sealed class TerminalProtocolTests
{
    [Fact]
    public void Unknown_frame_type_is_rejected()
    {
        var exception = Assert.Throws<TerminalProtocolException>(() =>
            TerminalProtocolValidator.ParseClientFrame("{\"type\":\"command\"}"));

        Assert.Equal("unknown_frame_type", exception.Code);
    }

    [Fact]
    public void Invalid_hello_version_is_rejected()
    {
        var exception = Assert.Throws<TerminalProtocolException>(() =>
            TerminalProtocolValidator.ValidateHello(
                new TerminalHelloFrame("phase-0x-terminal.v1", "personal"),
                "personal"));

        Assert.Equal("unsupported_protocol", exception.Code);
    }

    [Fact]
    public void Oversized_input_payload_is_rejected()
    {
        var data = new string('x', TerminalProtocol.MaxPayloadBytes + 1);

        var exception = Assert.Throws<TerminalProtocolException>(() =>
            TerminalProtocolValidator.ParseClientFrame($"{{\"type\":\"input\",\"sequence\":1,\"data\":\"{data}\"}}"));

        Assert.Equal("payload_too_large", exception.Code);
    }

    [Fact]
    public void Output_sequence_must_increase()
    {
        var exception = Assert.Throws<TerminalProtocolException>(() =>
            TerminalProtocolValidator.ValidateOutputSequence(4, 4));

        Assert.Equal("sequence_not_monotonic", exception.Code);
    }

    [Fact]
    public void Valid_client_frames_are_typed_and_resize_is_bounded()
    {
        var input = TerminalProtocolValidator.ParseClientFrame("{\"type\":\"input\",\"sequence\":3,\"data\":\"hello\"}");
        var resize = TerminalProtocolValidator.ParseClientFrame("{\"type\":\"resize\",\"columns\":120,\"rows\":36}");

        Assert.Equal(new TerminalInputFrame(3, "hello"), input);
        Assert.Equal(new TerminalResizeFrame(120, 36), resize);
    }
}
