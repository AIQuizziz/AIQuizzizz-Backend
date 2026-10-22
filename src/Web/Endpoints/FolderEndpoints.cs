using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.FolderTest;
using CleanArchitectureBase.Application.FolderTest.Dto;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureBase.Web.Endpoints;

public class Folder : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        var group = app.MapGroup(this);

        group.MapGet(SearchFolderTest, "");
        group.MapGet(SearchQuestionSetInFolder, "/{FolderId}/QuestionSets");
        group.MapGet(GetSharingInFolder, "/{FolderId}/Sharing");
        group.MapPost(AddQuestionSetToFolder, "/{FolderId}/QuestionSet/{QuestionSetId}");
        group.MapPost(CreateFolder, "");
        group.MapDelete(DeleteFolder, "/{FolderId}/");
        group.MapDelete(RemoveQuestionSetInFolder, "/{FolderId}/QuestionSet/{QuestionSetId}");
        group.MapPatch(UpdateSharingInFolder, "/{FolderId}/Sharing");

        app.MapGroup(this)
            .MapPatch("/{FolderId}", UpdateFolder);
    }

    public async Task<Ok<ApiResponse<Guid>>> CreateFolder([FromBody] CreateFolderCommand rq, ISender sender)
    {
        var result = await sender.Send(rq);
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<Guid>>> AddQuestionSetToFolder(
        [FromRoute] Guid folderId,
        [FromRoute] Guid questionSetId,
        ISender sender)
    {
        var rq = new AddQuestionSetToFolderCommand
        {
            FolderId = folderId,
            QuestionSetId = questionSetId,
        };
        var result = await sender.Send(rq);
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<Guid>>> RemoveQuestionSetInFolder(
        [FromRoute] Guid folderId,
        [FromRoute] Guid questionSetId,
        ISender sender)
    {
        var rq = new RemoveQuestionSetInFolderCommand
        {
            FolderId = folderId,
            QuestionSetId = questionSetId,
        };
        var result = await sender.Send(rq);
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<PaginatedList<SearchFolderTestDto>>>> SearchFolderTest(
        [FromQuery] string? folderName,
        [FromQuery] string? shareMode,
        ISender sender,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 5)
    {
        var rq = new SearchFolderQuery
        {
            FolderName = folderName, PageNumber = pageNumber, PageSize = pageSize, ShareMode = shareMode
        };
        
        var result = await sender.Send(rq);

        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<SearchQuestionSetInFolderDto>>> SearchQuestionSetInFolder(
        [FromRoute] Guid folderId,
        [FromQuery] string? questionSetName,
        ISender sender,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 5)
    {
        var rq = new SearchQuestionSetInFolderQuery
        {
            FolderId = folderId, PageNumber = pageNumber, PageSize = pageSize, QuestionSetName = questionSetName
        };

        var result = await sender.Send(rq);

        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<ResourceShareDto>>> GetSharingInFolder([FromRoute] Guid folderId, ISender sender)
    {
        var result = await sender.Send(new GetSharingInFolderQuery
        {
            FolderId = folderId
        });
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<Guid>>> DeleteFolder([FromRoute] Guid folderId, ISender sender)
    {
        var result = await sender.Send(new DeleteFolderCommand
        {
            FolderId = folderId
        });
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<Guid>>> UpdateSharingInFolder(
        [FromRoute] Guid folderId,
        [FromBody] UpdateSharingInFolderCommand rq,
        ISender sender)
    {
        rq.FolderId = folderId;
        var result = await sender.Send(rq);
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<Guid>>> UpdateFolder(
        [FromRoute] Guid folderId,
        [FromBody] UpdateFolderCommand rq,
        ISender sender)
    {
        rq.FolderId = folderId;
        var result = await sender.Send(rq);
        return result.ToOk();
    }
}
