using System.Collections.Concurrent;
using WorkflowEngine.Tests.UI.Backend.Models;

namespace WorkflowEngine.Tests.UI.Backend.Services;

public class InMemoryChatStore
{
    private readonly ConcurrentDictionary<string, Dialog> _dialogs = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ChatMessage>> _messages = new();

    public IReadOnlyCollection<Dialog> GetDialogs()
    {
        return _dialogs.Values
            .OrderByDescending(dialog => dialog.UpdatedAt)
            .ToList();
    }

    public Dialog? GetDialog(string dialogId)
    {
        return _dialogs.TryGetValue(dialogId, out var dialog) ? dialog : null;
    }

    public Dialog AddDialog(Dialog dialog)
    {
        _dialogs[dialog.Id] = dialog;
        _messages.TryAdd(dialog.Id, new ConcurrentDictionary<string, ChatMessage>());
        return dialog;
    }

    public void UpdateDialog(Dialog dialog)
    {
        dialog.UpdatedAt = DateTimeOffset.UtcNow;
        _dialogs[dialog.Id] = dialog;
    }

    public IReadOnlyCollection<ChatMessage> GetMessages(string dialogId)
    {
        if (!_messages.TryGetValue(dialogId, out var map))
            return Array.Empty<ChatMessage>();

        return map.Values.OrderBy(message => message.CreatedAt).ToList();
    }

    public IReadOnlyCollection<ChatMessage> AddMessages(string dialogId, IEnumerable<ChatMessage> messages)
    {
        var map = _messages.GetOrAdd(dialogId, _ => new ConcurrentDictionary<string, ChatMessage>());
        var added = new List<ChatMessage>();

        foreach (var message in messages)
        {
            if (map.TryAdd(message.Id, message))
            {
                added.Add(message);
            }
        }

        return added;
    }
}
