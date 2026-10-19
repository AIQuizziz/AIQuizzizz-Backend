using CleanArchitectureBase.Application.Common.Models;
using CleanArchitectureBase.Application.FolderTest;
using CleanArchitectureBase.Application.Payments.Dto;
using CleanArchitectureBase.Application.Plans;
using CleanArchitectureBase.Application.Plans.Dto;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitectureBase.Web.Endpoints;

public class PlanEndpoints : EndpointGroupBase
{
    public override void Map(WebApplication app)
    {
        var group = app.MapGroup(this);

        group.MapGet(GetDetailPlan, "/{planId}");
        group.MapGet(GetPlatformOverview, "/PlatformOverview");
        group.MapGet(GetCurrentPlan, "/CurrentPlan");
        group.MapPost(CreateUpdatePlan, "");
        group.MapPost(BuyPlan, "/{planId}/Buy");
        group.MapDelete(DeletePlan, "/{planId}");
        group.MapGet(SearchPlan);
    }

    public async Task<Ok<ApiResponse<PlanDetailDto>>> GetDetailPlan(
        [FromRoute] Guid planId,
        ISender sender)
    {
        var rq = new GetDetailPlanQuery
        {
            PlanId = planId
        };
        var result = await sender.Send(rq);
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<Guid>>> BuyPlan(
        [FromRoute] Guid planId,
        ISender sender)
    {
        var rq = new BuyPlanCommand
        {
            PlanId = planId
        };
        var result = await sender.Send(rq);
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<Guid>>> DeletePlan(
        [FromRoute] Guid planId,
        ISender sender)
    {
        var rq = new DeletePlanCommand
        {
            PlanId = planId
        };
        var result = await sender.Send(rq);
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<Guid>>> CreateUpdatePlan(
        [FromBody] CreateUpdatePlanCommand rq,
        ISender sender)
    {
        var result = await sender.Send(rq);
        return result.ToOk();
    }

    public async Task<Ok<ApiResponse<List<PlanDetailDto>>>> SearchPlan(
        [AsParameters] SearchPlanQuery query,
        ISender sender
    )
    {
        var result = await sender.Send(query);
        return result.ToOk();
    }
    
    public async Task<Ok<ApiResponse<PlatformOverviewDto>>> GetPlatformOverview(
        ISender sender
    )
    {
        var rq = new GetPlatformOverviewQuery();
        var result = await sender.Send(rq);
        return result.ToOk();
    }
    
    public async Task<Ok<ApiResponse<List<CurrentPlanDto>>>> GetCurrentPlan(
        ISender sender
    )
    {
        var rq = new GetCurrentPlanQuery();
        var result = await sender.Send(rq);
        return result.ToOk();
    }
}
