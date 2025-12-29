using AIChatApp.Model;
using Azure;
using Azure.AI.OpenAI;
using Azure.AI.OpenAI.Chat;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace AIChatApp.Services;

#pragma warning disable AOAI001 // Suppress evaluation-only API warnings
#pragma warning disable SKEXP0010 // Suppress evaluation-only API warnings for AzureChatDataSource usage
internal class ChatService
{
    private readonly IConfiguration _configuration;
    private readonly SearchClient _searchClient;
    private readonly IChatCompletionService _chatService;
    private readonly string _chatEndpoint;
    private readonly string _chatKey;
    private readonly string _searchIndex;
    private readonly string _searchEndpoint;
    private readonly string _searchKey;

    public ChatService(IConfiguration configuration, IChatCompletionService chatService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _chatService = chatService ?? throw new ArgumentNullException(nameof(chatService));

        _chatEndpoint = _configuration["AZURE_OPENAI_CHAT_ENDPOINT"] ?? throw new InvalidOperationException("AZURE_OPENAI_CHAT_ENDPOINT is not configured.");
        _chatKey = _configuration["AZURE_OPENAI_CHAT_KEY"] ?? throw new InvalidOperationException("AZURE_OPENAI_CHAT_KEY is not configured.");
        _searchKey = _configuration["AZURE_OPENAI_SEARCH_KEY"] ?? throw new InvalidOperationException("AZURE_OPENAI_SEARCH_KEY is not configured.");
        _searchEndpoint = _configuration["AZURE_OPENAI_SEARCH_ENDPOINT"] ?? throw new InvalidOperationException("AZURE_OPENAI_SEARCH_ENDPOINT is not configured.");
        _searchIndex = _configuration["AZURE_OPENAI_SEARCH_INDEXNAME"] ?? throw new InvalidOperationException("AZURE_OPENAI_SEARCH_INDEXNAME is not configured.");

    }

    internal async Task<Message> Chat(ChatRequest request)
    {
        ChatHistory history = CreateHistoryFromRequest(request);

        Microsoft.SemanticKernel.ChatMessageContent response = await _chatService.GetChatMessageContentAsync(history);

        string content = string.Empty;
        if (response.Items.Count > 0 && response.Items[0] is TextContent textContent && textContent.Text is not null)
        {
            content = textContent.Text;
        }

        return new Message()
        {
            IsAssistant = response.Role == AuthorRole.Assistant,
            Content = content
        };
    }

    internal async IAsyncEnumerable<string> Stream(ChatRequest request)
    {
        ChatHistory history = CreateHistoryFromRequest(request);



        //IAsyncEnumerable<StreamingChatMessageContent> response = _chatService.GetStreamingChatMessageContentsAsync(history);

        //await foreach (StreamingChatMessageContent content in response)
        //{
        //    if (content.Content is not null)
        //    {
        //        yield return content.Content;
        //    }
        //}
        AzureKeyCredential credential = new AzureKeyCredential(_chatKey);
        AzureOpenAIClient azureClient = new(new Uri(_chatEndpoint), credential);

        ChatClient chatClient = azureClient.GetChatClient("gpt-4.1-mini");
        ChatCompletionOptions options = new();

        options.AddDataSource(new AzureSearchChatDataSource()
        {
            Endpoint = new Uri(_searchEndpoint),
            IndexName = _searchIndex,
            Authentication = DataSourceAuthentication.FromApiKey(_searchKey),
        });

        ChatCompletion completion = await chatClient.CompleteChatAsync(
            [new UserChatMessage(GetLatestUserMessage(history))],
            options
            );

        if (completion.Content is not null)
        {
            yield return completion.Content[0].Text;
        }
    }

    private static string? GetLatestUserMessage(ChatHistory history)
    {
        // Assumes ChatHistory is enumerable and each message has Role and Content properties
        return history
            .Where(m => m.Role == AuthorRole.User)
            .LastOrDefault()?.Content;
    }

    private static ChatHistory CreateHistoryFromRequest(ChatRequest request)
    {
        ChatHistory history = new ChatHistory("You are a helpful assistant.");
        foreach (Message message in request.Messages)
        {
            if (message.IsAssistant)
            {
                history.AddAssistantMessage(message.Content);
            }
            else
            {
                history.AddUserMessage(message.Content);
            }
        }

        return history;
    }
}