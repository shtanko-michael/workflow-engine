using Microsoft.AspNetCore.Mvc;
using WorkflowEngine.Tests.UI.Backend.Data.Entities;
using WorkflowEngine.Tests.UI.Backend.Data.Mappers;
using WorkflowEngine.Tests.UI.Backend.Data.Repositories;
using WorkflowEngine.Tests.UI.Backend.Models;
using WorkflowEngine.Tests.UI.Backend.Services;
using WorkflowEngine.Tests.UI.Backend.Workflows;

namespace WorkflowEngine.Tests.UI.Backend.Controllers;

[ApiController]
[Route("api/v1/dialogs")]
public class DialogsController : ControllerBase
{
    private readonly ChatWorkflowServiceNew _workflowService;
    private readonly IConversationRepository _conversationRepo;
    private readonly IMessageRepository _messageRepo;

    public DialogsController(
        ChatWorkflowServiceNew workflowService,
        IConversationRepository conversationRepo,
        IMessageRepository messageRepo)
    {
        _workflowService = workflowService;
        _conversationRepo = conversationRepo;
        _messageRepo = messageRepo;
    }

    [HttpGet]
    public async Task<ActionResult<List<DialogDto>>> GetDialogs()
    {
        var conversations = await _workflowService.GetDialogsAsync();
        var dtos = conversations.Select(DtoMapper.ToDto).ToList();
        return Ok(dtos);
    }

    [HttpDelete("{dialogId}")]
    public async Task<ActionResult> DeleteDialog(string dialogId)
    {
        try
        {
            await _workflowService.DeleteDialogAsync(dialogId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost]
    public async Task<ActionResult<DialogDto>> CreateDialog([FromBody] CreateDialogRequest request)
    {
        var workflowId = request.WorkflowId ?? DemoChatWorkflow.WorkflowId;
        var conversation = await _workflowService.CreateDialogAsync(workflowId, request.Title);
        return Ok(DtoMapper.ToDto(conversation));
    }

    [HttpGet("{dialogId}/messages")]
    public async Task<ActionResult<List<MessageWithVersionsDto>>> GetMessages(string dialogId)
    {
        var branch = await _workflowService.GetMessagesAsync(dialogId);
        var messagesWithAlternatives = new List<MessageWithAlternatives>();

        for (var i = 0; i < branch.Count; i++)
        {
            var msg = branch[i];
            var alternatives = i == 0
                ? new List<MessageEntity>()
                : await _messageRepo.GetChildrenAsync(branch[i - 1].Id);
            var sorted = alternatives.OrderBy(m => m.CreatedAt).ToList();
            var currentIndex = sorted.FindIndex(m => m.Id == msg.Id);

            messagesWithAlternatives.Add(new MessageWithAlternatives
            {
                ActiveMessage = msg,
                Alternatives = sorted,
                CurrentIndex = currentIndex >= 0 ? currentIndex : 0,
                TotalAlternatives = sorted.Count
            });
        }

        var dtos = messagesWithAlternatives.Select(DtoMapper.ToDto).ToList();
        return Ok(dtos);
    }

    [HttpPost("{dialogId}/messages")]
    public async Task<ActionResult<MessageWithVersionsDto>> SendMessage(
        string dialogId,
        [FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Message content is required.");

        try
        {
            var msg = await _workflowService.SendMessageAsync(
                dialogId,
                request.Content,
                request.CheckpointId);

            return Ok(DtoMapper.FromSingleMessage(msg));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{dialogId}/messages/edit")]
    public async Task<ActionResult> EditMessage(
        string dialogId,
        [FromBody] EditMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest("Message content is required.");

        try
        {
            await _workflowService.EditMessageAsync(
                dialogId,
                request.VersionId,
                request.Content);

            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{dialogId}/messages/switch-version")]
    public async Task<ActionResult> SwitchVersion(
        string dialogId,
        [FromBody] SwitchVersionRequest request)
    {
        try
        {
            await _workflowService.SwitchVersionAsync(dialogId, request.VersionId);
            return Ok();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
