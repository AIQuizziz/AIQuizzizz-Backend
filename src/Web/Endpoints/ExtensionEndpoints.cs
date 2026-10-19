using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.Extension;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureBase.Web.Endpoints;

/// <summary>API cho extension trình duyệt (bôi đen từ -> thẻ ghi nhớ). Có giới hạn tốc độ theo người dùng.</summary>
public class ExtensionEndpoints : EndpointGroupBase
{
    public const string RateLimitPolicy = "extension";

    public override void Map(WebApplication app)
    {
        var group = app.MapGroup(this).RequireRateLimiting(RateLimitPolicy);

        group.MapGet(GetExtensionSettings, "Settings");
        group.MapPut(UpdateExtensionSettings, "Settings");
        group.MapGet(GetWritableQuestionSets, "QuestionSets");
        group.MapPost(CaptureFlashcard, "Flashcards");
    }

    public async Task<Ok<ApiResponse<ExtensionSettingsDto>>> GetExtensionSettings(ISender sender)
    {
        var result = await sender.Send(new GetExtensionSettingsQuery());
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<ExtensionSettingsDto>>> UpdateExtensionSettings(
        [FromBody] UpdateExtensionSettingsCommand command, ISender sender)
    {
        var result = await sender.Send(command);
        return result.ToOk();
    }

    /// <summary>Các bộ người dùng được thêm câu vào (chọn bộ đích)</summary>
    public async Task<Ok<ApiResponse<List<WritableQuestionSetDto>>>> GetWritableQuestionSets(
        [AsParameters] GetWritableQuestionSetsQuery query, ISender sender)
    {
        var result = await sender.Send(query);
        return result.ToOk();
    }

    /// <summary>Thêm một thẻ ghi nhớ từ từ/cụm từ được bôi đen</summary>
    public async Task<Ok<ApiResponse<CaptureFlashcardResult>>> CaptureFlashcard(
        [FromBody] CaptureFlashcardCommand command, ISender sender)
    {
        var result = await sender.Send(command);
        return result.ToOk();
    }
}
