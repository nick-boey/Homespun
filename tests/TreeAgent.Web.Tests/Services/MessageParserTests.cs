using TreeAgent.Web.Services;

namespace TreeAgent.Web.Tests.Services;

public class MessageParserTests
{
    private readonly MessageParser _parser = new();

    [Fact]
    public void Parse_TextMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """{"type":"text","content":"Hello, world!"}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("text", result.Type);
        Assert.Equal("Hello, world!", result.Content);
    }

    [Fact]
    public void Parse_ToolUseMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """{"type":"tool_use","name":"read_file","input":{"path":"/tmp/test.txt"}}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("tool_use", result.Type);
        Assert.Equal("read_file", result.ToolName);
    }

    [Fact]
    public void Parse_ToolResultMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """{"type":"tool_result","content":"File contents here"}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("tool_result", result.Type);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsNull()
    {
        // Arrange
        var json = "not valid json";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        // Act
        var result = _parser.Parse("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Parse_NullString_ReturnsNull()
    {
        // Act
        var result = _parser.Parse(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Parse_SystemMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """{"type":"system","content":"Initialization complete"}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("system", result.Type);
    }

    [Fact]
    public void Parse_ErrorMessage_ReturnsCorrectType()
    {
        // Arrange
        var json = """{"type":"error","message":"Something went wrong"}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("error", result.Type);
        Assert.Equal("Something went wrong", result.ErrorMessage);
    }

    [Fact]
    public void Parse_MessageWithMetadata_PreservesRawJson()
    {
        // Arrange
        var json = """{"type":"text","content":"Hello","timestamp":"2024-01-01T00:00:00Z"}""";

        // Act
        var result = _parser.Parse(json);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(json, result.RawJson);
    }
}
