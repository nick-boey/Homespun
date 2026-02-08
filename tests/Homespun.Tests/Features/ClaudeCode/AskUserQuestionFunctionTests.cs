using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Microsoft.Extensions.AI;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class AskUserQuestionFunctionTests
{
    [Test]
    public void Create_ReturnsAIFunctionWithCorrectName()
    {
        // Arrange
        var handler = (List<AskUserQuestionInput> questions, CancellationToken ct)
            => Task.FromResult(new Dictionary<string, string>());

        // Act
        var aiFunction = AskUserQuestionFunction.Create(handler);

        // Assert
        Assert.That(aiFunction.Name, Is.EqualTo("ask_user"));
    }

    [Test]
    public void Create_HasCorrectDescription()
    {
        // Arrange
        var handler = (List<AskUserQuestionInput> questions, CancellationToken ct)
            => Task.FromResult(new Dictionary<string, string>());

        // Act
        var aiFunction = AskUserQuestionFunction.Create(handler);

        // Assert
        Assert.That(aiFunction.Description, Does.Contain("question"));
    }

    [Test]
    public void Create_HasCorrectInputSchema()
    {
        // Arrange
        var handler = (List<AskUserQuestionInput> questions, CancellationToken ct)
            => Task.FromResult(new Dictionary<string, string>());

        // Act
        var aiFunction = AskUserQuestionFunction.Create(handler);

        // Assert - Schema should have 'questions' array property
        var schema = aiFunction.JsonSchema;
        Assert.That(schema.ValueKind, Is.EqualTo(JsonValueKind.Object));

        Assert.That(schema.TryGetProperty("properties", out var properties), Is.True);
        Assert.That(properties.TryGetProperty("questions", out var questionsSchema), Is.True);
        Assert.That(questionsSchema.GetProperty("type").GetString(), Is.EqualTo("array"));

        // Items should have question, header, options, multiSelect
        var items = questionsSchema.GetProperty("items");
        var itemProperties = items.GetProperty("properties");
        Assert.That(itemProperties.TryGetProperty("question", out _), Is.True);
        Assert.That(itemProperties.TryGetProperty("header", out _), Is.True);
        Assert.That(itemProperties.TryGetProperty("options", out _), Is.True);
        Assert.That(itemProperties.TryGetProperty("multiSelect", out _), Is.True);

        // Required should include 'questions'
        Assert.That(schema.TryGetProperty("required", out var required), Is.True);
        var requiredItems = required.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.That(requiredItems, Does.Contain("questions"));
    }

    [Test]
    public async Task Invoke_CallsHandlerWithParsedQuestions()
    {
        // Arrange
        List<AskUserQuestionInput>? receivedQuestions = null;
        var handler = (List<AskUserQuestionInput> questions, CancellationToken ct) =>
        {
            receivedQuestions = questions;
            return Task.FromResult(new Dictionary<string, string> { ["q1"] = "answer1" });
        };

        var aiFunction = AskUserQuestionFunction.Create(handler);

        var arguments = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["questions"] = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["question"] = "Which approach?",
                    ["header"] = "Approach",
                    ["multiSelect"] = false,
                    ["options"] = new List<object>
                    {
                        new Dictionary<string, object?> { ["label"] = "Option A", ["description"] = "First option" },
                        new Dictionary<string, object?> { ["label"] = "Option B", ["description"] = "Second option" }
                    }
                }
            }
        });

        // Act
        var result = await aiFunction.InvokeAsync(arguments);

        // Assert
        Assert.That(receivedQuestions, Is.Not.Null);
        Assert.That(receivedQuestions, Has.Count.EqualTo(1));
        Assert.That(receivedQuestions![0].Question, Is.EqualTo("Which approach?"));
        Assert.That(receivedQuestions[0].Header, Is.EqualTo("Approach"));
        Assert.That(receivedQuestions[0].MultiSelect, Is.False);
        Assert.That(receivedQuestions[0].Options, Has.Count.EqualTo(2));
        Assert.That(receivedQuestions[0].Options[0].Label, Is.EqualTo("Option A"));
        Assert.That(receivedQuestions[0].Options[1].Description, Is.EqualTo("Second option"));
    }

    [Test]
    public async Task Invoke_ReturnsHandlerResult()
    {
        // Arrange
        var expectedAnswers = new Dictionary<string, string>
        {
            ["q1"] = "Option A",
            ["q2"] = "Option C"
        };
        var handler = (List<AskUserQuestionInput> questions, CancellationToken ct)
            => Task.FromResult(expectedAnswers);

        var aiFunction = AskUserQuestionFunction.Create(handler);

        var arguments = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["questions"] = new List<object>
            {
                new Dictionary<string, object?> { ["question"] = "q1", ["header"] = "h" }
            }
        });

        // Act
        var result = await aiFunction.InvokeAsync(arguments);

        // Assert - result should contain the answers
        Assert.That(result, Is.Not.Null);
        var resultDict = result as Dictionary<string, string>;
        Assert.That(resultDict, Is.Not.Null);
        Assert.That(resultDict!["q1"], Is.EqualTo("Option A"));
        Assert.That(resultDict["q2"], Is.EqualTo("Option C"));
    }

    [Test]
    public void Invoke_PropagatesCancellation()
    {
        // Arrange
        var handler = async (List<AskUserQuestionInput> questions, CancellationToken ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new Dictionary<string, string>();
        };

        var aiFunction = AskUserQuestionFunction.Create(handler);

        var arguments = new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["questions"] = new List<object>
            {
                new Dictionary<string, object?> { ["question"] = "q1", ["header"] = "h" }
            }
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<TaskCanceledException>(async () =>
            await aiFunction.InvokeAsync(arguments, cts.Token));
    }

    [Test]
    public async Task Invoke_WithJsonElementArguments_ParsesCorrectly()
    {
        // Arrange - This tests the path when arguments come as JsonElement (from MCP bridge)
        List<AskUserQuestionInput>? receivedQuestions = null;
        var handler = (List<AskUserQuestionInput> questions, CancellationToken ct) =>
        {
            receivedQuestions = questions;
            return Task.FromResult(new Dictionary<string, string> { ["q1"] = "answer1" });
        };

        var aiFunction = AskUserQuestionFunction.Create(handler);

        // Simulate what DynamicAIFunctionMcpServer does: passes deserialized JsonElement values
        var json = """
        {
            "questions": [
                {
                    "question": "Which library?",
                    "header": "Library",
                    "multiSelect": true,
                    "options": [
                        {"label": "NUnit", "description": "Testing framework"},
                        {"label": "xUnit", "description": "Alternative framework"}
                    ]
                }
            ]
        }
        """;
        var jsonDoc = JsonDocument.Parse(json);
        var argsDict = new Dictionary<string, object?>();
        foreach (var prop in jsonDoc.RootElement.EnumerateObject())
        {
            argsDict[prop.Name] = DeserializeJsonElement(prop.Value);
        }
        var arguments = new AIFunctionArguments(argsDict);

        // Act
        var result = await aiFunction.InvokeAsync(arguments);

        // Assert
        Assert.That(receivedQuestions, Is.Not.Null);
        Assert.That(receivedQuestions, Has.Count.EqualTo(1));
        Assert.That(receivedQuestions![0].Question, Is.EqualTo("Which library?"));
        Assert.That(receivedQuestions[0].MultiSelect, Is.True);
        Assert.That(receivedQuestions[0].Options, Has.Count.EqualTo(2));
    }

    /// <summary>
    /// Mirrors DynamicAIFunctionMcpServer.DeserializeJsonElement to simulate MCP bridge input.
    /// </summary>
    private static object? DeserializeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal :
                                   element.TryGetInt64(out var longVal) ? longVal :
                                   element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(DeserializeJsonElement)
                .ToList(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => DeserializeJsonElement(p.Value)),
            _ => element.GetRawText()
        };
    }
}
