using CleanArchitectureBase.Application.AiGenerate;
using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.FolderTest.Dto;
using CleanArchitectureBase.Application.Questions.Dtos;
using CleanArchitectureBase.Application.QuestionSets;
using CleanArchitectureBase.Application.QuestionSets.Commands;
using CleanArchitectureBase.Application.QuestionSets.Dtos;
using CleanArchitectureBase.Application.QuestionSets.Queries;
using CleanArchitectureBase.Application.QuestionSets.QuestionJson;
using CleanArchitectureBase.Domain.Constants;
using CleanArchitectureBase.Web.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;

namespace CleanArchitectureBase.Web.Endpoints;

public class QuestionSetEndpoints : EndpointGroupBase
{

    public override void Map(WebApplication app)
    {
        var group = app.MapGroup(this);

        group.MapGet(SearchQuestionSets, "Public");
        group.MapGet(SearchRecentQuestionSets, "/Recent");
        group.MapGet(SearchQuestionSetsOwnedOrSharedWithMe, "/SharedOrOwned");
        group.MapPost(CreateQuestionSet);
        group.MapGet(GetQuestionSetDetail, "{questionSetId}");
        group.MapGet(GetQuestions, "{questionSetId}/Questions");
        group.MapGet(GetSharingInQuestionSet, "{questionSetId}/Sharing");
        group.MapGet(GetClassesHaveQuestionSet, "{questionSetId}/Classes");
        group.MapGet(GetTestFromQuestionSet, "{questionSetId}/Test");
        group.MapGet(GetRating, "{questionSetId}/Rating");
        group.MapGet(GetLinkDownloadFileImport, "/DownloadFileImport");
        group.MapGet(GetTypeOfQuestionInQuestionSet, "{questionSetId}/Types");
        group.MapPost(CreateRating, "{questionSetId}/Rating");
        group.MapPatch(UpdateQuestionSet, "{questionSetId}");
        group.MapDelete(DeleteQuestionSet, "{questionSetId}");
        group.MapGet(GetLearnQuestions, "{questionSetId}/LearnQuestions");
        group.MapPost(UpdateQuestionSetHistory, "{questionSetId}/LearnHistory");
        group.MapDelete(ResetQuestionSetHistory, "{questionSetId}/LearnHistory");
        group.MapGet(GetPermissions, "{questionSetId}/Permissions");
        group.MapGet(GetQuestionsForEdit, "{questionSetId}/QuestionsForEdit");
        group.MapGet(GetQuestionsForCopy, "{questionSetId}/QuestionsForCopy");
        group.MapPatch(UpdateQuestionSetSharing, "{questionSetId}/Sharing");
        group.MapGet(ExportQuestionsJson, "{questionSetId}/ExportJson");
        group.MapPost(ImportQuestionsJson, "ImportJson");
    }

    /// <summary>
    /// Create new question set
    /// </summary>
    /// <param name="command"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<Guid>>> CreateQuestionSet([FromBody] CreateQuestionSetCommand command, ISender sender)
    {
        var result = await sender.Send(command);
        return result.ToOk();
    }

    /// <summary>
    /// User (Owner or sharedmode is editable) Update question set
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="command"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<Guid>>> UpdateQuestionSet(
        [FromRoute] Guid questionSetId,
        [FromBody] UpdateQuestionSetCommand command,
        ISender sender)
    {
        command.QuestionSetId = questionSetId;
        var result = await sender.Send(command);
        return result.ToOk();
    }

