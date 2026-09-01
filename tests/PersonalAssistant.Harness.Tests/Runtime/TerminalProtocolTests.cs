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
    public void Screen_sequence_must_increase()
    {
        var exception = Assert.Throws<TerminalProtocolException>(() =>
            TerminalProtocolValidator.ValidateScreenSequence(4, 4));

        Assert.Equal("sequence_not_monotonic", exception.Code);
    }

    [Fact]
    public void Valid_client_frames_are_typed_without_a_resize_operation()
    {
        var input = TerminalProtocolValidator.ParseClientFrame("{\"type\":\"input\",\"sequence\":3,\"data\":\"hello\"}");
        var ping = TerminalProtocolValidator.ParseClientFrame("{\"type\":\"ping\",\"sequence\":4}");

        Assert.Equal(new TerminalInputFrame(3, "hello"), input);
        Assert.Equal(new TerminalPingFrame(4), ping);
    }

    [Fact]
    public void Resize_frames_are_rejected_because_terminal_geometry_is_fixed()
    {
        var exception = Assert.Throws<TerminalProtocolException>(() =>
            TerminalProtocolValidator.ParseClientFrame("{\"type\":\"resize\",\"columns\":120,\"rows\":36}"));

        Assert.Equal("unknown_frame_type", exception.Code);
    }
}
