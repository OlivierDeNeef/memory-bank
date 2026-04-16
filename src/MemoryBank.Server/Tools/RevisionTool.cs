using System.ComponentModel;
using ModelContextProtocol.Server;
using MemoryBank.Core.Models;
using MemoryBank.Core.Storage;

namespace MemoryBank.Server.Tools;

[McpServerToolType]
public class RevisionTool
{
    private readonly MemoryStore _store;

    public RevisionTool(MemoryStore store)
    {
        _store = store;
    }

    [McpServerTool(Name = "get_revisions"), Description("List all revisions of a memory with timestamps and change reasons.")]
    public string GetRevisions([Description("Memory UUID")] string memoryId)
    {
        if (!_store.Exists(memoryId))
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{memoryId}'").ToJson();

        var memory = _store.GetById(memoryId)!;
        var revisions = _store.GetRevisions(memoryId);

        return ToolResponse<object>.Ok(new
        {
            memoryId,
            currentRevision = memory.RevisionNumber,
            revisions = revisions.Select(r => new
            {
                revisionNumber = r.RevisionNumber,
                summary = r.Summary,
                reason = r.Reason,
                createdAt = r.CreatedAt.ToString("o")
            })
        }).ToJson();
    }

    [McpServerTool(Name = "get_revision"), Description("Get the full content of a specific revision.")]
    public string GetRevision(
        [Description("Memory UUID")] string memoryId,
        [Description("Revision number")] int revisionNumber)
    {
        var revision = _store.GetRevision(memoryId, revisionNumber);
        if (revision == null)
            return ToolResponse<object>.Fail(ErrorCodes.RevisionNotFound,
                $"Revision {revisionNumber} not found for memory '{memoryId}'").ToJson();

        return ToolResponse<object>.Ok(new
        {
            memoryId = revision.MemoryId,
            revisionNumber = revision.RevisionNumber,
            content = revision.Content,
            summary = revision.Summary,
            reason = revision.Reason,
            createdAt = revision.CreatedAt.ToString("o")
        }).ToJson();
    }

    [McpServerTool(Name = "diff_revisions"), Description("Show what changed between two revisions of a memory.")]
    public string DiffRevisions(
        [Description("Memory UUID")] string memoryId,
        [Description("From revision number")] int fromRevision,
        [Description("To revision number")] int toRevision)
    {
        var from = _store.GetRevision(memoryId, fromRevision);
        var to = _store.GetRevision(memoryId, toRevision);

        // If toRevision is the current version, get from memory itself
        var memory = _store.GetById(memoryId);
        if (memory == null)
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{memoryId}'").ToJson();

        var fromContent = from?.Content;
        var toContent = to?.Content ?? (toRevision == memory.RevisionNumber ? memory.Content : null);

        if (fromContent == null)
            return ToolResponse<object>.Fail(ErrorCodes.RevisionNotFound, $"Revision {fromRevision} not found").ToJson();
        if (toContent == null)
            return ToolResponse<object>.Fail(ErrorCodes.RevisionNotFound, $"Revision {toRevision} not found").ToJson();

        // Simple diff: show before/after
        return ToolResponse<object>.Ok(new
        {
            memoryId,
            fromRevision,
            toRevision,
            fromContent,
            toContent,
            changed = fromContent != toContent
        }).ToJson();
    }

    [McpServerTool(Name = "restore_revision"), Description("Restore a memory to a previous revision. Creates a new revision for the current state.")]
    public string RestoreRevision(
        [Description("Memory UUID")] string memoryId,
        [Description("Revision number to restore")] int revisionNumber)
    {
        var revision = _store.GetRevision(memoryId, revisionNumber);
        if (revision == null)
            return ToolResponse<object>.Fail(ErrorCodes.RevisionNotFound,
                $"Revision {revisionNumber} not found for memory '{memoryId}'").ToJson();

        var updated = _store.Update(memoryId, revision.Content, revision.Summary,
            null, null, null, null, null, $"restored from revision {revisionNumber}");

        if (updated == null)
            return ToolResponse<object>.Fail(ErrorCodes.MemoryNotFound, $"No memory found with id '{memoryId}'").ToJson();

        return ToolResponse<object>.Ok(new
        {
            memoryId,
            restoredFrom = revisionNumber,
            newRevisionNumber = updated.RevisionNumber
        }).ToJson();
    }
}