    /// <summary>
    /// User - Get permission to show delete and update button
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<QuestionSetPermissionsDto>>> GetPermissions([FromRoute] Guid questionSetId, ISender sender)
    {
        var query = new GetQuestionSetPermissionsQuery
        {
            QuestionSetId = questionSetId
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }

    /// <summary>
    /// User - Get questions of the question set
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<List<QuestionResponseDto>>>> GetQuestions([FromRoute] Guid questionSetId, ISender sender)
    {
        var query = new GetQuestionSetQuestionsQuery
        {
            QuestionSetId = questionSetId
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }

    /// <summary>
    /// Get questions data of the question set for editing
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<List<CreateUpdateQuestionDto>>>> GetQuestionsForEdit([FromRoute] Guid questionSetId, ISender sender)
    {
        var query = new GetQuestionSetQuestionsForEditQuery
        {
            QuestionSetId = questionSetId
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }

    /// <summary>
    /// Get questions data of the question set for copy/import 
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<List<CreateUpdateQuestionDto>>>> GetQuestionsForCopy([FromRoute] Guid questionSetId, ISender sender)
    {
        var query = new GetQuestionSetQuestionsForCopyQuery
        {
            QuestionSetId = questionSetId
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }

    /// <summary>
    /// Get question detail data (No questions)
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<QuestionSetDetailDto>>> GetQuestionSetDetail([FromRoute] Guid questionSetId, ISender sender)
    {
        var query = new GetQuestionSetDetailQuery
        {
            QuestionSetId = questionSetId
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }
    
   /// <summary>
    /// Retrieves a test with a specified number of questions from a question set, filtered by question types
    /// </summary>
    /// <param name="questionSetId">The </param>
    /// <param name="numberOfQuestion"></param>
    /// <param name="questionTypes"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<List<QuestionResponseDto>>>> GetTestFromQuestionSet(
        [FromRoute] Guid questionSetId,
        [FromQuery] int numberOfQuestion,
        [FromQuery] string questionTypes,
        ISender sender)
    {
        var questionTypeList = questionTypes?.Split(',')
                .Select(x => x.Trim()).ToList() ?? new List<string>();
        var query = new GetTestFromQuestionSetQuery
        {
            QuestionSetId = questionSetId,
            NumberOfQuestion = numberOfQuestion,
            QuestionTypes = questionTypeList
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }
    
    /// <summary>
    /// Retrieves sharing details for a specific question set
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<ResourceShareDto>>> GetSharingInQuestionSet(
        [FromRoute] Guid questionSetId,
        ISender sender)
    {
        var query = new GetSharingInQuestionSetQuery
        {
            QuestionSetId = questionSetId,
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }
    
    public async Task<Ok<ApiResponse<List<GetClassesHaveQuestionSetDto>>>> GetClassesHaveQuestionSet(
        [FromRoute] Guid questionSetId,
        ISender sender)
    {
        var query = new GetClassesHaveQuestionSetQuery
        {
            QuestionSetId = questionSetId,
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }

    /// <summary>
    /// User - mark questions that correct or fail in learn mode
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="questions"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<Unit>>> UpdateQuestionSetHistory(
        [FromRoute] Guid questionSetId,
        [FromBody] List<QuestionHistoryUpdate> questions,
        ISender sender)
    {
        var command = new UpdateQuestionSetHistoryCommand
        {
            QuestionSetId = questionSetId,
            Questions = questions
        };

        var result = await sender.Send(command);
        return result.ToOk();
    }

    /// <summary>
    /// User - Reset learn mode
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<Unit>>> ResetQuestionSetHistory(
        [FromRoute] Guid questionSetId,
        ISender sender)
    {
        var command = new ResetQuestionSetHistoryCommand
        {
            QuestionSetId = questionSetId
        };

        var result = await sender.Send(command);
        return result.ToOk();
    }

    /// <summary>
    /// Get question for learn
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="questionCount"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<GetQuestionSetLearnQuestionsQueryDto>>> GetLearnQuestions(
        [FromRoute] Guid questionSetId,
        [FromQuery] int questionCount,
        ISender sender)
    {
        var query = new GetQuestionSetLearnQuestionsQuery
        {
            QuestionSetId = questionSetId,
            QuestionCount = questionCount
        };

        var result = await sender.Send(query);
        return result.ToOk();
    }

    /// <summary>
    /// Delete a question set that current user own.
    /// </summary>
    /// <param name="questionSetId"></param>
    /// <param name="sender"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<Unit>>> DeleteQuestionSet(
        [FromRoute] Guid questionSetId,
        ISender sender)
    {
        var command = new DeleteQuestionSetCommand
        {
            QuestionSetId = questionSetId
        };

        var result = await sender.Send(command);
        return result.ToOk();
    }


    /// <summary>
    /// Use for search question by name/ search questionset by tag/ Recommend by tag
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="pageNumber"></param>
    /// <param name="pageSize"></param>
    /// <param name="name"></param>
    /// <param name="tagIds"></param>
    /// <param name="sortBy">"Rating" (default) or "Newest"</param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<PaginatedList<QuestionSetForListResponseDto>>>> SearchQuestionSets(
        ISender sender,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 5,
        [FromQuery] string? name = null,
        [FromQuery] Guid[]? tagIds = null,
        [FromQuery] string? sortBy = null
    )
    {
        var query = new SearchPublicQuestionSetQuery
        {
            Name = name,
            TagIds = tagIds?.ToList(),
            SortBy = sortBy,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="pageNumber"></param>
    /// <param name="pageSize"></param>
    /// <param name="name"></param>
    /// <param name="sortBy">"RecentAccess"(default) or "Newest"</param>
    /// <param name="filterBy">"ShareWithMe" or "CreatedByMe" (default)</param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<PaginatedList<QuestionSetForListResponseDto>>>> SearchQuestionSetsOwnedOrSharedWithMe(
        ISender sender,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 5,
        [FromQuery] string? name = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? filterBy = null
    )
    {
        var query = new SearchOwnAndSharedQuestionSetQuery
        {
            Name = name,
            SortBy = sortBy,
            PageNumber = pageNumber,
            PageSize = pageSize,
            FilterBy = filterBy
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }


    /// <summary>
    /// Search recent question sets
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="pageNumber"></param>
    /// <param name="pageSize"></param>
    /// <returns></returns>
    public async Task<Ok<ApiResponse<PaginatedList<QuestionSetForListResponseDto>>>> SearchRecentQuestionSets(
        ISender sender,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 5
    )
    {
        var query = new SearchRecentQuestionSetQuery
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
        var result = await sender.Send(query);
        return result.ToOk();
    }

    /// <summary>
    /// Updates the sharing settings for a specific question set
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="questionSetId"></param>
    /// <param name="command"></param>
    public async Task<Ok<ApiResponse<Guid>>> UpdateQuestionSetSharing(
        ISender sender,
        [FromRoute] Guid questionSetId,
        [FromBody] UpdateQuestionSetSharingCommand command
    )
    {
        command.QuestionSetId = questionSetId;
        var result = await sender.Send(command);
        return result.ToOk();
    }
    
    /// <summary>
    /// Retrieves the rating for a specific question set
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="questionSetId"></param>
    public async Task<Ok<ApiResponse<GetRatingDto>>> GetRating(
        ISender sender,
        [FromRoute] Guid questionSetId
    )
    {
        var rq = new GetRatingQuery { QuestionSetId = questionSetId };
        var result = await sender.Send(rq);
        return result.ToOk();
    }
    
    /// <summary>
    /// Creates a new rating for a specific question set
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="questionSetId"></param>
    /// <param name="rq"></param>
    public async Task<Ok<ApiResponse<Guid>>> CreateRating(
        ISender sender,
        [FromRoute] Guid questionSetId,
        [FromBody] CreateRatingCommand rq
    )
    {
        rq.QuestionSetId = questionSetId;
        var result = await sender.Send(rq);
        return result.ToOk();
    }
    
    /// <summary>
    /// Xuất câu hỏi của bộ ra JSON công khai (format "aiquizzizz/questions"). Cần quyền sửa bộ; không kèm ảnh / video.
    /// </summary>
    public async Task<Ok<ApiResponse<QuestionJsonFile>>> ExportQuestionsJson([FromRoute] Guid questionSetId, ISender sender)
    {
        var result = await sender.Send(new ExportQuestionSetJsonQuery { QuestionSetId = questionSetId });
        return result.ToOk();
    }

    /// <summary>
    /// Đọc + kiểm tra JSON câu hỏi (không lưu). Trả câu hợp lệ / không hợp lệ kèm lý do, như nhập Excel.
    /// </summary>
    public async Task<Ok<ApiResponse<ImportQuestionJsonResultDto>>> ImportQuestionsJson([FromBody] ImportQuestionJsonCommand command, ISender sender)
    {
        var result = await sender.Send(command);
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<string>>> GetLinkDownloadFileImport(
        ISender sender)
    {
        var result = await sender.Send(new GetLinkDownloadFileImportQuery());
        return result.ToOk();
    }
    
    public async Task<Ok<ApiResponse<List<TypeOfQuestionInQuestionSetDto>>>> GetTypeOfQuestionInQuestionSet(
        [FromRoute] Guid questionSetId,
        ISender sender)
    {
        var rq = new GetTypeOfQuestionInQuestionSetQuery { QuestionSetId = questionSetId };
        var result = await sender.Send(rq);
        return result.ToOk();
    }
}
