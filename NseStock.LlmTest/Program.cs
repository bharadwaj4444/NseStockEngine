
using Microsoft.Extensions.AI;
using OllamaSharp;

var uri = new Uri("http://localhost:11434/");
var model = "llama3.2:3b";

IChatClient ollamaClient = new OllamaApiClient(uri, model);

// The middleware executes tool calls requested by the model.
IChatClient chatClient = ollamaClient
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

var stockTools = new StockTools();
var options = new ChatOptions
{
    Tools =
    [
        AIFunctionFactory.Create(
            stockTools.GetTechnicalIndicatorsAsync)
    ]
};

var messages = new List<ChatMessage>
{
    new(ChatRole.System,
        "You are an assistant for an NSE stock analysis application. " +
        "When the user asks for technical indicators, call the available " +
        "tool instead of inventing values. Clearly disclose when returned " +
        "data is sample or fictional data.")
};

Console.WriteLine("NSE Stock Agent — Llama 3.2 3B");
Console.WriteLine("Type 'exit' to quit.\n");

while (true)
{
    Console.Write("You: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    messages.Add(new ChatMessage(ChatRole.User, input));

    try
    {
        var response = await chatClient.GetResponseAsync(
            messages, options);

        Console.WriteLine($"\nAssistant: {response.Text}\n");

        messages.Add(new ChatMessage(
            ChatRole.Assistant, response.Text ?? ""));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}\n");
        messages.RemoveAt(messages.Count - 1);
    }
}

chatClient.Dispose();