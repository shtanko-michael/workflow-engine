using Microsoft.AspNetCore.Mvc;
using WorkflowEngine.Tests.UI.Backend.Models;
using WorkflowEngine.Tests.UI.Backend.Services;

namespace WorkflowEngine.Tests.UI.Backend.Controllers;

[ApiController]
[Route("api/dialogs")]
public class DialogsController : ControllerBase
{
    private readonly ChatWorkflowService _workflowService;

    public DialogsController(ChatWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    [HttpGet]
    public ActionResult<IReadOnlyCollection<DialogDto>> GetDialogs()
    {
        var dialogs = _workflowService.GetDialogs()
            .Select(ChatWorkflowService.ToDto)
            .ToList();
        return Ok(dialogs);
    }

    [HttpPost]
    public async Task<ActionResult<DialogDto>> CreateDialog([FromBody] CreateDialogRequest request)
    {
        var dialog = await _workflowService.CreateDialogAsync(request.Title);
        return Ok(ChatWorkflowService.ToDto(dialog));
    }

    [HttpGet("{dialogId}/messages")]
    public ActionResult<IReadOnlyCollection<MessageDto>> GetMessages(string dialogId)
    {
        var messages = _workflowService.GetMessages(dialogId)
            .Select(ChatWorkflowService.ToDto)
            .ToList();
        return Ok(messages);
    }

    [HttpPost("{dialogId}/messages")]
    public async Task<ActionResult<IReadOnlyCollection<MessageDto>>> SendMessage(
        string dialogId,
        [FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Message content is required.");

        try
        {
            var messages = await _workflowService.SendMessageAsync(
                dialogId,
                request.Content,
                request.ThreadId,
                request.CheckpointId,
                request.RequestId);

            var payload = messages.Select(ChatWorkflowService.ToDto).ToList();
            return Ok(payload);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
