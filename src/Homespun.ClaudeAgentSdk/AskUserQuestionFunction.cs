using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Homespun.ClaudeAgentSdk;

/// <summary>
/// Input model for a single question in the AskUserQuestion tool.
/// </summary>
public record AskUserQuestionInput(
    string Question,
    string Header,
    List<AskUserQuestionOptionInput> Options,
    bool MultiSelect
)
{
    public AskUserQuestionInput() : this("", "", new List<AskUserQuestionOptionInput>(), false) { }
}

/// <summary>
/// Input model for a question option.
/// </summary>
public record AskUserQuestionOptionInput(
    string Label,
    string Description
)
{
    public AskUserQuestionOptionInput() : this("", "") { }
}

/// <summary>
/// Factory for creating an AIFunction that implements the ask_user MCP tool.
/// When invoked by Claude through the MCP bridge, it calls the provided handler
/// which can emit events and block until the user answers.
/// </summary>
public static class AskUserQuestionFunction
{
    private static readonly JsonElement Schema = BuildSchema();

    /// <summary>
    /// Creates an AIFunction named "ask_user" that delegates to the provided handler.
    /// </summary>
    /// <param name="handler">
    /// Called when Claude invokes the tool. Receives parsed questions and should
    /// block until the user answers, then return the answers dictionary.
    /// </param>
    public static AIFunction Create(
        Func<List<AskUserQuestionInput>, CancellationToken, Task<Dictionary<string, string>>> handler)
    {
        return new AskUserQuestionAIFunction(handler);
    }

    private static JsonElement BuildSchema()
    {
        var schema = new
        {
            type = "object",
            properties = new
            {
                questions = new
                {
                    type = "array",
                    description = "Questions to ask the user (1-4 questions)",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            question = new
                            {
                                type = "string",
                                description = "The question to ask the user"
                            },
                            header = new
                            {
                                type = "string",
                                description = "Short label displayed as a chip/tag (max 12 chars)"
                            },
                            options = new
                            {
                                type = "array",
                                description = "Available choices (2-4 options)",
                                items = new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        label = new
                                        {
                                            type = "string",
                                            description = "Display text for this option"
                                        },
                                        description = new
                                        {
                                            type = "string",
                                            description = "Explanation of what this option means"
                                        }
                                    },
                                    required = new[] { "label", "description" }
                                }
                            },
                            multiSelect = new
                            {
                                type = "boolean",
                                description = "Whether multiple options can be selected",
                                @default = false
                            }
                        },
                        required = new[] { "question", "header", "options", "multiSelect" }
                    }
                }
            },
            required = new[] { "questions" }
        };

        var json = JsonSerializer.Serialize(schema);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private sealed class AskUserQuestionAIFunction : AIFunction
    {
        private readonly Func<List<AskUserQuestionInput>, CancellationToken, Task<Dictionary<string, string>>> _handler;

        public AskUserQuestionAIFunction(
            Func<List<AskUserQuestionInput>, CancellationToken, Task<Dictionary<string, string>>> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public override string Name => "ask_user";
        public override string Description => "Ask the user a question and wait for their answer. Use this to gather preferences, clarify requirements, or get decisions.";
        public override JsonElement JsonSchema => Schema;

        protected override async ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            var questions = ParseQuestions(arguments);
            var answers = await _handler(questions, cancellationToken);
            return answers;
        }
    }

    internal static List<AskUserQuestionInput> ParseQuestions(AIFunctionArguments arguments)
    {
        var questions = new List<AskUserQuestionInput>();

        if (!arguments.TryGetValue("questions", out var questionsObj) || questionsObj == null)
            return questions;

        // Handle List<object?> (from DynamicAIFunctionMcpServer deserialization)
        if (questionsObj is List<object?> questionsList)
        {
            foreach (var item in questionsList)
            {
                if (item is Dictionary<string, object?> dict)
                {
                    questions.Add(ParseQuestionFromDictionary(dict));
                }
            }
        }
        // Handle JsonElement directly
        else if (questionsObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in jsonElement.EnumerateArray())
            {
                questions.Add(ParseQuestionFromJsonElement(item));
            }
        }

        return questions;
    }

    private static AskUserQuestionInput ParseQuestionFromDictionary(Dictionary<string, object?> dict)
    {
        var question = dict.GetValueOrDefault("question")?.ToString() ?? "";
        var header = dict.GetValueOrDefault("header")?.ToString() ?? "";
        var multiSelect = dict.GetValueOrDefault("multiSelect") is true;

        var options = new List<AskUserQuestionOptionInput>();
        if (dict.GetValueOrDefault("options") is List<object?> optionsList)
        {
            foreach (var optItem in optionsList)
            {
                if (optItem is Dictionary<string, object?> optDict)
                {
                    options.Add(new AskUserQuestionOptionInput(
                        optDict.GetValueOrDefault("label")?.ToString() ?? "",
                        optDict.GetValueOrDefault("description")?.ToString() ?? ""
                    ));
                }
            }
        }

        return new AskUserQuestionInput(question, header, options, multiSelect);
    }

    private static AskUserQuestionInput ParseQuestionFromJsonElement(JsonElement element)
    {
        var question = element.TryGetProperty("question", out var q) ? q.GetString() ?? "" : "";
        var header = element.TryGetProperty("header", out var h) ? h.GetString() ?? "" : "";
        var multiSelect = element.TryGetProperty("multiSelect", out var ms) && ms.GetBoolean();

        var options = new List<AskUserQuestionOptionInput>();
        if (element.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
        {
            foreach (var opt in opts.EnumerateArray())
            {
                options.Add(new AskUserQuestionOptionInput(
                    opt.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "",
                    opt.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""
                ));
            }
        }

        return new AskUserQuestionInput(question, header, options, multiSelect);
    }
}
