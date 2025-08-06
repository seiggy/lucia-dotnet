using Microsoft.SemanticKernel.ChatCompletion;

namespace lucia.Agents.Reducers;

/// <summary>
/// Extensions methods for <see cref="IChatCompletionService"/>
/// </summary>
internal static class ChatCompletionServiceExtensions
{
    /// <summary>
    /// Adds a wrapper to an instance of <see cref="IChatCompletionService"/> which will use
    /// the provided instance of <see cref="IChatHistoryReducer"/> to reduce the size of
    /// the <see cref="ChatHistory"/> before sending it tot he model.
    /// </summary>
    /// <param name="service">Instance of <see cref="IChatCompletionService"/></param>
    /// <param name="reducer">Instance of <see cref="IChatHistoryReducer"/></param>
    public static IChatCompletionService UsingChatHistoryReducer(
        this IChatCompletionService service,
        IChatHistoryReducer reducer)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(reducer);

        return new ChatCompletionServiceWithReducer(service, reducer);
    }
}