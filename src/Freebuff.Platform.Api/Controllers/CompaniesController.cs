using Freebuff.Platform.Application.DTOs;
using Freebuff.Platform.Infrastructure.Services;
using Freebuff.Platform.Shared.Extensions;
using Freebuff.Platform.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Freebuff.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class CompaniesController : ControllerBase
{
    private readonly CompanyService _companyService;

    public CompaniesController(CompanyService companyService) => _companyService = companyService;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<CompanyDto>>>> GetAll([FromQuery] PagedRequest filter)
    {
        var result = await _companyService.GetListAsync(filter);
        return Ok(ApiResponse<PagedResult<CompanyDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CompanyDto>>> GetById(Guid id)
    {
        var result = await _companyService.GetByIdAsync(id);
        if (result == null) return NotFound(ApiResponse<CompanyDto>.Fail("NOT_FOUND", "Company not found"));
        return Ok(ApiResponse<CompanyDto>.Ok(result));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CompanyDto>>> Create([FromBody] CreateCompanyDto dto)
    {
        var userId = User.GetUserIdString();
        var result = await _companyService.CreateAsync(dto, userId);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, ApiResponse<CompanyDto>.Ok(result));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CompanyDto>>> Update(Guid id, [FromBody] UpdateCompanyDto dto)
    {
        var userId = User.GetUserIdString();
        var result = await _companyService.UpdateAsync(id, dto, userId);
        if (result == null) return NotFound(ApiResponse<CompanyDto>.Fail("NOT_FOUND", "Company not found"));
        return Ok(ApiResponse<CompanyDto>.Ok(result));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, [FromQuery] string? reason = null)
    {
        var userId = User.GetUserIdString();
        var deleted = await _companyService.SoftDeleteAsync(id, userId, reason);
        if (!deleted) return NotFound(ApiResponse.Fail("NOT_FOUND", "Company not found"));
        return Ok(ApiResponse.Ok(message: "Company deleted"));
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<ActionResult<ApiResponse>> Restore(Guid id)
    {
        var userId = User.GetUserIdString();
        var restored = await _companyService.RestoreAsync(id, userId);
        if (!restored) return NotFound(ApiResponse.Fail("NOT_FOUND", "Company not found or not deleted"));
        return Ok(ApiResponse.Ok(message: "Company restored"));
    }

    [HttpGet("{id:guid}/audit")]
    public async Task<ActionResult<ApiResponse<List<AuditEntryDto>>>> GetAuditHistory(Guid id)
    {
        var result = await _companyService.GetAuditHistoryAsync(id);
        return Ok(ApiResponse<List<AuditEntryDto>>.Ok(result));
    }
}
